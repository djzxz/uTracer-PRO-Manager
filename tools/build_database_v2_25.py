#!/usr/bin/env python3
"""Build database v2.25.0 from v2.24.0.

The migration materialises 300 manufacturer/card-specific measurement profiles
from cards that already have a READY recommendation to an approved profile.
It does not invent electrical values and it does not promote catalog-only rows.
Every new row copies its execution values 1:1, is marked PASUJE DO, disables
condition percentages, and requires operator confirmation.
"""

from __future__ import annotations

import csv
import hashlib
import json
import os
import re
import shutil
import sqlite3
from datetime import datetime, timezone
from pathlib import Path
from urllib.parse import urlparse


ROOT = Path(__file__).resolve().parents[1]
SOURCE_DB = ROOT / "BAZA_DANYCH_v2_24_0" / "tube_measurements.db"
OUTPUT_DIR = ROOT / "BAZA_DANYCH_v2_25_0"
OUTPUT_DB = OUTPUT_DIR / "tube_measurements.db"
FULL_DB = OUTPUT_DIR / "tube_measurements_v2_25_0_FULL_SCHEMA_V7.db"
BATCH_SIZE = 300
RELEASE = "2.25.0"


def normalized(value: str) -> str:
    return re.sub(r"[^A-Z0-9]", "", (value or "").upper())


def file_stem(url: str) -> str:
    return normalized(os.path.basename(urlparse(url).path).rsplit(".", 1)[0])


def path_key(url: str) -> str:
    return urlparse(url).path.casefold()


def safe_id(value: str) -> str:
    result = re.sub(r"[^A-Z0-9]+", "_", (value or "").upper()).strip("_")
    return result[:36] or "UNKNOWN"


def audio_priority(tube_type: str) -> int:
    value = normalized(tube_type)
    prefixes = (
        "ECC", "ECL", "PCL", "EL", "EF86", "EF80", "EF85", "EF89",
        "KT", "GZ", "EZ", "AZ", "UY", "PY", "5Y", "5U", "5V", "5Z",
        "6V6", "6L6", "6P3", "6CA7", "6BQ5", "6AQ5", "6X4", "6CA4",
        "12AX7", "12AU7", "12AT7", "12AY7", "12BH7", "12DW7", "12SL7",
        "12SN7", "2A3", "300B", "5881", "6550", "7027", "7591", "274B",
    )
    return 0 if value.startswith(prefixes) else 1


def set_info(connection: sqlite3.Connection, key: str, value: object) -> None:
    connection.execute(
        "INSERT INTO catalog_info(key,value) VALUES(?,?) "
        "ON CONFLICT(key) DO UPDATE SET value=excluded.value",
        (key, str(value)),
    )


def select_candidates(connection: sqlite3.Connection) -> list[sqlite3.Row]:
    rows = connection.execute(
        """
        SELECT d.id AS datasheet_id, d.tube_type, d.normalized_type,
               d.manufacturer, d.data_sheet_url, d.file_name,
               d.source_page AS datasheet_source_page,
               r.recommended_profile_id, r.confidence,
               r.requires_manual_confirmation,
               p.*
        FROM datasheet_profile_recommendations AS r
        JOIN frank_datasheets AS d ON d.id=r.datasheet_id
        JOIN measurement_profiles AS p ON p.id=r.recommended_profile_id
        WHERE r.decision='READY'
          AND p.approved_for_hardware=1
          AND p.display_name NOT LIKE '%DO WERYFIKACJI%'
          AND EXISTS(
              SELECT 1 FROM profile_source_verification AS v
              WHERE v.profile_id=p.id
          )
          AND EXISTS(
              SELECT 1 FROM profile_hardware_compatibility AS h
              WHERE h.profile_id=p.id AND h.status<>'BLOCKED'
          )
        """
    ).fetchall()

    eligible: list[sqlite3.Row] = []
    for row in rows:
        tube = normalized(row["tube_type"])
        name = file_stem(row["data_sheet_url"])
        profile_types = normalized(row["tube_types"])
        if name not in {tube, normalized(row["normalized_type"])}:
            continue
        if tube not in profile_types:
            continue
        if not row["pinout"].strip() or row["heater_voltage"] <= 0:
            continue
        eligible.append(row)

    def order(row: sqlite3.Row) -> tuple[object, ...]:
        cross_manufacturer = (
            path_key(row["data_sheet_url"]) != path_key(row["source_url"])
            or normalized(row["manufacturer"]) not in normalized(row["manufacturer_scope"])
        )
        return (
            audio_priority(row["tube_type"]),
            0 if cross_manufacturer else 1,
            normalized(row["tube_type"]),
            normalized(row["manufacturer"]),
            row["datasheet_id"],
        )

    eligible.sort(key=order)
    if len(eligible) < BATCH_SIZE:
        raise RuntimeError(
            f"Only {len(eligible)} strict existing-READY candidates; {BATCH_SIZE} required."
        )
    return eligible[:BATCH_SIZE]


