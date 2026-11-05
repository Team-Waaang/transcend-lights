using System.Collections.Generic;
using AiHackathon.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Timeline;
using UnityEngine.UI;
using static AiHackathon.Cutscene.Editor.CutsceneKit;

namespace AiHackathon.Cutscene.Editor
{
    /// <summary>
    /// Period 2 ("암흑"): the same classroom, mid-lesson. The sun flickers and the lights cut out; Amelia flinches and
    /// looks into the dark, Nox calmly lights the tip of her wand and sweeps the room with it, Amelia steadies herself
    /// and lights hers. With only the two wand lights left, the parchment card unrolls over the stones, their runes
    /// glow once and fade, and the camera rises into play: <see cref="RunePeriod"/> in memory mode shows each rune,
    /// lets it go dark, and the players trace it from memory by wand light.
    /// </summary>
    public static class Period2CutsceneBuilder
    {
        const string Root = "Assets/Cutscene/Period2";
        const string ScenePath = Root + "/Period2Cutscene.unity";
        const string TimelinePath = Root + "/Period2.playable";

        // Beats (seconds)
        const float TFlicker = 2.2f;     // the sun stutters
        const float TBlackout = 3.0f;    // lights out
        const float TFlinch = 2.95f;     // Amelia_A3 starts (flinch 3.0, looks left 3.85, right 4.85)
        const float TNox = 6.2f;         // Nox_N3 starts (wand up 6.4 → 7.1, sweeps 7.3 → 9.9)
        const float TTorchN = 7.0f;      // Nox's wand lights
        const float TSteady = 9.3f;      // Amelia_A4 starts (breath 9.4, wand up 10.2 → 11.0)
        const float TTorchA = 10.85f;    // Amelia's wand lights
        const float TCardIn = 12.3f;
        const float TCardOut = 15.4f;
        const float TReady = 15.4f;      // both take the game stance
        const float TOpen = 16.4f;       // letterbox slides away
        const float TEnd = 17.8f;

        static readonly Color Moon = new Color(0.55f, 0.66f, 1f);
        static readonly Color NightGlass = new Color(0.07f, 0.09f, 0.18f);
        static readonly Color DayGlass = new Color(1.9f, 1.75f, 1.45f);

        [MenuItem("Tools/Cutscene/Build Period 2 Cutscene")]
        static void BuildFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build();
        }

        public static void Build() => Build(false);

