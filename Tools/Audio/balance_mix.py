"""Measure voice clips and rebalance catalog gains without changing source audio."""
import concurrent.futures
import json
import math
from pathlib import Path
import re

import audio_pipeline as audio
from generate_music import loudness

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).parent


def main():
    catalog_path = ROOT / "Assets/Game/Resources/Audio/SfxCatalog.json"
    catalog = json.loads(catalog_path.read_text(encoding="utf-8"))
    audit = {Path(r["file"]).stem: r for r in json.loads((HERE / "audio-audit.json").read_text())}
    changes = []
    loop_targets = {"beam_loop": -40, "rune_trace_loop": -39, "torch_loop": -43,
                    "petrify_loop": -40, "ward_loop": -40, "chain_loop": -40, "qte_loop": -37}
    targets = {"rune_success": -29, "rune_fail": -31, "rune_change": -32, "magic_reveal": -31,
               "orb_launch": -32, "orb_break": -30, "spell_hit": -37, "ward_hit": -34,
               "summon": -33, "curse_charge": -32, "curse_impact": -29, "chain_bind": -30,
               "chain_break": -28, "ward_break": -27, "qte_tap": -33, "qte_start": -29,
               "qte_retry": -31, "resonance": -28, "ultimate": -29, "blast": -28,
               "purify": -29, "period_clear": -28, "game_complete": -28}
    for cue in catalog["cues"]:
        name = cue["name"]
        if cue["ambience"]:
            target, cap, bus = -49, 0.28, 0.8 * 0.35
        elif cue["loop"]:
            target, cap, bus = loop_targets.get(name, -40), 0.24, 0.8 * 0.8
        else:
            target, cap, bus = targets.get(name, -31), 0.72, 0.8 * 0.8
            if name in ("spell_hit", "ward_hit", "qte_tap"):
                cap = 0.30
        mean = audit[name]["mean_db"]
        level = round(min(cap, max(0.045, 10 ** ((target - mean) / 20) / bus)), 4)
        changes.append({"name": name, "previous_gain": cue["volume"], "gain": level,
                        "source_mean_dbfs": mean, "estimated_bus_mean_dbfs": round(mean + 20 * math.log10(level * bus), 2)})
        cue["volume"] = level
    catalog_path.write_text(json.dumps(catalog, indent=2) + "\n", encoding="utf-8")

    guid_paths = {}
    for meta in (ROOT / "Assets/Voice").rglob("*.mp3.meta"):
        match = re.search(r"^guid: (\w+)", meta.read_text(), re.M)
        if match:
            guid_paths[match.group(1)] = Path(str(meta)[:-5])
    bank = (ROOT / "Assets/Game/Resources/VoiceBank.asset").read_text(encoding="utf-8")
    assigned = re.findall(r"clip: \{fileID: 8300000, guid: (\w+)", bank)
    paths = sorted({guid_paths[g] for g in assigned if g in guid_paths})

    def measure(path):
        data = loudness(path)
        level, peak = float(data["input_i"]), float(data["input_tp"])
        # Respect headroom and cap boosts. Unity source.volume itself remains <= 1.
        db = min(-18 - level, -2 - peak, 20 * math.log10(1.3))
        gain = round(max(0.35, 10 ** (db / 20)), 4)
        return {"name": path.stem, "gain": gain, "integrated_lufs": level, "true_peak_dbtp": peak}

    with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
        voices = list(pool.map(measure, paths))
    config = {"master": 0.8, "effects": 0.8, "ambience": 0.35, "music": 0.52, "voice": 0.95,
              "musicVoiceDuck": 0.30, "musicEventDuck": 0.65, "effectsVoiceDuck": 0.70,
              "importantEffectsVoiceDuck": 0.88, "ambienceVoiceDuck": 0.50,
              "voiceAttack": 0.045, "voiceRelease": 0.6, "voiceHold": 0.18,
              "musicCrossfade": 1.8,
              "voiceLevels": [{"name": v["name"], "gain": v["gain"]} for v in voices]}
    (ROOT / "Assets/Game/Resources/Audio/MixSettings.json").write_text(json.dumps(config, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    report = {"source_audio_modified": False, "effects": changes, "voices": voices,
              "voice_assignments": len(assigned), "unique_voice_clips_measured": len(voices)}
    (HERE / "mix-analysis.json").write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"effects_balanced": len(changes), "voice_assignments": len(assigned),
                      "unique_voice_clips_measured": len(voices)}, indent=2))


if __name__ == "__main__":
    main()