def insert_profile(
    connection: sqlite3.Connection,
    source: sqlite3.Row,
    generated_utc: str,
) -> dict[str, object]:
    digest = hashlib.sha256(
        f"{source['datasheet_id']}|{source['data_sheet_url']}|{source['recommended_profile_id']}".encode()
    ).hexdigest()[:10]
    new_id = (
        f"MFR25_{safe_id(source['tube_type'])}_{safe_id(source['manufacturer'])}_{digest}"
    )
    template_id = source["recommended_profile_id"]
    template_name = source["display_name"]
    aliases = json.loads(source["aliases_json"] or "[]")
    aliases.extend([source["tube_type"]])
    aliases = list(dict.fromkeys(str(item).strip() for item in aliases if str(item).strip()))

    profile_columns = [
        row[1]
        for row in connection.execute("PRAGMA table_info(measurement_profiles)").fetchall()
    ]
    values = {column: source[column] for column in profile_columns}
    values.update(
        {
            "id": new_id,
            "display_name": (
                f"{source['tube_type']} — {source['manufacturer']} — "
                f"PASUJE DO: {template_name}"
            ),
            "aliases_json": json.dumps(aliases, ensure_ascii=False),
            "tube_types": source["tube_type"],
            "manufacturer_scope": (
                f"{source['manufacturer']} • PASUJE DO: {template_name}"
            ),
            "critical_warning": (
                "PROFIL PRODUCENTA — PASUJE DO ISTNIEJĄCEGO PROFILU READY. "
                f"Wartości wykonawcze skopiowano 1:1 z {template_name}. "
                "Przed pomiarem potwierdź oznaczenie lampy, pinout i wybrany wariant "
                "sprzętu. Ocena procentowa jest wyłączona."
            ),
            "measurement_purpose": (
                "Profil producenta z istniejącej rekomendacji READY; "
                "pomiar bez oceny procentowej"
            ),
            "source_title": (
                f"{source['manufacturer']} {source['tube_type']} — karta katalogowa; "
                f"wartości wykonawcze 1:1 z: {source['source_title']}"
            ),
            "source_url": source["data_sheet_url"],
            "source_page": (
                f"{source['file_name']}; wartości wykonawcze ze źródła profilu "
                f"{template_id}: {source['source_page']}"
            ),
            "extraction_status": "READY_MANUFACTURER_ALIAS_EXISTING_RECOMMENDATION_V2_25",
            "approved_for_hardware": 1,
            "counts_for_condition_percent": 0,
            "notes": (
                f"{source['notes']}\n[v2.25.0] Osobny wariant producenta/karty utworzono "
                f"z istniejącej rekomendacji READY. PASUJE DO {template_id}; wszystkie "
                "wartości elektryczne, limity, pinout i krzywe skopiowano 1:1. "
                "Treści PDF nie weryfikowano ponownie w tej partii; wymagane jest "
                "potwierdzenie operatora, a ocena procentowa pozostaje wyłączona."
            ),
        }
    )
    placeholders = ",".join("?" for _ in profile_columns)
    connection.execute(
        f"INSERT INTO measurement_profiles({','.join(profile_columns)}) VALUES({placeholders})",
        [values[column] for column in profile_columns],
    )

    connection.execute(
        """
        INSERT INTO profile_hardware_compatibility(
            profile_id, hardware_id, status, short_label, reason,
            usable_curve_stop_v, usable_current_ma, requires_manual_confirmation
        )
        SELECT ?, hardware_id, status,
               CASE WHEN status='BLOCKED' THEN short_label ELSE 'PASUJE DO • ' || short_label END,
               'PASUJE DO ' || ? || ' | ' || reason,
               usable_curve_stop_v, usable_current_ma, 1
        FROM profile_hardware_compatibility
        WHERE profile_id=?
        """,
        (new_id, template_name, template_id),
    )
    connection.execute(
        """
        INSERT INTO frank_profile_links(datasheet_id,profile_id,link_method)
        VALUES(?,?,?)
        """,
        (
            source["datasheet_id"],
            new_id,
            f"PROFIL PRODUCENTA v2.25.0 — PASUJE DO {template_id}; wartości 1:1",
        ),
    )
    connection.execute(
        """
        UPDATE datasheet_profile_recommendations
        SET recommended_profile_id=?,
            confidence='READY_MANUFACTURER_ALIAS_V2_25',
            reason=?, requires_manual_confirmation=1
        WHERE datasheet_id=?
        """,
        (
            new_id,
            f"Osobny profil producenta; PASUJE DO {template_id}. Wartości 1:1, bez oceny %.",
            source["datasheet_id"],
        ),
    )
    connection.execute(
        """
        INSERT INTO profile_source_verification(
            profile_id, template_profile_id, manufacturer, tube_type,
            source_url, source_file_name, source_file_sha256,
            source_page_count, checked_page_count, text_method,
            verification_status, matched_fields_json, missing_fields_json,
            manual_visual_check, verification_note, verified_utc,
            source_identity_status
        ) VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)
        """,
        (
            new_id,
            template_id,
            source["manufacturer"],
            source["tube_type"],
            source["data_sheet_url"],
            source["file_name"],
            "EXISTING_READY_RECOMMENDATION_NO_NEW_DOWNLOAD",
            0,
            0,
            "EXACT_CATALOG_TYPE_AND_FILENAME_EXISTING_READY_RECOMMENDATION",
            "READY_MANUFACTURER_CARD_ALIAS_EXISTING_RECOMMENDATION_V2_25",
            json.dumps(
                [
                    "exact_catalog_type",
                    "exact_pdf_filename_type",
                    "existing_READY_recommendation",
                    "approved_template_profile",
                    "execution_values_copied_1_to_1",
                    "hardware_matrix_copied_1_to_1",
                ]
            ),
            json.dumps(
                ["independent_pdf_content_recheck_v2_25", "local_pdf_sha256"]
            ),
            0,
            (
                f"v2.25.0: profil producenta materializuje istniejącą rekomendację READY. "
                f"PASUJE DO {template_id}; parametry skopiowano 1:1. Nie jest to nowa, "
                "niezależna weryfikacja treści PDF. Ocena procentowa wyłączona; "
                "potwierdzenie operatora obowiązkowe."
            ),
            generated_utc,
            "EXACT_CATALOG_TYPE_FILENAME__EXISTING_READY_MAPPING",
        ),
    )
    connection.execute(
        """
        INSERT INTO profile_template_fallbacks(
            profile_id, template_profile_id, fallback_mode, safety_policy,
            source_identity_status, full_manufacturer_card_verified, promoted_utc
        ) VALUES(?,?,?,?,?,?,?)
        """,
        (
            new_id,
            template_id,
            "EXACT_TYPE_EXISTING_READY_RECOMMENDATION",
            "COPY_1_TO_1__NO_PERCENT__MANUAL_CONFIRMATION",
            "EXACT_CATALOG_TYPE_FILENAME__CONTENT_NOT_RECHECKED_V2_25",
            0,
            generated_utc,
        ),
    )
    return {
        "profile_id": new_id,
        "tube_type": source["tube_type"],
        "manufacturer": source["manufacturer"],
        "template_profile_id": template_id,
        "template_name": template_name,
        "data_sheet_url": source["data_sheet_url"],
        "confidence_before": source["confidence"],
        "condition_percent": "NIE",
        "manual_confirmation": "TAK",
    }


