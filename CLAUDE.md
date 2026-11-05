# ai-hackathon (Team Waaang)

Two-player magic-school game in Unity 6000.4.7f1 (URP 17, Cinemachine 3, Timeline, Input System, lilToon).
Players use their phones as air mice (`PhonePointerSDK`) to aim wand lasers; with no phones, mouse test mode
drives both (left click = P1 Amelia, right click = P2 Nox). The game runs in periods (교시):
1교시 rune class, 2교시 (TBD), 3교시 boss fight (`Assets/Game/Period3/Period3.unity`, see `Docs/Period3.md`).

## Layout

- `Assets/Cutscene/Editor/`: scene builders. `CutsceneKit` (shared shots/VFX/volumes/Timeline helpers),
  `BossArena` (Period 3 arena background), `Boss{Entrance,Phase2,Defeat}CutsceneBuilder`,
  `Period1CutsceneBuilder`, `PoseRig` / `PeriodPoseClips` (code-keyed character clips), `CutscenePreview`.
- `Assets/Cutscene/Runtime/`: `PeriodIntro` (title card, skip, end hand-over), `ResonanceQte` (defeat-cutscene QTE), `BeamTailFollow`, `Spin`, `Levitate`.
- `Assets/Game/`: `GameManager` (players, intro → play hand-over), `RunePeriod` (Period 1 rules),
  `BossPeriod` / `BossController` / `CurseOrb` / `WardFx` / `CasterMotion` (Period 3; ward shader in `Shaders/Ward.shader`), `CasterInput` (phone/mouse without a rig), `GameGui` (IMGUI HUD kit),
  `Editor/Period3Builder` (Tools/Period 3/Build Period 3 Scene),
  `CasterProfile` assets (`Profiles/`), rune stones (`RuneStoneFx`, `RuneTrace`, `RuneDefinition`, `RuneStoneBaker`).
- `Assets/Waaang/`: caster rig (`WandPointerRig`, `CasterPose`, `ArmIk`, `SecondCaster`), phone link, and
  `Editor/WaaangSceneSetup` (Connection/Game scenes, `CreateCaster(profile, ...)`).
- `Assets/Parchment/`: period title card (unrolling scroll).
- Art: `Assets/TripoModels/` (characters, wands, boss, rune stones), VFX packs `Assets/VFX_Klaus/` (HCFX_*) and
  `Assets/MasterStylizedProjectiles/`.

## Conventions

- Generated scenes are output: change the builder and re-run its menu (`Tools/Cutscene/...`, `Waaang/...`),
  never hand-edit the scene. Rebuilds rewrite clip/volume/material assets and their `.meta` files; that churn is expected.
- Builders' `Build()` opens a new scene in Single mode (discards unsaved open scenes); the menus ask to save first.
- Boss cutscene frame: boss at the origin facing -X (`CutsceneKit.BossRotation`), players off-screen at -X.
  Game/Period 1 frame: players 1.5x scale facing +Z, Amelia left, Nox right.
- Code comments and commit messages in English; PR titles in Korean. Commits end with a Co-Authored-By line.

## Checking work while the editor is open

The project is usually open in the Unity editor, which locks batch mode. `Assets/Editor/ClaudeBridge.cs` runs
static editor methods from files: write `Temp/ClaudeBridge/<id>.cmd` with `Namespace.Type.Method|arg|arg`, read
`<id>.out`; touch `Temp/ClaudeBridge/refresh.req` to recompile after editing code (watch `alive.txt` change).
Useful calls:
- `AiHackathon.Cutscene.Editor.CutscenePreview.SafeBuild|BossDefeatCutsceneBuilder` (refuses if a scene is dirty)
- `AiHackathon.Cutscene.Editor.CutscenePreview.RenderSceneCsv|<scene path>|1.5,4.2,...` → PNGs in `Temp/CutscenePreview/`
- `PlayProbe.Play|<scene>`, `PlayProbe.Status`, `PlayProbe.SetHp|55`, `PlayProbe.Seek|t`, `PlayProbe.MashQte|30`, `PlayProbe.Shot|name`, `PlayProbe.Stop` drive Play mode.
Compile errors land in `%LOCALAPPDATA%/Unity/Editor/Editor.log` (`error CS`).
