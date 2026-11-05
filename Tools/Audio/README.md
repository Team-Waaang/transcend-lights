# Non-character game audio

53 generated assets: 50 SFX WAV files and 3 environmental MP3 loops. No BGM, dialogue, character/creature vocals, exertions, breathing or footsteps are generated or replaced. Existing `Assets/Voice/` files are untouched.

## Playback

`Assets/Game/Runtime/GameAudio.cs` loads `Resources/Audio/SfxCatalog.json`, then loads each clip on demand. Gameplay event hooks and the automatically attached `CutsceneSoundtrack` survive scene rebuilds; generated `.unity` and Timeline files do not need to be edited.

- `GameAudio.Play("rune_success", transform)` emits one cue.
- `GameAudio.Loop("ward_loop", transform, strength)` must be renewed while active. A destroyed/inactive owner or missed renewal fades it out.
- Runtime defaults: master 0.8, SFX 0.8, ambience 0.45; per-cue levels/cooldowns/priorities are in the catalog.
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
| Phone pairing and calibration | `ui_connect.wav` | 0.55 |  | 0.43 |
| Phone pairing and calibration | `ui_ready.wav` | 0.8 |  | 0.43 |
| Phone pairing and calibration | `ui_disconnect.wav` | 0.65 |  | 0.43 |
| UI and school-day transitions | `ui_confirm.wav` | 0.25 |  | 0.43 |
| UI and school-day transitions | `parchment_open.wav` | 0.9 |  | 0.38 |
| UI and school-day transitions | `parchment_close.wav` | 0.7 |  | 0.32 |
| UI and school-day transitions | `title_chime.wav` | 1.3 |  | 0.43 |
| UI and school-day transitions | `transition.wav` | 1.1 |  | 0.43 |
| UI and school-day transitions | `period_clear.wav` | 2.3 |  | 0.66 |
| UI and school-day transitions | `game_complete.wav` | 3.2 |  | 0.66 |
| Wand equipment magic | `magic_ignite.wav` | 0.5 |  | 0.43 |
| Wand equipment magic | `beam_start.wav` | 0.4 |  | 0.32 |
| Wand equipment magic | `beam_loop.wav` | 4 | yes | 0.13 |
| Wand equipment magic | `beam_stop.wav` | 0.35 |  | 0.2 |
| Rune inscription and judgments | `rune_trace_loop.wav` | 4 | yes | 0.16 |
| Rune inscription and judgments | `rune_success.wav` | 1.6 |  | 0.43 |
| Rune inscription and judgments | `rune_fail.wav` | 0.95 |  | 0.43 |
| Rune inscription and judgments | `rune_change.wav` | 1 |  | 0.43 |
| Wand equipment magic | `torch_loop.wav` | 4 | yes | 0.09 |
| Light discovery and mineral transformation | `magic_reveal.wav` | 0.65 |  | 0.43 |
| Light discovery and mineral transformation | `petrify.wav` | 1.3 |  | 0.43 |
| Light discovery and mineral transformation | `petrify_loop.wav` | 4 | yes | 0.12 |
| Boss spell patterns and defenses | `curse_charge.wav` | 1.2 |  | 0.43 |
| Boss spell patterns and defenses | `orb_launch.wav` | 0.55 |  | 0.32 |
| Boss spell patterns and defenses | `orb_break.wav` | 0.65 |  | 0.43 |
| Boss spell patterns and defenses | `curse_impact.wav` | 0.95 |  | 0.43 |
| Boss spell patterns and defenses | `spell_hit.wav` | 0.3 |  | 0.18 |
| Boss spell patterns and defenses | `ward_raise.wav` | 1.1 |  | 0.43 |
| Boss spell patterns and defenses | `ward_loop.wav` | 4 | yes | 0.15 |
| Boss spell patterns and defenses | `ward_hit.wav` | 0.4 |  | 0.24 |
| Boss spell patterns and defenses | `ward_break.wav` | 1.7 |  | 0.66 |
| Boss spell patterns and defenses | `ward_expire.wav` | 0.85 |  | 0.43 |
| Boss spell patterns and defenses | `teleport.wav` | 0.8 |  | 0.43 |
| Boss spell patterns and defenses | `summon.wav` | 1.35 |  | 0.43 |
| Boss spell patterns and defenses | `clone_appear.wav` | 0.85 |  | 0.43 |
| Boss spell patterns and defenses | `clone_break.wav` | 0.75 |  | 0.43 |
| Boss spell patterns and defenses | `chain_bind.wav` | 1.05 |  | 0.43 |
| Boss spell patterns and defenses | `chain_loop.wav` | 4 | yes | 0.12 |
| Boss spell patterns and defenses | `chain_break.wav` | 1.2 |  | 0.43 |
| Cooperative resonance QTE | `qte_start.wav` | 1.15 |  | 0.66 |
| Cooperative resonance QTE | `qte_tap.wav` | 0.22 |  | 0.29 |
| Cooperative resonance QTE | `qte_retry.wav` | 0.8 |  | 0.43 |
| Cooperative resonance QTE | `resonance.wav` | 1.4 |  | 0.66 |
| Cooperative resonance QTE | `qte_loop.wav` | 4 | yes | 0.19 |
| Cooperative resonance QTE | `ultimate.wav` | 2.8 |  | 0.66 |
| Cinematic environment and VFX | `blast.wav` | 2.5 |  | 0.66 |
| Cinematic environment and VFX | `purify.wav` | 3 |  | 0.66 |
| Cinematic environment and VFX | `ominous_pulse.wav` | 0.75 |  | 0.38 |
| Cinematic environment and VFX | `blackout.wav` | 1 |  | 0.43 |
| Cinematic environment and VFX | `cutscene_slam.wav` | 0.9 |  | 0.43 |
| Environmental sound beds | `classroom_ambience.mp3` | 12 | yes | 0.17 |
| Environmental sound beds | `dark_hall_ambience.mp3` | 12 | yes | 0.2 |
| Environmental sound beds | `arena_ambience.mp3` | 12 | yes | 0.19 |

## Verification

`audio-audit.json` records per-file decoding, sample rate, channels, duration, peak, silence and loop-boundary checks. `playback-report.json` records actual Unity listener output and event/source observations during the smoke run. Numerical playback verification does not constitute subjective listening approval. Physical phone pairing/reconnection requires real devices.

Automated file checks: **53/53 passed** (6.63 MiB source audio). Unity compilation and the final runtime console have no errors. The actual results-screen recheck (`results-verification.json`) observed exactly one enabled listener and audible victory output (peak 0.3845, below clipping). `verification-summary.json` lists gameplay coverage and remaining device/listening checks.
