#!/usr/bin/env python3
"""Generate stable Unity .meta files for NEON VELOCITY game assets.

Unity normally creates these on first import. Generating them ahead of time keeps
GUIDs stable across machines and lets EditorBuildSettings reference the boot scene
without opening the editor. Existing .meta files are never modified.
"""
from __future__ import annotations

import hashlib
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
ASSET_ROOT = ROOT / "Assets" / "Game"

COMMON_TAIL = """  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""

FOLDER_META = """fileFormatVersion: 2
guid: {guid}
folderAsset: yes
DefaultImporter:
  externalObjects: {{}}
{tail}"""

DEFAULT_META = """fileFormatVersion: 2
guid: {guid}
DefaultImporter:
  externalObjects: {{}}
{tail}"""

SCRIPT_META = """fileFormatVersion: 2
guid: {guid}
MonoImporter:
  externalObjects: {{}}
  serializedVersion: 2
  defaultReferences: []
  executionOrder: 0
  icon: {{instanceID: 0}}
{tail}"""

ASMDEF_META = """fileFormatVersion: 2
guid: {guid}
AssemblyDefinitionImporter:
  externalObjects: {{}}
{tail}"""

AUDIO_META = """fileFormatVersion: 2
guid: {guid}
AudioImporter:
  externalObjects: {{}}
  serializedVersion: 6
  defaultSettings:
    loadType: 0
    sampleRateSetting: 0
    sampleRateOverride: 44100
    compressionFormat: 1
    quality: 0.7
    conversionMode: 0
  platformSettingOverrides: {{}}
  forceToMono: 0
  normalize: 1
  preloadAudioData: 1
  loadInBackground: 0
  ambisonic: 0
  3D: 0
{tail}"""


def guid_for(relative: str) -> str:
    return hashlib.md5(("NeonVelocity:" + relative).encode("utf-8")).hexdigest()


def meta_for(path: Path) -> str:
    relative = path.relative_to(ROOT).as_posix()
    guid = guid_for(relative)
    if path.is_dir():
        return FOLDER_META.format(guid=guid, tail=COMMON_TAIL)
    suffix = path.suffix.lower()
    if suffix == ".cs":
        return SCRIPT_META.format(guid=guid, tail=COMMON_TAIL)
    if suffix == ".asmdef":
        return ASMDEF_META.format(guid=guid, tail=COMMON_TAIL)
    if suffix == ".wav":
        return AUDIO_META.format(guid=guid, tail=COMMON_TAIL)
    return DEFAULT_META.format(guid=guid, tail=COMMON_TAIL)


def main() -> int:
    if not ASSET_ROOT.is_dir():
        print(f"Asset root not found: {ASSET_ROOT}", file=sys.stderr)
        return 1

    created = 0
    skipped = 0
    targets = [ASSET_ROOT] + sorted(
        (p for p in ASSET_ROOT.rglob("*") if "__pycache__" not in p.parts),
        key=lambda p: p.as_posix(),
    )
    for path in targets:
        if path.name.startswith(".") or path.name.endswith(".meta"):
            continue
        meta_path = path.with_name(path.name + ".meta")
        if meta_path.exists():
            skipped += 1
            continue
        meta_path.write_text(meta_for(path), encoding="utf-8")
        created += 1
        print(("DIR " if path.is_dir() else "FILE") + " " + path.relative_to(ROOT).as_posix())

    print(f"Created {created} meta file(s); skipped {skipped} existing.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
