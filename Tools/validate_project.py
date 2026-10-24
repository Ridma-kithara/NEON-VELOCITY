#!/usr/bin/env python3
"""Static integrity checks for the NEON VELOCITY project.

This does not replace opening the project in Unity; it catches packaging mistakes
that can be verified without an editor licence: missing/duplicate meta GUIDs,
broken build-scene wiring, wrong branding, invalid audio and placeholder markers.
"""
from __future__ import annotations

import re
import sys
import wave
from collections import defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
GAME_ASSETS = ROOT / "Assets" / "Game"
AUDIO_DIR = GAME_ASSETS / "Resources" / "Audio"
MAIN_SCENE = GAME_ASSETS / "Scenes" / "Main.unity"
BUILD_SETTINGS = ROOT / "ProjectSettings" / "EditorBuildSettings.asset"
PLAYER_SETTINGS = ROOT / "ProjectSettings" / "ProjectSettings.asset"

errors: list[str] = []
checks = 0


def check(condition: bool, message: str) -> None:
    global checks
    checks += 1
    if not condition:
        errors.append(message)


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8", errors="replace")


def meta_guid(path: Path) -> str:
    match = re.search(r"^guid: ([0-9a-f]{32})$", read(path), re.MULTILINE)
    return match.group(1) if match else ""


def validate_meta_coverage() -> None:
    guids: dict[str, list[str]] = defaultdict(list)
    asset_paths = [GAME_ASSETS] + [
        p for p in GAME_ASSETS.rglob("*")
        if "__pycache__" not in p.parts and not p.name.endswith(".meta")
    ]
    for path in asset_paths:
        meta = path.with_name(path.name + ".meta")
        check(meta.exists(), f"missing meta for {path.relative_to(ROOT)}")
        if not meta.exists():
            continue
        guid = meta_guid(meta)
        check(bool(guid), f"invalid or missing GUID in {meta.relative_to(ROOT)}")
        if guid:
            guids[guid].append(path.relative_to(ROOT).as_posix())

    duplicate = {guid: paths for guid, paths in guids.items() if len(paths) > 1}
    check(not duplicate, f"duplicate meta GUIDs: {duplicate}")

    for meta in GAME_ASSETS.rglob("*.meta"):
        source = meta.with_name(meta.name[:-5])
        check(source.exists(), f"orphan meta file {meta.relative_to(ROOT)}")

    print(f"[meta] {len(asset_paths)} assets, {len(guids)} unique GUIDs")


def validate_build_scene() -> None:
    scene_meta = MAIN_SCENE.with_name(MAIN_SCENE.name + ".meta")
    check(MAIN_SCENE.exists(), "Main scene missing")
    check(scene_meta.exists(), "Main scene meta missing")
    if not (MAIN_SCENE.exists() and scene_meta.exists()):
        return
    scene_guid = meta_guid(scene_meta)
    settings = read(BUILD_SETTINGS)
    check("Assets/Game/Scenes/Main.unity" in settings, "EditorBuildSettings does not reference Main scene")
    check(scene_guid in settings, "EditorBuildSettings scene GUID does not match Main scene meta")
    check(settings.count("path: Assets/Game/Scenes/") == 1, "unexpected number of build scenes")
    check("m_Roots: []" in read(MAIN_SCENE), "Main scene should be an intentionally empty boot scene")
    print(f"[scene] build GUID {scene_guid}")


