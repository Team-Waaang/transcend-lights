# Game audio: music, effects and voice balance

59 generated assets: 50 SFX WAV files, 3 environmental MP3 loops and 6 BGM Ogg Vorbis loops. Dialogue, character/creature vocals, exertions, breathing and footsteps are not generated or replaced. Existing `Assets/Voice/` files are untouched.

## Playback

`Assets/Game/Runtime/GameAudio.cs` loads `Resources/Audio/SfxCatalog.json`, then loads each clip on demand. Gameplay event hooks and the automatically attached `CutsceneSoundtrack` survive scene rebuilds; generated `.unity` and Timeline files do not need to be edited.

- `GameAudio.Play("rune_success", transform)` emits one cue.
- `GameAudio.Loop("ward_loop", transform, strength)` must be renewed while active. A destroyed/inactive owner or missed renewal fades it out.
- Runtime defaults: master 0.8, SFX 0.8, ambience 0.35, music 0.52 and voice 0.95. Shared settings and measured per-voice gains are in `Resources/Audio/MixSettings.json`; per-effect levels/cooldowns/priorities are in `SfxCatalog.json`.
- At most 16 one-shots and 16 loops. At most 3 simultaneous instances of a one-shot clip; cooldowns limit rapid repeats. Important completion and ultimate cues have priority.
- Character voice integration is separate from this service. The audio service never loads anything under `Assets/Voice/`.
- The Windows Build Profile now follows the existing global school-day scene list; its previous Period-1-only override prevented progression and boss cutscene loading. The phone controller uses its existing haptics; the desktop emits SFX once. Desktop-only audio Resources are excluded by the existing phone APK build filter.

## Asset contract

- One-shots: 44.1 kHz, mono PCM 16-bit WAV, peak normalized to -1.5 dBFS; leading silence trimmed, 2 ms attack smoothing and up to 30 ms fadeout.
- Continuous magic: 44.1 kHz mono PCM WAV, -4 dBFS peak, 250 ms loop crossfade and 3 ms boundary smoothing. Unity imports PCM, avoiding codec boundary padding.
- Environment: 44.1 kHz, stereo MP3 128 kbps, -4 dBFS target with a loop crossfade. Unity imports Vorbis in compressed memory.
- The boss results screen creates a listener only when no enabled listener remains after unloading the defeat cutscene, so the victory cue remains audible.
- Runtime gains leave headroom for two players, repeated impacts and multiple patterns. Ambient sound beds stay quieter than event cues.

## Reproduction

Provider: ElevenLabs Sound Generation, model `eleven_text_to_sound_v2`. All prompts, durations and scope are in `audio-plan.json`. The key is read from the project root `.env` or `ELEVENLABS_API_KEY`; it is never written to reports. Required tools: `python`, `ffmpeg`, `ffprobe`.

```powershell
python Tools/Audio/generate_sfx.py --dry-run
python Tools/Audio/generate_sfx.py --audit-only
python Tools/Audio/generate_sfx.py --only rune_success.wav
python Tools/Audio/generate_sfx.py --reprocess --only rune_success.wav
python Tools/Audio/generate_sfx.py --force --only rune_success.wav
```

Default generation keeps existing final files. `--force` requests a new generation; `--reprocess` renders cached raw audio without another generation request when the raw cache exists. Raw generations and timestamped replacement backups live under ignored `Temp/AudioGeneration/`. Unity may clear Temp on restart, so retain important raw audio/backups elsewhere before restarting. To restore a replaced audio file, copy its timestamped backup over the final file; keep its existing Unity `.meta`. The initial install adds new files and does not replace pre-existing sound assets.

The generation helper reuses the bundled `audio_pipeline.py` from the sound-generation skill for plan validation, safe key loading and measurement. Official API contract: https://elevenlabs.io/docs/api-reference/text-to-sound-effects/convert

## Inventory

