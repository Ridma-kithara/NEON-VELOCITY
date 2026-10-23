#!/usr/bin/env python3
"""Generate the original NEON VELOCITY soundtrack, ambience beds and SFX.

The output is deterministic (fixed RNG seeds), 22.05 kHz mono 16-bit PCM, and written
directly into Assets/Game/Resources/Audio so Unity's Resources loader can find it:

    python3 Tools/generate_audio.py

Every sound is synthesized from sine/noise/envelope primitives in this file. No samples,
loops or third-party material are used. The generated audio is original work created for
this project and is covered by the repository's asset licensing notes.
"""

from __future__ import annotations

import argparse
import math
import pathlib
import random
import struct
import wave
from typing import Callable, List, Sequence, Tuple

SAMPLE_RATE = 22050
ROOT = pathlib.Path(__file__).resolve().parents[1]
DEFAULT_OUTPUT = ROOT / "Assets" / "Game" / "Resources" / "Audio"

TAU = math.pi * 2.0


def clamp(value: float, low: float = -1.0, high: float = 1.0) -> float:
    return low if value < low else high if value > high else value


def midi(note: int) -> float:
    return 440.0 * (2.0 ** ((note - 69) / 12.0))


def sine(freq: float, t: float) -> float:
    return math.sin(TAU * freq * t)


def soft_saw(freq: float, t: float, harmonics: int = 7) -> float:
    value = 0.0
    for h in range(1, harmonics + 1):
        if freq * h >= SAMPLE_RATE * 0.45:
            break
        value += math.sin(TAU * freq * h * t) / h
    return value * 0.55


def squareish(freq: float, t: float) -> float:
    return 0.7 * math.sin(TAU * freq * t) + 0.3 * math.sin(TAU * freq * 3.0 * t)


def adsr(t: float, duration: float, attack: float, decay: float, sustain: float,
         release: float) -> float:
    if t < 0.0 or t > duration:
        return 0.0
    if t < attack:
        return t / max(1e-6, attack)
    if t < attack + decay:
        return 1.0 - (1.0 - sustain) * ((t - attack) / max(1e-6, decay))
    if t < duration - release:
        return sustain
    return sustain * max(0.0, (duration - t) / max(1e-6, release))


def exp_env(t: float, decay: float) -> float:
    return math.exp(-t / max(1e-6, decay))


def add(buffer: List[float], start: float, samples: Sequence[float], gain: float = 1.0) -> None:
    offset = int(start * SAMPLE_RATE)
    if offset >= len(buffer):
        return
    count = min(len(samples), len(buffer) - offset)
    for i in range(count):
        buffer[offset + i] += samples[i] * gain


def note_wave(freq: float, duration: float, gain: float = 1.0,
              wave_fn: Callable[[float, float], float] = sine,
              attack: float = 0.01, decay: float = 0.2,
              sustain: float = 0.4, release: float = 0.08) -> List[float]:
    count = max(1, int(duration * SAMPLE_RATE))
    output = [0.0] * count
    for i in range(count):
        t = i / SAMPLE_RATE
        output[i] = wave_fn(freq, t) * adsr(t, duration, attack, decay, sustain, release) * gain
    return output


def kick(duration: float = 0.22, gain: float = 0.8) -> List[float]:
    count = int(duration * SAMPLE_RATE)
    output = [0.0] * count
    phase = 0.0
    for i in range(count):
        t = i / SAMPLE_RATE
        frequency = 42.0 + 90.0 * math.exp(-t * 32.0)
        phase += TAU * frequency / SAMPLE_RATE
        output[i] = math.sin(phase) * exp_env(t, 0.075) * gain
    return output


def snare(duration: float = 0.16, gain: float = 0.45, rng: random.Random | None = None) -> List[float]:
    random_source = rng or random.Random(5)
    count = int(duration * SAMPLE_RATE)
    output = [0.0] * count
    for i in range(count):
        t = i / SAMPLE_RATE
        noise = random_source.uniform(-1.0, 1.0)
        tone = math.sin(TAU * 185.0 * t) * 0.35
        output[i] = (noise * 0.65 + tone) * exp_env(t, 0.055) * gain
    return output


