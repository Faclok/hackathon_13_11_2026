#!/usr/bin/env python3
"""Validate and resumably import the two Bashperevod fragment CSVs via expert API.

Set EXPERT_API_USERNAME and EXPERT_API_PASSWORD, or EXPERT_API_TOKEN (optionally
EXPERT_API_REFRESH_TOKEN). The script does not print or persist credentials.
Run without --execute to validate only.
"""

from __future__ import annotations

import argparse
import csv
import json
import os
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path


def rows(path: Path):
    with path.open(encoding="utf-8-sig", newline="") as source:
        reader = csv.DictReader(source)
        names = reader.fieldnames or []
        required = {"fragment_id", "text_ba", "source_dataset"}
        missing = required.difference(names)
        if missing:
            raise ValueError(f"{path.name}: отсутствуют колонки {', '.join(sorted(missing))}")
        verified = "text_ru" in names
        result = []
        seen = set()
        for line, data in enumerate(reader, 2):
            if None in data:
                raise ValueError(f"{path.name}:{line}: число значений не совпадает с заголовком")
            fragment_id = data["fragment_id"]
            text_ba = data["text_ba"]
            translation = data["text_ru"] if verified else None
            dataset = data["source_dataset"]
            if not fragment_id or not text_ba or not dataset or verified and not translation:
                raise ValueError(f"{path.name}:{line}: обязательное значение пусто")
            if fragment_id in seen:
                raise ValueError(f"{path.name}:{line}: повторяется fragment_id {fragment_id}")
            seen.add(fragment_id)
            if len(fragment_id) > 200 or len(dataset) > 200 or len(text_ba) > 10000 or len(translation or "") > 10000:
                raise ValueError(f"{path.name}:{line}: превышена длина поля")
            if any("\ufffd" in value for value in (fragment_id, text_ba, dataset, translation or "")):
                raise ValueError(f"{path.name}:{line}: повреждённая кодировка")
            chapter = json.dumps({key: value for key, value in data.items()
                                  if key not in {"text_ba", "text_ru", "fragment_id", "attribution"}},
                                 ensure_ascii=False, separators=(",", ":"))
            source_ref = {"document_id": dataset, "title": data.get("attribution") or dataset, "chapter": chapter}
            if len(json.dumps(source_ref, ensure_ascii=False)) > 9000:
                raise ValueError(f"{path.name}:{line}: сведения об источнике слишком длинные")
            result.append((line, {
                "fragment_id": fragment_id, "text_ba": text_ba, "lang_pair": "ba-ru",
                "translation": translation, "expertVerified": verified, "source": source_ref,
            }))
        return result, verified


def request_json(url: str, payload: dict, token: str | None = None):
    body = json.dumps(payload, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
    headers = {"Content-Type": "application/json"}
    if token:
        headers["Authorization"] = f"Bearer {token}"
    request = urllib.request.Request(url, data=body, headers=headers, method="POST")
    with urllib.request.urlopen(request, timeout=30) as response:
        return json.load(response)


class Session:
    def __init__(self, base_url: str):
        self.base_url = base_url.rstrip("/")
        self.token = os.getenv("EXPERT_API_TOKEN")
        self.refresh = os.getenv("EXPERT_API_REFRESH_TOKEN")
        self.username = os.getenv("EXPERT_API_USERNAME")
        self.password = os.getenv("EXPERT_API_PASSWORD")
        if not self.token:
            self.sign_in()

    def sign_in(self):
        if not self.username or not self.password:
            raise ValueError("Укажите EXPERT_API_USERNAME и EXPERT_API_PASSWORD либо EXPERT_API_TOKEN")
        data = request_json(self.base_url + "/auth/sign-in", {"username": self.username, "password": self.password})
        self.token = data["accessToken"]
        self.refresh = data.get("refreshToken")

    def renew(self):
        if self.refresh:
            try:
                data = request_json(self.base_url + "/auth/refresh", {"refreshToken": self.refresh})
                self.token = data["accessToken"]
                self.refresh = data.get("refreshToken")
                return
            except urllib.error.HTTPError:
                pass
        self.sign_in()


def send(url: str, session: Session, payload: dict):
    for attempt in range(5):
        try:
            return request_json(url, payload, session.token)
        except urllib.error.HTTPError as error:
            if error.code == 401:
                session.renew()
                continue
            if error.code not in {429, 502, 503, 504} or attempt == 4:
                detail = error.read(1000).decode("utf-8", "replace")
                raise RuntimeError(f"HTTP {error.code}: {detail}") from error
        except (TimeoutError, urllib.error.URLError):
            if attempt == 4:
                raise
        time.sleep(min(2 ** attempt, 15))
    raise AssertionError("unreachable")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("files", type=Path, nargs="+", help="CSV paths")
    parser.add_argument("--base-url", default="https://expert.bashperevod.ru")
    parser.add_argument("--execute", action="store_true", help="write to the API")
    args = parser.parse_args()
    all_rows = []
    seen_ids = set()
    for path in args.files:
        parsed, verified = rows(path)
        for line, payload in parsed:
            if payload["fragment_id"] in seen_ids:
                raise ValueError(f"{path.name}:{line}: fragment_id повторяется между файлами")
            seen_ids.add(payload["fragment_id"])
        print(f"{path.name}: {len(parsed)} записей, {'проверенный перевод' if verified else 'перевод неизвестен'}", flush=True)
        all_rows.extend((path.name, line, payload) for line, payload in parsed)
    if not args.execute:
        print(f"Проверено {len(all_rows)} записей. Для отправки добавьте --execute и учётные данные экспертного API.")
        return
    session = Session(args.base_url)
    url = args.base_url.rstrip("/") + "/api/expert/imports"
    for count, (file_name, line, payload) in enumerate(all_rows, 1):
        try:
            receipt = send(url, session, payload)
        except Exception as error:
            print(f"Остановлено на {file_name}:{line}, ID {payload['fragment_id']}: {error}", file=sys.stderr)
            print("Повторите ту же команду: уже сохранённые записи возвращаются без создания копий.", file=sys.stderr)
            raise SystemExit(1) from error
        expected = "expert_verified" if payload["expertVerified"] else "untranslated"
        if receipt.get("status") != expected:
            raise RuntimeError(f"{file_name}:{line}: неожиданный статус {receipt.get('status')}")
        if count % 100 == 0 or count == len(all_rows):
            print(f"Обработано {count}/{len(all_rows)}", flush=True)


if __name__ == "__main__":
    main()