def update_catalog_info(connection: sqlite3.Connection, generated_utc: str) -> None:
    counts = {
        "profile_count": "SELECT COUNT(*) FROM measurement_profiles",
        "ready_profile_count": (
            "SELECT COUNT(*) FROM measurement_profiles WHERE approved_for_hardware=1"
        ),
        "blocked_profile_count": (
            "SELECT COUNT(*) FROM measurement_profiles WHERE approved_for_hardware=0"
        ),
        "hardware_compatibility_row_count": (
            "SELECT COUNT(*) FROM profile_hardware_compatibility"
        ),
        "source_verification_record_count": (
            "SELECT COUNT(*) FROM profile_source_verification"
        ),
        "profile_recommendation_count": (
            "SELECT COUNT(*) FROM datasheet_profile_recommendations"
        ),
        "ready_datasheet_count": (
            "SELECT COUNT(*) FROM datasheet_profile_recommendations WHERE decision='READY'"
        ),
        "verified_cards_ready": (
            "SELECT COUNT(*) FROM datasheet_profile_recommendations WHERE decision='READY'"
        ),
    }
    for key, query in counts.items():
        set_info(connection, key, connection.execute(query).fetchone()[0])
    ready_profiles = connection.execute(
        "SELECT COUNT(*) FROM measurement_profiles WHERE approved_for_hardware=1"
    ).fetchone()[0]
    set_info(connection, "catalog_version", RELEASE)
    set_info(connection, "database_version", RELEASE)
    set_info(connection, "verification_release", RELEASE)
    set_info(connection, "generated_utc", generated_utc)
    set_info(connection, "batch_2_25_manufacturer_profiles_added", BATCH_SIZE)
    set_info(connection, "batch_2_25_values_copied_1_to_1", BATCH_SIZE)
    set_info(connection, "batch_2_25_percent_grade_disabled", BATCH_SIZE)
    set_info(connection, "batch_2_25_manual_confirmation", BATCH_SIZE)
    set_info(connection, "catalog_stage", "MANUFACTURER_READY_ALIASES_AND_REFERENCE_CURVES")
    set_info(
        connection,
        "release_label",
        f"v{RELEASE} — 300 profili producentów PASUJE DO; łącznie {ready_profiles} READY",
    )
    set_info(
        connection,
        "verification_note",
        "v2.25.0: 300 osobnych wariantów producent/karta utworzono wyłącznie z "
        "istniejących rekomendacji READY i dokładnej zgodności oznaczenia typu z nazwą "
        "pliku. Wartości wykonawcze skopiowano 1:1; brak nowej niezależnej weryfikacji "
        "treści PDF, ocena procentowa wyłączona, potwierdzenie operatora obowiązkowe.",
    )


