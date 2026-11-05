# Lesson and boss music

Six instrumental loops generated with ElevenLabs Music `music_v1`. The boss variations are three consecutive sections of one composition plan at 120 BPM in D minor, with the same requested motif; phase 2 increases subdivisions and percussion, while resonance adds a cooperative finale arrangement. Rune class and the dark hall use separate tempos, palettes and moods. Subjective listening approval remains pending.

| Track | Runtime use | Loop duration | LUFS | True peak dBTP |
| --- | --- | ---: | ---: | ---: |
| `bgm_rune_class.ogg` | Connection and period 1 rune class | 38.4 s | -19.98 | -3.98 |
| `bgm_dark_hall.ogg` | Period 2 light hunt | 48.0 s | -19.96 | -7.97 |
| `bgm_boss_phase1.ogg` | Boss entrance, title and phase 1 | 32.0 s | -19.99 | -6.15 |
| `bgm_boss_phase2.ogg` | Boss awakening cutscene and phase 2 | 32.0 s | -20.0 | -6.77 |
| `bgm_boss_resonance.ogg` | Final beam-clash cutscene and live QTE | 32.0 s | -19.99 | -6.31 |
| `bgm_victory.ogg` | Purification and final results | 32.0 s | -19.98 | -7.18 |

## Runtime

`GameMusic` is attached to the persistent `GameAudio` service. It selects the lesson from the scene and the boss variation from `BossPeriod.Current`. Boss entrance/title use phase 1; awakening and phase 2 use phase 2; defeat/clash/QTE use resonance; purification at timeline 28.7 s and results use victory. QTE retries retain playback position instead of restarting the score. Scene rebuilding does not require music references.

Two stereo sources crossfade with bounded linear amplitude. Streaming import avoids loading all six decoded tracks into memory. GameAudio applies shared master volume, voice-aware ducking and important-event ducking. Desktop music is already covered by the phone APK audio-folder exclusion.

## Generation and checks

```powershell
python Tools/Audio/generate_music.py --dry-run
python Tools/Audio/generate_music.py
python Tools/Audio/generate_music.py --audit-only
python Tools/Audio/generate_music.py --force --only boss_suite
```

The exact prompts/composition plan and split positions are in `music-plan.json`. The API key is read from the project `.env` and is not logged. Generation requires Music API permission and an eligible paid account. The generator reuses cached raw compositions and keeps existing final files by default. `--force` makes a new generation, backing up replaced final clips. Cache and backups are under ignored `Tools/Audio/.cache/music/` so editor restarts do not remove them.

The final bars crossfade into the beginning, followed by measured constant-gain normalization, 3 ms boundary smoothing and 44.1 kHz stereo Ogg Vorbis 128 kbps export. `music-audit.json` checks decoding, duration, channels, peak, LUFS and loop head/tail balance. All six files passed. The files add 2.94 MiB of source audio.

Official API contract: [ElevenLabs Compose Music](https://elevenlabs.io/docs/api-reference/music/compose).

Unity imported the MP3 exports with 2,304 extra samples (about 52 ms). Runtime assets therefore use Ogg Vorbis, which imports with the exact planned sample count. Original MP3 deliveries are retained under ignored `.cache/music/mp3-exports/`. Music resource IDs are extension-independent. The purification state is retained while the additive cutscene unloads, preventing a one-frame switch back to resonance.

## Final runtime verification

- All six tracks selected through actual lesson/boss flow, with at most two playing music sources and one persistent audio service/listener.
- QTE retry retains music playback position. Purification remains on victory while its cutscene unloads.
- Victory naturally completes a loop in the final Ogg build. Unity imports exact planned sample counts without the MP3 padding.
- During speech, measured music gain changed from 0.416 to 0.1248 (30%); the steady beam loop changed from 0.1531 to 0.1072 (70%), then music recovered to 0.4142.
- Final combined output peak was 0.6013, below clipping. Compilation and game runtime checks have no errors.
- Reports: `music-audit.json`, `music-mix-verification.json`, `music-flow-verification.json`, and `music-verification-summary.json`. Subjective listening and physical two-phone/Android packaging checks remain.
