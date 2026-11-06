using System.Collections.Generic;
using System.IO;
using AiHackathon.Game;
using AiHackathon.Game.Editor;
using AiHackathon.Parchment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Timeline;
using UnityEngine.UI;
using Waaang;
using Waaang.EditorTools;
using static AiHackathon.Cutscene.Editor.CutsceneKit;

namespace AiHackathon.Cutscene.Editor
{
    /// <summary>
    /// Period 1 ("룬"): a sunlit rune classroom with a stone ahead of each player. Amelia's proud bow, Nox's taunt,
    /// both draw and their stones light, the parchment title card unrolls, then the camera rises behind them into the
    /// Game scene's duo framing as they take the two-handed stance, and play begins in the same scene:
    /// <see cref="GameManager"/> hands the characters to the game's aiming rigs and <see cref="RunePeriod"/> judges
    /// each player's trace on her own stone.
    /// Characters come from the <see cref="CasterProfile"/> assets, stones from the baked <see cref="RuneDefinition"/>s.
    /// </summary>
    public static class Period1CutsceneBuilder
    {
        const string Root = "Assets/Cutscene/Period1";
        const string ScenePath = Root + "/Period1Cutscene.unity";
        const string TimelinePath = Root + "/Period1.playable";

        // Beats (seconds)
        const float TBow = 2.5f;          // Amelia_A1 starts (hand to chest 2.85, bow 3.55)
        const float TNox = 5.7f;          // Nox_N1 starts (wand up 5.9)
        const float TDrawA = 8.4f;        // Amelia_A2: palm up 8.55, circle 9.3 → 11.0
        const float TDrawN = 9.0f;        // Nox_N2: sweep 9.8 → 10.45
        const float TIgnite = 11.0f;      // the runes light
        const float TCardIn = 11.8f;
        const float TCardOut = 15.2f;
        const float TReady = 15.2f;       // both take the game stance
        const float TOpen = 16.2f;        // letterbox slides away
        const float TEnd = 17.6f;

        static readonly Color Gold = new Color(1f, 0.78f, 0.35f);

        [MenuItem("Tools/Cutscene/Build Period 1 Cutscene")]
        static void BuildFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build();
        }

        public static void Build() => Build(false);

