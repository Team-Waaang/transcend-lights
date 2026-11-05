#!/usr/bin/env python3
"""Plan-driven ElevenLabs audio generation, post-processing, and audit."""

from __future__ import annotations

import argparse
import datetime as dt
import json
import math
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
import urllib.error
import urllib.request

SOUND_URL = "https://api.elevenlabs.io/v1/sound-generation"
MUSIC_URL = "https://api.elevenlabs.io/v1/music"
KINDS = {"sfx", "ambience", "bgm"}


def fail(message: str) -> None:
    raise SystemExit(message)


def command(*parts: str, capture: bool = False) -> subprocess.CompletedProcess[str]:
    return subprocess.run(parts, check=True, text=True, capture_output=capture)


def require_tools() -> None:
    missing = [name for name in ("ffmpeg", "ffprobe") if not shutil.which(name)]
    if missing:
        fail(f"Missing required tools: {', '.join(missing)}")


def read_plan(path: Path) -> dict:
    try:
        plan = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        fail(f"Cannot read plan {path}: {error}")
    if plan.get("provider", "elevenlabs") != "elevenlabs":
        fail("Only provider 'elevenlabs' is supported by this script.")
    assets = plan.get("assets")
    if not isinstance(assets, list) or not assets:
        fail("Plan must contain a non-empty assets array.")
    seen: set[str] = set()
    for index, asset in enumerate(assets):
        if not isinstance(asset, dict):
            fail(f"assets[{index}] must be an object")
        kind = asset.get("kind")
        filename = asset.get("file")
        prompt = asset.get("prompt")
        seconds = asset.get("seconds")
        if kind not in KINDS:
            fail(f"assets[{index}].kind must be one of {sorted(KINDS)}")
        if not isinstance(filename, str) or Path(filename).name != filename:
            fail(f"assets[{index}].file must be a plain filename")
        if filename in seen:
            fail(f"Duplicate output filename: {filename}")
        seen.add(filename)
        expected = ".wav" if kind == "sfx" else ".mp3"
        if Path(filename).suffix.lower() != expected:
            fail(f"{filename}: {kind} must use {expected}")
        if not isinstance(prompt, str) or len(prompt.strip()) < 12:
            fail(f"{filename}: prompt is too short")
        if not isinstance(seconds, (int, float)) or not math.isfinite(seconds) or seconds <= 0:
            fail(f"{filename}: seconds must be positive")
    return plan


def api_key(plan_path: Path) -> str:
    key = os.environ.get("ELEVENLABS_API_KEY", "").strip()
    env_file = plan_path.parent / ".env"
    if not key and not env_file.exists():
        env_file = Path.cwd() / ".env"
    if not key and env_file.exists():
        for line in env_file.read_text(encoding="utf-8").splitlines():
            name, separator, value = line.partition("=")
            if separator and name.strip() == "ELEVENLABS_API_KEY":
                key = value.strip().strip("\"'")
                break
    if not key:
        fail("ELEVENLABS_API_KEY is missing. Set it in the environment or project .env.")
    if not key.startswith("sk_"):
        fail("ELEVENLABS_API_KEY does not look like a secret key (expected prefix sk_).")
    return key


def post(url: str, key: str, payload: dict) -> bytes:
    request = urllib.request.Request(
        url,
        data=json.dumps(payload).encode("utf-8"),
        headers={
            "xi-api-key": key,
            "Content-Type": "application/json",
            "Accept": "audio/mpeg",
        },
    )
    try:
        with urllib.request.urlopen(request, timeout=360) as response:
            return response.read()
    except urllib.error.HTTPError as error:
        detail = error.read().decode("utf-8", "replace")[:500]
        fail(f"ElevenLabs HTTP {error.code}: {detail}")


def probe(path: Path) -> dict:
    result = command(
        "ffprobe", "-v", "error", "-select_streams", "a:0",
        "-show_entries", "stream=sample_rate,channels:format=duration,size",
        "-of", "json", str(path), capture=True,
    )
    data = json.loads(result.stdout)
    stream = data.get("streams", [{}])[0]
    fmt = data.get("format", {})
    return {
        "duration": round(float(fmt.get("duration", 0)), 3),
        "bytes": int(fmt.get("size", path.stat().st_size)),
        "sample_rate": int(stream.get("sample_rate", 0)),
        "channels": int(stream.get("channels", 0)),
    }


def volume(path: Path) -> dict:
    result = subprocess.run(
        ["ffmpeg", "-hide_banner", "-i", str(path), "-af", "volumedetect", "-f", "null", "-"],
        text=True, capture_output=True,
    )
    def value(label: str) -> float | None:
        match = re.search(rf"{label}:\s*(-?(?:inf|\d+(?:\.\d+)?))\s*dB", result.stderr)
        if not match or match.group(1) == "-inf":
            return None
        return float(match.group(1))
    return {"mean_db": value("mean_volume"), "peak_db": value("max_volume")}