| Category | File | Seconds requested | Loop | Catalog gain |
| --- | --- | ---: | :---: | ---: |
| Phone pairing and calibration | `ui_connect.wav` | 0.55 |  | 0.2013 |
| Phone pairing and calibration | `ui_ready.wav` | 0.8 |  | 0.3227 |
| Phone pairing and calibration | `ui_disconnect.wav` | 0.65 |  | 0.3082 |
| UI and school-day transitions | `ui_confirm.wav` | 0.25 |  | 0.4719 |
| UI and school-day transitions | `parchment_open.wav` | 0.9 |  | 0.6979 |
| UI and school-day transitions | `parchment_close.wav` | 0.7 |  | 0.6439 |
| UI and school-day transitions | `title_chime.wav` | 1.3 |  | 0.4404 |
| UI and school-day transitions | `transition.wav` | 1.1 |  | 0.3792 |
| UI and school-day transitions | `period_clear.wav` | 2.3 |  | 0.69 |
| UI and school-day transitions | `game_complete.wav` | 3.2 |  | 0.6821 |
| Wand equipment magic | `magic_ignite.wav` | 0.5 |  | 0.4884 |
| Wand equipment magic | `beam_start.wav` | 0.4 |  | 0.5608 |
| Wand equipment magic | `beam_loop.wav` | 4 | yes | 0.2392 |
| Wand equipment magic | `beam_stop.wav` | 0.35 |  | 0.2182 |
| Rune inscription and judgments | `rune_trace_loop.wav` | 4 | yes | 0.1458 |
| Rune inscription and judgments | `rune_success.wav` | 1.6 |  | 0.622 |
| Rune inscription and judgments | `rune_fail.wav` | 0.95 |  | 0.6365 |
| Rune inscription and judgments | `rune_change.wav` | 1 |  | 0.4941 |
| Wand equipment magic | `torch_loop.wav` | 4 | yes | 0.1967 |
| Light discovery and mineral transformation | `magic_reveal.wav` | 0.65 |  | 0.3663 |
| Light discovery and mineral transformation | `petrify.wav` | 1.3 |  | 0.72 |
| Light discovery and mineral transformation | `petrify_loop.wav` | 4 | yes | 0.24 |
| Boss spell patterns and defenses | `curse_charge.wav` | 1.2 |  | 0.4829 |
| Boss spell patterns and defenses | `orb_launch.wav` | 0.55 |  | 0.706 |
| Boss spell patterns and defenses | `orb_break.wav` | 0.65 |  | 0.72 |
| Boss spell patterns and defenses | `curse_impact.wav` | 0.95 |  | 0.3418 |
| Boss spell patterns and defenses | `spell_hit.wav` | 0.3 |  | 0.3 |
| Boss spell patterns and defenses | `ward_raise.wav` | 1.1 |  | 0.3792 |
| Boss spell patterns and defenses | `ward_loop.wav` | 4 | yes | 0.1158 |
| Boss spell patterns and defenses | `ward_hit.wav` | 0.4 |  | 0.2108 |
| Boss spell patterns and defenses | `ward_break.wav` | 1.7 |  | 0.5805 |
| Boss spell patterns and defenses | `ward_expire.wav` | 0.85 |  | 0.4254 |
| Boss spell patterns and defenses | `teleport.wav` | 0.8 |  | 0.4303 |
| Boss spell patterns and defenses | `summon.wav` | 1.35 |  | 0.72 |
| Boss spell patterns and defenses | `clone_appear.wav` | 0.85 |  | 0.2623 |
| Boss spell patterns and defenses | `clone_break.wav` | 0.75 |  | 0.5418 |
| Boss spell patterns and defenses | `chain_bind.wav` | 1.05 |  | 0.72 |
| Boss spell patterns and defenses | `chain_loop.wav` | 4 | yes | 0.24 |
| Boss spell patterns and defenses | `chain_break.wav` | 1.2 |  | 0.72 |
| Cooperative resonance QTE | `qte_start.wav` | 1.15 |  | 0.4016 |
| Cooperative resonance QTE | `qte_tap.wav` | 0.22 |  | 0.242 |
| Cooperative resonance QTE | `qte_retry.wav` | 0.8 |  | 0.5056 |
| Cooperative resonance QTE | `resonance.wav` | 1.4 |  | 0.6665 |
| Cooperative resonance QTE | `qte_loop.wav` | 4 | yes | 0.24 |
| Cooperative resonance QTE | `ultimate.wav` | 2.8 |  | 0.72 |
| Cinematic environment and VFX | `blast.wav` | 2.5 |  | 0.3012 |
| Cinematic environment and VFX | `purify.wav` | 3 |  | 0.4719 |
| Cinematic environment and VFX | `ominous_pulse.wav` | 0.75 |  | 0.2563 |
| Cinematic environment and VFX | `blackout.wav` | 1 |  | 0.5356 |
| Cinematic environment and VFX | `cutscene_slam.wav` | 0.9 |  | 0.4353 |
| Environmental sound beds | `classroom_ambience.mp3` | 12 | yes | 0.169 |
| Environmental sound beds | `dark_hall_ambience.mp3` | 12 | yes | 0.28 |
| Environmental sound beds | `arena_ambience.mp3` | 12 | yes | 0.1729 |

## Verification

`audio-audit.json` records per-file decoding, sample rate, channels, duration, peak, silence and loop-boundary checks. `playback-report.json` records actual Unity listener output and event/source observations during the smoke run. Numerical playback verification does not constitute subjective listening approval. Physical phone pairing/reconnection requires real devices.

Automated file checks: **53/53 passed** (6.63 MiB source audio). Unity compilation and the final runtime console have no errors. The actual results-screen recheck (`results-verification.json`) observed exactly one enabled listener and audible victory output (peak 0.3845, below clipping). `verification-summary.json` lists gameplay coverage and remaining device/listening checks.

Main-branch integration preserves all 36 existing voice-bank assignments. The ultimate SFX follows the QTE voice lead-in (`ultimateDelay`, default 0.6 s). The merged runtime check (`merge-verification.json`) observed the voice immediately on success, then simultaneous voice/effect playback after 0.606 s, with output peak 0.821 (below clipping).

## Music and dynamic mix

See `MUSIC.md` for the six-track score, generation and playback verification. Music is normalized near -20 LUFS, with true-peak headroom. The two music sources crossfade over 1.8 seconds and stream from disk. While speech is actually playing, music ducks to 30%, ordinary effects to 70%, important effects to 88% and ambience to 50%; attack is 45 ms, with 180 ms hold and 600 ms recovery. Important effects also briefly duck music to 65%. Dense ordinary effect bursts receive an additional gain reduction. Source audio and voice-bank clip assignments stay unchanged.

`python Tools/Audio/balance_mix.py` reproduces the measured gains from the SFX audit and 36 voice clips. `mix-analysis.json` records original perceived levels and resulting catalog/source gains.