        /// <summary>
        /// <paramref name="keepOpenScenes"/>: build in an extra scene and close it afterwards, leaving whatever is
        /// open (and unsaved) untouched.
        /// </summary>
        public static void Build(bool keepOpenScenes)
        {
            var c = PeriodSceneKit.Begin(Root, TimelinePath, 0, keepOpenScenes);
            var timeline = c.timeline;
            var director = c.director;
            var amelia = c.Amelia;
            var nox = c.Nox;
            var stones = c.stones;
            var profiles = c.profiles;
            // Amelia's palm writes the circle in gold; the tip of Nox's wand leaves her blue.
            var palmTrail = PeriodSceneKit.Trail(FindBone(amelia.transform, "Left_Hand"), PeriodPoseClips.PalmLocal(PeriodPoseClips.AmeliaFbx, "Left_"), "PalmTrail", Gold, 0.06f, 1.4f);
            var tipTrail = PeriodSceneKit.Trail(c.rigs[1].staff.Find("Staff Tip"), Vector3.zero, "WandTrail", PeriodSceneKit.NoxColor, 0.055f, 0.7f);

            var fx = BuildFx(c.runeCenters, c.runeColors, out var controlled);
            c.intro.title = "룬";
            c.intro.subtitle = "손끝이 떨리면, 룬도 떨린다.";
            c.intro.showAt = TCardIn;
            c.intro.hideAt = TCardOut;
            c.intro.skipTo = TReady;
            c.period.clearedText = "1교시 통과!";

            var chars = timeline.CreateTrack<GroupTrack>(null, "Characters");
            var bow = PeriodPoseClips.Load("Amelia_A1_Bow");
            var rune = PeriodPoseClips.Load("Amelia_A2_Rune");
            var ameliaReady = PeriodPoseClips.Load("Amelia_Ready");
            var taunt = PeriodPoseClips.Load("Nox_N1_Taunt");
            var flourish = PeriodPoseClips.Load("Nox_N2_Flourish");
            var noxReady = PeriodPoseClips.Load("Nox_Ready");

            var aTrack = timeline.CreateTrack<AnimationTrack>(chars, "Amelia Anim");
            director.SetGenericBinding(aTrack, amelia);
            // Each clip's first frames are a still stance, played slowly to hold until its beat.
            Place(aTrack, bow, 0f, TBow, 0f, 0.1f);
            Place(aTrack, bow, TBow, TDrawA + 0.3f - TBow);
            Place(aTrack, rune, TDrawA, TReady + 0.4f - TDrawA);
            Place(aTrack, ameliaReady, TReady, TEnd - TReady);

            var nTrack = timeline.CreateTrack<AnimationTrack>(chars, "Nox Anim");
            director.SetGenericBinding(nTrack, nox);
            Place(nTrack, taunt, 0f, TNox, 0f, 0.025f);
            Place(nTrack, taunt, TNox, TDrawN + 0.5f - TNox);
            Place(nTrack, flourish, TDrawN, TReady + 0.4f - TDrawN);
            Place(nTrack, noxReady, TReady, TEnd - TReady);

            var trails = timeline.CreateTrack<GroupTrack>(null, "Trails");
            AddAnimation(timeline, trails, director, "Palm Trail", palmTrail, PeriodSceneKit.EmitClip("Trail_Palm", 9.3f, 11.0f, TEnd), TEnd);
            AddAnimation(timeline, trails, director, "Wand Trail", tipTrail, PeriodSceneKit.EmitClip("Trail_Wand", 9.75f, 10.6f, TEnd), TEnd);

            var stoneGroup = timeline.CreateTrack<GroupTrack>(null, "Rune Stones");
            var stoneClip = BuildStoneClip();
            for (int i = 0; i < 2; i++)
                AddAnimation(timeline, stoneGroup, director, "Stone " + profiles[i].displayName, stones[i].gameObject.AddComponent<Animator>(), stoneClip, TEnd);

            AddShots(timeline, director, c.cam, new[]
            {
                // High from the window corner: the stones in the foreground, the pair beyond in the sun.
                new Shot { Name = "A_Classroom", Start = 0f, End = 3.0f, From = new Vector3(-2.6f, 2.15f, 4.4f), To = new Vector3(-2.2f, 1.9f, 4.0f), LookPoint = new Vector3(0f, 0.9f, 1.0f), Fov = 46f, Shake = 0.06f },
                // Near-frontal mediums, tight enough that the other girl stays out of frame.
                new Shot { Name = "B_Amelia", Start = 2.6f, End = 6.0f, From = new Vector3(-0.97f, 1.3f, 1.68f), To = new Vector3(-0.92f, 1.28f, 1.46f), LookPoint = new Vector3(-0.78f, 1.24f, 0f), Fov = 34f, Shake = 0.05f },
                new Shot { Name = "C_Nox", Start = 5.85f, End = 8.7f, From = new Vector3(0.97f, 1.27f, 1.58f), To = new Vector3(0.92f, 1.3f, 1.39f), LookPoint = new Vector3(0.78f, 1.25f, -0.08f), Fov = 34f, Shake = 0.05f },
                // Low between the stones, looking back up at the pair as they draw.
                new Shot { Name = "D_Draw", Start = 8.6f, End = 11.6f, From = new Vector3(0f, 0.5f, 1.65f), To = new Vector3(0f, 0.58f, 1.45f), LookPoint = new Vector3(0f, 1.12f, 0f), Fov = 56f, Shake = 0.06f, Kicks = new[] { TIgnite + 0.03f }, KickPeak = 0.7f, KickDecay = 0.7f },
                // Cut to the two lit stones; it holds under the title card.
                new Shot { Name = "E_Stones", Start = 11.6f, End = 16.0f, From = new Vector3(0f, 1.12f, -0.45f), To = new Vector3(0f, 1.05f, 0.15f), LookPoint = new Vector3(0f, 0.82f, 2.3f), Fov = 44f, Shake = 0.05f },
                // Pulls back out between the two and rises into the play camera behind them.
                new Shot { Name = "F_ToGame", Start = 15.2f, End = TEnd, From = new Vector3(0f, 1.3f, -0.3f), To = c.playCam, LookPoint = c.playCamLook, Fov = 50f, FovTo = PeriodSceneKit.PlayCamFov, Shake = 0.03f },
            });

            var fxGroup = timeline.CreateTrack<GroupTrack>(null, "FX");
            AddAnimation(timeline, fxGroup, director, "Lights Grading", fx, BuildFxClip(), TEnd);
            AddAnimation(timeline, fxGroup, director, "Fade Letterbox", c.ui, BuildUiClip(), TEnd);
            AddControlled(timeline, fxGroup, director, controlled);

            PeriodSceneKit.Finish(c, ScenePath);
        }

