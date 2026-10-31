"""Build a lead-vocal pitch contour and an instrumental stem for Karaoke Vox."""

from __future__ import annotations

import argparse
import json
import logging
import math
import shutil
import sys
import traceback
from pathlib import Path

MAJOR = (0, 2, 4, 5, 7, 9, 11)
MINOR = (0, 2, 3, 5, 7, 8, 10)


def stage(name: str) -> None:
    print(f"STAGE\t{name}", flush=True)


def hz_to_midi(hz: float) -> float:
    return 69.0 + 12.0 * math.log2(hz / 440.0)


def midi_to_hz(midi: float) -> float:
    return 440.0 * (2.0 ** ((midi - 69.0) / 12.0))


def note_centers(f0, confidence, voiced) -> list[float]:
    midi = []
    for hz, conf, flag in zip(f0, confidence, voiced):
        if flag and conf >= 0.5 and hz and hz > 0 and math.isfinite(hz):
            midi.append(hz_to_midi(float(hz)))
        else:
            midi.append(None)
    centers = []
    for index, value in enumerate(midi):
        if value is None:
            centers.append(0.0)
            continue
        window = [item for item in midi[max(0, index - 4) : index + 5] if item is not None]
        median = sorted(window)[len(window) // 2]
        centers.append(round(midi_to_hz(round(median)), 2))
    return centers


def estimate_key(center_hz, voiced) -> tuple[int, str]:
    histogram = [0.0] * 12
    for hz, flag in zip(center_hz, voiced):
        if not flag or not hz or hz <= 0:
            continue
        histogram[int(round(hz_to_midi(float(hz)))) % 12] += 1.0
    best_root = 0
    best_scale = "major"
    best_score = -1.0
    for root in range(12):
        for name, degrees in (("major", MAJOR), ("minor", MINOR)):
            score = sum(histogram[(root + degree) % 12] for degree in degrees)
            score += histogram[root] * 0.25
            if score > best_score:
                best_root = root
                best_scale = name
                best_score = score
    return best_root, best_scale


def track_pyin(vocal_path: Path):
    import librosa
    import numpy as np

    samples, sample_rate = librosa.load(vocal_path, sr=48000, mono=True)
    hop = 480
    f0, flags, probability = librosa.pyin(
        samples,
        fmin=librosa.note_to_hz("C2"),
        fmax=librosa.note_to_hz("C6"),
        sr=sample_rate,
        frame_length=2048,
        hop_length=hop,
    )
    hz = []
    voiced = []
    confidence = []
    for value, flag, prob in zip(f0, flags, probability):
        number = float(value) if value is not None and np.isfinite(value) else 0.0
        is_voiced = bool(flag) and number > 0.0 and float(prob) >= 0.5
        hz.append(number if is_voiced else 0.0)
        voiced.append(1 if is_voiced else 0)
        confidence.append(round(float(prob) if np.isfinite(prob) else 0.0, 4))
    return sample_rate, hop, hz, confidence, voiced


def separate(source: Path, outdir: Path, model_dir: Path) -> tuple[Path, Path]:
    from audio_separator.separator import Separator

    separator = Separator(
        log_level=logging.ERROR,
        model_file_dir=str(model_dir),
        output_dir=str(outdir),
        output_format="WAV",
    )
    separator.load_model(model_filename="UVR-MDX-NET-Voc_FT.onnx")
    produced = [resolve_output(path, outdir) for path in separator.separate(str(source))]
    vocal = None
    instrumental = None
    for path in produced:
        name = path.name.lower()
        if "instrument" in name or "no vocal" in name or "no_vocal" in name:
            instrumental = path
        elif "vocal" in name:
            vocal = path
    if vocal is None or instrumental is None:
        remaining = [path for path in produced if path not in (vocal, instrumental)]
        if vocal is None and remaining:
            vocal = remaining.pop(0)
        if instrumental is None and remaining:
            instrumental = remaining.pop(0)
    if vocal is None or instrumental is None:
        raise RuntimeError("Vocal separation did not return a vocal stem and an instrumental.")
    vocal_dest = outdir / "vocal.wav"
    instrumental_dest = outdir / "instrumental.wav"
    publish(vocal, vocal_dest)
    publish(instrumental, instrumental_dest)
    return vocal_dest, instrumental_dest


def resolve_output(path: str, outdir: Path) -> Path:
    candidate = Path(path)
    if candidate.is_file():
        return candidate
    nested = outdir / path
    if nested.is_file():
        return nested
    return candidate


def publish(source: Path, dest: Path) -> None:
    if source.resolve() == dest.resolve():
        return
    shutil.copyfile(source, dest)


def write_contour(path: Path, sample_rate: int, hop: int, hz, confidence, voiced) -> tuple[int, str]:
    centers = note_centers(hz, confidence, voiced)
    root, scale = estimate_key(centers, voiced)
    payload = {
        "hopSec": hop / sample_rate,
        "keyRoot": root,
        "scale": scale,
        "centerHz": centers,
        "conf": confidence,
        "voiced": voiced,
    }
    path.write_text(json.dumps(payload), encoding="utf-8")
    return root, scale


def main() -> int:
    logging.basicConfig(level=logging.ERROR, stream=sys.stderr)
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", required=True)
    parser.add_argument("--outdir", required=True)
    parser.add_argument("--model-dir", required=True)
    args = parser.parse_args()
    source = Path(args.input)
    outdir = Path(args.outdir)
    model_dir = Path(args.model_dir)
    outdir.mkdir(parents=True, exist_ok=True)
    model_dir.mkdir(parents=True, exist_ok=True)
    try:
        stage("separating")
        separate(source, outdir, model_dir)
        vocal = outdir / "vocal.wav"
        stage("tracking")
        sample_rate, hop, hz, confidence, voiced = track_pyin(vocal)
        root, scale = write_contour(outdir / "contour.json", sample_rate, hop, hz, confidence, voiced)
        stage(f"done\t{root}\t{scale}")
        return 0
    except Exception as exc:
        print(f"ERROR\t{exc}", flush=True)
        traceback.print_exc()
        return 1


def self_test() -> int:
    vibrato = [440.0 * (2.0 ** (0.3 * math.sin(i / 2.0) / 12.0)) for i in range(15)]
    centers = note_centers(vibrato, [0.9] * 15, [1] * 15)
    assert all(abs(center - 440.0) < 1.0 for center in centers), centers

    def hz_of(midi: int) -> float:
        return midi_to_hz(midi)

    notes = []
    for midi in (60, 62, 64, 65, 67, 69, 71, 72):
        notes.extend([hz_of(midi)] * 4)
    root, scale = estimate_key(notes, [1] * len(notes))
    assert (root, scale) == (0, "major"), (root, scale)

    a_notes = [440.0] * 20
    root, scale = estimate_key(a_notes, [1] * len(a_notes))
    assert root == 9 and scale == "major", (root, scale)
    print("self-test ok")
    return 0


if __name__ == "__main__":
    if "--self-test" in sys.argv:
        raise SystemExit(self_test())
    raise SystemExit(main())
