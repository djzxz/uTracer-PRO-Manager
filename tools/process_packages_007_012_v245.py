#!/usr/bin/env python3
"""Resumable verification and OCR cache for uTracer PDF packages 007-012."""

from __future__ import annotations

import argparse
import csv
import hashlib
import io
import os
import sqlite3
import subprocess
import tempfile
import zipfile
from concurrent.futures import ThreadPoolExecutor, as_completed
from datetime import datetime, timezone
from pathlib import Path, PurePosixPath


def utc_now() -> str:
    return datetime.now(timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z")


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def parse_parts(value: str) -> list[int]:
    parts: set[int] = set()
    for item in value.split(","):
        item = item.strip()
        if not item:
            continue
        if "-" in item:
            start, stop = map(int, item.split("-", 1))
            parts.update(range(start, stop + 1))
        else:
            parts.add(int(item))
    if not parts:
        raise argparse.ArgumentTypeError("Brak numerów paczek")
    return sorted(parts)


def open_cache(path: Path) -> sqlite3.Connection:
    path.parent.mkdir(parents=True, exist_ok=True)
    connection = sqlite3.connect(path)
    connection.row_factory = sqlite3.Row
    # The Work workspace is mirrored between executions; a separate WAL file
    # can be observed before its checkpoint.  A single rollback journal keeps
    # each committed cache state self-contained and resumable.
    connection.execute("PRAGMA journal_mode=DELETE")
    connection.execute("PRAGMA synchronous=FULL")
    connection.execute("PRAGMA foreign_keys=ON")
    connection.executescript(
        """
        CREATE TABLE IF NOT EXISTS cache_info(key TEXT PRIMARY KEY,value TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS packages(
            part INTEGER PRIMARY KEY,archive_path TEXT NOT NULL,archive_size INTEGER NOT NULL,
            archive_sha256 TEXT NOT NULL,manifest_rows INTEGER NOT NULL,unique_pdfs INTEGER NOT NULL,
            unique_models INTEGER NOT NULL,index_files INTEGER NOT NULL,pdf_bytes INTEGER NOT NULL,
            verified_utc TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS cards(
            source_sha256 TEXT PRIMARY KEY,part INTEGER NOT NULL,zip_member TEXT NOT NULL,
            file_size INTEGER NOT NULL,pdf_path TEXT NOT NULL,content_sha256 TEXT NOT NULL,
            hash_ok INTEGER NOT NULL CHECK(hash_ok IN (0,1)),
            FOREIGN KEY(part) REFERENCES packages(part)
        );
        CREATE TABLE IF NOT EXISTS manifest_rows(
            part INTEGER NOT NULL,row_number INTEGER NOT NULL,model TEXT NOT NULL,
            normalized_model TEXT NOT NULL,manufacturer TEXT NOT NULL,system_code TEXT NOT NULL,
            classification TEXT NOT NULL,classification_reason TEXT NOT NULL,
            source_html TEXT NOT NULL,source_href TEXT NOT NULL,pdf_relative_path TEXT NOT NULL,
            pdf_filename TEXT NOT NULL,pdf_exists INTEGER NOT NULL,size_bytes INTEGER NOT NULL,
            path_resolution TEXT NOT NULL,source_sha256 TEXT NOT NULL,package_pdf TEXT NOT NULL,
            package_index_html TEXT NOT NULL,package_name TEXT NOT NULL,
            PRIMARY KEY(part,row_number),FOREIGN KEY(source_sha256) REFERENCES cards(source_sha256)
        );
        CREATE INDEX IF NOT EXISTS idx_manifest_source ON manifest_rows(source_sha256);
        CREATE INDEX IF NOT EXISTS idx_manifest_model_maker
            ON manifest_rows(normalized_model,manufacturer);
        CREATE TABLE IF NOT EXISTS card_text(
            source_sha256 TEXT PRIMARY KEY,page_count INTEGER NOT NULL,text_chars INTEGER NOT NULL,
            alnum_chars INTEGER NOT NULL,text_sha256 TEXT NOT NULL,status TEXT NOT NULL,
            error TEXT NOT NULL,processed_utc TEXT NOT NULL,
            FOREIGN KEY(source_sha256) REFERENCES cards(source_sha256)
        );
        CREATE TABLE IF NOT EXISTS page_text(
            source_sha256 TEXT NOT NULL,page_number INTEGER NOT NULL,page_text TEXT NOT NULL,
            text_chars INTEGER NOT NULL,alnum_chars INTEGER NOT NULL,text_sha256 TEXT NOT NULL,
            method TEXT NOT NULL,PRIMARY KEY(source_sha256,page_number),
            FOREIGN KEY(source_sha256) REFERENCES cards(source_sha256)
        );
        """
    )
    connection.execute(
        "INSERT INTO cache_info(key,value) VALUES('schema_version','2') "
        "ON CONFLICT(key) DO UPDATE SET value=excluded.value"
    )
    connection.commit()
    return connection


def read_manifest(archive: zipfile.ZipFile) -> list[dict[str, str]]:
    rows = list(
        csv.DictReader(
            io.StringIO(archive.read("manifest_batch.csv").decode("utf-8-sig")),
            delimiter=";",
        )
    )
    required = {
        "model", "normalized_model", "manufacturer", "system_code", "classification",
        "classification_reason", "source_html", "source_href", "pdf_relative_path",
        "pdf_filename", "pdf_exists", "size_bytes", "path_resolution", "sha256",
        "package_pdf", "package_index_html", "part",
    }
    if not rows or not required.issubset(rows[0]):
        raise RuntimeError("Niepełny manifest_batch.csv")
    return rows


def inventory_package(
    connection: sqlite3.Connection, archive_path: Path, part: int, pdf_root: Path
) -> dict[str, int | str]:
    if not zipfile.is_zipfile(archive_path):
        raise RuntimeError(f"Nieprawidłowa paczka: {archive_path}")
    archive_sha = sha256_file(archive_path)
    with zipfile.ZipFile(archive_path) as archive:
        bad = archive.testzip()
        if bad:
            raise RuntimeError(f"Uszkodzony wpis ZIP: {bad}")
        rows = read_manifest(archive)
        pdf_infos = {
            info.filename: info
            for info in archive.infolist()
            if info.filename.lower().startswith("pdf/") and info.filename.lower().endswith(".pdf")
        }
        expected = {row["sha256"].lower() for row in rows}
        named = {PurePosixPath(name).stem.lower() for name in pdf_infos}
        if expected != named:
            raise RuntimeError(
                f"PART_{part:03d}: manifest/PDF mismatch missing={len(expected-named)} extra={len(named-expected)}"
            )
        connection.execute("DELETE FROM manifest_rows WHERE part=?", (part,))
        connection.execute(
            """
            INSERT INTO packages(part,archive_path,archive_size,archive_sha256,manifest_rows,
              unique_pdfs,unique_models,index_files,pdf_bytes,verified_utc)
            VALUES(?,?,?,?,?,?,?,?,?,?)
            ON CONFLICT(part) DO UPDATE SET archive_path=excluded.archive_path,
              archive_size=excluded.archive_size,archive_sha256=excluded.archive_sha256,
              manifest_rows=excluded.manifest_rows,unique_pdfs=excluded.unique_pdfs,
              unique_models=excluded.unique_models,index_files=excluded.index_files,
              pdf_bytes=excluded.pdf_bytes,verified_utc=excluded.verified_utc
            """,
            (
                part, str(archive_path.resolve()), archive_path.stat().st_size, archive_sha,
                len(rows), len(pdf_infos), len({row["normalized_model"] for row in rows}),
                sum(i.filename.lower().startswith("index/") and i.filename.lower().endswith(".html") for i in archive.infolist()),
                sum(i.file_size for i in pdf_infos.values()), utc_now(),
            ),
        )
        extracted = 0
        for member_name, info in sorted(pdf_infos.items()):
            expected_sha = PurePosixPath(member_name).stem.lower()
            destination = pdf_root / expected_sha[:2] / f"{expected_sha}.pdf"
            destination.parent.mkdir(parents=True, exist_ok=True)
            actual_sha = ""
            if destination.is_file() and destination.stat().st_size == info.file_size:
                actual_sha = sha256_file(destination)
            if actual_sha != expected_sha:
                with archive.open(info) as source, tempfile.NamedTemporaryFile(
                    dir=destination.parent, prefix=f".{expected_sha}.", suffix=".tmp", delete=False
                ) as temporary:
                    temporary_path = Path(temporary.name)
                    digest = hashlib.sha256()
                    for chunk in iter(lambda: source.read(1024 * 1024), b""):
                        digest.update(chunk)
                        temporary.write(chunk)
                actual_sha = digest.hexdigest()
                if actual_sha != expected_sha:
                    temporary_path.unlink(missing_ok=True)
                    raise RuntimeError(f"PART_{part:03d}: SHA mismatch {member_name}")
                os.replace(temporary_path, destination)
                extracted += 1
            previous = connection.execute(
                "SELECT part FROM cards WHERE source_sha256=?", (expected_sha,)
            ).fetchone()
            if previous is not None and previous["part"] != part:
                raise RuntimeError(f"PDF {expected_sha} występuje też w PART_{previous['part']:03d}")
            connection.execute(
                """
                INSERT INTO cards(source_sha256,part,zip_member,file_size,pdf_path,content_sha256,hash_ok)
                VALUES(?,?,?,?,?,?,1) ON CONFLICT(source_sha256) DO UPDATE SET
                  part=excluded.part,zip_member=excluded.zip_member,file_size=excluded.file_size,
                  pdf_path=excluded.pdf_path,content_sha256=excluded.content_sha256,hash_ok=1
                """,
                (expected_sha, part, member_name, info.file_size, str(destination.resolve()), actual_sha),
            )
        package_name = f"uTracer_PDF_BATCH_PART_{part:03d}"
        for row_number, row in enumerate(rows, 1):
            connection.execute(
                """
                INSERT INTO manifest_rows(part,row_number,model,normalized_model,manufacturer,
                  system_code,classification,classification_reason,source_html,source_href,
                  pdf_relative_path,pdf_filename,pdf_exists,size_bytes,path_resolution,
                  source_sha256,package_pdf,package_index_html,package_name)
                VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)
                """,
                (
                    part,row_number,row["model"],row["normalized_model"],row["manufacturer"],
                    row["system_code"],row["classification"],row["classification_reason"],
                    row["source_html"],row["source_href"],row["pdf_relative_path"],
                    row["pdf_filename"],int(row["pdf_exists"]),int(row["size_bytes"]),
                    row["path_resolution"],row["sha256"].lower(),row["package_pdf"],
                    row["package_index_html"],package_name,
                ),
            )
        connection.commit()
    return {"part": part, "rows": len(rows), "pdfs": len(pdf_infos), "extracted": extracted, "sha256": archive_sha}


def normalize_pages(text: str) -> list[str]:
    pages = text.replace("\r\n", "\n").replace("\r", "\n").split("\f")
    while pages and not pages[-1].strip():
        pages.pop()
    return [page.rstrip() + "\n" if page else "" for page in pages]


def pdftotext_card(card: tuple[str, str]) -> tuple[str, list[str], str]:
    source_sha, pdf_path = card
    completed = subprocess.run(
        ["pdftotext", "-layout", "-enc", "UTF-8", pdf_path, "-"],
        stdout=subprocess.PIPE, stderr=subprocess.PIPE, check=False,
    )
    text = completed.stdout.decode("utf-8", errors="replace")
    error = completed.stderr.decode("utf-8", errors="replace").strip()
    if completed.returncode:
        error = f"returncode={completed.returncode}; {error}"
    return source_sha, normalize_pages(text), error


def ocr_card(card: tuple[str, str], dpi: int, psm: int) -> tuple[str, list[str], str]:
    import fitz

    source_sha, pdf_path = card
    pages: list[str] = []
    errors: list[str] = []
    try:
        with fitz.open(pdf_path) as document:
            for page_index, page in enumerate(document):
                try:
                    scale = dpi / 72.0
                    pixmap = page.get_pixmap(
                        matrix=fitz.Matrix(scale, scale), colorspace=fitz.csGRAY, alpha=False
                    )
                    completed = subprocess.run(
                        ["tesseract", "stdin", "stdout", "-l", "eng", "--dpi", str(dpi), "--psm", str(psm)],
                        input=pixmap.tobytes("png"), stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                        check=False, timeout=180, env={**os.environ, "OMP_THREAD_LIMIT": "1"},
                    )
                    text = completed.stdout.decode("utf-8", errors="replace")
                    pages.append(text.rstrip() + "\n" if text else "")
                    if completed.returncode:
                        errors.append(f"page={page_index+1},returncode={completed.returncode}")
                except subprocess.TimeoutExpired:
                    pages.append("")
                    errors.append(f"page={page_index+1}:timeout")
                except Exception as exc:
                    pages.append("")
                    errors.append(f"page={page_index+1}:{type(exc).__name__}:{exc}")
    except Exception as exc:
        return source_sha, [], f"{type(exc).__name__}:{exc}"
    return source_sha, pages, " | ".join(errors)


def save_text(
    connection: sqlite3.Connection, source_sha: str, pages: list[str], error: str,
    method: str, ocr: bool,
) -> str:
    combined = "".join(pages)
    alnum = sum(character.isalnum() for character in combined)
    if error and not combined.strip():
        status = "OCR_ERROR" if ocr else "ERROR"
    elif alnum < 80:
        status = "OCR_LOW" if ocr else "LOW_TEXT"
    else:
        status = "OCR_OK" if ocr else "TEXT_OK"
    connection.execute("DELETE FROM page_text WHERE source_sha256=?", (source_sha,))
    for page_number, page in enumerate(pages, 1):
        page_alnum = sum(character.isalnum() for character in page)
        connection.execute(
            "INSERT INTO page_text VALUES(?,?,?,?,?,?,?)",
            (source_sha,page_number,page,len(page),page_alnum,hashlib.sha256(page.encode()).hexdigest(),method),
        )
    values = (
        len(pages),len(combined),alnum,hashlib.sha256(combined.encode()).hexdigest(),
        status,error,utc_now(),source_sha,
    )
    connection.execute(
        """
        INSERT INTO card_text(source_sha256,page_count,text_chars,alnum_chars,text_sha256,
          status,error,processed_utc) VALUES(?,?,?,?,?,?,?,?)
        ON CONFLICT(source_sha256) DO UPDATE SET page_count=excluded.page_count,
          text_chars=excluded.text_chars,alnum_chars=excluded.alnum_chars,
          text_sha256=excluded.text_sha256,status=excluded.status,error=excluded.error,
          processed_utc=excluded.processed_utc
        """,
        (source_sha, *values[:-1]),
    )
    return status


def process_text(connection: sqlite3.Connection, workers: int) -> None:
    cards = [tuple(row) for row in connection.execute(
        """SELECT c.source_sha256,c.pdf_path FROM cards c LEFT JOIN card_text t
           ON t.source_sha256=c.source_sha256 WHERE t.source_sha256 IS NULL ORDER BY c.part,c.source_sha256"""
    )]
    statuses: dict[str, int] = {}
    with ThreadPoolExecutor(max_workers=max(1, workers)) as executor:
        futures = [executor.submit(pdftotext_card, card) for card in cards]
        for index, future in enumerate(as_completed(futures), 1):
            source_sha, pages, error = future.result()
            status = save_text(connection, source_sha, pages, error, "PDFTOTEXT_LAYOUT", False)
            statuses[status] = statuses.get(status, 0) + 1
            if index % 25 == 0 or index == len(cards):
                connection.commit()
                print(f"TEXT {index}/{len(cards)} OK={statuses.get('TEXT_OK',0)} LOW={statuses.get('LOW_TEXT',0)} ERROR={statuses.get('ERROR',0)}", flush=True)


def process_ocr(connection: sqlite3.Connection, workers: int, dpi: int, psm: int) -> None:
    cards = [tuple(row) for row in connection.execute(
        """SELECT c.source_sha256,c.pdf_path FROM cards c JOIN card_text t USING(source_sha256)
           WHERE t.status IN ('LOW_TEXT','ERROR','OCR_LOW','OCR_ERROR') ORDER BY c.part,c.source_sha256"""
    )]
    statuses: dict[str, int] = {}
    method = f"TESSERACT_ENG_{dpi}DPI_PSM{psm}"
    with ThreadPoolExecutor(max_workers=max(1, workers)) as executor:
        futures = [executor.submit(ocr_card, card, dpi, psm) for card in cards]
        for index, future in enumerate(as_completed(futures), 1):
            source_sha, pages, error = future.result()
            status = save_text(connection, source_sha, pages, error, method, True)
            statuses[status] = statuses.get(status, 0) + 1
            if index % 10 == 0 or index == len(cards):
                connection.commit()
                print(f"OCR {index}/{len(cards)} OK={statuses.get('OCR_OK',0)} LOW={statuses.get('OCR_LOW',0)} ERROR={statuses.get('OCR_ERROR',0)}", flush=True)


def print_stats(connection: sqlite3.Connection) -> None:
    print("packages", connection.execute("SELECT COUNT(*) FROM packages").fetchone()[0])
    print("manifest_rows", connection.execute("SELECT COUNT(*) FROM manifest_rows").fetchone()[0])
    print("cards", connection.execute("SELECT COUNT(*) FROM cards").fetchone()[0])
    print("statuses", dict(connection.execute("SELECT status,COUNT(*) FROM card_text GROUP BY status")))


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--packages-dir", type=Path, required=True)
    parser.add_argument("--work-dir", type=Path, required=True)
    parser.add_argument("--parts", type=parse_parts, default=parse_parts("7-12"))
    parser.add_argument("--stage", choices=("inventory", "text", "ocr", "stats", "all"), default="all")
    parser.add_argument("--workers", type=int, default=8)
    parser.add_argument("--dpi", type=int, default=300)
    parser.add_argument("--psm", type=int, default=6)
    args = parser.parse_args()
    cache = args.work_dir / "packages_007_012.sqlite"
    pdf_root = args.work_dir / "pdf"
    connection = open_cache(cache)
    if args.stage in ("inventory", "all"):
        for part in args.parts:
            archive = args.packages_dir / f"uTracer_PDF_BATCH_PART_{part:03d}.zip"
            if not archive.is_file():
                print(f"SKIP PART_{part:03d}: brak pliku", flush=True)
                continue
            print(inventory_package(connection, archive, part, pdf_root), flush=True)
    if args.stage in ("text", "all"):
        process_text(connection, args.workers)
    if args.stage in ("ocr", "all"):
        process_ocr(connection, args.workers, args.dpi, args.psm)
    print_stats(connection)
    connection.close()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