        public static void Build(bool keepOpenScenes)
        {
            // Lane offset 1: each player meets a different first rune than in Period 1.
            var c = PeriodSceneKit.Begin(Root, TimelinePath, 1, keepOpenScenes);
            var timeline = c.timeline;
            var director = c.director;

            c.intro.title = "암흑";
            c.intro.subtitle = "빛이 꺼져도 지팡이 끝은 기억한다.";
            c.intro.showAt = TCardIn;
            c.intro.hideAt = TCardOut;
            c.intro.skipTo = TReady;
            c.period.memory = true;
            // In the dark the stones (paint and all) sink to this, so the rune has to be remembered; the lit rune
            // shows only while RunePeriod reveals it.
            c.period.darkShade = 0.22f;
            c.period.clearedText = "2교시 통과!";

            var fx = BuildFx(c, out var controlled);

            var chars = timeline.CreateTrack<GroupTrack>(null, "Characters");
            var flinch = PeriodPoseClips.Load("Amelia_A3_Flinch");
            var steady = PeriodPoseClips.Load("Amelia_A4_Steady");
            var ameliaReady = PeriodPoseClips.Load("Amelia_Ready");
            var torch = PeriodPoseClips.Load("Nox_N3_Torch");
            var noxReady = PeriodPoseClips.Load("Nox_Ready");

            var aTrack = timeline.CreateTrack<AnimationTrack>(chars, "Amelia Anim");
            director.SetGenericBinding(aTrack, c.Amelia);
            // Each clip's first frames are a still stance, played slowly to hold until its beat.
            Place(aTrack, flinch, 0f, TFlinch, 0f, 0.015f);
            Place(aTrack, flinch, TFlinch, TSteady + 0.3f - TFlinch);
            Place(aTrack, steady, TSteady, TReady + 0.4f - TSteady);
            Place(aTrack, ameliaReady, TReady, TEnd - TReady);

            var nTrack = timeline.CreateTrack<AnimationTrack>(chars, "Nox Anim");
            director.SetGenericBinding(nTrack, c.Nox);
            Place(nTrack, torch, 0f, TNox, 0f, 0.02f);
            Place(nTrack, torch, TNox, TReady + 0.4f - TNox);
            Place(nTrack, noxReady, TReady, TEnd - TReady);

            // The wand lights come on at their beats and stay on into play: in the dark they are what the players
            // see by.
            var lights = timeline.CreateTrack<GroupTrack>(null, "Wand Lights");
            var lightAt = new[] { TTorchA, TTorchN };
            for (int i = 0; i < 2; i++)
            {
                var glow = TipGlow(c.rigs[i].staff.Find("Staff Tip"), i == 0 ? new Color(1f, 0.8f, 0.5f) : new Color(0.55f, 0.8f, 1f));
                var track = timeline.CreateTrack<ActivationTrack>(lights, c.profiles[i].displayName + " Wand Light");
                track.postPlaybackState = ActivationTrack.PostPlaybackState.LeaveAsIs;
                director.SetGenericBinding(track, glow);
                var clip = track.CreateDefaultClip();
                clip.start = lightAt[i];
                clip.duration = TEnd - lightAt[i];
            }

            var stoneGroup = timeline.CreateTrack<GroupTrack>(null, "Rune Stones");
            var stoneClip = BuildStoneClip();
            for (int i = 0; i < 2; i++)
                AddAnimation(timeline, stoneGroup, director, "Stone " + c.profiles[i].displayName, c.stones[i].gameObject.AddComponent<Animator>(), stoneClip, TEnd);

            // Windows go from daylight to night; the sun shafts go with the sun.
            var windowAnimator = c.room.windows.gameObject.AddComponent<Animator>();
            AddAnimation(timeline, timeline.CreateTrack<GroupTrack>(null, "Room"), director, "Windows", windowAnimator, BuildWindowClip(c.room.windows), TEnd);
            var shafts = timeline.CreateTrack<ActivationTrack>(null, "Sun Shafts");
            shafts.postPlaybackState = ActivationTrack.PostPlaybackState.LeaveAsIs;
            director.SetGenericBinding(shafts, c.room.shafts.gameObject);
            var shaftClip = shafts.CreateDefaultClip();
            shaftClip.start = 0f;
            shaftClip.duration = TBlackout - 0.15f;

            AddShots(timeline, director, c.cam, new[]
            {
                // From the other window corner: the class in daylight; the lights die inside this shot.
                new Shot { Name = "A_Classroom", Start = 0f, End = 3.6f, From = new Vector3(2.9f, 2.6f, 4.6f), To = new Vector3(2.5f, 2.35f, 4.2f), LookPoint = new Vector3(0f, 0.9f, 0.6f), Fov = 50f, Shake = 0.06f, Kicks = new[] { TBlackout }, KickPeak = 0.5f, KickDecay = 0.6f },
                new Shot { Name = "B_Amelia", Start = 3.5f, End = 6.5f, From = new Vector3(-1.0f, 1.32f, 1.85f), To = new Vector3(-0.95f, 1.3f, 1.62f), LookPoint = new Vector3(-0.78f, 1.28f, 0f), Fov = 38f, Shake = 0.05f },
                new Shot { Name = "C_Nox", Start = 6.4f, End = 9.4f, From = new Vector3(1.0f, 1.3f, 1.78f), To = new Vector3(0.95f, 1.33f, 1.56f), LookPoint = new Vector3(0.7f, 1.32f, -0.08f), Fov = 40f, Shake = 0.05f },
                // Both in front of the stones, their two lights in the dark.
                new Shot { Name = "D_Both", Start = 9.3f, End = 12.1f, From = new Vector3(0f, 1.0f, 2.0f), To = new Vector3(0f, 1.05f, 1.75f), LookPoint = new Vector3(0f, 1.25f, 0f), Fov = 58f, Shake = 0.05f },
                // The dark stones; their runes glow once under the title card and fade, to be remembered.
                new Shot { Name = "E_Stones", Start = 12.0f, End = 16.0f, From = new Vector3(0f, 1.12f, -0.45f), To = new Vector3(0f, 1.05f, 0.15f), LookPoint = new Vector3(0f, 0.82f, 2.3f), Fov = 44f, Shake = 0.05f },
                new Shot { Name = "F_ToGame", Start = 15.4f, End = TEnd, From = new Vector3(0f, 1.3f, -0.3f), To = c.playCam, LookPoint = c.playCamLook, Fov = 50f, FovTo = PeriodSceneKit.PlayCamFov, Shake = 0.03f },
            });

            var fxGroup = timeline.CreateTrack<GroupTrack>(null, "FX");
            AddAnimation(timeline, fxGroup, director, "Lights Grading", fx, BuildFxClip(), TEnd);
            AddAnimation(timeline, fxGroup, director, "Fade Letterbox", c.ui, BuildUiClip(), TEnd);
            AddControlled(timeline, fxGroup, director, controlled);

            PeriodSceneKit.Finish(c, ScenePath);
        }