def validate_branding() -> None:
    player = read(PLAYER_SETTINGS)
    required = {
        "companyName: RK VANTAGE GAMES": "company name",
        "productName: NEON VELOCITY": "product name",
        "bundleVersion: 1.0.0": "bundle version",
    }
    for needle, label in required.items():
        check(needle in player, f"ProjectSettings missing {label}")
    check(player.count("games.rkvantage.neonvelocity") >= 3,
          "bundle identifier must be set for Android, Standalone and iPhone")
    check("defaultScreenOrientation: 3" in player, "default orientation should be landscape")
    check("activeInputHandler: 1" in player, "active input handling should be Input System")
    check("m_ActiveColorSpace: 1" in player, "colour space should be linear")
    check("AndroidMinSdkVersion: 26" in player, "Android min SDK should be 26")
    check("AndroidTargetArchitectures: 2" in player, "Android target should be ARM64")
    check("ForceInternetPermission: 1" in player, "Android internet permission missing")
    check("Android: 1" in player, "Android scripting backend should be IL2CPP")
    print("[branding] RK VANTAGE GAMES / NEON VELOCITY / games.rkvantage.neonvelocity")


def validate_audio() -> None:
    clips = sorted(AUDIO_DIR.glob("*.wav"))
    check(len(clips) == 27, f"expected 27 audio clips, found {len(clips)}")
    total_seconds = 0.0
    for clip in clips:
        try:
            with wave.open(str(clip), "rb") as handle:
                channels = handle.getnchannels()
                rate = handle.getframerate()
                width = handle.getsampwidth()
                frames = handle.getnframes()
                duration = frames / float(rate) if rate else 0.0
                total_seconds += duration
                check(channels == 1, f"{clip.name} should be mono")
                check(width == 2, f"{clip.name} should be 16-bit PCM")
                check(rate == 22050, f"{clip.name} should be 22.05 kHz")
                check(duration > 0.05, f"{clip.name} is too short")
        except (wave.Error, OSError) as exc:
            errors.append(f"invalid WAV {clip.name}: {exc}")
    print(f"[audio] {len(clips)} mono PCM clips, {total_seconds:.1f}s total")


def validate_sources() -> None:
    scripts = list((GAME_ASSETS / "Scripts").rglob("*.cs"))
    tests = list((GAME_ASSETS / "Tests").rglob("*.cs"))
    check(len(scripts) >= 45, f"unexpected script count: {len(scripts)}")
    check(len(tests) >= 3, f"unexpected Unity test count: {len(tests)}")
    markers = ("TODO", "FIXME", "NotImplementedException", "throw new NotImplemented")
    for path in scripts + tests + list((ROOT / "Backend").rglob("*.py")):
        text = read(path)
        for marker in markers:
            check(marker not in text, f"placeholder marker '{marker}' in {path.relative_to(ROOT)}")
    check((ROOT / "Assets" / "Game" / "Scripts" / "Core" / "NeonVelocity.Core.asmdef").exists(),
          "core assembly definition missing")
    check((GAME_ASSETS / "Tests" / "EditMode" / "NeonVelocity.Tests.EditMode.asmdef").exists(),
          "EditMode test assembly missing")
    check((GAME_ASSETS / "Tests" / "PlayMode" / "NeonVelocity.Tests.PlayMode.asmdef").exists(),
          "PlayMode test assembly missing")
    print(f"[sources] {len(scripts)} runtime/editor scripts, {len(tests)} Unity test scripts")


def validate_docs() -> None:
    for name in ("README.md", "Documentation/SETUP.md", "Documentation/BUILD.md",
                 "Documentation/TEST_REPORT.md", "Documentation/KNOWN_LIMITATIONS.md",
                 "Documentation/SERVER_PROTOCOL.md", "Documentation/ASSET_LICENSING.md",
                 "Documentation/DEVELOPMENT_PROGRESS.md"):
        check((ROOT / name).exists(), f"required document missing: {name}")
    print("[docs] required documentation present")


def main() -> int:
    print("NEON VELOCITY - static project validation")
    print("----------------------------------------")
    validate_meta_coverage()
    validate_build_scene()
    validate_branding()
    validate_audio()
    validate_sources()
    validate_docs()
    print("----------------------------------------")
    if errors:
        print(f"FAIL: {len(errors)} issue(s) across {checks} checks")
        for issue in errors:
            print("  - " + issue)
        return 1
    print(f"PASS: {checks} checks")
    return 0


if __name__ == "__main__":
    sys.exit(main())