def write_outputs(connection: sqlite3.Connection, added: list[dict[str, object]]) -> None:
    csv_path = OUTPUT_DIR / "NOWE_PROFILE_PRODUCENTOW_300.csv"
    with csv_path.open("w", encoding="utf-8-sig", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=list(added[0]))
        writer.writeheader()
        writer.writerows(added)

    status = connection.execute("SELECT key,value FROM catalog_info ORDER BY key").fetchall()
    with (OUTPUT_DIR / "STATUS_BAZY_v2_25_0.csv").open(
        "w", encoding="utf-8-sig", newline=""
    ) as handle:
        writer = csv.writer(handle)
        writer.writerow(["klucz", "wartość"])
        writer.writerows(status)

    integrity = connection.execute("PRAGMA integrity_check").fetchone()[0]
    foreign_keys = connection.execute("PRAGMA foreign_key_check").fetchall()
    profile_count = connection.execute("SELECT COUNT(*) FROM measurement_profiles").fetchone()[0]
    ready_count = connection.execute(
        "SELECT COUNT(*) FROM measurement_profiles WHERE approved_for_hardware=1"
    ).fetchone()[0]
    blocked_count = profile_count - ready_count
    red_count = connection.execute(
        """
        SELECT COUNT(*) FROM profile_hardware_compatibility
        WHERE hardware_id='UTRACER3_PLUS_STOCK' AND status='BLOCKED'
        """
    ).fetchone()[0]
    report = (
        "WERYFIKACJA BAZY uTracer PRO Manager v2.25.0\n"
        "================================================\n"
        f"Dodane warianty producent/karta: {len(added)}\n"
        f"Profile razem: {profile_count}\n"
        f"Profile READY: {ready_count}\n"
        f"Profile zablokowane: {blocked_count}\n"
        f"Blokady dla uTracer 3+ stock (czerwone): {red_count}\n"
        f"PRAGMA integrity_check: {integrity}\n"
        f"PRAGMA foreign_key_check: {'OK' if not foreign_keys else foreign_keys}\n\n"
        "Zasada partii: dokładne oznaczenie typu = nazwa pliku karty, istniejąca "
        "rekomendacja READY i zatwierdzony profil wzorcowy. Parametry i macierz "
        "sprzętowa skopiowane 1:1. Nie promowano wpisów CATALOG_ONLY. Każdy nowy "
        "profil ma dopisek PASUJE DO, wymaga potwierdzenia operatora i nie oblicza "
        "procentowej kondycji.\n"
    )
    (OUTPUT_DIR / "WERYFIKACJA_v2_25_0.txt").write_text(report, encoding="utf-8")
    readme = (
        "BAZA DANYCH v2.25.0 — PODMIANA\n"
        "================================\n"
        "1. Zamknij uTracer PRO Manager.\n"
        "2. W programie użyj USTAWIENIA → IMPORTUJ BAZĘ albo podmień plik "
        "Data\\tube_measurements.db przy zamkniętym programie.\n"
        "3. Program tworzy kopię poprzedniej bazy w folderze BACKUP_BAZY.\n\n"
        "Nowość: 300 osobnych profili producent/karta oznaczonych PASUJE DO. "
        "Wpisy nieobsługiwane pozostają widoczne na czerwono i nie można ich załadować.\n"
    )
    (OUTPUT_DIR / "README_PODMIANA_BAZY.txt").write_text(readme, encoding="utf-8")


