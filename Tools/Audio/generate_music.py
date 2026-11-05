"""ElevenLabs Music generation and measured, loopable music delivery.

python Tools/Audio/generate_music.py --dry-run
python Tools/Audio/generate_music.py [--only boss_suite] [--force]
python Tools/Audio/generate_music.py --audit-only
"""
from __future__ import annotations

import argparse
import datetime as dt
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile
import urllib.error
import urllib.request

import audio_pipeline as audio
from generate_sfx import smooth_boundary

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).parent
OUTPUT = ROOT / "Assets/Game/Resources/Audio/Music"
CACHE = HERE / ".cache/music"
PLAN = HERE / "music-plan.json"


def run(*args):
    return subprocess.run(args, check=True, capture_output=True, text=True)


def loudness(path):
    result = run("ffmpeg", "-hide_banner", "-i", str(path), "-af",
                 "loudnorm=I=-20:TP=-3:LRA=9:print_format=json", "-f", "null", "-")
    match = re.search(r'\{\s*"input_i".*?\}', result.stderr, re.S)
    if not match:
        raise ValueError("Loudness analysis failed")
    return json.loads(match.group())


def render(raw, asset, force):
    destination = OUTPUT / asset["file"]
    if destination.exists() and not force:
        print("kept " + destination.name, flush=True)
        return
    with tempfile.TemporaryDirectory(dir=CACHE) as directory:
        temp = Path(directory)
        section, joined, staged = temp / "section.wav", temp / "loop.wav", temp / destination.name
        seconds, crossfade = asset["seconds"], asset["crossfade"]
        run("ffmpeg", "-v", "error", "-y", "-ss", str(asset.get("start", 0)), "-i", str(raw),
            "-t", str(seconds), "-ar", "44100", "-ac", "2", "-c:a", "pcm_s16le", str(section))
        if abs(audio.probe(section)["duration"] - seconds) > 0.15:
            raise ValueError(f"{destination.name}: generated section is shorter than planned")
        end = seconds - crossfade
        graph = (f"[0:a]atrim=start={crossfade}:end={end},asetpts=PTS-STARTPTS[body];"
                 f"[0:a]atrim=start=0:end={crossfade},asetpts=PTS-STARTPTS[head];"
                 f"[0:a]atrim=start={end}:end={seconds},asetpts=PTS-STARTPTS[tail];"
                 f"[tail][head]acrossfade=d={crossfade}:c1=tri:c2=tri[seam];"
                 "[seam][body]concat=n=2:v=0:a=1[out]")
        run("ffmpeg", "-v", "error", "-y", "-i", str(section), "-filter_complex", graph,
            "-map", "[out]", str(joined))
        smooth_boundary(joined)
        measurement = loudness(joined)
        # Constant gain preserves transients and loop continuity; true-peak headroom wins over loudness.
        gain = min(-20 - float(measurement["input_i"]), -3.5 - float(measurement["input_tp"]))
        run("ffmpeg", "-v", "error", "-y", "-i", str(joined), "-af", f"volume={gain:.4f}dB",
            "-ar", "44100", "-ac", "2", "-c:a", "libvorbis", "-b:a", "128k", str(staged))
        if destination.exists():
            backup = CACHE / "backups" / dt.datetime.now().strftime("%Y%m%d-%H%M%S")
            backup.mkdir(parents=True, exist_ok=True)
            shutil.copy2(destination, backup / destination.name)
        os.replace(staged, destination)
        print("rendered " + destination.name, flush=True)


def audit(asset):
    path = OUTPUT / asset["file"]
    result = audio.audit({**asset, "kind": "bgm"}, path, {"sample_rate": 44100})
    if result.get("duration", 0) > 0:
        measured = loudness(path)
        result["integrated_lufs"] = float(measured["input_i"])
        result["true_peak_dbtp"] = float(measured["input_tp"])
        result["ok"] &= result["true_peak_dbtp"] <= -3 and result["integrated_lufs"] <= -19.5
        result["ok"] &= abs(result["duration"] - (asset["seconds"] - asset["crossfade"])) < 0.15
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--audit-only", action="store_true")
    parser.add_argument("--force", action="store_true")
    parser.add_argument("--only", nargs="*")
    args = parser.parse_args()
    audio.require_tools()
    plan = json.loads(PLAN.read_text(encoding="utf-8"))
    jobs = [j for j in plan["jobs"] if not args.only or j["id"] in args.only]
    if args.only and set(args.only) - {j["id"] for j in jobs}:
        parser.error("Unknown music job")
    assets = [a for a in plan["assets"] if a["job"] in {j["id"] for j in jobs}]
    assert len({a["file"] for a in assets}) == len(assets)
    for asset in assets:
        assert Path(asset["file"]).name == asset["file"] and asset["file"].endswith(".ogg")
        assert 0 < asset["crossfade"] < asset["seconds"] / 4
    if args.dry_run:
        print(json.dumps({"requests": len(jobs), "files": len(assets),
                          "jobs": [j["id"] for j in jobs], "assets": assets}, indent=2))
        return
    OUTPUT.mkdir(parents=True, exist_ok=True)
    CACHE.mkdir(parents=True, exist_ok=True)
    if not args.audit_only:
        key = audio.api_key(ROOT / "music-plan.json")
        for job in jobs:
            relevant = [a for a in assets if a["job"] == job["id"]]
            if not args.force and all((OUTPUT / a["file"]).exists() for a in relevant):
                continue
            raw = CACHE / (job["id"] + ".mp3")
            if not raw.exists() or args.force:
                request = urllib.request.Request(
                    "https://api.elevenlabs.io/v1/music?output_format=mp3_44100_128",
                    data=json.dumps(job["request"]).encode(),
                    headers={"xi-api-key": key, "Content-Type": "application/json", "Accept": "audio/mpeg"})
                print("composing " + job["id"], flush=True)
                try:
                    with urllib.request.urlopen(request, timeout=420) as response:
                        data = response.read()
                    if len(data) < 1000:
                        raise ValueError("Empty music response")
                    raw.write_bytes(data)
                except urllib.error.HTTPError as error:
                    raise SystemExit(f"Music API {job['id']}: HTTP {error.code}: "
                                     + error.read().decode("utf-8", "replace")[:400]) from None
            for asset in relevant:
                render(raw, asset, args.force)
    report = [audit(a) for a in assets]
    (HERE / "music-audit.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"checked": len(report), "passed": sum(r["ok"] for r in report),
                      "bytes": sum(r.get("bytes", 0) for r in report),
                      "failed": [r for r in report if not r["ok"]]}, indent=2), flush=True)
    if not all(r["ok"] for r in report):
        raise SystemExit("Music audit failed")


if __name__ == "__main__":
    main()
