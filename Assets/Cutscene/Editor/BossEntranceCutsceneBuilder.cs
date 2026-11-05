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
    /// Boss entrance: the curse gathers in the dark arena, the hooded child rises out of violet smoke, lifts a cold stare
    /// at the players, then raises the wand and calls up the curse (summon) as the grade sinks deeper.
    /// </summary>
    public static class BossEntranceCutsceneBuilder
    {
        const string Root = "Assets/Cutscene/BossEntrance";
        const string ScenePath = Root + "/BossEntranceCutscene.unity";
        const string TimelinePath = Root + "/BossEntrance.playable";
        const string NewAnimFbx = "Assets/TripoModels/fantasy_character_3d_model_2/fantasy_character_3d_model_2.fbx";

        // Story beats (seconds)
        const float TAppear = 2.45f;      // smoke bursts, boss starts crouched inside it
        const float TLand = 2.53f;        // the planted crouch reads as the one landing
        const float TStare = 6.2f;        // cold stare begins
        const float TGlint = 8.4f;        // face light peaks as the head comes up
        const float TSummon = 10.0f;      // summon wind-up
        const float TSummonPeak = 10.9f;  // wand at its highest: the curse answers
        const float TSlam = 14.05f;     // punch-in lands and the shutter slams shut
        const float TEnd = 14.8f;

        static readonly Color Violet = new Color(0.62f, 0.38f, 1f);

        [MenuItem("Tools/Cutscene/Build Boss Entrance Cutscene")]
        static void BuildFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build();
        }

        public static void Build()
        {
            AssetRoot = Root;
            PrepareFolders();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            BuildEnvironment();

            var boss = SpawnCharacter(HoodedFbx, "Boss_Hooded", false);
            var body = boss.GetComponentInChildren<SkinnedMeshRenderer>(true);
            PinHoodToHead(body);
            var rise = FindClip(NewAnimFbx, "Young mage crouches");
            var stare = FindClip(NewAnimFbx, "Young mage standing still");
            var summon = FindClip(HoodedFbx, "Young mage raises the wand");

            // Wand tip at the top of the summon, where the curse is called down.
            boss.gameObject.SetActive(true);
            summon.SampleAnimation(boss.gameObject, 1.75f);
            var tip = WandTip(body, "Right_Hand");
            var head = FindBone(boss.transform, "Head");
            stare.SampleAnimation(boss.gameObject, 2f);
            var headPos = head.position;
            boss.gameObject.SetActive(false);

            var fx = BuildFx(tip, headPos, out var controlled);
            var cam = BuildCamera();
            var ui = BuildUi(cam);

            var (timeline, director) = CreateTimeline(TimelinePath);
            var charGroup = timeline.CreateTrack<GroupTrack>(null, "Characters");
            var anim = timeline.CreateTrack<AnimationTrack>(charGroup, "Boss Anim");
            director.SetGenericBinding(anim, boss);
            // Rises out of the crouch, settles into the stare, then the summon: wind-up at normal speed, peak held at a crawl.
            Place(anim, rise, TAppear, TStare + 0.4f - TAppear, 0f, 0.8f);
            Place(anim, stare, TStare, TSummon + 0.4f - TStare, 0f, 0.9f);
            // Summon clip: wand reaches its top at src 1.5 (= TSummonPeak) and starts lowering after ~2.0.
            Place(anim, summon, TSummon, 1.15f, 0.6f);
            Place(anim, summon, TSummon + 1.15f, TEnd - TSummon - 1.15f, 1.75f, 0.06f);
            var active = timeline.CreateTrack<ActivationTrack>(charGroup, "Boss Active");
            active.postPlaybackState = ActivationTrack.PostPlaybackState.LeaveAsIs;
            director.SetGenericBinding(active, boss.gameObject);
            var ac = active.CreateDefaultClip();
            ac.start = TAppear; ac.duration = TEnd - TAppear;

            var bossHead = head;
            AddShots(timeline, director, cam, new[]
            {
                new Shot { Name = "A_Establish", Start = 0f, End = TAppear, From = new Vector3(-4.0f, 1.1f, -2.6f), To = new Vector3(-3.3f, 0.9f, -2.1f), LookPoint = new Vector3(0f, 0.45f, 0f), Fov = 40f, Shake = 0.1f },
                new Shot { Name = "B_RiseLow", Start = TAppear, End = 4.6f, From = new Vector3(-1.45f, 0.12f, -0.7f), To = new Vector3(-1.3f, 0.16f, -0.6f), LookPoint = new Vector3(0f, 0.5f, 0f), Fov = 42f, Shake = 0.2f, Kicks = new[] { TLand }, KickPeak = 2.2f, KickDecay = 0.9f, Thud = TLand },
                new Shot { Name = "C_Silhouette", Start = 4.6f, End = TStare, From = new Vector3(-0.7f, 0.6f, -1.7f), To = new Vector3(-0.55f, 0.62f, -1.45f), LookPoint = new Vector3(0f, 0.6f, 0f), Fov = 38f, Shake = 0.12f },
                new Shot { Name = "D_ColdStare", Start = TStare, End = TSummon, From = new Vector3(-0.64f, 0.78f, -0.43f), To = new Vector3(-0.52f, 0.79f, -0.36f), LookAt = bossHead, Fov = 32f, Shake = 0.1f },
                new Shot { Name = "E_Summon", Start = TSummon, End = 13.5f, From = new Vector3(-1.9f, 0.25f, -1.3f), To = new Vector3(-2.3f, 0.3f, -1.6f), LookPoint = new Vector3(0f, 0.85f, 0f), Fov = 44f, Shake = 0.25f, Kicks = new[] { TSummonPeak + 0.02f } },
                new Shot { Name = "Z_Snap", Start = 13.5f, End = TSlam + 0.12f, From = new Vector3(-1.16f, 0.75f, -0.74f), To = new Vector3(-0.38f, 0.78f, -0.29f), LookAt = bossHead, Fov = 38f, FovTo = 24f, SnapIn = true, Shake = 0.2f, Kicks = new[] { TSlam } },
            });

            var fxGroup = timeline.CreateTrack<GroupTrack>(null, "FX");
            AddAnimation(timeline, fxGroup, director, "Lights Grading", fx, BuildFxClip(), TEnd);
            AddAnimation(timeline, fxGroup, director, "Fade Flash", ui, BuildUiClip(), TEnd);
            AddControlled(timeline, fxGroup, director, controlled);

            AssetDatabase.SaveAssets();
            director.time = 0;
            director.Evaluate();
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("[Cutscene] Built " + ScenePath);
        }

        static Animator BuildFx(Vector3 tip, Vector3 head, out System.Collections.Generic.List<(GameObject, float, float)> controlled)
        {
            controlled = new System.Collections.Generic.List<(GameObject, float, float)>();
            var fx = new GameObject("FX");
            var animator = fx.AddComponent<Animator>();

            var lights = StandardLights(fx.transform);
            // Glow on the face for the stare, and a pulse where the boss will appear.
            var face = Light(lights, "Face", LightType.Point, Violet, 0f, 1.2f);
            face.transform.position = head + BossRotation * Vector3.forward * 0.35f;
            var omen = Light(lights, "Omen", LightType.Point, Violet, 0f, 3f);
            omen.transform.position = new Vector3(0f, 0.15f, 0f);

            var volumes = StandardVolumes(fx.transform);
            Volume(volumes, "Dread", 2, 0f, Profile("CutsceneDread", p =>
            {
                var ca = p.Add<ColorAdjustments>(); ca.saturation.Override(-55f); ca.colorFilter.Override(new Color(0.8f, 0.7f, 1f)); ca.contrast.Override(22f); ca.postExposure.Override(-0.15f);
                var v = p.Add<Vignette>(); v.intensity.Override(0.5f); v.color.Override(new Color(0.12f, 0f, 0.2f));
            }));

            var vfx = new GameObject("VFX").transform;
            vfx.SetParent(fx.transform);
            controlled.Add((Motes(vfx, "AshMotes", new Color(0.7f, 0.68f, 0.76f, 0.7f), 45f, 0.018f, 0.06f), 0f, TEnd));
            // Omen: a small ring of violet petals where the boss will rise.
            controlled.Add((Vfx(vfx, "HCFX_Appear_03", Vector3.zero, 0.1f), 0.8f, TAppear + 0.6f));
            // The boss rises out of a dark violet smoke burst that hides the pop-in.
            var smoke = Tint(Vfx(vfx, "HCFX_Smoke_02_Floor", Vector3.zero, 0.1f, 0.6f), new Color(0.28f, 0.18f, 0.38f));
            controlled.Add((smoke, TAppear - 0.15f, 4.6f));
            controlled.Add((FloorRing(vfx, 0.22f, Violet), TAppear - 0.05f, TAppear + 1.2f));
            // One ash puff when the crouch plants. Glows stay off so it reads as dust, not a spell.
            var dust = Tint(Vfx(vfx, "HCFX_Smoke_01_Floor", new Vector3(0f, 0.02f, 0f), 0.14f, 0.8f), new Color(0.55f, 0.5f, 0.48f));
            Mute(dust, "glow_view", "glow_floor", "smoke_mesh");
            controlled.Add((dust, TLand - 0.02f, TLand + 1.5f));
            // Summon: power gathers at the raised wand, then the curse answers with a burst, a floor ring and pillars behind.
            controlled.Add((Vfx(vfx, MspDir + "LightningExplosion/Prefabs/LightningExplosionCharge", tip, 0.04f), TSummon + 0.3f, TEnd));
            var burst = Vfx(vfx, MspDir + "LightningExplosion/Prefabs/LightningExplosionHit", tip, 0.06f);
            MuteScreenFill(burst);
            // Its lightning bolts cross the whole frame at this camera distance; keep just the star burst.
            Mute(burst, "LightningState0_1", "LightningState0_2", "LightningState1_1", "LightningState1_2", "LightningState1_3", "LightningState1_4", "LightningState2_1", "LightningState2_2", "LightningState3_1");
            controlled.Add((burst, TSummonPeak - 0.05f, TSummonPeak + 2.5f));
            controlled.Add((FloorRing(vfx, 0.22f, Violet), TSummonPeak, TSummonPeak + 1.5f));
            foreach (var spot in new[] { new Vector3(1.4f, 0f, -1.5f), new Vector3(2.4f, 0f, 0.2f), new Vector3(1.5f, 0f, 1.6f) })
            {
                var pillar = Tint(Vfx(vfx, "HCFX_Blessed_02_Small", spot, 0.28f, 0.5f), Violet);
                Mute(pillar, "particle_star");
                controlled.Add((pillar, TSummonPeak + 0.15f, TEnd));
            }
            return animator;
        }

        static AnimationClip BuildFxClip()
        {
            var clip = new AnimationClip { name = "FX_BossEntrance" };
            // Rim light finds the silhouette as he rises; the omen light breathes before he appears.
            Bind(clip, "Lights/Rim", typeof(Light), "m_Intensity", Smooth(0f, 0f, TAppear, 0.5f, 5f, 7f, TSummonPeak, 7f, TSummonPeak + 0.3f, 11f, TEnd, 9f));
            Bind(clip, "Lights/Omen", typeof(Light), "m_Intensity", Smooth(0f, 0f, 0.8f, 0f, 1.4f, 2.5f, 1.9f, 1f, 2.3f, 3f, TAppear + 0.2f, 4f, 3.6f, 0f));
            Bind(clip, "Lights/Face", typeof(Light), "m_Intensity", Smooth(0f, 0f, TGlint - 0.8f, 0f, TGlint, 1.2f, TGlint + 1.2f, 0.5f, TSummon, 0f, TSlam - 0.35f, 0f, TSlam - 0.02f, 3f, TEnd, 3f));
            Bind(clip, "Lights/Key", typeof(Light), "m_Intensity", Smooth(0f, 0.5f, TAppear, 0.6f, 5f, 1.15f, TSummonPeak, 1.15f, TSummonPeak + 0.6f, 0.8f));
            // The grade sinks into dread when the curse answers the summon.
            Bind(clip, "Volumes/Dread", typeof(Volume), "weight", Smooth(0f, 0f, TSummonPeak - 0.05f, 0f, TSummonPeak + 0.8f, 1f));
            Bind(clip, "Volumes/Impact", typeof(Volume), "weight", Lin(Pulses(0f, 0.45f, 0.7f, TLand, TSummonPeak, TSlam)));
            return SaveClip(clip);
        }

        static AnimationClip BuildUiClip()
        {
            var clip = new AnimationClip { name = "UI_BossEntrance" };
            Bind(clip, "BlackFade", typeof(Image), "m_Color.a", Lin(0f, 1f, 1.0f, 0f, TEnd, 0f));
            Bind(clip, "WhiteFlash", typeof(Image), "m_Color.a", Lin(Concat(Pulses(0f, 0.1f, 0.5f, TSummonPeak), new[] { TSlam - 0.02f, 0f, TSlam + 0.02f, 0.35f, TSlam + 0.12f, 0f })));
            BindShutter(clip, TSlam);
            return SaveClip(clip);
        }
    }
}
