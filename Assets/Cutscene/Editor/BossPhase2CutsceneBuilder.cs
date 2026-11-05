using System.Collections.Generic;
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
    /// Phase-2 transition: the players' attack lands, the boss drops to one knee and the curse pulses through him like a
    /// heartbeat, he pushes himself up on the wand, then throws his arms wide and the curse erupts into its second form.
    /// </summary>
    public static class BossPhase2CutsceneBuilder
    {
        const string Root = "Assets/Cutscene/BossPhase2";
        const string ScenePath = Root + "/BossPhase2Cutscene.unity";
        const string TimelinePath = Root + "/BossPhase2.playable";
        const string NewAnimFbx = "Assets/TripoModels/fantasy_character_3d_model_2/fantasy_character_3d_model_2.fbx";

        // Story beats (seconds)
        const float THit = 0.6f;          // players' attack lands
        const float TKneel = 1.7f;        // crossfade into the one-knee pose
        const float HitRecoilStart = 1.15f;
        const float TRise = 5.4f;         // pushes up on the wand (kneel clip src 0.5 -> 2.0)
        const float TRoarStart = 8.0f;    // arms-wide clip begins
        const float TErupt = 9.05f;       // arms fully open (src 1.45): phase 2 begins
        const float TSlam = 13.35f;     // punch-in lands and the shutter slams shut
        const float TEnd = 14f;
        static readonly float[] Heartbeats = { 3.0f, 3.9f, 4.6f, 5.15f };

        static readonly Color Violet = new Color(0.62f, 0.38f, 1f);
        static readonly Color Magenta = new Color(0.85f, 0.3f, 1f);

        [MenuItem("Tools/Cutscene/Build Boss Phase 2 Cutscene")]
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

            var boss = SpawnCharacter(HoodedFbx, "Boss_Hooded", true);
            var body = boss.GetComponentInChildren<SkinnedMeshRenderer>();
            PinHoodToHead(body);
            var idle = FindClip(HoodedFbx, "Idle stance");
            var hit = FindClip(HoodedFbx, "Young mage gets hit");
            var kneelRise = FindClip(NewAnimFbx, "Young mage kneeling on one knee");
            var armsWide = FindClip(NewAnimFbx, "Young mage throws both arms");
            var head = FindBone(boss.transform, "Head");
            var chest = FindBone(boss.transform, "UpperChest");

            var fx = BuildFx(body, out var controlled);
            var cam = BuildCamera();
            var ui = BuildUi(cam);

            var (timeline, director) = CreateTimeline(TimelinePath);
            var charGroup = timeline.CreateTrack<GroupTrack>(null, "Characters");
            var anim = timeline.CreateTrack<AnimationTrack>(charGroup, "Boss Anim");
            director.SetGenericBinding(anim, boss);
            Place(anim, idle, 0f, THit + 0.05f, 0f, 1f, true);
            // Hit clip: the recoil starts at src ~1.15 and peaks at 1.49, so start it there to land on the impact.
            Place(anim, hit, THit - 0.03f, TKneel + 0.6f - THit + 0.03f, HitRecoilStart, 1f);
            // Kneel clip: held on one knee (src 0..0.45 crawls by as heavy breathing), then the push-up plays at speed.
            Place(anim, kneelRise, TKneel, TRise - TKneel, 0f, 0.45f / (TRise - TKneel));
            Place(anim, kneelRise, TRise, TRoarStart + 0.4f - TRise, 0.45f, 1f);
            Place(anim, armsWide, TRoarStart, TEnd - TRoarStart, 0.5f, 0.9f);
            // Kneeling: slow, shallow laboured breathing on the held pose, a small shudder in the chest on each heartbeat.
            AddBreathing(timeline, anim, boss, kneelRise, 0.2f, TKneel + 0.4f, TRise + 0.15f, 1.3f, 1.2f, 2.5f, Heartbeats);

            AddShots(timeline, director, cam, new[]
            {
                new Shot { Name = "A_Hit", Start = 0f, End = TKneel + 0.6f, From = new Vector3(-1.6f, 0.62f, -0.95f), To = new Vector3(-1.4f, 0.6f, -0.85f), LookPoint = new Vector3(0f, 0.6f, 0f), Fov = 38f, Shake = 0.3f, StrongShake = true, Kicks = new[] { THit + 0.02f } },
                new Shot { Name = "B_Kneel", Start = TKneel + 0.6f, End = TRise + 0.2f, From = new Vector3(-0.85f, 0.12f, -0.28f), To = new Vector3(-0.72f, 0.14f, -0.22f), LookAt = head, Fov = 38f, Shake = 0.15f, Kicks = Heartbeats },
                new Shot { Name = "C_Rise", Start = TRise + 0.2f, End = TRoarStart + 0.2f, From = new Vector3(-1.3f, 0.32f, 1.1f), To = new Vector3(-1.55f, 0.4f, 0.45f), LookAt = chest, Fov = 38f, Shake = 0.2f },
                new Shot { Name = "D_Erupt", Start = TRoarStart + 0.2f, End = 10.4f, From = new Vector3(-1.9f, 0.2f, -0.6f), To = new Vector3(-2.2f, 0.22f, -0.7f), LookPoint = new Vector3(0f, 0.75f, 0f), Fov = 46f, Shake = 0.3f, StrongShake = true, Kicks = new[] { TErupt + 0.02f } },
                new Shot { Name = "E_Phase2", Start = 10.4f, End = 12.85f, From = new Vector3(-0.7f, 0.68f, -0.78f), To = new Vector3(-0.62f, 0.71f, -0.67f), LookAt = head, Fov = 32f, Shake = 0.15f },
                new Shot { Name = "Z_Snap", Start = 12.85f, End = TSlam + 0.12f, From = new Vector3(-0.62f, 0.71f, -0.67f), To = new Vector3(-0.3f, 0.76f, -0.3f), LookAt = head, Fov = 32f, FovTo = 20f, SnapIn = true, Shake = 0.2f, Kicks = new[] { TSlam } },
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

        static Animator BuildFx(SkinnedMeshRenderer body, out List<(GameObject, float, float)> controlled)
        {
            controlled = new List<(GameObject, float, float)>();
            var fx = new GameObject("FX");
            var animator = fx.AddComponent<Animator>();

            StandardLights(fx.transform);
            var volumes = StandardVolumes(fx.transform);
            // Heartbeat: a dark violet squeeze at the edges of the frame.
            Volume(volumes, "Pulse", 2, 0f, Profile("CutscenePulse", p =>
            {
                var v = p.Add<Vignette>(); v.intensity.Override(0.6f); v.color.Override(new Color(0.18f, 0f, 0.22f));
                p.Add<ChromaticAberration>().intensity.Override(0.35f);
            }));
            // Phase 2 look: the grey curse turns into a saturated violet one.
            Volume(volumes, "Phase2", 4, 0f, Profile("CutscenePhase2", p =>
            {
                var ca = p.Add<ColorAdjustments>(); ca.saturation.Override(-20f); ca.colorFilter.Override(new Color(0.92f, 0.78f, 1f)); ca.contrast.Override(20f);
                var v = p.Add<Vignette>(); v.intensity.Override(0.45f); v.color.Override(new Color(0.16f, 0f, 0.22f));
                var b = p.Add<Bloom>(); b.intensity.Override(1.6f); b.threshold.Override(0.85f); b.tint.Override(new Color(0.9f, 0.7f, 1f));
            }));

            var vfx = new GameObject("VFX").transform;
            vfx.SetParent(fx.transform);
            var chest = new Vector3(0f, 0.55f, 0f) + BossRotation * Vector3.forward * 0.15f;
            controlled.Add((Motes(vfx, "AshMotes", new Color(0.7f, 0.68f, 0.76f, 0.7f), 45f, 0.018f, 0.06f), 0f, TEnd));
            // The players' attack lands on the chest.
            var impact = Vfx(vfx, "HCFX_Hit_04", chest, 0.1f);
            Mute(impact, "glow");
            controlled.Add((impact, THit - 0.02f, THit + 1f));
            controlled.Add((Vfx(vfx, "HCFX_Flash_05", chest, 0.06f), THit - 0.02f, THit + 0.8f));
            // Kneeling: the curse leaks off him, thickest while he struggles, and lightning crawls at his feet as he rises.
            controlled.Add((CurseWisps(vfx, body, TKneel, TErupt), TKneel, TErupt));
            controlled.Add((Vfx(vfx, MspDir + "LightningExplosion/Prefabs/LightningExplosionCharge", new Vector3(0f, 0.05f, 0f), 0.08f), TRise, TErupt + 0.3f));
            var gather = Tint(Vfx(vfx, "HCFX_Gathering_Energy", chest + Vector3.up * 0.1f, 0.18f, 0.7f), Violet);
            Mute(gather, "dust_floor", "get_pop");
            controlled.Add((gather, TRise + 0.3f, TErupt));
            // Eruption: burst from the chest and a floor shockwave; then a lingering violet aura for phase 2.
            var erupt = Vfx(vfx, MspDir + "LightningExplosion/Prefabs/LightningExplosionHit", chest, 0.1f);
            MuteScreenFill(erupt);
            controlled.Add((erupt, TErupt - 0.05f, TErupt + 2.5f));
            controlled.Add((FloorRing(vfx, 0.3f, Magenta), TErupt, TErupt + 1.5f));
            controlled.Add((Tint(Vfx(vfx, "HCFX_Appear_03", Vector3.zero, 0.12f, 0.6f), Violet), TErupt + 0.1f, TEnd));
            controlled.Add((CurseWisps(vfx, body, TErupt, TEnd + 6f), TErupt, TEnd));
            return animator;
        }

        static AnimationClip BuildFxClip()
        {
            var clip = new AnimationClip { name = "FX_BossPhase2" };
            // Rim light fades while he kneels, then floods magenta on the eruption.
            Bind(clip, "Lights/Rim", typeof(Light), "m_Intensity", Smooth(0f, 7f, TKneel, 7f, TKneel + 1f, 3f, TRise, 4f, TErupt - 0.1f, 6f, TErupt + 0.2f, 14f, TSlam - 0.3f, 10f, TSlam, 20f, TEnd, 20f));
            Bind(clip, "Lights/Rim", typeof(Light), "m_Color.r", Smooth(0f, 0.65f, TErupt, 0.65f, TErupt + 0.3f, 0.9f));
            Bind(clip, "Lights/Rim", typeof(Light), "m_Color.g", Smooth(0f, 0.3f, TErupt, 0.3f, TErupt + 0.3f, 0.25f));
            Bind(clip, "Lights/Rim", typeof(Light), "m_Color.b", Smooth(0f, 1f, TErupt, 1f, TErupt + 0.3f, 1f));
            Bind(clip, "Lights/Key", typeof(Light), "m_Intensity", Smooth(0f, 1.15f, TKneel, 1.15f, TKneel + 1f, 0.75f, TErupt, 0.75f, TErupt + 0.5f, 0.95f));
            Bind(clip, "Volumes/Pulse", typeof(Volume), "weight", Lin(Pulses(0f, 1f, 0.4f, Heartbeats)));
            Bind(clip, "Volumes/Phase2", typeof(Volume), "weight", Smooth(0f, 0f, TErupt - 0.05f, 0f, TErupt + 0.6f, 1f));
            Bind(clip, "Volumes/Curse", typeof(Volume), "weight", Smooth(0f, 1f, TErupt, 1f, TErupt + 0.6f, 0.4f));
            Bind(clip, "Volumes/Impact", typeof(Volume), "weight", Lin(Pulses(0f, 0.5f, 0.6f, THit, TErupt, TSlam)));
            return SaveClip(clip);
        }

        static AnimationClip BuildUiClip()
        {
            var clip = new AnimationClip { name = "UI_BossPhase2" };
            Bind(clip, "BlackFade", typeof(Image), "m_Color.a", Lin(0f, 1f, 0.3f, 0f, TEnd, 0f));
            Bind(clip, "WhiteFlash", typeof(Image), "m_Color.a", Lin(Concat(Pulses(0f, 0.15f, 0.5f, THit, TErupt), new[] { TSlam - 0.02f, 0f, TSlam + 0.02f, 0.35f, TSlam + 0.12f, 0f })));
            BindShutter(clip, TSlam);
            return SaveClip(clip);
        }
    }
}