        /// <summary>
        /// A light just past the wand head (inside it, it burns the ornament white) and a soft orb of glow.
        /// Starts off; the timeline switches it on.
        /// </summary>
        static GameObject TipGlow(Transform tip, Color color)
        {
            var go = new GameObject("Tip Glow");
            go.transform.SetParent(tip, false);
            go.transform.localPosition = new Vector3(0f, 0.07f, 0f);
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = 1.1f;
            light.range = 1.8f;
            light.shadows = LightShadows.None;

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.duration = 1f;
            main.startLifetime = 0.6f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.2f, 0.28f);
            main.startColor = new Color(color.r * 2.2f, color.g * 2.2f, color.b * 2.2f, 0.6f);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Shape;
            main.maxParticles = 30;
            var emission = ps.emission;
            emission.rateOverTime = 14f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.004f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(0f, 1f) });
            col.color = fade;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = ((UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline).defaultParticleMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            go.SetActive(false);
            return go;
        }

        static AnimationClip BuildStoneClip()
        {
            var clip = new AnimationClip { name = "RuneStone_Period2" };
            // Dark through the intro; under the card the runes surface once, hold, and sink away. Play starts dark
            // and RunePeriod shows each rune itself.
            Bind(clip, "", typeof(RuneStoneFx), "cutsceneGlow", Smooth(0f, 0f, TCardIn - 0.1f, 0f, TCardIn + 0.5f, 0.55f,
                13.6f, 0.85f, 14.6f, 0.55f, TCardOut, 0f, TEnd, 0f));
            Bind(clip, "", typeof(RuneStoneFx), "cutsceneHover", Lin(0f, 0f, TEnd, 0f));
            return SaveClip(clip);
        }

        // Flicker as multipliers on the lit value: stutters, dips, a last flare, then out.
        static readonly float[] Flicker2 = { 0f, 1f, TFlicker, 1f, TFlicker + 0.1f, 0.25f, TFlicker + 0.2f, 0.9f, TFlicker + 0.35f, 0.12f,
            TFlicker + 0.45f, 0.7f, TFlicker + 0.6f, 0.05f, TFlicker + 0.65f, 0.5f, TBlackout, 0f, TEnd, 0f };

        static float[] Scaled(float[] tv, float lit, float dark)
        {
            var o = (float[])tv.Clone();
            for (int i = 1; i < o.Length; i += 2) o[i] = Mathf.Lerp(dark, lit, o[i]);
            return o;
        }

        static AnimationClip BuildWindowClip(Transform windows)
        {
            var clip = new AnimationClip { name = "Windows_Period2" };
            foreach (Transform pane in windows)
            {
                Bind(clip, pane.name, typeof(MeshRenderer), "material._BaseColor.r", Lin(Scaled(Flicker2, DayGlass.r, NightGlass.r)));
                Bind(clip, pane.name, typeof(MeshRenderer), "material._BaseColor.g", Lin(Scaled(Flicker2, DayGlass.g, NightGlass.g)));
                Bind(clip, pane.name, typeof(MeshRenderer), "material._BaseColor.b", Lin(Scaled(Flicker2, DayGlass.b, NightGlass.b)));
            }
            return SaveClip(clip);
        }

        static Animator BuildFx(PeriodSceneKit.Context c, out List<(GameObject, float, float)> controlled)
        {
            controlled = new List<(GameObject, float, float)>();
            var fx = new GameObject("FX");
            var animator = fx.AddComponent<Animator>();
            var fader = fx.AddComponent<AmbientFader>();
            fader.viewCamera = c.cam;

            var lights = new GameObject("Lights").transform;
            lights.SetParent(fx.transform);
            var sun = Light(lights, "Sun", LightType.Directional, new Color(1f, 0.9f, 0.76f), 1.25f, 0f);
            sun.transform.rotation = Quaternion.Euler(38f, 200f, 0f);
            sun.shadows = LightShadows.Soft;
            // Moonlight through the same windows once the sun is gone: cool and faint, enough to read shapes.
            var moon = Light(lights, "Moon", LightType.Directional, Moon, 0f, 0f);
            moon.transform.rotation = Quaternion.Euler(30f, 195f, 0f);
            moon.shadows = LightShadows.Soft;
            var rim = Light(lights, "Rim", LightType.Spot, new Color(0.65f, 0.78f, 1f), 6f, 7f);
            rim.transform.position = new Vector3(0.3f, 2.9f, -2.2f);
            rim.transform.LookAt(new Vector3(0f, 1.1f, 0f));
            rim.spotAngle = 60f;
            rim.shadows = LightShadows.None;

            var volumes = new GameObject("Volumes").transform;
            volumes.SetParent(fx.transform);
            Volume(volumes, "Base", 0, 1f, Profile("Period2Base", p =>
            {
                p.Add<Tonemapping>().mode.Override(TonemappingMode.Neutral);
                var b = p.Add<Bloom>(); b.threshold.Override(1.1f); b.intensity.Override(0.6f); b.scatter.Override(0.65f);
                var v = p.Add<Vignette>(); v.intensity.Override(0.24f); v.smoothness.Override(0.5f);
                var ca = p.Add<ColorAdjustments>(); ca.contrast.Override(12f); ca.saturation.Override(8f);
                p.Add<WhiteBalance>().temperature.Override(8f);
            }));
            // Night grade: cooler, less saturated, heavier vignette.
            Volume(volumes, "Dark", 1, 0f, Profile("Period2Dark", p =>
            {
                var ca = p.Add<ColorAdjustments>(); ca.saturation.Override(-22f); ca.contrast.Override(8f);
                p.Add<WhiteBalance>().temperature.Override(-28f);
                var v = p.Add<Vignette>(); v.intensity.Override(0.42f); v.color.Override(new Color(0.02f, 0.03f, 0.08f));
            }));
            Volume(volumes, "Card", 2, 0f, Profile("Period2Card", p =>
            {
                var ca = p.Add<ColorAdjustments>(); ca.postExposure.Override(-0.5f); ca.saturation.Override(-20f);
                var v = p.Add<Vignette>(); v.intensity.Override(0.45f);
                var d = p.Add<DepthOfField>(); d.mode.Override(DepthOfFieldMode.Gaussian); d.gaussianStart.Override(2.6f); d.gaussianEnd.Override(7f); d.gaussianMaxRadius.Override(1.2f);
            }));

            var vfx = new GameObject("VFX").transform;
            vfx.SetParent(fx.transform);
            // Dust in the sun, then a few cold motes in the moonlight.
            controlled.Add((Motes(vfx, "SunDust", new Color(1f, 0.88f, 0.6f, 0.55f), 28f, 0.014f, 0.025f), 0f, TBlackout));
            controlled.Add((Motes(vfx, "NightMotes", new Color(0.6f, 0.72f, 1f, 0.35f), 12f, 0.012f, 0.015f), TBlackout, TEnd));
            return animator;
        }

        static AnimationClip BuildFxClip()
        {
            var clip = new AnimationClip { name = "FX_Period2" };
            Bind(clip, "", typeof(AmbientFader), "darkness", Lin(Scaled(Flicker2, 0f, 1f)));
            Bind(clip, "Lights/Sun", typeof(Light), "m_Intensity", Lin(Scaled(Flicker2, 1.25f, 0f)));
            Bind(clip, "Lights/Moon", typeof(Light), "m_Intensity", Smooth(0f, 0f, TBlackout + 0.3f, 0f, TBlackout + 1.6f, 0.2f, TCardOut, 0.2f, TEnd, 0.1f));
            Bind(clip, "Lights/Rim", typeof(Light), "m_Intensity", Smooth(0f, 6f, TBlackout, 6f, TBlackout + 0.5f, 2.2f, TEnd, 2.2f));
            Bind(clip, "Volumes/Dark", typeof(Volume), "weight", Smooth(0f, 0f, TBlackout - 0.05f, 0f, TBlackout + 0.6f, 1f, TEnd, 1f));
            Bind(clip, "Volumes/Card", typeof(Volume), "weight", Smooth(0f, 0f, TCardIn - 0.1f, 0f, TCardIn + 0.4f, 1f, TCardOut, 1f, TCardOut + 0.5f, 0f));
            return SaveClip(clip);
        }

        static AnimationClip BuildUiClip()
        {
            var clip = new AnimationClip { name = "UI_Period2" };
            Bind(clip, "BlackFade", typeof(Image), "m_Color.a", Smooth(0f, 1f, 0.2f, 1f, 1.2f, 0f, TEnd, 0f));
            Bind(clip, "LetterboxTop", typeof(RectTransform), "m_AnchorMin.y", Smooth(0f, 0.88f, TOpen, 0.88f, TOpen + 0.6f, 1f));
            Bind(clip, "LetterboxBottom", typeof(RectTransform), "m_AnchorMax.y", Smooth(0f, 0.12f, TOpen, 0.12f, TOpen + 0.6f, 0f));
            return SaveClip(clip);
        }
    }
}
