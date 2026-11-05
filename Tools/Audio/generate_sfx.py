"""Generate the game's non-vocal audio; credentials stay in the project .env.

Usage: python Tools/Audio/generate_sfx.py --dry-run
       python Tools/Audio/generate_sfx.py [--only filename.wav] [--force]
       python Tools/Audio/generate_sfx.py --audit-only
"""
from __future__ import annotations

import argparse
from array import array
import concurrent.futures
import datetime as dt
import importlib.util
import json
import math
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import urllib.error
import urllib.request
import wave

ROOT = Path(__file__).resolve().parents[2]
PLAN = Path(__file__).with_name("audio-plan.json")
OUTPUT = ROOT / "Assets/Game/Resources/Audio/SFX"
CACHE = ROOT / "Temp/AudioGeneration"


def run(*args: str) -> subprocess.CompletedProcess:
    return subprocess.run(args, check=True, capture_output=True, text=True)


def pipeline():
    path = Path(__file__).with_name("audio_pipeline.py")
    spec = importlib.util.spec_from_file_location("audio_pipeline", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def smooth_boundary(path: Path) -> None:
    """Join endpoints over 3 ms without a silence gap or a hard waveform jump."""
    with wave.open(str(path), "rb") as wav:
        params = wav.getparams()
        samples = array("h", wav.readframes(wav.getnframes()))
    channels, frames = params.nchannels, len(samples) // params.nchannels
    width = min(132, frames // 8)
    for channel in range(channels):
        head, tail = samples[channel], samples[(frames - 1) * channels + channel]
        target = (head + tail) / 2
        for i in range(width):
            weight = (1 - i / (width - 1)) ** 2
            for index, correction in ((i * channels + channel, target - head),
                                      ((frames - 1 - i) * channels + channel, target - tail)):
                samples[index] = max(-32768, min(32767, round(samples[index] + correction * weight)))
    with wave.open(str(path), "wb") as wav:
        wav.setparams(params)
        wav.writeframes(samples.tobytes())


def render(source: Path, destination: Path, asset: dict, audio) -> None:
    with tempfile.TemporaryDirectory(dir=CACHE) as directory:
        temp = Path(directory)
        decoded, shaped = temp / "decoded.wav", temp / "shaped.wav"
        channels = 2 if asset["kind"] == "ambience" else 1
        args = ["ffmpeg", "-v", "error", "-y", "-i", str(source), "-ar", "44100", "-ac", str(channels)]
        if not asset.get("loop"):
            args += ["-af", "silenceremove=start_periods=1:start_threshold=-50dB:start_silence=0.005"]
        run(*args, "-c:a", "pcm_s16le", str(decoded))
        length = min(audio.probe(decoded)["duration"], asset["seconds"])
        if asset.get("loop"):
            crossfade = min(0.25, length / 8)
            end = length - crossfade
            graph = (
                f"[0:a]atrim=start={crossfade}:end={end},asetpts=PTS-STARTPTS[body];"
                f"[0:a]atrim=start=0:end={crossfade},asetpts=PTS-STARTPTS[head];"
                f"[0:a]atrim=start={end}:end={length},asetpts=PTS-STARTPTS[tail];"
                f"[tail][head]acrossfade=d={crossfade}:c1=tri:c2=tri[seam];"
                "[seam][body]concat=n=2:v=0:a=1[out]"
            )
            run("ffmpeg", "-v", "error", "-y", "-i", str(decoded), "-filter_complex", graph,
                "-map", "[out]", str(shaped))
            smooth_boundary(shaped)
        else:
            fade = min(0.03, length / 4)
            run("ffmpeg", "-v", "error", "-y", "-i", str(decoded), "-t", str(length),
                "-af", f"afade=t=in:d=0.002,afade=t=out:st={max(0, length-fade)}:d={fade}", str(shaped))
        peak = audio.volume(shaped)["peak_db"]
        if peak is None or peak < -65:
            raise ValueError(f"{asset['file']}: generated audio is silent")
        target = -4.0 if asset.get("loop") else -1.5
        codec = ["-c:a", "libmp3lame", "-b:a", "128k"] if asset["kind"] == "ambience" else ["-c:a", "pcm_s16le"]
        staged = temp / asset["file"]
        # A look-ahead limiter pads/truncates a stream, which destroys a loop boundary.
        # Loops already have 4 dB headroom and only need linear gain.
        filters = f"volume={target-peak:.3f}dB"
        if not asset.get("loop"):
            filters += f",alimiter=limit={10**(target/20):.6f}:level=false:latency=true"
        run("ffmpeg", "-v", "error", "-y", "-i", str(shaped), "-af", filters,
            "-ar", "44100", "-ac", str(channels), *codec, str(staged))
        if destination.exists():
            backup = CACHE / "backups" / dt.datetime.now().strftime("%Y%m%d-%H%M%S")
            backup.mkdir(parents=True, exist_ok=True)
            shutil.copy2(destination, backup / destination.name)
        os.replace(staged, destination)


def generate(asset: dict, key: str, force: bool, reprocess: bool, audio) -> str:
    destination = OUTPUT / asset["file"]
    if destination.exists() and not force and not reprocess:
        return f"kept {asset['file']}"
    raw = CACHE / (Path(asset["file"]).stem + ".mp3")
    if reprocess and not raw.exists():
        raise ValueError("Cached raw audio is missing; --reprocess never requests another generation. Use --force for a new generation.")
    if not raw.exists() or force:
        payload = {
            "text": asset["prompt"], "duration_seconds": max(0.5, asset["seconds"]),
            "prompt_influence": 0.45, "loop": asset.get("loop", False),
            "model_id": "eleven_text_to_sound_v2",
        }
        request = urllib.request.Request(
            "https://api.elevenlabs.io/v1/sound-generation?output_format=mp3_44100_128",
            data=json.dumps(payload).encode(),
            headers={"xi-api-key": key, "Content-Type": "application/json", "Accept": "audio/mpeg"},
        )
        try:
            with urllib.request.urlopen(request, timeout=180) as response:
                data = response.read()
            if len(data) < 100:
                raise ValueError("Empty generation response")
            raw.write_bytes(data)
        except urllib.error.HTTPError as error:
            # Never include a request object or credentials in errors.
            raise RuntimeError(f"ElevenLabs HTTP {error.code}: {error.read().decode('utf-8', 'replace')[:350]}") from None
    render(raw, destination, asset, audio)
    return f"generated {asset['file']}"


def audit(asset: dict, audio) -> dict:
    result = audio.audit(asset, OUTPUT / asset["file"], {"sample_rate": 44100})
    if asset.get("loop") and result.get("duration", 0) > 0:
        path = OUTPUT / asset["file"]
        window = min(0.1, result["duration"] / 4)
        head = audio.segment_mean_db(path, 0, window)
        tail = audio.segment_mean_db(path, result["duration"] - window, window)
        delta = abs(head - tail) if head is not None and tail is not None else None
        result["loop_seam_delta_db"] = round(delta, 2) if delta is not None else None
        result["ok"] = (delta is not None and delta <= 8 and result["sample_rate"] == 44100
                        and result["channels"] == (2 if asset["kind"] == "ambience" else 1)
                        and result["peak_db"] is not None and result["peak_db"] <= -2)
        if path.suffix == ".wav":
            import wave
            import struct
            with wave.open(str(path), "rb") as wav:
                frames = wav.readframes(wav.getnframes())
            samples = struct.unpack("<" + "h" * (len(frames) // 2), frames)
            jump = abs(samples[0] - samples[-1]) / 32768
            result["boundary_jump_dbfs"] = round(20 * math.log10(max(jump, 1e-9)), 2)
            result["ok"] = result["ok"] and jump < 0.035
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--audit-only", action="store_true")
    parser.add_argument("--force", action="store_true")
    parser.add_argument("--reprocess", action="store_true", help="Re-render cached generations with backups; no new API request")
    parser.add_argument("--only", nargs="*")
    args = parser.parse_args()
    if args.force and args.reprocess:
        parser.error("Choose --force or --reprocess, not both")
    audio = pipeline()
    audio.require_tools()
    plan = audio.read_plan(PLAN)
    selected = set(args.only or [])
    if selected - {a["file"] for a in plan["assets"]}:
        raise SystemExit("Unknown --only filename")
    assets = [a for a in plan["assets"] if not selected or a["file"] in selected]
    if args.dry_run:
        print(json.dumps({"files": len(assets), "requested_seconds": sum(a["seconds"] for a in assets),
                          "output": str(OUTPUT), "assets": [a["file"] for a in assets]}, indent=2))
        return
    OUTPUT.mkdir(parents=True, exist_ok=True)
    CACHE.mkdir(parents=True, exist_ok=True)
    if not args.audit_only:
        key = "" if args.reprocess else audio.api_key(ROOT / "audio-plan.json")
        # Two concurrent jobs keep generation modest and preserve per-file resumability.
        with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
            futures = {pool.submit(generate, a, key, args.force, args.reprocess, audio): a for a in assets}
            for future in concurrent.futures.as_completed(futures):
                try:
                    print(future.result(), flush=True)
                except Exception as error:
                    for other in futures:
                        other.cancel()
                    raise SystemExit(f"{futures[future]['file']}: {error}") from None
    report = [audit(a, audio) for a in assets]
    report_path = Path(__file__).with_name("audio-audit.json")
    report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"checked": len(report), "passed": sum(r["ok"] for r in report),
                      "bytes": sum(r.get("bytes", 0) for r in report),
                      "failed": [r for r in report if not r["ok"]]}, indent=2), flush=True)
    if not all(r["ok"] for r in report):
        raise SystemExit("Audio audit failed; see Tools/Audio/audio-audit.json")


if __name__ == "__main__":
    main()