def main() -> None:
    if not SOURCE_DB.exists():
        raise FileNotFoundError(SOURCE_DB)
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    shutil.copy2(SOURCE_DB, OUTPUT_DB)
    generated_utc = datetime.now(timezone.utc).replace(microsecond=0).isoformat().replace(
        "+00:00", "Z"
    )
    connection = sqlite3.connect(OUTPUT_DB)
    connection.row_factory = sqlite3.Row
    connection.execute("PRAGMA foreign_keys=ON")
    try:
        candidates = select_candidates(connection)
        added: list[dict[str, object]] = []
        connection.execute("BEGIN IMMEDIATE")
        for candidate in candidates:
            added.append(insert_profile(connection, candidate, generated_utc))
        update_catalog_info(connection, generated_utc)
        connection.commit()
        write_outputs(connection, added)
        if connection.execute("PRAGMA integrity_check").fetchone()[0] != "ok":
            raise RuntimeError("SQLite integrity_check failed")
        if connection.execute("PRAGMA foreign_key_check").fetchall():
            raise RuntimeError("SQLite foreign_key_check failed")
        connection.execute("VACUUM")
        connection.execute("ANALYZE")
        connection.commit()
    finally:
        connection.close()
    shutil.copy2(OUTPUT_DB, FULL_DB)
    print(f"Built {OUTPUT_DB}")
    print(f"Added {BATCH_SIZE} manufacturer/card profiles")


if __name__ == "__main__":
    main()
