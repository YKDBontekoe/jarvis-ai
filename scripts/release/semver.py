#!/usr/bin/env python3
"""Semantic versioning helpers for Jarvis release tags and mobile builds."""

from __future__ import annotations

import argparse
import os
import re
import sys
from pathlib import Path

# SemVer 2.0.0 core: MAJOR.MINOR.PATCH (no leading-zero segments).
SEMVER_CORE_RE = re.compile(
    r"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$"
)
# Release git tags: v1.2.3
SEMVER_TAG_RE = re.compile(
    r"^v(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$"
)

_PUBSPEC_VERSION = re.compile(
    r"^version:\s*([0-9]+(?:\.[0-9]+){0,3})(?:\+([0-9]+))?\s*$",
    re.MULTILINE,
)


class SemVerError(ValueError):
    """Raised when a version string is not valid SemVer for this repository."""


def is_valid_semver(version: str) -> bool:
    return SEMVER_CORE_RE.match(version) is not None


def parse_semver(version: str) -> tuple[int, int, int]:
    match = SEMVER_CORE_RE.match(version)
    if match is None:
        raise SemVerError(
            f"Version '{version}' is not valid SemVer (expected MAJOR.MINOR.PATCH)."
        )
    return int(match.group(1)), int(match.group(2)), int(match.group(3))


def format_semver(major: int, minor: int, patch: int) -> str:
    return f"{major}.{minor}.{patch}"


def parse_semver_tag(tag_name: str) -> str:
    match = SEMVER_TAG_RE.match(tag_name)
    if match is None:
        raise SemVerError(
            f"Git tag '{tag_name}' must match vMAJOR.MINOR.PATCH "
            "(for example v1.2.3)."
        )
    return format_semver(int(match.group(1)), int(match.group(2)), int(match.group(3)))


def parse_pubspec_version(pubspec_path: Path) -> tuple[str, str]:
    text = pubspec_path.read_text(encoding="utf-8")
    match = _PUBSPEC_VERSION.search(text)
    if match is None:
        raise SemVerError(f"No version: field found in {pubspec_path}")
    return match.group(1), match.group(2) or "1"


def ios_build_number(version: str, *, run_number: int | None = None) -> str:
    """Return a monotonic iOS CFBundleVersion for a SemVer release."""
    major, minor, patch = parse_semver(version)
    encoded = major * 1_000_000 + minor * 1_000 + patch
    if run_number is not None and run_number > 0:
        return str(encoded * 10_000 + (run_number % 10_000))
    return str(encoded)


def resolve_release(
    *,
    ref_type: str,
    ref_name: str,
    input_version: str | None,
    pubspec_path: Path,
    run_number: int | None = None,
) -> tuple[str, str, str]:
    """
    Resolve marketing version, git tag name, and iOS build number.

    Returns (version, tag_name, build_number).
    """
    if ref_type == "tag":
        version = parse_semver_tag(ref_name)
        tag_name = ref_name
    else:
        pubspec_version, pubspec_build = parse_pubspec_version(pubspec_path)
        version = (input_version or pubspec_version).strip()
        parse_semver(version)
        tag_name = f"v{version}"
        if input_version is None and not is_valid_semver(pubspec_version):
            raise SemVerError(
                f"pubspec version '{pubspec_version}' is not SemVer; "
                "set version: MAJOR.MINOR.PATCH+N or pass workflow input version."
            )
        if input_version is None:
            return version, tag_name, pubspec_build

    build = ios_build_number(version, run_number=run_number)
    return version, tag_name, build


def write_pubspec_version(pubspec_path: Path, version: str, build: str) -> None:
    parse_semver(version)
    text = pubspec_path.read_text(encoding="utf-8")
    updated, count = _PUBSPEC_VERSION.subn(
        f"version: {version}+{build}\n",
        text,
        count=1,
    )
    if count != 1:
        raise SemVerError(f"Could not update version in {pubspec_path}")
    pubspec_path.write_text(updated, encoding="utf-8")


def bump_semver(version: str, bump: str) -> str:
    major, minor, patch = parse_semver(version)
    if bump == "major":
        return format_semver(major + 1, 0, 0)
    if bump == "minor":
        return format_semver(major, minor + 1, 0)
    if bump == "patch":
        return format_semver(major, minor, patch + 1)
    raise SemVerError(f"Unknown bump '{bump}' (expected major, minor, or patch).")


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--ref-type", default="")
    parser.add_argument("--ref-name", default="")
    parser.add_argument("--input-version", default="")
    parser.add_argument(
        "--pubspec",
        type=Path,
        default=Path("apps/mobile/pubspec.yaml"),
    )
    parser.add_argument("--run-number", type=int, default=0)
    parser.add_argument("--validate", metavar="VERSION")
    parser.add_argument("--parse-tag", metavar="TAG")
    parser.add_argument("--bump", choices=["major", "minor", "patch"])
    parser.add_argument("--from-version", default="0.1.0")
    parser.add_argument("--write-pubspec", action="store_true")
    parser.add_argument("--version", default="")
    parser.add_argument("--build", default="")
    parser.add_argument(
        "--github-output",
        action="store_true",
        help="Append version, tag, and build lines to GITHUB_OUTPUT",
    )
    return parser


def main(argv: list[str] | None = None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)

    try:
        if args.validate:
            parse_semver(args.validate)
            print(args.validate)
            return 0
        if args.parse_tag:
            print(parse_semver_tag(args.parse_tag))
            return 0
        if args.bump:
            print(bump_semver(args.from_version, args.bump))
            return 0
        if args.write_pubspec:
            if not args.version or not args.build:
                parser.error("--write-pubspec requires --version and --build")
            write_pubspec_version(args.pubspec, args.version, args.build)
            return 0

        version, tag_name, build = resolve_release(
            ref_type=args.ref_type,
            ref_name=args.ref_name,
            input_version=args.input_version or None,
            pubspec_path=args.pubspec,
            run_number=args.run_number or None,
        )
    except SemVerError as exc:
        print(exc, file=sys.stderr)
        return 1

    lines = (
        f"version={version}\n",
        f"tag={tag_name}\n",
        f"build={build}\n",
    )
    if args.github_output:
        output_path = os.environ.get("GITHUB_OUTPUT")
        if not output_path:
            print("GITHUB_OUTPUT is not set.", file=sys.stderr)
            return 1
        with open(output_path, "a", encoding="utf-8") as handle:
            handle.writelines(lines)
    else:
        for line in lines:
            print(line, end="")
    return 0


if __name__ == "__main__":
    sys.exit(main())