def segment_mean_db(path: Path, start: float, seconds: float) -> float | None:
    result = subprocess.run(
        [
            "ffmpeg", "-hide_banner", "-ss", str(max(0, start)), "-t", str(seconds),
            "-i", str(path), "-af", "volumedetect", "-f", "null", "-",
        ],
        text=True, capture_output=True,
    )
    match = re.search(r"mean_volume:\s*(-?(?:inf|\d+(?:\.\d+)?))\s*dB", result.stderr)
    if not match or match.group(1) == "-inf":
        return None
    return float(match.group(1))


def silence_stats(path: Path, total_seconds: float) -> dict:
    result = subprocess.run(
        [
            "ffmpeg", "-hide_banner", "-i", str(path),
            "-af", "silencedetect=noise=-50dB:d=.01", "-f", "null", "-",
        ],
        text=True, capture_output=True,
    )
    durations = [float(value) for value in re.findall(r"silence_duration:\s*([0-9.]+)", result.stderr)]
    leading_match = re.search(r"silence_start:\s*0(?:\.0+)?\b.*?silence_end:\s*([0-9.]+)", result.stderr, re.S)
    return {
        "silence_ratio": round(min(1, sum(durations) / max(.001, total_seconds)), 4),
        "leading_silence_ms": round(float(leading_match.group(1)) * 1000, 1) if leading_match else 0,
    }


def backup(destination: Path) -> Path | None:
    if not destination.exists():
        return None
    stamp = dt.datetime.now().strftime("%Y%m%d%H%M%S")
    target = destination.with_name(f"{destination.name}.bak-{stamp}")
    shutil.copy2(destination, target)
    return target


def render(raw: bytes, asset: dict, destination: Path, defaults: dict) -> None:
    sample_rate = int(defaults.get("sample_rate", 44100))
    kind = asset["kind"]
    with tempfile.TemporaryDirectory() as directory:
        temp = Path(directory)
        source = temp / "source.mp3"
        source.write_bytes(raw)
        staged = temp / "staged.wav"
        if kind == "sfx":
            filters = (
                "silenceremove=start_periods=1:start_threshold=-50dB:start_silence=0.01,"
                f"afade=t=out:st={max(0, float(asset['seconds']) - .03):.3f}:d=0.03"
            )
            command(
                "ffmpeg", "-loglevel", "error", "-y", "-i", str(source),
                "-t", str(asset["seconds"]), "-af", filters,
                "-ar", str(sample_rate), "-ac", "1", "-codec:a", "pcm_s16le", str(staged),
            )
            target_db = float(defaults.get("sfx_peak_db", -1))
            measured = volume(staged)["peak_db"]
            gain = target_db - measured if measured is not None else 0
            command(
                "ffmpeg", "-loglevel", "error", "-y", "-i", str(staged),
                "-af", f"volume={gain:.2f}dB,alimiter=limit={10 ** (target_db / 20):.6f}:level=false",
                "-ar", str(sample_rate), "-ac", "1", "-codec:a", "pcm_s16le", str(destination),
            )
            return

        crossfade = float(defaults.get("loop_crossfade_seconds", 2.0))
        decoded = temp / "decoded.wav"
        command("ffmpeg", "-loglevel", "error", "-y", "-i", str(source), "-ar", str(sample_rate), "-ac", "2", str(decoded))
        length = probe(decoded)["duration"]
        crossfade = min(crossfade, max(.25, length / 4))
        body_end = max(crossfade, length - crossfade)
        looped = temp / "looped.wav"
        graph = (
            f"[0]atrim=start={crossfade}:end={body_end},asetpts=PTS-STARTPTS[body];"
            f"[0]atrim=0:{crossfade},asetpts=PTS-STARTPTS[head];"
            f"[0]atrim=start={body_end},asetpts=PTS-STARTPTS[tail];"
            f"[tail][head]acrossfade=d={crossfade}:c1=tri:c2=tri[seam];"
            "[seam][body]concat=n=2:v=0:a=1[out]"
        )
        command("ffmpeg", "-loglevel", "error", "-y", "-i", str(decoded), "-filter_complex", graph, "-map", "[out]", str(looped))
        target_db = float(defaults.get("loop_peak_db", -3))
        measured = volume(looped)["peak_db"]
        gain = target_db - measured if measured is not None else 0
        limit = 10 ** (target_db / 20)
        command(
            "ffmpeg", "-loglevel", "error", "-y", "-i", str(looped),
            "-af", f"volume={gain:.2f}dB,alimiter=limit={limit:.6f}:level=false", "-ar", str(sample_rate), "-ac", "2",
            "-codec:a", "libmp3lame", "-b:a", "128k", str(destination),
        )