def hat(duration: float = 0.045, gain: float = 0.2, rng: random.Random | None = None) -> List[float]:
    random_source = rng or random.Random(9)
    count = int(duration * SAMPLE_RATE)
    output = [0.0] * count
    previous = 0.0
    for i in range(count):
        t = i / SAMPLE_RATE
        noise = random_source.uniform(-1.0, 1.0)
        high = noise - previous * 0.92
        previous = noise
        output[i] = high * exp_env(t, 0.012) * gain
    return output


def edge_fade(buffer: List[float], seconds: float = 0.02) -> None:
    count = min(len(buffer) // 2, max(1, int(seconds * SAMPLE_RATE)))
    for i in range(count):
        factor = i / count
        buffer[i] *= factor
        buffer[-1 - i] *= factor


def delay(buffer: List[float], seconds: float, feedback: float = 0.28,
          mix: float = 0.22) -> List[float]:
    offset = max(1, int(seconds * SAMPLE_RATE))
    output = list(buffer)
    for i in range(offset, len(buffer)):
        output[i] += output[i - offset] * feedback
    for i in range(len(buffer)):
        output[i] = buffer[i] * (1.0 - mix) + output[i] * mix
    return output


def normalize(buffer: List[float], peak: float = 0.86) -> List[float]:
    maximum = max((abs(value) for value in buffer), default=0.0)
    if maximum < 1e-8:
        return buffer
    scale = peak / maximum
    return [value * scale for value in buffer]


def write_wav(path: pathlib.Path, buffer: Sequence[float], peak: float = 0.86) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    scaled = normalize(list(buffer), peak)
    payload = bytearray()
    for value in scaled:
        payload.extend(struct.pack("<h", int(clamp(value) * 32760)))
    with wave.open(str(path), "wb") as handle:
        handle.setnchannels(1)
        handle.setsampwidth(2)
        handle.setframerate(SAMPLE_RATE)
        handle.writeframes(bytes(payload))


# ---- music ---------------------------------------------------------------------------


def music_menu() -> List[float]:
    bpm = 92.0
    beat = 60.0 / bpm
    bar = beat * 4.0
    chords = [
        (57, 60, 64),  # Am
        (53, 57, 60),  # F
        (48, 52, 55),  # C
        (55, 59, 62),  # G
    ]
    bars = 4
    duration = bar * bars
    buffer = [0.0] * int(duration * SAMPLE_RATE)
    rng = random.Random(101)
    for b, chord in enumerate(chords):
        start = b * bar
        # Warm detuned pad.
        for note in chord:
            freq = midi(note)
            for detune in (-0.35, 0.35):
                pad = note_wave(freq + detune, bar + 0.15, gain=0.09,
                                wave_fn=lambda f, t: soft_saw(f, t, 5),
                                attack=0.35, decay=0.4, sustain=0.65, release=0.45)
                add(buffer, start, pad)
        # Sub bass on each beat.
        root = midi(chord[0] - 24)
        for step in range(4):
            add(buffer, start + step * beat, note_wave(root, beat * 0.9, gain=0.26,
                                                       wave_fn=squareish, attack=0.008,
                                                       decay=0.12, sustain=0.6, release=0.06))
        # Shimmering eighth-note arpeggio.
        for step in range(8):
            note = chord[(step + b) % 3] + 12
            add(buffer, start + step * beat * 0.5,
                note_wave(midi(note), beat * 0.42, gain=0.075, wave_fn=sine,
                          attack=0.005, decay=0.08, sustain=0.22, release=0.08))
        # Sparse percussion.
        for step in range(4):
            add(buffer, start + step * beat, kick(gain=0.16 if step else 0.24))
            if step in (1, 3):
                add(buffer, start + step * beat, snare(gain=0.10, rng=rng))
            add(buffer, start + step * beat + beat * 0.5, hat(gain=0.055, rng=rng))
    buffer = delay(buffer, beat * 0.75, feedback=0.25, mix=0.30)
    edge_fade(buffer, 0.03)
    return normalize(buffer, 0.72)


def music_run() -> List[float]:
    bpm = 132.0
    beat = 60.0 / bpm
    bar = beat * 4.0
    chords = [
        (52, 55, 59),  # Em
        (48, 52, 55),  # C
        (55, 59, 62),  # G
        (50, 54, 57),  # D
    ]
    bars = 4
    duration = bar * bars
    buffer = [0.0] * int(duration * SAMPLE_RATE)
    rng = random.Random(202)
    for b, chord in enumerate(chords):
        start = b * bar
        root = midi(chord[0] - 12)
        for step in range(8):
            add(buffer, start + step * beat * 0.5,
                note_wave(root, beat * 0.44, gain=0.23, wave_fn=soft_saw,
                          attack=0.004, decay=0.09, sustain=0.5, release=0.04))
        for step in range(16):
            note = chord[step % 3] + (12 if step % 4 else 24)
            add(buffer, start + step * beat * 0.25,
                note_wave(midi(note), beat * 0.22, gain=0.065, wave_fn=squareish,
                          attack=0.003, decay=0.05, sustain=0.3, release=0.03))
        for step in range(4):
            add(buffer, start + step * beat, kick(gain=0.40))
            if step in (1, 3):
                add(buffer, start + step * beat, snare(gain=0.24, rng=rng))
            add(buffer, start + step * beat + beat * 0.5, hat(gain=0.10, rng=rng))
            add(buffer, start + step * beat + beat * 0.25, hat(gain=0.035, rng=rng))
        # Stab accent at the end of each bar.
        for note in chord:
            add(buffer, start + bar - beat * 0.5,
                note_wave(midi(note + 12), beat * 0.4, gain=0.075, wave_fn=soft_saw,
                          attack=0.005, decay=0.1, sustain=0.2, release=0.12))
    buffer = delay(buffer, beat * 0.5, feedback=0.22, mix=0.18)
    edge_fade(buffer, 0.03)
    return normalize(buffer, 0.80)


# ---- ambience ------------------------------------------------------------------------


def filtered_noise(duration: float, cutoff: float, gain: float, seed: int,
                   high_pass: bool = False, modulation: float = 0.0) -> List[float]:
    rng = random.Random(seed)
    count = int(duration * SAMPLE_RATE)
    output = [0.0] * count
    alpha = clamp(TAU * cutoff / SAMPLE_RATE, 0.0001, 1.0)
    low = 0.0
    for i in range(count):
        t = i / SAMPLE_RATE
        noise = rng.uniform(-1.0, 1.0)
        low += alpha * (noise - low)
        sample = noise - low if high_pass else low
        envelope = 1.0 + modulation * math.sin(TAU * 0.09 * t + 0.7)
        output[i] = sample * gain * envelope
    return output


def ambience_city() -> List[float]:
    duration = 8.0
    buffer = filtered_noise(duration, 420.0, 0.32, seed=301)
    hum = filtered_noise(duration, 90.0, 0.16, seed=302)
    for i in range(len(buffer)):
        t = i / SAMPLE_RATE
        buffer[i] += hum[i] + math.sin(TAU * 55.0 * t) * 0.025
        # Distant transit swells.
        buffer[i] += math.sin(TAU * 220.0 * t + math.sin(TAU * 0.05 * t) * 2.0) * 0.008
    return delay(buffer, 0.37, feedback=0.2, mix=0.12)


def ambience_skyway() -> List[float]:
    duration = 8.0
    buffer = filtered_noise(duration, 700.0, 0.26, seed=401, modulation=0.55)
    for i in range(len(buffer)):
        t = i / SAMPLE_RATE
        shimmer = math.sin(TAU * 880.0 * t) * 0.012 * (0.5 + 0.5 * math.sin(TAU * 0.13 * t))
        buffer[i] += shimmer
    return buffer


def ambience_reactor() -> List[float]:
    duration = 8.0
    buffer = filtered_noise(duration, 260.0, 0.20, seed=501)
    for i in range(len(buffer)):
        t = i / SAMPLE_RATE
        pulse = (0.5 + 0.5 * math.sin(TAU * 1.6 * t)) ** 6
        buffer[i] += math.sin(TAU * 48.0 * t) * (0.10 + 0.10 * pulse)
        buffer[i] += math.sin(TAU * 96.0 * t) * 0.035 * pulse
    return buffer


def ambience_tunnel() -> List[float]:
    duration = 8.0
    buffer = filtered_noise(duration, 300.0, 0.25, seed=601)
    echo = delay(buffer, 0.21, feedback=0.42, mix=0.55)
    for i in range(len(echo)):
        t = i / SAMPLE_RATE
        echo[i] += math.sin(TAU * 41.0 * t) * 0.05
    return echo


def ambience_rain() -> List[float]:
    duration = 8.0
    buffer = filtered_noise(duration, 2600.0, 0.22, seed=701, high_pass=True)
    rng = random.Random(702)
    for _ in range(68):
        start = rng.uniform(0.0, duration - 0.08)
        freq = rng.uniform(1400.0, 3200.0)
        drop = note_wave(freq, 0.06, gain=rng.uniform(0.015, 0.04), wave_fn=sine,
                         attack=0.001, decay=0.015, sustain=0.05, release=0.03)
        add(buffer, start, drop)
    for i in range(len(buffer)):
        t = i / SAMPLE_RATE
        buffer[i] += math.sin(TAU * 38.0 * t) * 0.03
    return buffer


# ---- SFX -----------------------------------------------------------------------------


def sfx_jump() -> List[float]:
    buffer = [0.0] * int(0.24 * SAMPLE_RATE)
    phase = 0.0
    for i in range(len(buffer)):
        t = i / SAMPLE_RATE
        frequency = 260.0 + 660.0 * (t / 0.24) ** 0.7
        phase += TAU * frequency / SAMPLE_RATE
        buffer[i] = math.sin(phase) * exp_env(t, 0.11) * 0.7
    return buffer


def sfx_land() -> List[float]:
    buffer = [0.0] * int(0.20 * SAMPLE_RATE)
    for i in range(len(buffer)):
        t = i / SAMPLE_RATE
        frequency = 120.0 - 60.0 * min(1.0, t / 0.2)
        buffer[i] = math.sin(TAU * frequency * t) * exp_env(t, 0.05) * 0.8
    noise = filtered_noise(0.20, 900.0, 0.28, seed=801)
    return [a + b for a, b in zip(buffer, noise)]


def sfx_slide() -> List[float]:
    buffer = filtered_noise(0.34, 1500.0, 0.5, seed=901, high_pass=True)
    for i in range(len(buffer)):
        t = i / SAMPLE_RATE
        buffer[i] *= math.sin(math.pi * min(1.0, t / 0.34)) * 0.8
    return buffer


def sfx_coin() -> List[float]:
    buffer = [0.0] * int(0.26 * SAMPLE_RATE)
    add(buffer, 0.0, note_wave(1320.0, 0.12, gain=0.48, attack=0.002, decay=0.04,
                               sustain=0.2, release=0.05))
    add(buffer, 0.055, note_wave(1760.0, 0.16, gain=0.42, attack=0.002, decay=0.05,
                                 sustain=0.2, release=0.08))
    return buffer


def sfx_shard() -> List[float]:
    buffer = [0.0] * int(0.30 * SAMPLE_RATE)
    for index, freq in enumerate((2093.0, 2637.0, 3136.0)):
        add(buffer, 0.03 * index, note_wave(freq, 0.18, gain=0.32, attack=0.001,
                                            decay=0.04, sustain=0.15, release=0.08))
    return buffer


def sfx_hit() -> List[float]:
    duration = 0.42
    buffer = [0.0] * int(duration * SAMPLE_RATE)
    rng = random.Random(1001)
    for i in range(len(buffer)):
        t = i / SAMPLE_RATE
        noise = rng.uniform(-1.0, 1.0)
        buffer[i] = (math.sin(TAU * (65.0 - 30.0 * t) * t) * 0.6 + noise * 0.35) * exp_env(t, 0.11)
    return buffer


def sfx_powerup() -> List[float]:
    buffer = [0.0] * int(0.62 * SAMPLE_RATE)
    for index, freq in enumerate((523.25, 659.25, 783.99, 1046.5)):
        add(buffer, 0.075 * index,
            note_wave(freq, 0.30, gain=0.30, attack=0.004, decay=0.06,
                      sustain=0.35, release=0.12))
    return buffer


def sfx_click() -> List[float]:
    buffer = [0.0] * int(0.075 * SAMPLE_RATE)
    add(buffer, 0.0, note_wave(1050.0, 0.05, gain=0.4, attack=0.001, decay=0.012,
                               sustain=0.15, release=0.02))
    add(buffer, 0.012, note_wave(620.0, 0.04, gain=0.2, attack=0.001, decay=0.01,
                                 sustain=0.1, release=0.02))
    return buffer


def sfx_countdown() -> List[float]:
    buffer = [0.0] * int(0.18 * SAMPLE_RATE)
    for i in range(len(buffer)):
        t = i / SAMPLE_RATE
        buffer[i] = (math.sin(TAU * 440.0 * t) * 0.6 + math.sin(TAU * 880.0 * t) * 0.18) * \
            adsr(t, 0.18, 0.005, 0.03, 0.6, 0.06)
    return buffer


def sfx_go() -> List[float]:
    buffer = [0.0] * int(0.45 * SAMPLE_RATE)
    for freq in (660.0, 990.0, 1320.0):
        add(buffer, 0.0, note_wave(freq, 0.40, gain=0.24, attack=0.004, decay=0.08,
                                   sustain=0.5, release=0.16))
    return buffer


def sfx_victory() -> List[float]:
    buffer = [0.0] * int(1.3 * SAMPLE_RATE)
    melody = ((523.25, 0.0), (659.25, 0.13), (783.99, 0.26), (1046.5, 0.42))
    for freq, start in melody:
        add(buffer, start, note_wave(freq, 0.8 - start * 0.3, gain=0.30, attack=0.008,
                                     decay=0.12, sustain=0.4, release=0.25))
    for freq in (523.25, 659.25, 783.99):
        add(buffer, 0.42, note_wave(freq, 0.8, gain=0.10, attack=0.01, decay=0.2,
                                    sustain=0.35, release=0.3))
    return buffer


def sfx_defeat() -> List[float]:
    buffer = [0.0] * int(1.0 * SAMPLE_RATE)
    for index, freq in enumerate((392.0, 311.13, 233.08)):
        add(buffer, 0.18 * index, note_wave(freq, 0.62, gain=0.30, attack=0.01,
                                            decay=0.14, sustain=0.35, release=0.24))
    return buffer


def sfx_nearmiss() -> List[float]:
    duration = 0.24
    buffer = [0.0] * int(duration * SAMPLE_RATE)
    rng = random.Random(1101)
    for i in range(len(buffer)):
        t = i / SAMPLE_RATE
        noise = rng.uniform(-1.0, 1.0)
        envelope = math.sin(math.pi * min(1.0, t / duration))
        sweep = math.sin(TAU * (900.0 + 1400.0 * (t / duration)) * t) * 0.25
        buffer[i] = (noise * 0.25 + sweep) * envelope
    return buffer


def sfx_step_wet(seed: int) -> List[float]:
    duration = 0.18
    buffer = filtered_noise(duration, 1900.0, 0.30, seed=seed, high_pass=True)
    low = filtered_noise(duration, 210.0, 0.28, seed=seed + 7)
    for i in range(len(buffer)):
        t = i / SAMPLE_RATE
        buffer[i] = buffer[i] * exp_env(t, 0.026) + low[i] * exp_env(t, 0.055) * 0.72
        buffer[i] += math.sin(TAU * (82.0 - 24.0 * t / duration) * t) * exp_env(t, 0.04) * 0.34
    return buffer


def sfx_step_metal() -> List[float]:
    duration = 0.16
    buffer = filtered_noise(duration, 4200.0, 0.24, seed=1401, high_pass=True)
    for i in range(len(buffer)):
        t = i / SAMPLE_RATE
        env = exp_env(t, 0.023)
        buffer[i] = buffer[i] * env + math.sin(TAU * 430.0 * t) * env * 0.16
        buffer[i] += math.sin(TAU * 116.0 * t) * exp_env(t, 0.045) * 0.3
    return buffer


def sfx_lane() -> List[float]:
    duration = 0.24
    buffer = filtered_noise(duration, 2600.0, 0.2, seed=1501, high_pass=True)
    phase = 0.0
    for i in range(len(buffer)):
        t = i / SAMPLE_RATE
        frequency = 260.0 + 1060.0 * (t / duration) ** 0.55
        phase += TAU * frequency / SAMPLE_RATE
        envelope = math.sin(math.pi * min(1.0, t / duration))
        buffer[i] = buffer[i] * 0.4 * envelope + math.sin(phase) * envelope * 0.14
    return buffer


def sfx_thunder() -> List[float]:
    duration = 3.4
    buffer = filtered_noise(duration, 140.0, 0.48, seed=1601)
    deep = filtered_noise(duration, 58.0, 0.52, seed=1602)
    for i in range(len(buffer)):
        t = i / SAMPLE_RATE
        swell = math.sin(math.pi * min(1.0, t / 0.16)) * exp_env(t, 1.15)
        crack = exp_env(t, 0.045) * 0.35
        buffer[i] = (buffer[i] * 1.15 + deep[i] * 0.55) * swell + buffer[i] * crack
        buffer[i] += math.sin(TAU * (44.0 - 8.0 * t / duration) * t) * swell * 0.3
    return buffer


def sfx_train_pass() -> List[float]:
    duration = 4.2
    buffer = filtered_noise(duration, 520.0, 0.34, seed=1701)
    rumble = filtered_noise(duration, 85.0, 0.4, seed=1702)
    for i in range(len(buffer)):
        t = i / SAMPLE_RATE
        envelope = math.sin(math.pi * min(1.0, t / duration)) ** 1.5
        doppler = 245.0 - 92.0 * min(1.0, t / duration)
        buffer[i] = (buffer[i] + rumble[i]) * envelope
        buffer[i] += math.sin(TAU * doppler * t) * envelope * 0.16
        buffer[i] += math.sin(TAU * 390.0 * t + math.sin(TAU * 0.6 * t)) * envelope * 0.045
    return buffer


def sfx_daily() -> List[float]:
    buffer = [0.0] * int(1.1 * SAMPLE_RATE)
    for index, freq in enumerate((1046.5, 1318.5, 1568.0, 2093.0)):
        add(buffer, index * 0.11, note_wave(freq, 0.65, gain=0.22, attack=0.005,
                                            decay=0.08, sustain=0.28, release=0.3))
    return buffer


BUILDERS: List[Tuple[str, Callable[[], List[float]], float]] = [
    ("music_menu", music_menu, 0.72),
    ("music_run", music_run, 0.80),
    ("ambience_city", ambience_city, 0.55),
    ("ambience_skyway", ambience_skyway, 0.55),
    ("ambience_reactor", ambience_reactor, 0.55),
    ("ambience_tunnel", ambience_tunnel, 0.55),
    ("ambience_rain", ambience_rain, 0.55),
    ("sfx_jump", sfx_jump, 0.85),
    ("sfx_land", sfx_land, 0.85),
    ("sfx_slide", sfx_slide, 0.80),
    ("sfx_coin", sfx_coin, 0.85),
    ("sfx_shard", sfx_shard, 0.80),
    ("sfx_hit", sfx_hit, 0.90),
    ("sfx_powerup", sfx_powerup, 0.85),
    ("sfx_click", sfx_click, 0.75),
    ("sfx_countdown", sfx_countdown, 0.80),
    ("sfx_go", sfx_go, 0.85),
    ("sfx_victory", sfx_victory, 0.85),
    ("sfx_defeat", sfx_defeat, 0.85),
    ("sfx_nearmiss", sfx_nearmiss, 0.70),
    ("sfx_daily", sfx_daily, 0.80),
    ("sfx_step_wet_1", lambda: sfx_step_wet(1301), 0.84),
    ("sfx_step_wet_2", lambda: sfx_step_wet(1307), 0.84),
    ("sfx_step_metal", sfx_step_metal, 0.84),
    ("sfx_lane", sfx_lane, 0.80),
    ("sfx_thunder", sfx_thunder, 0.88),
    ("sfx_train_pass", sfx_train_pass, 0.82),
]


def main() -> int:
    parser = argparse.ArgumentParser(description="Generate NEON VELOCITY original audio.")
    parser.add_argument("--output", type=pathlib.Path, default=DEFAULT_OUTPUT)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    for name, builder, peak in BUILDERS:
        buffer = builder()
        if name.startswith("ambience_"):
            edge_fade(buffer, 0.15)
        path = args.output / f"{name}.wav"
        write_wav(path, buffer, peak)
        seconds = len(buffer) / SAMPLE_RATE
        print(f"{path.relative_to(ROOT)}  {seconds:6.2f}s  {path.stat().st_size / 1024:7.1f} KiB")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
