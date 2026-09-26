#!/usr/bin/env python3
"""Build an AltStore/SideStore source JSON for the Jarvis Flutter IPA."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

SOURCE_IDENTIFIER = "com.example.jarvis_mobile.source"
BUNDLE_IDENTIFIER = "com.example.jarvis_mobile"
APP_NAME = "Jarvis Mobile"
SOURCE_NAME = "Jarvis"
DEVELOPER_NAME = "Jarvis"
SUBTITLE = "Self-hosted personal assistant"
CATEGORY = "utilities"
TINT_COLOR = "4F46E5"
MIN_OS_VERSION = "15.0"
DEFAULT_DESCRIPTION = (
    "Jarvis is a self-hosted personal assistant with chat, tasks, memory, "
    "voice, and owner-scoped integrations."
)
APP_PERMISSIONS: dict[str, Any] = {
    "entitlements": ["aps-environment"],
    "privacy": {
        "NSMicrophoneUsageDescription": (
            "Jarvis uses your microphone for voice conversations."
        )
    },
}

_PUBSPEC_VERSION = re.compile(
    r"^version:\s*([0-9]+(?:\.[0-9]+){0,3})(?:\+([0-9]+))?\s*$",
    re.MULTILINE,
)


def parse_pubspec_version(pubspec_path: Path) -> tuple[str, str]:
    text = pubspec_path.read_text(encoding="utf-8")
    match = _PUBSPEC_VERSION.search(text)
    if match is None:
        raise ValueError(f"No version: field found in {pubspec_path}")
    name = match.group(1)
    build = match.group(2) or "1"
    return name, build


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def utc_now_iso() -> str:
    return datetime.now(timezone.utc).replace(microsecond=0).isoformat().replace(
        "+00:00", "Z"
    )


def load_source(path: Path | None) -> dict[str, Any] | None:
    if path is None or not path.is_file():
        return None
    return json.loads(path.read_text(encoding="utf-8"))


def version_key(entry: dict[str, Any]) -> tuple[str, str]:
    return str(entry.get("version", "")), str(entry.get("buildVersion", ""))


def merge_versions(
    previous: list[dict[str, Any]],
    new_entry: dict[str, Any],
) -> list[dict[str, Any]]:
    key = version_key(new_entry)
    remaining = [entry for entry in previous if version_key(entry) != key]
    return [new_entry, *remaining]


def empty_source(
    *,
    icon_url: str,
    website: str,
    description: str,
) -> dict[str, Any]:
    return {
        "name": SOURCE_NAME,
        "subtitle": SUBTITLE,
        "description": description,
        "iconURL": icon_url,
        "website": website,
        "tintColor": TINT_COLOR,
        "identifier": SOURCE_IDENTIFIER,
        "apps": [
            {
                "name": APP_NAME,
                "bundleIdentifier": BUNDLE_IDENTIFIER,
                "developerName": DEVELOPER_NAME,
                "subtitle": SUBTITLE,
                "localizedDescription": description,
                "iconURL": icon_url,
                "tintColor": TINT_COLOR,
                "category": CATEGORY,
                "versions": [],
                "appPermissions": APP_PERMISSIONS,
            }
        ],
    }


def build_source(
    *,
    ipa_path: Path,
    download_url: str,
    icon_url: str,
    website: str,
    version: str,
    build_version: str,
    date: str,
    changelog: str,
    previous: dict[str, Any] | None,
    description: str = DEFAULT_DESCRIPTION,
) -> dict[str, Any]:
    source = previous if previous else empty_source(
        icon_url=icon_url,
        website=website,
        description=description,
    )
    source["name"] = SOURCE_NAME
    source["subtitle"] = SUBTITLE
    source["description"] = description
    source["iconURL"] = icon_url
    source["website"] = website
    source["tintColor"] = TINT_COLOR
    source["identifier"] = SOURCE_IDENTIFIER

    apps = source.setdefault("apps", [])
    app = next(
        (
            candidate
            for candidate in apps
            if candidate.get("bundleIdentifier") == BUNDLE_IDENTIFIER
        ),
        None,
    )
    if app is None:
        app = empty_source(
            icon_url=icon_url,
            website=website,
            description=description,
        )["apps"][0]
        apps.insert(0, app)

    app["name"] = APP_NAME
    app["bundleIdentifier"] = BUNDLE_IDENTIFIER
    app["developerName"] = DEVELOPER_NAME
    app["subtitle"] = SUBTITLE
    app["localizedDescription"] = description
    app["iconURL"] = icon_url
    app["tintColor"] = TINT_COLOR
    app["category"] = CATEGORY
    app["appPermissions"] = APP_PERMISSIONS

    new_entry = {
        "version": version,
        "buildVersion": str(build_version),
        "date": date,
        "localizedDescription": changelog,
        "downloadURL": download_url,
        "size": ipa_path.stat().st_size,
        "sha256": sha256_file(ipa_path),
        "minOSVersion": MIN_OS_VERSION,
    }
    app["versions"] = merge_versions(list(app.get("versions") or []), new_entry)
    return source


def write_source(path: Path, source: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(source, indent=2) + "\n", encoding="utf-8")


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--ipa", type=Path, help="Path to the signed IPA")
    parser.add_argument("--download-url", help="Public HTTPS URL for the IPA")
    parser.add_argument("--icon-url", help="Public HTTPS URL for the source icon")
    parser.add_argument("--website", help="Source website URL")
    parser.add_argument("--version", help="Marketing version, e.g. 1.0.0")
    parser.add_argument("--build-version", help="CFBundleVersion / build number")
    parser.add_argument("--date", help="ISO-8601 version date (UTC)")
    parser.add_argument("--changelog", help="Version localizedDescription")
    parser.add_argument("--description", default=DEFAULT_DESCRIPTION)
    parser.add_argument("--previous-source", type=Path)
    parser.add_argument("--output", type=Path, default=Path("source.json"))
    parser.add_argument(
        "--pubspec",
        type=Path,
        default=Path("apps/mobile/pubspec.yaml"),
        help="Flutter pubspec used when version/build are omitted",
    )
    parser.add_argument(
        "--print-pubspec-version",
        action="store_true",
        help="Print the pubspec marketing version and exit",
    )
    parser.add_argument(
        "--print-pubspec-build",
        action="store_true",
        help="Print the pubspec build number and exit",
    )
    return parser


def main(argv: list[str] | None = None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)

    if args.print_pubspec_version or args.print_pubspec_build:
        version, build = parse_pubspec_version(args.pubspec)
        print(version if args.print_pubspec_version else build)
        return 0

    missing = [
        name
        for name, value in (
            ("--ipa", args.ipa),
            ("--download-url", args.download_url),
            ("--icon-url", args.icon_url),
            ("--website", args.website),
        )
        if not value
    ]
    if missing:
        parser.error(f"the following arguments are required: {', '.join(missing)}")

    version = args.version
    build_version = args.build_version
    if version is None or build_version is None:
        pubspec_version, pubspec_build = parse_pubspec_version(args.pubspec)
        version = version or pubspec_version
        build_version = build_version or pubspec_build

    ipa_path = args.ipa.expanduser().resolve()
    if not ipa_path.is_file():
        parser.error(f"IPA not found: {ipa_path}")

    source = build_source(
        ipa_path=ipa_path,
        download_url=args.download_url,
        icon_url=args.icon_url,
        website=args.website,
        version=version,
        build_version=str(build_version),
        date=args.date or utc_now_iso(),
        changelog=args.changelog or f"Jarvis {version}",
        previous=load_source(args.previous_source),
        description=args.description,
    )
    write_source(args.output, source)
    return 0


if __name__ == "__main__":
    sys.exit(main())
