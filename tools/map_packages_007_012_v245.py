#!/usr/bin/env python3
"""Map every package manifest row to its exact catalog datasheet and profile."""

from __future__ import annotations

import argparse
import re
import sqlite3
import urllib.parse
from collections import defaultdict
from datetime import datetime, timezone
from pathlib import Path


POTENTIAL_SYSTEM = re.compile(r"^[PQRTrpqrt]+[`~°^]*$")


def utc_now() -> str:
    return datetime.now(timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z")


def compact(value: str) -> str:
    return re.sub(r"[^A-Z0-9]", "", (value or "").upper())


def source_path(value: str) -> str:
    parsed = urllib.parse.urlparse(value or "")
    return urllib.parse.unquote(parsed.path).replace("\\", "/").lstrip("/").lower()


def classify_system(value: str) -> str:
    value = (value or "").strip()
    if not value:
        return "UNKNOWN_SYSTEM"
    if POTENTIAL_SYSTEM.fullmatch(value):
        return "POTENTIAL_ELECTRICAL_TUBE"
    return "UNSUPPORTED_DEVICE_FAMILY"


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--cache", type=Path, required=True)
    parser.add_argument("--database", type=Path, required=True)
    args = parser.parse_args()

    cache = sqlite3.connect(args.cache)
    cache.row_factory = sqlite3.Row
    cache.execute("PRAGMA journal_mode=DELETE")
    cache.execute("PRAGMA synchronous=FULL")
    cache.execute("PRAGMA foreign_keys=ON")
    catalog = sqlite3.connect(args.database)
    catalog.row_factory = sqlite3.Row

    datasheets: dict[tuple[str, str, str], list[sqlite3.Row]] = defaultdict(list)
    for row in catalog.execute("SELECT * FROM frank_datasheets"):
        datasheets[(compact(row["normalized_type"]), compact(row["manufacturer"]), source_path(row["data_sheet_url"]))].append(row)
    links: dict[int, list[str]] = defaultdict(list)
    for row in catalog.execute("SELECT datasheet_id,profile_id FROM frank_profile_links"):
        links[row["datasheet_id"]].append(row["profile_id"])

    cache.executescript(
        """
        DROP TABLE IF EXISTS package_profile_summary;
        DROP TABLE IF EXISTS manifest_profile_map;
        CREATE TABLE manifest_profile_map(
            part INTEGER NOT NULL,row_number INTEGER NOT NULL,datasheet_id INTEGER NOT NULL,
            profile_id TEXT NOT NULL,source_sha256 TEXT NOT NULL,model TEXT NOT NULL,
            manufacturer TEXT NOT NULL,system_code TEXT NOT NULL,hardware_class TEXT NOT NULL,
            mapping_method TEXT NOT NULL,mapped_utc TEXT NOT NULL,
            PRIMARY KEY(part,row_number),
            FOREIGN KEY(part,row_number) REFERENCES manifest_rows(part,row_number),
            FOREIGN KEY(source_sha256) REFERENCES cards(source_sha256)
        );
        CREATE INDEX idx_manifest_profile_map_profile ON manifest_profile_map(profile_id);
        CREATE TABLE package_profile_summary(
            profile_id TEXT PRIMARY KEY,manifest_row_count INTEGER NOT NULL,
            source_count INTEGER NOT NULL,system_codes TEXT NOT NULL,initial_class TEXT NOT NULL,
            audit_status TEXT NOT NULL,audit_reason TEXT NOT NULL,
            ready_after_audit INTEGER NOT NULL DEFAULT 0 CHECK(ready_after_audit IN (0,1))
        );
        """
    )

    failures: list[str] = []
    mapped: list[tuple[str, str, str]] = []
    timestamp = utc_now()
    rows = list(cache.execute("SELECT * FROM manifest_rows ORDER BY part,row_number"))
    for row in rows:
        key = (compact(row["normalized_model"]), compact(row["manufacturer"]), source_path(row["source_href"]))
        matches = datasheets.get(key, [])
        candidates = [(item, profile_id) for item in matches for profile_id in links[item["id"]]]
        if len(matches) != 1 or len(candidates) != 1:
            failures.append(
                f"PART_{row['part']:03d}:{row['row_number']} {row['model']} / {row['manufacturer']} "
                f"datasheets={len(matches)} profiles={len(candidates)}"
            )
            continue
        datasheet, profile_id = candidates[0]
        hardware_class = classify_system(row["system_code"])
        cache.execute(
            """
            INSERT INTO manifest_profile_map VALUES(?,?,?,?,?,?,?,?,?,?,?)
            """,
            (
                row["part"],row["row_number"],datasheet["id"],profile_id,row["source_sha256"],
                row["model"],row["manufacturer"],row["system_code"],hardware_class,
                "EXACT_MODEL_MANUFACTURER_SOURCE_PATH",timestamp,
            ),
        )
        mapped.append((profile_id, row["source_sha256"], row["system_code"]))
    if failures:
        cache.rollback()
        raise RuntimeError("Błędy mapowania:\n" + "\n".join(failures[:100]))

    grouped: dict[str, list[tuple[str, str]]] = defaultdict(list)
    for profile_id, source_sha, system_code in mapped:
        grouped[profile_id].append((source_sha, system_code))
    class_counts: dict[str, int] = defaultdict(int)
    for profile_id, values in sorted(grouped.items()):
        classes = {classify_system(system_code) for _, system_code in values}
        if "POTENTIAL_ELECTRICAL_TUBE" in classes:
            initial_class = "POTENTIAL_ELECTRICAL_TUBE"
            status = "PENDING"
            reason = "Wymaga pełnej weryfikacji karty, pinoutu, punktu pracy, limitów i zgodności uTracer 3+."
        elif "UNSUPPORTED_DEVICE_FAMILY" in classes:
            initial_class = "UNSUPPORTED_DEVICE_FAMILY"
            status = "BLOCKED"
            reason = "Rodzina urządzenia nie jest obsługiwana jako profil pomiarowy lampy uTracer 3+."
        else:
            initial_class = "UNKNOWN_SYSTEM"
            status = "BLOCKED"
            reason = "Brak jednoznacznego kodu systemu; bez automatycznej promocji do pomiaru."
        class_counts[initial_class] += 1
        cache.execute(
            "INSERT INTO package_profile_summary VALUES(?,?,?,?,?,?,?,0)",
            (
                profile_id,len(values),len({source for source,_ in values}),
                ",".join(sorted({system for _,system in values})),initial_class,status,reason,
            ),
        )
    cache.commit()

    mapped_count = cache.execute("SELECT COUNT(*) FROM manifest_profile_map").fetchone()[0]
    profile_count = cache.execute("SELECT COUNT(*) FROM package_profile_summary").fetchone()[0]
    if mapped_count != len(rows):
        raise RuntimeError(f"Niepełne mapowanie: {mapped_count}/{len(rows)}")
    print(f"manifest_rows={len(rows)} mapped={mapped_count} profiles={profile_count}")
    for name in ("POTENTIAL_ELECTRICAL_TUBE", "UNKNOWN_SYSTEM", "UNSUPPORTED_DEVICE_FAMILY"):
        print(f"{name}={class_counts.get(name,0)}")
    catalog.close()
    cache.close()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