        static AnimationClip BuildStoneClip()
        {
            var clip = new AnimationClip { name = "RuneStone_Period1" };
            // Asleep, a flicker answers Amelia's palm, it catches as the circle closes, flares and settles to the
            // steady glow it keeps through the title; ends at the idle glow it waits with during play.
            Bind(clip, "", typeof(RuneStoneFx), "cutsceneGlow", Smooth(0f, 0f, 9.3f, 0f, 9.7f, 0.18f, 10.2f, 0.08f, 10.6f, 0.3f,
                TIgnite, 0.75f, TIgnite + 0.1f, 1.25f, TIgnite + 0.8f, 0.75f, TCardOut, 0.75f, TOpen, 0.35f, TEnd, 0.35f));
            Bind(clip, "", typeof(RuneStoneFx), "cutsceneHover", Smooth(0f, 0f, TIgnite - 0.1f, 0f, TIgnite + 0.9f, 0.07f,
                13.4f, 0.09f, TCardOut, 0.07f, TOpen, 0.04f, TEnd, 0.04f));
            return SaveClip(clip);
        }

        // ------------------------------------------------------------------ lights, grading, VFX, UI

        static Animator BuildFx(Vector3[] runeCenters, Color[] runeColors, out List<(GameObject, float, float)> controlled)
        {
            controlled = new List<(GameObject, float, float)>();
            var fx = new GameObject("FX");
            var animator = fx.AddComponent<Animator>();

            var lights = new GameObject("Lights").transform;
            lights.SetParent(fx.transform);
            // Sun through the window wall, toward the students.
            var sun = Light(lights, "Sun", LightType.Directional, new Color(1f, 0.9f, 0.76f), 1.25f, 0f);
            sun.transform.rotation = Quaternion.Euler(38f, 200f, 0f);
            sun.shadows = LightShadows.Soft;
            // Cool back light from the room behind them, to cut their silhouettes from the warm room.
            var rim = Light(lights, "Rim", LightType.Spot, new Color(0.65f, 0.78f, 1f), 6f, 7f);
            rim.transform.position = new Vector3(0.3f, 2.9f, -2.2f);
            rim.transform.LookAt(new Vector3(0f, 1.1f, 0f));
            rim.spotAngle = 60f;
            rim.shadows = LightShadows.None;
            // Rune bounce on the two faces when the stone lights.
            var bounce = Light(lights, "RuneBounce", LightType.Point, new Color(1f, 0.92f, 0.8f), 0f, 3.2f);
            bounce.transform.position = (runeCenters[0] + runeCenters[1]) * 0.5f + new Vector3(0f, 0.2f, -1.2f);
            bounce.shadows = LightShadows.None;

            var volumes = new GameObject("Volumes").transform;
            volumes.SetParent(fx.transform);
            Volume(volumes, "Base", 0, 1f, Profile("Period1Base", PeriodBaseGrade));
            // Under the title card: the scene sinks back and the far room blurs so the parchment reads.
            Volume(volumes, "Card", 2, 0f, Profile("Period1Card", p =>
            {
                var ca = p.Add<ColorAdjustments>(); ca.postExposure.Override(-0.75f); ca.saturation.Override(-25f);
                var v = p.Add<Vignette>(); v.intensity.Override(0.42f);
                var d = p.Add<DepthOfField>(); d.mode.Override(DepthOfFieldMode.Gaussian); d.gaussianStart.Override(2.6f); d.gaussianEnd.Override(7f); d.gaussianMaxRadius.Override(1.2f);
            }));
            Volume(volumes, "Impact", 3, 0f, Profile("Period1Impact", p =>
            {
                var b = p.Add<Bloom>(); b.intensity.Override(2.6f); b.threshold.Override(0.7f);
                p.Add<ColorAdjustments>().postExposure.Override(0.3f);
                p.Add<ChromaticAberration>().intensity.Override(0.25f);
            }));

            var vfx = new GameObject("VFX").transform;
            vfx.SetParent(fx.transform);
            controlled.Add((Motes(vfx, "GoldDust", new Color(1f, 0.88f, 0.6f, 0.55f), 28f, 0.014f, 0.025f), 0f, TEnd));
            // While they draw, faint motes drift into each rune; when it catches, a thin ring runs across its dais.
            for (int i = 0; i < runeCenters.Length; i++)
            {
                float max = Mathf.Max(runeColors[i].r, Mathf.Max(runeColors[i].g, runeColors[i].b, 0.001f));
                var tint = new Color(runeColors[i].r / max, runeColors[i].g / max, runeColors[i].b / max);
                var gather = Tint(Vfx(vfx, "HCFX_Gathering_Star", runeCenters[i] + Vector3.back * 0.05f, 0.06f, 0.5f), tint);
                gather.name = "Gather " + i;
                controlled.Add((gather, 9.4f, TIgnite + 0.2f));
                var stoneBase = new GameObject("StoneBase " + i).transform;
                stoneBase.SetParent(vfx);
                stoneBase.position = PeriodSceneKit.StonePos[i];
                var ring = FloorRing(stoneBase, 0.16f, tint);
                controlled.Add((ring, TIgnite, TIgnite + 1.4f));
            }
            return animator;
        }

