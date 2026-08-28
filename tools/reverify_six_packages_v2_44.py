#!/usr/bin/env python3
"""Re-audit every current READY profile and all six-package manifest rows.

The script is intentionally conservative.  It never promotes a catalog-only
record.  A current READY profile stays READY only when its exact-source audit,
critical-field evidence, source/page integrity, uTracer 3+ limits and the 5%
dissipation reserve all pass.  Failed profiles are retained in the catalog but
are blocked from hardware use.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import shutil
import sqlite3
import urllib.parse
import zipfile
from collections import defaultdict
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Iterable


VERSION = "2.44.0"
AUDIT_POLICY = (
    "EXACT_SOURCE_SHA256__COMPLETE_CRITICAL_FIELDS__PINOUT__PAGE_HASHES__"
    "UTRACER3_PLUS_LIMITS__95_PERCENT_DISSIPATION_GUARD__NO_AUTO_PROMOTION"
)

METRIC_QUERIES = {
    "profile_count": "SELECT COUNT(*) FROM measurement_profiles",
    "ready_profile_count": (
        "SELECT COUNT(*) FROM measurement_profiles WHERE approved_for_hardware=1"
    ),
    "datasheet_count": "SELECT COUNT(*) FROM frank_datasheets",
    "model_count": """
        SELECT COUNT(*) FROM (
            SELECT tube_type FROM frank_datasheets GROUP BY tube_type COLLATE NOCASE
        )
    """,
    "manufacturer_count": """
        SELECT COUNT(*) FROM (
            SELECT manufacturer
            FROM frank_datasheets
            WHERE manufacturer <> ''
            GROUP BY manufacturer COLLATE NOCASE
        )
    """,
    "linked_datasheet_count": "SELECT COUNT(*) FROM datasheet_profile_recommendations",
    "ready_datasheet_count": (
        "SELECT COUNT(*) FROM datasheet_profile_recommendations WHERE decision='READY'"
    ),
    "linked_model_count": """
        SELECT COUNT(*) FROM (
            SELECT datasheet.normalized_type
            FROM datasheet_profile_recommendations AS recommendation
            INNER JOIN frank_datasheets AS datasheet
                ON datasheet.id = recommendation.datasheet_id
            GROUP BY datasheet.normalized_type COLLATE NOCASE
        )
    """,
    "ready_model_count": """
        SELECT COUNT(*) FROM (
            SELECT datasheet.tube_type
            FROM datasheet_profile_recommendations AS recommendation
            INNER JOIN frank_datasheets AS datasheet
                ON datasheet.id = recommendation.datasheet_id
            WHERE recommendation.decision = 'READY'
            GROUP BY datasheet.tube_type COLLATE NOCASE
        )
    """,
}


def utc_now() -> str:
    return (
        datetime.now(timezone.utc)
        .replace(microsecond=0)
        .isoformat()
        .replace("+00:00", "Z")
    )


def compact(value: str) -> str:
    return re.sub(r"[^A-Z0-9]+", "", (value or "").upper())


def canonical_maker(value: str) -> str:
    result = compact(value)
    for suffix in ("RC30", "HB3"):
        result = result.replace(suffix, "")
    return result


def json_list(value: str) -> list[str]:
    try:
        parsed = json.loads(value)
    except (TypeError, ValueError, json.JSONDecodeError):
        return []
    if isinstance(parsed, list):
        return [str(item) for item in parsed]
    if isinstance(parsed, dict):
        return [str(item) for item in parsed]
    return []


def missing_is_empty(value: str) -> bool:
    try:
        parsed = json.loads(value)
    except (TypeError, ValueError, json.JSONDecodeError):
        return False
    return parsed in ([], {})


def designations(*values: str) -> set[str]:
    found: set[str] = set()
    for value in values:
        if not value:
            continue
        pieces = re.split(r"[/,;=()\[\]\s]+", value)
        pieces.append(value)
        for piece in pieces:
            normalized = compact(piece)
            if len(normalized) >= 2:
                found.add(normalized)
    return found


def source_identity_is_strong(value: str) -> bool:
    upper = (value or "").upper()
    rejected = (
        "MISMATCH",
        "UNCONFIRMED",
        "INDEX_IDENTITY_ONLY",
        "TARGET_CARD_NOT_RECHECKED",
    )
    if any(token in upper for token in rejected):
        return False
    return "EXACT" in upper or "CONFIRMED" in upper or "REVERIFIED" in upper


def verification_status_is_usable(value: str) -> bool:
    upper = (value or "").upper()
    return not any(
        token in upper
        for token in ("BLOCKED", "PARTIAL", "PENDING", "CATALOG_ONLY", "MISMATCH")
    )


def field_categories(
    connection: sqlite3.Connection,
    profile_id: str,
    verification: sqlite3.Row,
) -> dict[str, bool]:
    fields = [item.lower() for item in json_list(verification["matched_fields_json"])]
    joined = "|".join(fields)

    identity = "identity" in joined or source_identity_is_strong(
        verification["source_identity_status"]
    )
    pinout = "pinout" in joined
    if not pinout:
        pinout = (
            connection.execute(
                """
                SELECT 1 FROM profile_field_evidence
                WHERE profile_id=? AND field_name='pinout' LIMIT 1
                """,
                (profile_id,),
            ).fetchone()
            is not None
        )
    if not pinout:
        pinout = (
            connection.execute(
                """
                SELECT 1 FROM six_batch_candidate_fingerprint_audit
                WHERE profile_id=? AND accepted=1 AND pinout_ok=1 LIMIT 1
                """,
                (profile_id,),
            ).fetchone()
            is not None
        )

    heater = "heater" in fields or (
        any("heater_voltage" in item for item in fields)
        and any("heater_current" in item for item in fields)
    )
    point = "operating_point" in joined or (
        any(
            token in joined
            for token in ("anode_voltage", "operating_point_va", "plate_voltage")
        )
        and any(
            token in joined
            for token in ("nominal_anode_current", "anode_current_ma", "plate_current")
        )
    )
    limits = "limits" in fields or "limit_term" in joined or (
        any(
            token in joined
            for token in ("max_anode_voltage", "maximum_anode_voltage")
        )
        and any(
            token in joined
            for token in (
                "max_anode_power",
                "maximum_anode_power",
                "maximum_anode_dissipation",
            )
        )
    )
    return {
        "identity": identity,
        "pinout": pinout,
        "heater": heater,
        "operating_point": point,
        "limits": limits,
    }


def parse_curve_grid_values(value: str) -> tuple[list[float], bool]:
    text = (value or "").strip()
    if not text:
        return [], True
    numbers = []
    for match in re.finditer(r"[-+]?\d+(?:[.,]\d+)?", text):
        numbers.append(float(match.group(0).replace(",", ".")))
    return numbers, bool(numbers)


def hardware_guard(
    connection: sqlite3.Connection, profile: sqlite3.Row
) -> tuple[bool, list[str]]:
    issues: list[str] = []
    if profile["anode_voltage"] > profile["max_anode_voltage"] + 1e-9:
        issues.append("anode_voltage_above_documented_maximum")
    if profile["screen_voltage"] > profile["max_screen_voltage"] + 1e-9:
        issues.append("screen_voltage_above_documented_maximum")
    if profile["curve_va_stop_v"] > profile["max_anode_voltage"] + 1e-9:
        issues.append("curve_stop_above_documented_anode_maximum")
    if profile["anode_compliance_ma"] < profile["nominal_anode_current_ma"]:
        issues.append("anode_compliance_below_nominal_current")
    if profile["screen_compliance_ma"] < profile["nominal_screen_current_ma"]:
        issues.append("screen_compliance_below_nominal_current")
    if profile["grid_voltage"] > 0:
        issues.append("positive_grid_point_not_supported_by_stock_hardware")

    curve_grids, parsed = parse_curve_grid_values(profile["curve_grid_voltages"])
    if profile["curve_grid_voltages"].strip() and not parsed:
        issues.append("curve_grid_values_not_parseable")
    if any(value > 0 for value in curve_grids):
        issues.append("positive_grid_curve_not_supported_by_stock_hardware")

    stock = connection.execute(
        """
        SELECT compatibility.*, hardware.max_anode_voltage_v,
               hardware.max_screen_voltage_v, hardware.min_grid_voltage_v,
               hardware.max_grid_voltage_v, hardware.max_current_with_compliance_ma
        FROM profile_hardware_compatibility AS compatibility
        INNER JOIN hardware_variants AS hardware ON hardware.id=compatibility.hardware_id
        WHERE compatibility.profile_id=? AND compatibility.hardware_id='UTRACER3_PLUS_STOCK'
        """,
        (profile["id"],),
    ).fetchone()
    if stock is None:
        issues.append("missing_stock_hardware_compatibility")
    elif stock["status"] == "BLOCKED":
        issues.append("stock_hardware_compatibility_blocked")
    else:
        if profile["anode_voltage"] > stock["max_anode_voltage_v"]:
            issues.append("anode_voltage_above_stock_hardware")
        if profile["screen_voltage"] > stock["max_screen_voltage_v"]:
            issues.append("screen_voltage_above_stock_hardware")
        if profile["grid_voltage"] < stock["min_grid_voltage_v"]:
            issues.append("grid_voltage_below_stock_hardware")
        if profile["grid_voltage"] > stock["max_grid_voltage_v"]:
            issues.append("grid_voltage_above_stock_hardware")
        if profile["anode_compliance_ma"] > stock["max_current_with_compliance_ma"]:
            issues.append("anode_compliance_above_stock_hardware")
        if profile["screen_compliance_ma"] > stock["max_current_with_compliance_ma"]:
            issues.append("screen_compliance_above_stock_hardware")
        if any(
            value < stock["min_grid_voltage_v"]
            or value > stock["max_grid_voltage_v"]
            for value in curve_grids
        ):
            issues.append("curve_grid_outside_stock_hardware")
    return not issues, sorted(set(issues))


def power_guard(profile: sqlite3.Row) -> tuple[bool, float, float, list[str]]:
    plate_power = (
        float(profile["anode_voltage"])
        * float(profile["nominal_anode_current_ma"])
        / 1000.0
    )
    screen_power = (
        float(profile["screen_voltage"])
        * float(profile["nominal_screen_current_ma"])
        / 1000.0
    )
    issues: list[str] = []
    if profile["max_anode_power_w"] <= 0:
        issues.append("missing_max_anode_power")
    elif plate_power > 0.95 * float(profile["max_anode_power_w"]) + 1e-9:
        issues.append("anode_point_has_less_than_5_percent_power_reserve")
    if profile["screen_voltage"] > 0 or profile["nominal_screen_current_ma"] > 0:
        if profile["max_screen_power_w"] <= 0:
            issues.append("missing_max_screen_power")
        elif screen_power > 0.95 * float(profile["max_screen_power_w"]) + 1e-9:
            issues.append("screen_point_has_less_than_5_percent_power_reserve")
    return not issues, plate_power, screen_power, issues


def build_page_integrity(
    connection: sqlite3.Connection,
) -> tuple[dict[str, dict[str, Any]], int]:
    by_source: dict[str, list[sqlite3.Row]] = defaultdict(list)
    bad_pages = 0
    for row in connection.execute(
        """
        SELECT * FROM six_batch_card_page_text_v243
        ORDER BY source_sha256, page_number
        """
    ):
        text = row["page_text"]
        correct = (
            len(text) == row["text_chars"]
            and hashlib.sha256(text.encode("utf-8")).hexdigest() == row["text_sha256"]
        )
        if not correct:
            bad_pages += 1
        by_source[row["source_sha256"]].append(row)

    result: dict[str, dict[str, Any]] = {}
    for card in connection.execute("SELECT * FROM six_batch_cards_v243"):
        rows = by_source.get(card["source_sha256"], [])
        combined = "".join(row["page_text"] for row in rows)
        rows_ok = all(
            len(row["page_text"]) == row["text_chars"]
            and hashlib.sha256(row["page_text"].encode("utf-8")).hexdigest()
            == row["text_sha256"]
            for row in rows
        )
        result[card["source_sha256"]] = {
            "part": card["part"],
            "page_count": len(rows),
            "page_count_ok": len(rows) == card["page_count"],
            "page_hashes_ok": rows_ok,
            "full_text_chars": len(combined),
            "full_text_sha256": hashlib.sha256(combined.encode("utf-8")).hexdigest(),
        }
    return result, bad_pages


def verify_pack_archive(
    part: str,
    path: Path,
    expected_sources: set[str],
) -> dict[str, Any]:
    if not path.is_file():
        raise RuntimeError(f"Brak paczki {part}: {path}")
    if not zipfile.is_zipfile(path):
        raise RuntimeError(f"Nieprawidłowy ZIP paczki {part}: {path}")

    sources: set[str] = set()
    hashed_bytes = 0
    bad_names: list[str] = []
    with zipfile.ZipFile(path) as archive:
        bad_member = archive.testzip()
        if bad_member:
            raise RuntimeError(f"Uszkodzony wpis ZIP paczki {part}: {bad_member}")
        for info in archive.infolist():
            if not info.filename.lower().endswith(".pdf"):
                continue
            digest = hashlib.sha256()
            with archive.open(info) as stream:
                while True:
                    chunk = stream.read(1024 * 1024)
                    if not chunk:
                        break
                    digest.update(chunk)
                    hashed_bytes += len(chunk)
            actual = digest.hexdigest()
            expected = Path(info.filename).stem.lower()
            if actual != expected:
                bad_names.append(info.filename)
            sources.add(actual)

    missing = expected_sources - sources
    extra = sources - expected_sources
    if bad_names or missing or extra:
        raise RuntimeError(
            f"Paczka {part} nie zgadza się z bazą: "
            f"bad_sha={len(bad_names)}, missing={len(missing)}, extra={len(extra)}"
        )
    return {
        "path": str(path.resolve()),
        "pdf_count": len(sources),
        "hashed_bytes": hashed_bytes,
        "sha256_ok": True,
    }


def identity_signals(
    connection: sqlite3.Connection,
    profile: sqlite3.Row,
    verification: sqlite3.Row,
    aliases: Iterable[str],
) -> dict[str, bool]:
    source_sha = verification["source_file_sha256"]
    names = designations(
        profile["tube_types"],
        verification["tube_type"],
        *aliases,
    )

    manifest_rows = connection.execute(
        """
        SELECT model, normalized_model, manufacturer
        FROM six_batch_manifest_profiles_v243
        WHERE source_sha256=?
        """,
        (source_sha,),
    ).fetchall()
    manifest_model = any(
        compact(row["model"]) in names or compact(row["normalized_model"]) in names
        for row in manifest_rows
    )

    maker = canonical_maker(verification["manufacturer"])
    manifest_maker = any(
        maker
        and (
            maker == canonical_maker(row["manufacturer"])
            or maker in canonical_maker(row["manufacturer"])
            or canonical_maker(row["manufacturer"]) in maker
        )
        for row in manifest_rows
    )

    base = urllib.parse.unquote(
        urllib.parse.urlsplit(verification["source_url"]).path.rsplit("/", 1)[-1]
    )
    base = compact(base.rsplit(".", 1)[0])
    url_model = bool(base) and any(
        base == name or base in name or name in base for name in names
    )

    page_model = False
    for row in connection.execute(
        """
        SELECT page_text FROM six_batch_card_page_text_v243
        WHERE source_sha256=?
        """,
        (source_sha,),
    ):
        normalized_text = compact(row["page_text"])
        if any(name in normalized_text for name in names if len(name) >= 3):
            page_model = True
            break

    strong = source_identity_is_strong(verification["source_identity_status"])
    external_manual = (
        source_sha
        and len(source_sha) == 64
        and verification["manual_visual_check"] == 1
        and strong
    )
    return {
        "strong_source_identity": strong,
        "manifest_model": manifest_model,
        "manifest_manufacturer": manifest_maker,
        "url_model": url_model,
        "page_model": page_model,
        "external_manual": external_manual,
        "model_identity_ok": strong
        and (manifest_model or url_model or page_model or external_manual),
        "manufacturer_identity_ok": strong and (manifest_maker or external_manual),
    }


def choose_verification(
    connection: sqlite3.Connection,
    profile: sqlite3.Row,
    page_integrity: dict[str, dict[str, Any]],
) -> tuple[sqlite3.Row | None, dict[str, bool], dict[str, bool]]:
    aliases = json_list(profile["aliases_json"])
    candidates: list[tuple[tuple[int, ...], sqlite3.Row, dict[str, bool], dict[str, bool]]] = []
    for verification in connection.execute(
        "SELECT * FROM profile_source_verification WHERE profile_id=?",
        (profile["id"],),
    ):
        if not missing_is_empty(verification["missing_fields_json"]):
            continue
        if not verification_status_is_usable(verification["verification_status"]):
            continue
        source_sha = verification["source_file_sha256"] or ""
        if len(source_sha) != 64:
            continue
        categories = field_categories(connection, profile["id"], verification)
        signals = identity_signals(
            connection, profile, verification, aliases
        )
        card = page_integrity.get(source_sha)
        page_ok = bool(card and card["page_count_ok"] and card["page_hashes_ok"])
        external_ok = bool(verification["manual_visual_check"] and signals["external_manual"])
        score = (
            int(page_ok or external_ok),
            int(signals["model_identity_ok"]),
            int(signals["manufacturer_identity_ok"]),
            sum(categories.values()),
            int(verification["manual_visual_check"]),
            int(card is not None),
        )
        candidates.append((score, verification, categories, signals))
    if not candidates:
        return None, {}, {}
    _, verification, categories, signals = max(candidates, key=lambda item: item[0])
    return verification, categories, signals


def set_info(connection: sqlite3.Connection, key: str, value: object) -> None:
    connection.execute(
        """
        INSERT INTO catalog_info(key,value) VALUES(?,?)
        ON CONFLICT(key) DO UPDATE SET value=excluded.value
        """,
        (key, str(value)),
    )


def create_audit_tables(connection: sqlite3.Connection) -> None:
    connection.executescript(
        """
        DROP TABLE IF EXISTS profile_reverification_v244;
        CREATE TABLE profile_reverification_v244(
            profile_id TEXT PRIMARY KEY,
            was_approved INTEGER NOT NULL,
            source_sha256 TEXT NOT NULL,
            source_url TEXT NOT NULL,
            source_part TEXT NOT NULL,
            identity_ok INTEGER NOT NULL,
            pinout_ok INTEGER NOT NULL,
            heater_ok INTEGER NOT NULL,
            operating_point_ok INTEGER NOT NULL,
            limits_ok INTEGER NOT NULL,
            power_guard_ok INTEGER NOT NULL,
            hardware_ok INTEGER NOT NULL,
            page_integrity_ok INTEGER NOT NULL,
            decision TEXT NOT NULL,
            reason TEXT NOT NULL,
            audit_policy TEXT NOT NULL,
            evidence_json TEXT NOT NULL,
            audited_utc TEXT NOT NULL,
            FOREIGN KEY(profile_id) REFERENCES measurement_profiles(id)
        );

        DROP TABLE IF EXISTS six_batch_manifest_reaudit_v244;
        CREATE TABLE six_batch_manifest_reaudit_v244(
            part TEXT NOT NULL,
            row_number INTEGER NOT NULL,
            model TEXT NOT NULL,
            manufacturer TEXT NOT NULL,
            source_sha256 TEXT NOT NULL,
            profile_id TEXT NOT NULL,
            decision TEXT NOT NULL,
            reason TEXT NOT NULL,
            audited_utc TEXT NOT NULL,
            PRIMARY KEY(part,row_number),
            FOREIGN KEY(profile_id) REFERENCES measurement_profiles(id),
            FOREIGN KEY(source_sha256) REFERENCES six_batch_cards_v243(source_sha256)
        );

        DROP TABLE IF EXISTS six_package_card_integrity_v244;
        CREATE TABLE six_package_card_integrity_v244(
            source_sha256 TEXT PRIMARY KEY,
            part TEXT NOT NULL,
            page_count INTEGER NOT NULL,
            page_count_ok INTEGER NOT NULL,
            page_hashes_ok INTEGER NOT NULL,
            original_pdf_rehashed INTEGER NOT NULL,
            integrity_status TEXT NOT NULL,
            audited_utc TEXT NOT NULL,
            FOREIGN KEY(source_sha256) REFERENCES six_batch_cards_v243(source_sha256)
        );
        """
    )


def audit_and_update(
    connection: sqlite3.Connection,
    page_integrity: dict[str, dict[str, Any]],
    rehashed_parts: set[str],
    audited_utc: str,
) -> dict[str, int]:
    former_ready_ids = [
        row[0]
        for row in connection.execute(
            "SELECT id FROM measurement_profiles WHERE approved_for_hardware=1"
        )
    ]
    former_ready = set(former_ready_ids)
    profile_results: dict[str, dict[str, Any]] = {}

    for profile in connection.execute("SELECT * FROM measurement_profiles ORDER BY id"):
        if profile["id"] not in former_ready:
            audit_row = connection.execute(
                """
                SELECT classification,reason FROM six_batch_card_audit
                WHERE recommended_profile_id=? OR canonical_profile_id=?
                ORDER BY CASE WHEN classification='BLOCKED_AFTER_FULL_CARD_AUDIT'
                              THEN 0 ELSE 1 END, id
                LIMIT 1
                """,
                (profile["id"], profile["id"]),
            ).fetchone()
            reason = (
                audit_row["reason"]
                if audit_row is not None
                else "Profil katalogowy nie ma kompletnego, zatwierdzonego zestawu danych pomiarowych."
            )
            profile_results[profile["id"]] = {
                "was_approved": 0,
                "source_sha256": "",
                "source_url": profile["source_url"],
                "source_part": "",
                "identity_ok": 0,
                "pinout_ok": 0,
                "heater_ok": 0,
                "operating_point_ok": 0,
                "limits_ok": 0,
                "power_guard_ok": 0,
                "hardware_ok": 0,
                "page_integrity_ok": 0,
                "decision": "BLOCKED_NO_COMPLETE_MEASUREMENT_EVIDENCE",
                "reason": reason,
                "evidence": {"previously_approved": False},
            }
            continue

        verification, categories, signals = choose_verification(
            connection, profile, page_integrity
        )
        hardware_ok, hardware_issues = hardware_guard(connection, profile)
        power_ok, plate_power, screen_power, power_issues = power_guard(profile)

        if verification is None:
            source_sha = ""
            source_url = profile["source_url"]
            source_part = ""
            identity_ok = False
            page_ok = False
            source_issue = ["no_complete_exact_source_verification"]
        else:
            source_sha = verification["source_file_sha256"]
            source_url = verification["source_url"]
            source_part = page_integrity.get(source_sha, {}).get("part", "")
            identity_ok = bool(
                signals.get("model_identity_ok")
                and signals.get("manufacturer_identity_ok")
                and categories.get("identity")
            )
            card = page_integrity.get(source_sha)
            page_ok = bool(
                card and card["page_count_ok"] and card["page_hashes_ok"]
            ) or bool(
                verification["manual_visual_check"]
                and signals.get("external_manual")
            )
            source_issue = []
            if not identity_ok:
                source_issue.append("exact_model_or_manufacturer_identity_not_confirmed")
            if not page_ok:
                source_issue.append("source_page_integrity_not_confirmed")

        category_issues: list[str] = []
        for category in ("pinout", "heater", "operating_point", "limits"):
            if not categories.get(category, False):
                category_issues.append(f"missing_complete_{category}_evidence")

        issues = source_issue + category_issues + power_issues + hardware_issues
        decision = "READY_REVERIFIED_V2_44" if not issues else "BLOCKED_REAUDIT_V2_44"
        if issues:
            reason = "BLOKADA po ponownym audycie: " + "; ".join(issues) + "."
        else:
            reason = (
                "Ponownie zweryfikowany dokładny profil: komplet pól krytycznych, "
                "spójność źródła, zakres uTracer 3+ i co najmniej 5% zapasu mocy."
            )

        evidence = {
            "verification_status": verification["verification_status"]
            if verification is not None
            else "",
            "source_identity_status": verification["source_identity_status"]
            if verification is not None
            else "",
            "categories": categories,
            "identity_signals": signals,
            "plate_power_w": round(plate_power, 9),
            "screen_power_w": round(screen_power, 9),
            "max_anode_power_w": profile["max_anode_power_w"],
            "max_screen_power_w": profile["max_screen_power_w"],
            "hardware_issues": hardware_issues,
            "power_issues": power_issues,
            "issues": issues,
        }
        profile_results[profile["id"]] = {
            "was_approved": 1,
            "source_sha256": source_sha,
            "source_url": source_url,
            "source_part": source_part,
            "identity_ok": int(identity_ok),
            "pinout_ok": int(categories.get("pinout", False)),
            "heater_ok": int(categories.get("heater", False)),
            "operating_point_ok": int(categories.get("operating_point", False)),
            "limits_ok": int(categories.get("limits", False)),
            "power_guard_ok": int(power_ok),
            "hardware_ok": int(hardware_ok),
            "page_integrity_ok": int(page_ok),
            "decision": decision,
            "reason": reason,
            "evidence": evidence,
            "verification": verification,
        }

    for profile_id, result in profile_results.items():
        connection.execute(
            """
            INSERT INTO profile_reverification_v244(
                profile_id,was_approved,source_sha256,source_url,source_part,
                identity_ok,pinout_ok,heater_ok,operating_point_ok,limits_ok,
                power_guard_ok,hardware_ok,page_integrity_ok,decision,reason,
                audit_policy,evidence_json,audited_utc
            ) VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)
            """,
            (
                profile_id,
                result["was_approved"],
                result["source_sha256"],
                result["source_url"],
                result["source_part"],
                result["identity_ok"],
                result["pinout_ok"],
                result["heater_ok"],
                result["operating_point_ok"],
                result["limits_ok"],
                result["power_guard_ok"],
                result["hardware_ok"],
                result["page_integrity_ok"],
                result["decision"],
                result["reason"],
                AUDIT_POLICY,
                json.dumps(result["evidence"], ensure_ascii=False, sort_keys=True),
                audited_utc,
            ),
        )

    demoted = {
        profile_id
        for profile_id, result in profile_results.items()
        if result["was_approved"] and result["decision"] != "READY_REVERIFIED_V2_44"
    }
    retained = former_ready - demoted

    for profile_id in demoted:
        result = profile_results[profile_id]
        profile = connection.execute(
            "SELECT display_name,critical_warning,notes FROM measurement_profiles WHERE id=?",
            (profile_id,),
        ).fetchone()
        display_name = profile["display_name"]
        if not display_name.startswith("[BLOKADA v2.44]"):
            display_name = "[BLOKADA v2.44] " + display_name
        warning = (
            "BLOKADA v2.44 — profil nie przeszedł ponownego audytu: "
            + result["reason"]
            + " "
            + profile["critical_warning"]
        )
        notes = profile["notes"] + " | v2.44: " + result["reason"]
        connection.execute(
            """
            UPDATE measurement_profiles
            SET approved_for_hardware=0,
                counts_for_condition_percent=0,
                extraction_status='BLOCKED_REAUDIT_STRICT_V2_44',
                display_name=?, critical_warning=?, notes=?
            WHERE id=?
            """,
            (display_name, warning, notes, profile_id),
        )
        connection.execute(
            """
            UPDATE profile_hardware_compatibility
            SET status='BLOCKED',short_label='BLOKADA',reason=?,
                requires_manual_confirmation=1
            WHERE profile_id=?
            """,
            (result["reason"], profile_id),
        )
        connection.execute(
            """
            UPDATE datasheet_profile_recommendations
            SET decision='BLOCKED',confidence='BLOCKED_REAUDIT_V2_44',
                reason=?,requires_manual_confirmation=1
            WHERE recommended_profile_id=?
            """,
            (result["reason"], profile_id),
        )
        connection.execute(
            """
            UPDATE profile_verification_queue
            SET queue_status='BLOCKED_REAUDIT_V2_44',next_action=?,updated_utc=?
            WHERE profile_id=?
            """,
            (result["reason"], audited_utc, profile_id),
        )
        connection.execute(
            """
            UPDATE six_batch_card_audit
            SET classification='BLOCKED_REAUDIT_V2_44',reason=?,
                audit_method='STRICT_REAUDIT_V2_44',verified_utc=?
            WHERE (recommended_profile_id=? OR canonical_profile_id=?)
              AND (classification LIKE 'READY%' OR classification LIKE 'DUPLICATE%')
            """,
            (result["reason"], audited_utc, profile_id, profile_id),
        )

    for profile_id in former_ready:
        result = profile_results[profile_id]
        verification = result.get("verification")
        if verification is None:
            continue
        matched = [
            "exact_manufacturer_type_identity",
            "numbered_pinout",
            "heater_voltage_and_current",
            "operating_point",
            "documented_limits",
            "source_sha256",
            "page_text_sha256",
            "utracer3_plus_hardware_guard",
        ]
        missing: list[str] = []
        status = "REVERIFIED_STRICT_V2_44"
        if profile_id in retained:
            matched.append("95_percent_power_guard")
            note = (
                "Ponowny audyt v2.44: dokładne źródło, komplet pól krytycznych, "
                "spójność stron, zakres sprzętu i 5% zapasu mocy potwierdzone."
            )
        else:
            status = "BLOCKED_REAUDIT_STRICT_V2_44"
            missing = result["evidence"].get("issues", [])
            note = result["reason"]
        connection.execute(
            """
            INSERT INTO profile_source_verification(
                profile_id,template_profile_id,manufacturer,tube_type,source_url,
                source_file_name,source_file_sha256,source_page_count,
                checked_page_count,text_method,verification_status,
                matched_fields_json,missing_fields_json,manual_visual_check,
                verification_note,verified_utc,source_identity_status
            ) VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)
            """,
            (
                profile_id,
                verification["template_profile_id"],
                verification["manufacturer"],
                verification["tube_type"],
                verification["source_url"],
                verification["source_file_name"],
                verification["source_file_sha256"],
                verification["source_page_count"],
                verification["checked_page_count"],
                verification["text_method"] + "+STRICT_REAUDIT_V2_44",
                status,
                json.dumps(matched, ensure_ascii=False),
                json.dumps(missing, ensure_ascii=False),
                verification["manual_visual_check"],
                note,
                audited_utc,
                "REVERIFIED_EXACT_SOURCE_V2_44",
            ),
        )

    connection.execute(
        """
        UPDATE six_batch_manifest_profiles_v243
        SET approved_for_hardware=(
                SELECT approved_for_hardware FROM measurement_profiles
                WHERE id=six_batch_manifest_profiles_v243.profile_id
            ),
            extraction_status=(
                SELECT extraction_status FROM measurement_profiles
                WHERE id=six_batch_manifest_profiles_v243.profile_id
            )
        """
    )

    for manifest in connection.execute(
        "SELECT * FROM six_batch_manifest_profiles_v243 ORDER BY part,row_number"
    ):
        result = profile_results[manifest["profile_id"]]
        connection.execute(
            """
            INSERT INTO six_batch_manifest_reaudit_v244(
                part,row_number,model,manufacturer,source_sha256,profile_id,
                decision,reason,audited_utc
            ) VALUES(?,?,?,?,?,?,?,?,?)
            """,
            (
                manifest["part"],
                manifest["row_number"],
                manifest["model"],
                manifest["manufacturer"],
                manifest["source_sha256"],
                manifest["profile_id"],
                result["decision"],
                result["reason"],
                audited_utc,
            ),
        )

    for source_sha, integrity in page_integrity.items():
        original_rehashed = int(integrity["part"] in rehashed_parts)
        if not integrity["page_count_ok"] or not integrity["page_hashes_ok"]:
            status = "BLOCKED_PAGE_INTEGRITY_ERROR"
        elif original_rehashed:
            status = "ORIGINAL_PDF_AND_PAGE_TEXT_SHA256_VERIFIED"
        else:
            status = "PAGE_TEXT_SHA256_VERIFIED_ORIGINAL_ARCHIVE_REHASH_PENDING"
        connection.execute(
            """
            INSERT INTO six_package_card_integrity_v244(
                source_sha256,part,page_count,page_count_ok,page_hashes_ok,
                original_pdf_rehashed,integrity_status,audited_utc
            ) VALUES(?,?,?,?,?,?,?,?)
            """,
            (
                source_sha,
                integrity["part"],
                integrity["page_count"],
                int(integrity["page_count_ok"]),
                int(integrity["page_hashes_ok"]),
                original_rehashed,
                status,
                audited_utc,
            ),
        )
        connection.execute(
            """
            UPDATE six_batch_cards_v243
            SET full_text_chars=?,full_text_sha256=?
            WHERE source_sha256=?
            """,
            (
                integrity["full_text_chars"],
                integrity["full_text_sha256"],
                source_sha,
            ),
        )

    return {
        "former_ready": len(former_ready),
        "retained_ready": len(retained),
        "demoted": len(demoted),
        "manifest_rows": connection.execute(
            "SELECT COUNT(*) FROM six_batch_manifest_reaudit_v244"
        ).fetchone()[0],
        "profile_audit_rows": connection.execute(
            "SELECT COUNT(*) FROM profile_reverification_v244"
        ).fetchone()[0],
    }


def update_catalog_info(
    connection: sqlite3.Connection,
    stats: dict[str, int],
    audited_utc: str,
    rehashed_parts: set[str],
) -> None:
    for key, query in METRIC_QUERIES.items():
        set_info(connection, key, int(connection.execute(query).fetchone()[0]))
    set_info(connection, "catalog_version", VERSION)
    set_info(connection, "database_version", VERSION)
    set_info(connection, "verification_release", VERSION)
    set_info(connection, "generated_utc", audited_utc)
    set_info(
        connection,
        "catalog_stage",
        "SIX_PACKAGES_ALL_ROWS_REAUDITED_AND_ALL_READY_PROFILES_STRICTLY_REVERIFIED",
    )
    set_info(
        connection,
        "release_label",
        "v2.44.0 — ponowny audyt wszystkich profili READY i 6 paczek",
    )
    set_info(
        connection,
        "verification_note",
        "Każdy dotychczasowy profil READY sprawdzono ponownie. Rekordy bez "
        "pełnego dowodu lub bez 5% zapasu mocy pozostają w katalogu, ale są "
        "zablokowane sprzętowo. Nie wykonano automatycznej promocji OCR.",
    )
    set_info(connection, "v244_audit_policy", AUDIT_POLICY)
    set_info(connection, "v244_former_ready_count", stats["former_ready"])
    set_info(connection, "v244_reverified_ready_count", stats["retained_ready"])
    set_info(connection, "v244_demoted_after_reaudit_count", stats["demoted"])
    set_info(connection, "v244_manifest_rows_reaudited", stats["manifest_rows"])
    set_info(connection, "v244_profile_rows_audited", stats["profile_audit_rows"])
    set_info(connection, "v244_original_pdf_rehashed_parts", ",".join(sorted(rehashed_parts)))


def validate(connection: sqlite3.Connection, expected_former_ready: int) -> dict[str, int]:
    quick_check = connection.execute("PRAGMA quick_check").fetchone()[0]
    if str(quick_check).lower() != "ok":
        raise RuntimeError(f"PRAGMA quick_check: {quick_check}")
    foreign_keys = list(connection.execute("PRAGMA foreign_key_check"))
    if foreign_keys:
        raise RuntimeError(f"PRAGMA foreign_key_check: {len(foreign_keys)} błędów")

    info = dict(connection.execute("SELECT key,value FROM catalog_info"))
    metrics: dict[str, int] = {}
    for key, query in METRIC_QUERIES.items():
        value = int(connection.execute(query).fetchone()[0])
        metrics[key] = value
        if info.get(key) != str(value):
            raise RuntimeError(
                f"Metadane nie odpowiadają bazie: {key} metadata={info.get(key)!r}, actual={value}"
            )

    checks = {
        "invalid_ready_recommendations": """
            SELECT COUNT(*)
            FROM datasheet_profile_recommendations AS recommendation
            LEFT JOIN measurement_profiles AS profile
                ON profile.id=recommendation.recommended_profile_id
            LEFT JOIN profile_hardware_compatibility AS compatibility
                ON compatibility.profile_id=profile.id
               AND compatibility.hardware_id='UTRACER3_PLUS_STOCK'
            WHERE recommendation.decision='READY'
              AND (profile.id IS NULL OR profile.approved_for_hardware<>1
                   OR compatibility.profile_id IS NULL OR compatibility.status='BLOCKED')
        """,
        "approved_without_reaudit": """
            SELECT COUNT(*)
            FROM measurement_profiles AS profile
            LEFT JOIN profile_reverification_v244 AS audit ON audit.profile_id=profile.id
            WHERE profile.approved_for_hardware=1
              AND (audit.profile_id IS NULL OR audit.decision<>'READY_REVERIFIED_V2_44')
        """,
        "reaudit_ready_but_profile_blocked": """
            SELECT COUNT(*)
            FROM profile_reverification_v244 AS audit
            JOIN measurement_profiles AS profile ON profile.id=audit.profile_id
            WHERE audit.decision='READY_REVERIFIED_V2_44'
              AND profile.approved_for_hardware<>1
        """,
        "approved_power_guard_violation": """
            SELECT COUNT(*)
            FROM measurement_profiles
            WHERE approved_for_hardware=1 AND (
                max_anode_power_w<=0
                OR anode_voltage*nominal_anode_current_ma/1000.0 > 0.95*max_anode_power_w+1e-9
                OR ((screen_voltage>0 OR nominal_screen_current_ma>0) AND (
                    max_screen_power_w<=0
                    OR screen_voltage*nominal_screen_current_ma/1000.0 > 0.95*max_screen_power_w+1e-9
                ))
            )
        """,
        "bad_page_integrity": """
            SELECT COUNT(*) FROM six_package_card_integrity_v244
            WHERE page_count_ok<>1 OR page_hashes_ok<>1
        """,
        "manifest_reaudit_count_mismatch": """
            SELECT ABS(
                (SELECT COUNT(*) FROM six_batch_manifest_profiles_v243)-
                (SELECT COUNT(*) FROM six_batch_manifest_reaudit_v244)
            )
        """,
        "former_ready_audit_count_mismatch": """
            SELECT ABS(?-(SELECT COUNT(*) FROM profile_reverification_v244 WHERE was_approved=1))
        """,
    }
    for name, query in checks.items():
        args = (expected_former_ready,) if "?" in query else ()
        value = int(connection.execute(query, args).fetchone()[0])
        if value:
            raise RuntimeError(f"Kontrola {name} zwróciła {value}, oczekiwano 0")
    return metrics


def parse_pack(value: str) -> tuple[str, Path]:
    if "=" not in value:
        raise argparse.ArgumentTypeError("Użyj formatu PART=ścieżka.zip")
    part, path = value.split("=", 1)
    part = part.strip().zfill(3)
    if not re.fullmatch(r"\d{3}", part):
        raise argparse.ArgumentTypeError(f"Nieprawidłowy numer paczki: {part}")
    return part, Path(path)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("source", type=Path)
    parser.add_argument("destination", type=Path)
    parser.add_argument(
        "--pack",
        action="append",
        default=[],
        type=parse_pack,
        metavar="PART=ZIP",
        help="Opcjonalnie przelicz SHA-256 wszystkich PDF wskazanej paczki.",
    )
    args = parser.parse_args()

    args.destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(args.source, args.destination)
    audited_utc = utc_now()

    connection = sqlite3.connect(args.destination)
    connection.row_factory = sqlite3.Row
    try:
        connection.execute("PRAGMA foreign_keys=ON")
        page_integrity, bad_pages = build_page_integrity(connection)
        if bad_pages:
            raise RuntimeError(f"Błędne sumy stron: {bad_pages}")

        archive_results: dict[str, dict[str, Any]] = {}
        for part, path in args.pack:
            expected_sources = {
                row[0]
                for row in connection.execute(
                    "SELECT source_sha256 FROM six_batch_cards_v243 WHERE part=?",
                    (part,),
                )
            }
            archive_results[part] = verify_pack_archive(part, path, expected_sources)

        with connection:
            create_audit_tables(connection)
            stats = audit_and_update(
                connection, page_integrity, set(archive_results), audited_utc
            )
            update_catalog_info(
                connection, stats, audited_utc, set(archive_results)
            )
        metrics = validate(connection, stats["former_ready"])
        connection.execute("PRAGMA optimize")
        connection.commit()
    finally:
        connection.close()

    digest = hashlib.sha256(args.destination.read_bytes()).hexdigest()
    print(f"OK: {args.destination}")
    print(f"sha256={digest}")
    for key, value in stats.items():
        print(f"{key}={value}")
    for key, value in metrics.items():
        print(f"{key}={value}")
    for part, result in sorted(archive_results.items()):
        print(
            f"pack_{part}_pdf_count={result['pdf_count']} "
            f"hashed_bytes={result['hashed_bytes']} sha256_ok=1"
        )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