def audit(asset: dict, path: Path, defaults: dict) -> dict:
    if not path.exists() or path.stat().st_size == 0:
        return {"file": asset["file"], "ok": False, "error": "missing or empty"}
    try:
        info = {**probe(path), **volume(path)}
    except (subprocess.CalledProcessError, ValueError, json.JSONDecodeError) as error:
        return {"file": asset["file"], "ok": False, "error": f"decode failed: {error}"}
    expected_channels = 1 if asset["kind"] == "sfx" else 2
    expected_sample_rate = int(defaults.get("sample_rate", 44100))
    duration_ok = info["duration"] <= float(asset["seconds"]) + .16 if asset["kind"] == "sfx" else info["duration"] > 1
    peak_limit = -.5 if asset["kind"] == "sfx" else -2
    info.update(silence_stats(path, info["duration"]))
    seam_ok = True
    if asset["kind"] != "sfx":
        window = min(.5, info["duration"] / 4)
        first_db = segment_mean_db(path, 0, window)
        last_db = segment_mean_db(path, max(0, info["duration"] - window), window)
        seam_delta = abs(first_db - last_db) if first_db is not None and last_db is not None else None
        info.update({
            "loop_head_mean_db": first_db,
            "loop_tail_mean_db": last_db,
            "loop_seam_delta_db": round(seam_delta, 2) if seam_delta is not None else None,
        })
        seam_ok = seam_delta is not None and seam_delta <= 8
    info.update({
        "file": asset["file"],
        "kind": asset["kind"],
        "ok": info["sample_rate"] == expected_sample_rate and info["channels"] == expected_channels
              and duration_ok and info["peak_db"] is not None and info["peak_db"] <= peak_limit
              and seam_ok and (asset["kind"] != "sfx" or info["leading_silence_ms"] <= 50),
    })
    return info


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("plan", type=Path)
    parser.add_argument("--only", nargs="*", help="Generate/audit only these filenames")
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--audit-only", action="store_true")
    parser.add_argument("--force", action="store_true")
    args = parser.parse_args()

    require_tools()
    plan = read_plan(args.plan.resolve())
    output = (args.plan.resolve().parent / plan.get("output_dir", "sounds")).resolve()
    wanted = set(args.only or [])
    assets = [asset for asset in plan["assets"] if not wanted or asset["file"] in wanted]
    unknown = wanted - {asset["file"] for asset in assets}
    if unknown:
        fail(f"Unknown --only filenames: {', '.join(sorted(unknown))}")
    if not assets:
        fail("No assets selected")

    print(json.dumps({
        "output_dir": str(output),
        "assets": [{key: asset[key] for key in ("kind", "file", "seconds")} for asset in assets],
    }, ensure_ascii=False, indent=2))
    if args.dry_run:
        return

    output.mkdir(parents=True, exist_ok=True)
    if not args.audit_only:
        key = api_key(args.plan.resolve())
        for asset in assets:
            destination = output / asset["file"]
            if destination.exists() and not args.force:
                fail(f"Refusing to overwrite {destination}; pass --force")
            payload = (
                {
                    "prompt": asset["prompt"],
                    "music_length_ms": int(float(asset["seconds"]) * 1000),
                    "model_id": "music_v1",
                }
                if asset["kind"] == "bgm"
                else {
                    "text": asset["prompt"],
                    "duration_seconds": max(.5, min(30, float(asset["seconds"]))),
                    "prompt_influence": float(asset.get("prompt_influence", .35)),
                    "loop": asset["kind"] == "ambience",
                    "model_id": "eleven_text_to_sound_v2",
                    "output_format": "mp3_44100_128",
                }
            )
            raw = post(MUSIC_URL if asset["kind"] == "bgm" else SOUND_URL, key, payload)
            with tempfile.TemporaryDirectory(dir=output) as directory:
                staged = Path(directory) / destination.name
                render(raw, asset, staged, plan.get("defaults", {}))
                previous = backup(destination)
                os.replace(staged, destination)
                print(f"generated {destination}" + (f" (backup {previous.name})" if previous else ""))

    report = [audit(asset, output / asset["file"], plan.get("defaults", {})) for asset in assets]
    report_path = output / "audio-audit.json"
    report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report, ensure_ascii=False, indent=2))
    if not all(item["ok"] for item in report):
        fail(f"Audio audit failed. See {report_path}")


if __name__ == "__main__":
    main()