        static AnimationClip BuildFxClip()
        {
            var clip = new AnimationClip { name = "FX_Period1" };
            Bind(clip, "Lights/RuneBounce", typeof(Light), "m_Intensity", Smooth(0f, 0f, 10.6f, 0.08f, TIgnite, 0.35f, TIgnite + 0.1f, 0.6f, TIgnite + 0.8f, 0.35f, TCardOut, 0.25f, TOpen, 0f, TEnd, 0f));
            Bind(clip, "Lights/Sun", typeof(Light), "m_Intensity", Smooth(0f, 1.25f, TCardIn, 1.25f, TCardIn + 0.5f, 0.9f, TCardOut, 0.9f, TCardOut + 0.6f, 1.25f));
            Bind(clip, "Volumes/Card", typeof(Volume), "weight", Smooth(0f, 0f, TCardIn - 0.1f, 0f, TCardIn + 0.4f, 1f, TCardOut, 1f, TCardOut + 0.5f, 0f));
            Bind(clip, "Volumes/Impact", typeof(Volume), "weight", Lin(Pulses(0f, 0.35f, 0.8f, TIgnite)));
            return SaveClip(clip);
        }

        static AnimationClip BuildUiClip()
        {
            var clip = new AnimationClip { name = "UI_Period1" };
            Bind(clip, "BlackFade", typeof(Image), "m_Color.a", Smooth(0f, 1f, 0.2f, 1f, 1.2f, 0f, TEnd, 0f));
            Bind(clip, "WhiteFlash", typeof(Image), "m_Color.a", Lin(Pulses(0f, 0.05f, 0.4f, TIgnite)));
            // Bars slide off as the shot settles into the game's framing.
            Bind(clip, "LetterboxTop", typeof(RectTransform), "m_AnchorMin.y", Smooth(0f, 0.88f, TOpen, 0.88f, TOpen + 0.6f, 1f));
            Bind(clip, "LetterboxBottom", typeof(RectTransform), "m_AnchorMax.y", Smooth(0f, 0.12f, TOpen, 0.12f, TOpen + 0.6f, 0f));
            return SaveClip(clip);
        }
    }
}
