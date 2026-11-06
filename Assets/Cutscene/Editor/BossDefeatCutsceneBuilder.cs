using System.Collections.Generic;
using AiHackathon.Game;
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
    /// Generates the boss defeat cutscene scene + Timeline: the beam struggle between the boss and Amelia + Nox, the
    /// resonance QTE (see <see cref="ResonanceQte"/>), the ultimate beam, blast (hood blown off, model swap under the
    /// white flash), collapse, the light returning, and the purified child standing up.
    ///
    /// Timeline: 0..TQte is the struggle; TQte..TQteLoop the resonance intro; TQteLoop..TQteEnd the live QTE, which
    /// repeats until both players fill their gauges and then jumps to TUlt (the ultimate beam). Everything after that
    /// is the original defeat sequence, shifted by <see cref="Shift"/>.
    /// Re-run from Tools/Cutscene to rebuild after tweaking the numbers below.
    /// </summary>
    public static class BossDefeatCutsceneBuilder
    {
        const string Root = "Assets/Cutscene";
        public const string ScenePath = Root + "/BossDefeatCutscene.unity";
        const string TimelinePath = Root + "/BossDefeat.playable";
        const string ProfileDir = "Assets/Game/Profiles";
        public const string TitleFontPath = "Assets/Parchment/Fonts/GowunBatang-Bold.ttf";
        public const string BodyFontPath = "Assets/Parchment/Fonts/GowunBatang-Regular.ttf";

        const string HoodedMat = "Assets/TripoModels/fantasy_character_3d_model_1/Materials/fantasy_character_3d_model_1.mat";
        const string UnhoodedFbx = "Assets/TripoModels/anime_character_3d_model/anime_character_3d_model.fbx";
        const string UnhoodedMat = "Assets/TripoModels/anime_character_3d_model/Materials/anime_character_3d_model.mat";
        const string UnhoodedTex = "Assets/TripoModels/anime_character_3d_model/anime_character_3d_model.fbm/anime_character_3d_model_BaseColor.jpg";

        // ---- Beam struggle ----
        // Both beams fire at TFire (the boss's arm thrusts out in the cast clip at src 1.22s); heads meet at TMeet.
        const float TFire = 0.7f;
        const float TMeet = 1.1f;

        // ---- Resonance QTE ----
        public const float TQte = 4.9f;                 // the boss has pushed the clash toward the players: resonance intro
        public const float TQteLoop = 7.1f;             // live QTE starts
        public const float QteWindow = 8f;              // the live stretch repeats every this many seconds until success
        public const float TQteEnd = TQteLoop + QteWindow;
        public const float TUlt = TQteEnd;              // success jumps here: the ultimate beam
        const float UltLength = 1.8f;

        // ---- The original defeat sequence, from its 5.4s mark, shifted to follow the ultimate ----
        const float Shift = TUlt + UltLength - 5.4f;
        const float TStruggleEnd = 5.4f + Shift;
        const float TBlast = 6.15f + Shift;
        const float TSwap = 6.85f + Shift;
        // White flash is fully opaque from TWhiteFull to TWhiteHoldEnd; effects are switched off inside that window.
        const float TWhiteFull = TBlast + 0.25f;
        const float TWhiteHoldEnd = 7.25f + Shift;
        const float TFxEnd = TWhiteFull + 0.05f;
        // Cast clip: arm pulls back ~1.0s and thrusts forward at 1.22s, held until ~2.1s.
        const float CastThrust = 1.22f;
        const float CastHeld = 1.82f;
        const float TStandStart = 12.8f + Shift;
        // Lying-to-kneeling crossfade length.
        const float StandBlend = 1.4f;
        // Purification: light strikes the fallen child, then the curse breaks as he finishes standing (cut to shot G).
        const float TLightStrike = 12.0f + Shift;
        const float TPurify = 17.2f + Shift;
        public const float TEnd = 25.5f + Shift;

        // Players stand back on the -X side facing the boss (+X), as far from the clash as the boss's wand.
        public static readonly Vector3 AmeliaPos = new Vector3(-4.6f, 0f, 0.78f);
        public static readonly Vector3 NoxPos = new Vector3(-4.68f, 0f, -0.78f);
        public static readonly Quaternion PlayerFacing = Quaternion.Euler(0f, 90f, 0f);
        // The players' stance: Ready clip held near its end.
        const float ReadyHold = 2.6f;

        static readonly Color Gold = new Color(1f, 0.8f, 0.35f);
        static readonly Color Blue = new Color(0.45f, 0.75f, 1f);

        [MenuItem("Tools/Cutscene/Build Boss Defeat Cutscene")]
        static void BuildFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build();
        }

        public static void Build()
        {
            AssetRoot = Root;
            PrepareFolders();
            ApplyToonMaterial();
            var ameliaProfile = AssetDatabase.LoadAssetAtPath<CasterProfile>(ProfileDir + "/Amelia.asset");
            var noxProfile = AssetDatabase.LoadAssetAtPath<CasterProfile>(ProfileDir + "/Nox.asset");
            if (ameliaProfile == null || noxProfile == null) throw new System.Exception("Build the Period 1 cutscene first: it creates the caster profiles.");

            PeriodPoseClips.BuildBattle();
            // The cast clip's arm-out hold (src ~1.82-2.1s) creeping back and forth, with a straining tremor and a shove
            // into the beam every second, as a 4s loop.
            PeriodPoseClips.BakeStrain(HoodedFbx, FindClip(HoodedFbx, "Young mage casts"), "Boss_PushStrain", 2.2f, 4f,
                t => CastHeld + 0.14f + 0.12f * Mathf.Sin(t / 4f * Mathf.PI * 2f), 5f);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var strain = PeriodPoseClips.Load("Boss_PushStrain");
            var envLoops = BuildEnvironment();
            // The side-on QTE shot looks in from -Z right past this brazier: move it back behind the camera.
            var brazier = GameObject.Find("Environment/Brazier 3");
            if (brazier != null) brazier.transform.localPosition = brazier.transform.localPosition.normalized * 6.2f;
            var cam = BuildCamera();

            var hooded = SpawnCharacter(HoodedFbx, "Boss_Hooded", true);
            PrepareBossBody(hooded.GetComponentInChildren<SkinnedMeshRenderer>());
            var unhooded = SpawnCharacter(UnhoodedFbx, "Boss_Unhooded", false);
            var castClip = FindClip(HoodedFbx, "Young mage casts");
            var hitClip = FindClip(HoodedFbx, "Young mage gets hit");
            var defeatClip = FindClip(UnhoodedFbx, "Young mage loses strength");
            var standClip = FindClip(UnhoodedFbx, "Young mage kneeling");

            // Wand tip with the arm held out after the thrust = where the boss beam starts.
            castClip.SampleAnimation(hooded.gameObject, CastHeld + 0.14f);
            var tip = WandTip(hooded.GetComponentInChildren<SkinnedMeshRenderer>(), "Right_Hand");
            // The wand is rigid in the hand: a marker there lets the boss beam follow his arm whatever the clip does.
            var bossTip = new GameObject("WandTip").transform;
            bossTip.SetParent(FindBone(hooded.transform, "Right_Hand"), false);
            bossTip.position = tip;

            // Amelia and Nox in the game stance, staff in the lead hand (as the Period 1 intro does).
            var profiles = new[] { ameliaProfile, noxProfile };
            var positions = new[] { AmeliaPos, NoxPos };
            var ready = new[] { PeriodPoseClips.Load("Amelia_Ready"), PeriodPoseClips.Load("Nox_Ready") };
            var players = new Animator[2];
            var staffTips = new Transform[2];
            var sources = new Vector3[2];
            var playerChests = new Vector3[2];
            for (int i = 0; i < 2; i++)
            {
                var rig = WaaangSceneSetup.CreateCaster(profiles[i], cam, positions[i], i);
                rig.toonScene = false;
                rig.gameObject.SetActive(false);
                rig.character.GetComponent<CasterPose>().enabled = false;
                rig.character.rotation = PlayerFacing * rig.character.rotation; // turn from the builder's +Z facing to +X
                PeriodSceneKit.HoldStaff(rig.character, rig.staff, AssetDatabase.GetAssetPath(profiles[i].characterModel), profiles[i].LeadSide, profiles[i].staffScale);
                var animator = rig.character.gameObject.AddComponent<Animator>();
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                players[i] = animator;
                ready[i].SampleAnimation(rig.character.gameObject, ReadyHold);
                var staffTip = rig.staff.Find("Staff Tip");
                staffTips[i] = staffTip;
                sources[i] = staffTip.position + staffTip.up * 0.04f;
                playerChests[i] = FindBone(rig.character, "UpperChest").position;
                // Hit markers, lasers and lights of the game rig have no part in the cutscene.
                foreach (var n in new[] { "Hit Marker ", "Hit Light ", "Laser " })
                {
                    var go = GameObject.Find(n + profiles[i].displayName);
                    if (go != null) Object.DestroyImmediate(go);
                }
            }

            // Clash roughly midway between the boss's wand and the players' staffs.
            var clashX0 = (tip.x + (sources[0].x + sources[1].x) * 0.5f) * 0.5f;

            // The defeat clip ends with the knees ~30cm further forward than the stand clip starts. Wrap the
            // unhooded model in a rig whose child slides by that gap during the crossfade, so the knees stay planted.
            var unhoodedRig = new GameObject("Boss_Unhooded_Rig");
            var unhoodedRigAnimator = unhoodedRig.AddComponent<Animator>();
            var unhoodedOffset = new GameObject("Offset").transform;
            unhoodedOffset.SetParent(unhoodedRig.transform, false);
            unhooded.transform.SetParent(unhoodedOffset, true);
            var kneeGap = KneeGap(unhooded, defeatClip, standClip);

            var clashKeys = ClashKeys(tip, clashX0);
            var fx = BuildFx(tip, clashX0, sources, playerChests, positions, unhooded.GetComponentInChildren<SkinnedMeshRenderer>(true), out var controlled);
            // Beams stay on the moving wand and staff tips.
            fx.transform.Find("BeamPurple").gameObject.AddComponent<BeamTailFollow>().tail = bossTip;
            fx.transform.Find("BeamAmelia").gameObject.AddComponent<BeamTailFollow>().tail = staffTips[0];
            fx.transform.Find("BeamNox").gameObject.AddComponent<BeamTailFollow>().tail = staffTips[1];
            // The curse-fire goes out with the purification (the cut to shot G hides it); the mist stays.
            foreach (var loop in envLoops) controlled.Add((loop, 0f, loop.name == "Curse Flame" ? TPurify + 0.05f : TEnd));
            var ui = BuildUi(cam);

            var hoodedHead = FindBone(hooded.transform, "Head");
            var unhoodedHead = FindBone(unhooded.transform, "Head");
            var unhoodedChest = FindBone(unhooded.transform, "UpperChest");
            float clashQte = Smooth(clashKeys).Evaluate(TQteLoop);
            var playersMid = (sources[0] + sources[1]) * 0.5f;
            var (timeline, director) = CreateTimeline(TimelinePath);

            var qte = director.gameObject.AddComponent<ResonanceQte>();
            qte.introAt = TQte;
            qte.loopStart = TQteLoop;
            qte.loopEnd = TQteEnd;
            qte.ultimateAt = TUlt;
            qte.clash = fx.transform.Find("Clash");
            qte.names = new[] { ameliaProfile.displayName, noxProfile.displayName };
            qte.colors = new[] { ameliaProfile.playerColor, noxProfile.playerColor };
            qte.pushed = new[] { fx.transform.Find("Clash"), fx.transform.Find("BeamPurple"), fx.transform.Find("BeamAmelia"), fx.transform.Find("BeamNox") };
            qte.resonanceLight = fx.transform.Find("Lights/Resonance").GetComponent<Light>();
            qte.titleFont = AssetDatabase.LoadAssetAtPath<Font>(TitleFontPath);
            qte.bodyFont = AssetDatabase.LoadAssetAtPath<Font>(BodyFontPath);

            var charGroup = timeline.CreateTrack<GroupTrack>(null, "Characters");
            var hoodedAnim = timeline.CreateTrack<AnimationTrack>(charGroup, "Hooded Anim");
            director.SetGenericBinding(hoodedAnim, hooded);
            // Wind-up and thrust land the arm on TFire, then he leans into the beam: the push loop with a straining
            // tremor (overlaps crossfade), until the blast hits him.
            var windup = Place(hoodedAnim, castClip, 0f, CastHeld - (CastThrust - TFire), CastThrust - TFire);
            const float HitStart = 5.85f + Shift;
            Place(hoodedAnim, strain, (float)windup.end - 0.35f, HitStart + 0.25f - ((float)windup.end - 0.35f), 0f, 1f, true);
            Place(hoodedAnim, hitClip, HitStart, TSwap + 0.1f - HitStart, 0.6f, 0.8f);

            var unhoodedAnim = timeline.CreateTrack<AnimationTrack>(charGroup, "Unhooded Anim");
            director.SetGenericBinding(unhoodedAnim, unhooded);
            Place(unhoodedAnim, defeatClip, TSwap, TStandStart + StandBlend - TSwap, 0.25f, 0.75f);
            Place(unhoodedAnim, standClip, TStandStart, TEnd - TStandStart, 0f, 0.7f);
            // Same ease-in-out as the clips' default mix curves, so offset and blend weight move together.
            var slide = new AnimationClip { name = "Unhooded_KneeAlign" };
            Bind(slide, "Offset", typeof(Transform), "m_LocalPosition.x", Smooth(0f, 0f, TStandStart, 0f, TStandStart + StandBlend, kneeGap.x));
            Bind(slide, "Offset", typeof(Transform), "m_LocalPosition.y", Lin(0f, 0f, TEnd, 0f));
            Bind(slide, "Offset", typeof(Transform), "m_LocalPosition.z", Smooth(0f, 0f, TStandStart, 0f, TStandStart + StandBlend, kneeGap.z));
            var slideTrack = timeline.CreateTrack<AnimationTrack>(charGroup, "Unhooded Knee Align");
            director.SetGenericBinding(slideTrack, unhoodedRigAnimator);
            Place(slideTrack, SaveClip(slide), 0f, TEnd);

            Activation(timeline, charGroup, director, "Hooded Active", hooded.gameObject, 0, TSwap);
            Activation(timeline, charGroup, director, "Unhooded Active", unhooded.gameObject, TSwap, TEnd);

            // Players: ready stance, then brace and push against the boss beam, the resonance beat, push again through
            // the live QTE, and the ultimate thrust. Overlapping clips crossfade.
            string[] who = { "Amelia", "Nox" };
            for (int i = 0; i < 2; i++)
            {
                var track = timeline.CreateTrack<AnimationTrack>(charGroup, profiles[i].displayName + " Anim");
                director.SetGenericBinding(track, players[i]);
                var struggle = PeriodPoseClips.Load(who[i] + "_Struggle");
                Place(track, ready[i], 0f, TFire + 0.3f, ReadyHold, 0.004f);
                Place(track, struggle, TFire, TQte + 0.35f - TFire, 0.37f * i, 1f, true);
                Place(track, PeriodPoseClips.Load(who[i] + "_Resonance"), TQte, PeriodPoseClips.ResonanceLength);
                Place(track, struggle, TQteLoop - 0.1f, TQteEnd + 0.3f - (TQteLoop - 0.1f), 0.37f * i, 1.15f, true);
                Place(track, PeriodPoseClips.Load(who[i] + "_Ultimate"), TUlt, TSwap - TUlt);
                // Off after the blast: the purification shots look back across where they stand.
                Activation(timeline, charGroup, director, profiles[i].displayName + " Active", players[i].gameObject, 0f, TSwap);
            }

            AddShots(timeline, director, cam, new[]
            {
                new Shot { Name = "A_ClashWide", Start = 0f, End = 3.2f, From = new Vector3(-1.45f, 0.62f, -3.4f), To = new Vector3(-1.75f, 0.68f, -3.1f), LookPoint = new Vector3(clashX0 + 0.3f, 0.7f, 0f), Fov = 40f, Shake = 0.5f, Kicks = new[] { TMeet, 2.1f, 2.7f } },
                new Shot { Name = "B_BossClose", Start = 3.2f, End = TQte, From = new Vector3(-0.62f, 0.45f, -1.38f), To = new Vector3(-0.52f, 0.5f, -1.22f), LookPoint = Vector3.Lerp(hoodedHead.position, tip, 0.45f), Fov = 46f, Shake = 0.7f, StrongShake = true, Kicks = new[] { 3.9f, 4.6f } },
                // Resonance: in front of the two, their beams running past the lens toward the clash.
                new Shot { Name = "R_Resonance", Start = TQte, End = TQteLoop, From = new Vector3(-3.0f, 1.0f, -1.85f), To = new Vector3(-3.2f, 1.08f, -1.6f), LookPoint = new Vector3(-4.6f, 1.12f, 0f), Fov = 42f, Shake = 0.2f, Kicks = new[] { TQte + 0.05f }, KickPeak = 1.2f },
                // Live QTE: side-on, the clash point dead centre, players left, boss right.
                // Slow arc round the clash, which stays centred, creeping in.
                new Shot { Name = "Q_Qte", Start = TQteLoop, End = TQteEnd, From = new Vector3(clashQte - 0.7f, 1.0f, -4.4f), To = new Vector3(clashQte + 0.7f, 1.2f, -4.0f), LookPoint = new Vector3(clashQte, tip.y, 0f), Fov = 52f, FovTo = 47f, Shake = 0.35f },
                // Ultimate: low behind the two, the doubled beam driving down the frame into the boss.
                new Shot { Name = "U_Ultimate", Start = TUlt, End = TStruggleEnd, From = new Vector3(AmeliaPos.x - 2.1f, 1.05f, 0.05f), To = new Vector3(AmeliaPos.x - 1.8f, 0.98f, 0f), LookPoint = new Vector3(-1.0f, 0.75f, 0f), Fov = 52f, FovTo = 40f, Shake = 0.6f, StrongShake = true, Kicks = new[] { TUlt + 0.02f }, KickPeak = 3.2f, KickDecay = 0.9f },
                new Shot { Name = "C_Overwhelm", Start = TStruggleEnd, End = TSwap, From = new Vector3(-2.3f, 0.3f, 1.3f), To = new Vector3(-1.8f, 0.36f, 1.1f), LookPoint = new Vector3(0f, 0.7f, 0f), Fov = 42f, Shake = 1.0f, StrongShake = true, Kicks = new[] { TStruggleEnd + 0.05f, TBlast } },
                new Shot { Name = "D_Collapse", Start = TSwap, End = 10.5f + Shift, From = new Vector3(-0.6f, 0.45f, -1.9f), To = new Vector3(-0.45f, 0.38f, -1.65f), LookPoint = new Vector3(-0.25f, 0.33f, 0f), Fov = 40f, Shake = 0.25f },
                new Shot { Name = "E_SilenceHigh", Start = 10.5f + Shift, End = 13.8f + Shift, From = new Vector3(-2.2f, 2.3f, -1.9f), To = new Vector3(-1.7f, 1.7f, -1.45f), LookPoint = new Vector3(0f, 0.15f, 0f), Fov = 38f, Shake = 0.15f, Kicks = new[] { TLightStrike + 0.02f } },
                new Shot { Name = "F_LiftHead", Start = 13.8f + Shift, End = 17.2f + Shift, From = new Vector3(-1.12f, 0.22f, -0.12f), To = new Vector3(-0.99f, 0.62f, -0.16f), LookAt = unhoodedHead, LookOffset = new Vector3(0f, 0.03f, 0f), Fov = 30f, Shake = 0.2f },
                new Shot { Name = "G_StandUp", Start = 17.2f + Shift, End = 21.7f + Shift, From = new Vector3(-1.25f, 0.55f, -0.95f), To = new Vector3(-1.45f, 0.62f, -0.3f), LookAt = unhoodedChest, LookOffset = new Vector3(0f, 0.12f, 0f), Fov = 34f, Shake = 0.15f, Kicks = new[] { TPurify + 0.02f } },
                new Shot { Name = "H_Finale", Start = 20.9f + Shift, End = TEnd, From = new Vector3(-1.5f, 0.65f, -0.2f), To = new Vector3(-3.4f, 1.0f, -0.9f), LookPoint = new Vector3(0f, 0.5f, 0f), Fov = 38f, Shake = 0.1f },
            });

            var fxGroup = timeline.CreateTrack<GroupTrack>(null, "FX");
            AddAnimation(timeline, fxGroup, director, "Beams Lights Grading", fx, BuildFxClip(tip, clashX0, clashKeys, sources), TEnd);
            AddAnimation(timeline, fxGroup, director, "Fade Flash", ui, BuildUiClip(), TEnd);
            AddControlled(timeline, fxGroup, director, controlled);
            // The seal and the curse lights fade as the light fights the curse and die on the purification.
            BossArena.AddTimelineClip(timeline, fxGroup, director, "Arena_BossDefeat", TEnd, clip =>
            {
                BossArena.SealCurve(clip, Smooth(0f, BossArena.SealAlpha, TQte, BossArena.SealAlpha, TUlt, 0.55f, TLightStrike, BossArena.SealAlpha, TPurify - 0.05f, 0.18f, TPurify + 0.6f, 0f, TEnd, 0f));
                BossArena.FireCurve(clip, Smooth(0f, BossArena.FireLight, TLightStrike, BossArena.FireLight, TPurify - 0.05f, 1f, TPurify + 0.05f, 0f, TEnd, 0f));
                BossArena.CrystalCurve(clip, Smooth(0f, BossArena.CrystalLight, TLightStrike, BossArena.CrystalLight, TPurify - 0.05f, 0.5f, TPurify + 0.5f, 0f, TEnd, 0f));
            });

            AssetDatabase.SaveAssets();
            director.time = 0;
            director.Evaluate();
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("[Cutscene] Built " + ScenePath);
        }

        // ---------- Materials ----------

        static void ApplyToonMaterial()
        {
            // Tripo exported the unhooded boss with Standard (pink in URP). Reuse the hooded boss's lilToon look.
            var src = AssetDatabase.LoadAssetAtPath<Material>(HoodedMat);
            var dst = AssetDatabase.LoadAssetAtPath<Material>(UnhoodedMat);
            if (src == null || dst == null || dst.shader == src.shader) return;
            dst.shader = src.shader;
            dst.CopyPropertiesFromMaterial(src);
            dst.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(UnhoodedTex));
            EditorUtility.SetDirty(dst);
        }

        // ---------- Scene content ----------

        /// <summary>
        /// Clash point x over time after contact (negative = toward the players): see-saws, the boss gains ground at
        /// 3.9s and keeps pushing into the QTE, trembles in place through it, then the ultimate beam drives it into him.
        /// </summary>
        static float[] ClashKeys(Vector3 tip, float x0)
        {
            var keys = new List<float>
            {
                TMeet, x0, 1.6f, x0 + 0.06f, 2.1f, x0 - 0.1f, 2.7f, x0 + 0.14f, 3.2f, x0 - 0.05f,
                3.9f, x0 - 0.22f, 4.6f, x0 - 0.18f, TQte, x0 - 0.3f, TQteLoop, x0 - 0.36f,
            };
            // A slow tremor while the players resonate (the live stretch may repeat; it starts and ends on the same value).
            for (float t = TQteLoop + 1f; t < TQteEnd - 0.5f; t += 1f) keys.AddRange(new[] { t, x0 - 0.36f + ((int)t % 2 == 0 ? 0.04f : -0.03f) });
            keys.AddRange(new[] { TQteEnd, x0 - 0.36f, TUlt + 0.5f, x0 - 0.1f, TUlt + 1.2f, x0 + 0.45f, TStruggleEnd, x0 + 0.7f, TBlast, tip.x - 0.02f });
            return keys.ToArray();
        }

        static Animator BuildFx(Vector3 tip, float clashX0, Vector3[] sources, Vector3[] chests, Vector3[] feet, SkinnedMeshRenderer unhoodedBody, out List<(GameObject, float, float)> controlled)
        {
            controlled = new List<(GameObject, float, float)>();
            var fx = new GameObject("FX");
            var animator = fx.AddComponent<Animator>();

            // Beams: each rig's pivot is the beam head (the clash point) and its +Y points back at the caster,
            // so the prefab's impact burst lands on the clash and uniform scale = length / 20.
            // The one-shot lasers are switched to looping emitters so they re-fire in place every half second.
            var head = new Vector3(clashX0, tip.y, tip.z);
            var purpleRig = BeamRig(fx.transform, "BeamPurple", head, Vector3.right);
            controlled.Add((Looping(KlausBeam(purpleRig, "HCFX_Beam_04", 1f), 0.5f), TFire, TFxEnd));
            // One beam per player from her staff tip: Amelia gold, Nox blue. At the ultimate a fatter layer fires
            // rapidly on top of each.
            string[] names = { "BeamAmelia", "BeamNox" };
            string[] prefabs = { "HCFX_Beam_03", "HCFX_Beam_02" };
            for (int i = 0; i < 2; i++)
            {
                var rig = BeamRig(fx.transform, names[i], head, (sources[i] - head).normalized);
                controlled.Add((Looping(KlausBeam(rig, prefabs[i], 1f), 0.5f), TFire, TSwap));
                var surge = KlausBeam(rig, prefabs[i], 1f);
                // Thicker across the beam only: local +Y is its length, which must still end at the staff tip.
                surge.transform.localScale = new Vector3(1.8f, 1f, 1.8f);
                surge.name = names[i] + " Ultimate";
                Mute(surge, "hit_impact_view", "hit_impact_floor");
                controlled.Add((Looping(surge, 0.2f), TUlt, TSwap));
            }

            // Boss wand: one muzzle flash as the arm thrusts out, then a steady glow while the beam is held.
            var muzzle = Vfx(fx.transform, MspDir + "Shoot_Purple/Prefabs/Par_PurpleShoot_Muzzle", tip, 0.15f);
            // The pack's muzzle prefab carries a broken script reference; strip it from our copy.
            PrefabUtility.UnpackPrefabInstance(muzzle, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            foreach (var t in muzzle.GetComponentsInChildren<Transform>(true)) GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
            muzzle.transform.localRotation = BossRotation;
            Mute(muzzle, "Par_Smoke", "BG");
            controlled.Add((muzzle, TFire - 0.05f, TFire + 1f));
            controlled.Add((Vfx(fx.transform, MspDir + "LightningExplosion/Prefabs/LightningExplosionCharge", tip, 0.03f), TFire, TFxEnd));

            var clash = new GameObject("Clash").transform;
            clash.SetParent(fx.transform);
            clash.position = head;
            var clashLight = Light(clash, "Light", LightType.Point, new Color(1f, 0.82f, 0.5f), 5f, 3.5f);
            clashLight.transform.localPosition = Vector3.zero;
            // Moment of contact, then shockwave rings pulsing out across the beam axis.
            controlled.Add((Vfx(clash, "HCFX_Hit_04", Vector3.zero, 0.18f), TMeet, TMeet + 1f));
            var ring = Vfx(clash, "HCFX_Energy_04", Vector3.zero, 0.18f);
            ring.transform.localRotation = Quaternion.FromToRotation(Vector3.up, Vector3.right);
            controlled.Add((Looping(ring, 1.3f), TMeet, TFxEnd));
            // Clash point: a looping lightning core plus gold/purple impact bursts that speed up with the ultimate.
            controlled.Add((Vfx(clash, MspDir + "LightningExplosion/Prefabs/LightningExplosionCharge", Vector3.zero, 0.18f), TMeet, TFxEnd));
            var sparks = Vfx(clash, MspDir + "YellowSwordBeam/Prefabs/Par_YellowSwordBeam_Hit", Vector3.zero, 0.22f);
            var rapidSparks = Vfx(clash, MspDir + "YellowSwordBeam/Prefabs/Par_YellowSwordBeam_Hit", Vector3.zero, 0.22f);
            var purpleBurst = Vfx(clash, MspDir + "Shoot_Purple/Prefabs/Par_PurpleShoot_Hit", Vector3.zero, 0.2f);
            // Their smoke/backdrop layers stack into a muddy cloud when re-fired this often.
            foreach (var go in new[] { sparks, rapidSparks, purpleBurst }) Mute(go, "Par_Smoke", "BG");
            controlled.Add((Looping(sparks, 0.45f), TMeet, TUlt + 0.6f));
            controlled.Add((Looping(rapidSparks, 0.2f), TUlt, TFxEnd));
            controlled.Add((Looping(purpleBurst, 1.3f), TMeet + 0.45f, TStruggleEnd + 1f));

            var lights = new GameObject("Lights").transform;
            lights.SetParent(fx.transform);
            var key = Light(lights, "Key", LightType.Directional, new Color(0.72f, 0.72f, 0.9f), 0.8f, 0f);
            key.transform.rotation = Quaternion.Euler(38f, 55f, 0f);
            key.shadows = LightShadows.Soft;
            var rim = Light(lights, "Rim", LightType.Spot, new Color(0.65f, 0.3f, 1f), 7f, 6f);
            rim.transform.position = new Vector3(1.8f, 1.6f, 0.9f);
            rim.transform.LookAt(new Vector3(0f, 0.6f, 0f));
            rim.spotAngle = 55f;
            var heaven = Light(lights, "Heaven", LightType.Spot, new Color(1f, 0.93f, 0.8f), 0f, 9f);
            heaven.transform.position = new Vector3(-0.4f, 4.5f, 0f);
            heaven.transform.LookAt(Vector3.zero);
            heaven.spotAngle = 32f;
            heaven.innerSpotAngle = 12f;
            // Faces of the two, lit from the clash side; it warms and swells as they resonate.
            var playersMid = (feet[0] + feet[1]) * 0.5f;
            var playerKey = Light(lights, "PlayerKey", LightType.Spot, new Color(1f, 0.9f, 0.75f), 2.5f, 6f);
            playerKey.transform.position = playersMid + new Vector3(2.0f, 2.2f, -0.6f);
            playerKey.transform.LookAt(playersMid + Vector3.up * 1.1f);
            playerKey.spotAngle = 60f;
            playerKey.shadows = LightShadows.None;
            var resonance = Light(lights, "Resonance", LightType.Point, new Color(1f, 0.92f, 0.75f), 0f, 4f);
            resonance.transform.position = (sources[0] + sources[1]) * 0.5f + Vector3.up * 0.2f;
            resonance.shadows = LightShadows.None;

            var volumes = new GameObject("Volumes").transform;
            volumes.SetParent(fx.transform);
            Volume(volumes, "Base", 0, 1f, Profile("CutsceneBase", p =>
            {
                p.Add<Tonemapping>().mode.Override(TonemappingMode.Neutral);
                var b = p.Add<Bloom>(); b.threshold.Override(0.95f); b.intensity.Override(1.1f); b.scatter.Override(0.7f);
                var v = p.Add<Vignette>(); v.intensity.Override(0.3f); v.smoothness.Override(0.45f);
                p.Add<ColorAdjustments>().contrast.Override(8f);
                var g = p.Add<FilmGrain>(); g.type.Override(FilmGrainLookup.Thin1); g.intensity.Override(0.18f);
            }));
            Volume(volumes, "Curse", 1, 1f, Profile("CutsceneCurse", p =>
            {
                var ca = p.Add<ColorAdjustments>(); ca.saturation.Override(-75f); ca.colorFilter.Override(new Color(0.85f, 0.82f, 0.96f)); ca.postExposure.Override(-0.05f);
                var v = p.Add<Vignette>(); v.intensity.Override(0.45f); v.color.Override(new Color(0.08f, 0f, 0.12f));
            }));
            Volume(volumes, "Warm", 2, 0f, Profile("CutsceneWarm", p =>
            {
                var ca = p.Add<ColorAdjustments>(); ca.saturation.Override(5f); ca.colorFilter.Override(new Color(1f, 0.97f, 0.92f));
                var b = p.Add<Bloom>(); b.intensity.Override(0.9f); b.threshold.Override(1f); b.tint.Override(new Color(1f, 0.94f, 0.84f));
            }));
            Volume(volumes, "Impact", 3, 0f, Profile("CutsceneImpact", p =>
            {
                var b = p.Add<Bloom>(); b.intensity.Override(7f); b.threshold.Override(0.5f);
                p.Add<ChromaticAberration>().intensity.Override(1f);
                p.Add<LensDistortion>().intensity.Override(-0.35f);
                p.Add<ColorAdjustments>().postExposure.Override(0.8f);
            }));

            var vfx = new GameObject("VFX").transform;
            vfx.SetParent(fx.transform);
            // ---- Resonance: the two glow in their colours, the glow climbs their staffs and fires as one ----
            var colors = new[] { Gold, Blue };
            for (int i = 0; i < 2; i++)
            {
                var aura = Tint(Vfx(vfx, "HCFX_Gathering_Energy", chests[i], 0.16f, 0.8f), colors[i]);
                aura.name = "Resonance Aura " + i;
                Mute(aura, "dust_floor", "get_pop");
                controlled.Add((Looping(aura, 1.6f), TQte + 0.3f, TUlt + 0.3f));
                var footRing = FloorRing(vfx, 0.14f, colors[i]);
                footRing.name = "Resonance Ring " + i;
                footRing.transform.position = feet[i] + Vector3.up * 0.02f;
                controlled.Add((Looping(footRing, 1.1f), TQte + 0.2f, TUlt + 0.5f));
                var charge = Tint(Vfx(vfx, MspDir + "LightningExplosion/Prefabs/LightningExplosionCharge", sources[i], 0.035f), colors[i]);
                charge.name = "Staff Charge " + i;
                controlled.Add((charge, TQte + 0.4f, TStruggleEnd));
                var flash = Vfx(vfx, "HCFX_Flash_05", sources[i], 0.14f);
                flash.name = "Ultimate Flash " + i;
                controlled.Add((flash, TUlt - 0.02f, TUlt + 1f));
            }
            // Between them, the glow meets.
            var meet = Tint(Vfx(vfx, "HCFX_Gathering_Star", (sources[0] + sources[1]) * 0.5f, 0.09f, 0.7f), new Color(1f, 0.95f, 0.85f));
            controlled.Add((Looping(meet, 1.4f), TQteLoop, TUlt + 0.3f));

            var chest = new Vector3(0f, 0.55f, 0f) + BossRotation * Vector3.forward * 0.15f;
            controlled.Add((Vfx(vfx, "HCFX_Appear_03", Vector3.zero, 0.12f), 0f, TSwap));
            // Blast effects end while the screen is still fully white so nothing pops out as the flash fades.
            controlled.Add((Vfx(vfx, "HCFX_Explosion_02", chest, 0.25f), TBlast, TWhiteHoldEnd));
            controlled.Add((Vfx(vfx, MspDir + "LightningExplosion/Prefabs/LightningExplosionHit", chest, 0.22f), TBlast - 0.05f, TWhiteHoldEnd));
            controlled.Add((Vfx(vfx, "HCFX_Flash_05", chest, 0.3f), TBlast - 0.05f, TWhiteHoldEnd));
            // ---- Purification ----
            // The curse seeps off the fallen body and thins out as the light takes over.
            controlled.Add((CurseWisps(vfx, unhoodedBody, 8.5f + Shift, TPurify), 8.5f + Shift, TPurify));
            // Light strikes down from the sky onto the child (the Klaus beams are sky-to-ground by design).
            var strike = KlausBeam(vfx, "HCFX_Beam_03", 0.22f);
            strike.transform.localPosition = new Vector3(-0.25f, 0f, 0f);
            controlled.Add((strike, TLightStrike, TLightStrike + 1.5f));
            // The curse spirals away as grey smoke while blue spirit lights gather in.
            var swirl = Vfx(vfx, "HCFX_Energy_02", new Vector3(0f, 0.05f, 0f), 0.2f);
            Mute(swirl, "leaves", "particle_stone");
            controlled.Add((Looping(swirl, 1.2f), TLightStrike + 0.5f, TPurify));
            var spirits = Vfx(vfx, "HCFX_Gathering_Energy", new Vector3(0.1f, 0.35f, 0f), 0.15f);
            Mute(spirits, "dust_floor", "get_pop");
            controlled.Add((spirits, 13.8f + Shift, TPurify));
            // Standing up completes the purification: burst, floor shockwave, and the colour snaps back.
            var purifyBurst = Vfx(vfx, MspDir + "EnergyExplosion/Prefabs/EnergyExplosionHit", new Vector3(0.2f, 0.45f, 0f), 0.13f);
            Mute(purifyBurst, "Core", "Flare", "Light", "Light (1)", "ScreenSmoke", "Smoke (1)", "Smoke (4)"); // layers that fill the screen and hide the child
            controlled.Add((purifyBurst, TPurify - 0.05f, TPurify + 2.5f));
            // Cartoon light stripes only while the light is still fighting the curse; the cut at TPurify hides their end.
            controlled.Add((Vfx(vfx, "HCFX_Blessed_01_Big", Vector3.zero, 0.35f, 0.45f), 12f + Shift, TPurify));
            // After the purification the light settles into faint warm-white shafts instead of saturated yellow.
            var shafts = Tint(Vfx(vfx, "HCFX_Blessed_01_Big", new Vector3(0.15f, 0f, 0f), 0.4f, 0.22f), new Color(1f, 0.95f, 0.86f));
            Mute(shafts, "particle_star");
            controlled.Add((shafts, TPurify, TEnd));
            controlled.Add((Motes(vfx, "AshMotes", new Color(0.7f, 0.68f, 0.76f, 0.7f), 45f, 0.018f, 0.06f), 0f, TEnd));
            controlled.Add((Motes(vfx, "LightMotes", new Color(1f, 0.96f, 0.88f, 0.45f), 22f, 0.014f, 0.08f), 13.5f + Shift, TEnd));
            return animator;
        }

        // ---------- Animation curves ----------

        static AnimationClip BuildFxClip(Vector3 tip, float clashX0, float[] clashKeys, Vector3[] sources)
        {
            var clip = new AnimationClip { name = "FX_BossDefeat" };

            var clash = Concat(new[] { 0f, clashX0 }, clashKeys);
            var clashCurve = Smooth(clash);
            BindPosition(clip, "Clash", clashCurve, tip, TEnd);

            // Purple beam head flies from the boss's wand to the clash point (TFire..TMeet), then rides it.
            // Scale = head-to-caster length / 20.
            var purpleHead = Concat(new[] { 0f, tip.x, TFire, tip.x }, clashKeys);
            BindPosition(clip, "BeamPurple", Smooth(purpleHead), tip, TEnd);
            var purpleLength = Concat(new[] { 0f, 0f }, MapValues(purpleHead, x => Mathf.Max(0f, tip.x - x) / KlausBeamLength, 1), new[] { TBlast + 0.05f, 0f });
            BindUniformScale(clip, "BeamPurple", Smooth(purpleLength));

            // Player beams run from each staff tip to the clash, which is off their line, so bake position, rotation
            // and length together: the head flies out from the tip (TFire..TMeet), rides the clash, is driven into
            // the boss with the blast and the beam shrinks away by TSwap.
            string[] names = { "BeamAmelia", "BeamNox" };
            for (int i = 0; i < 2; i++)
            {
                var src = sources[i];
                Vector3 ClashAt(float t) => new Vector3(t >= TSwap ? tip.x - 0.02f : clashCurve.Evaluate(t), tip.y, tip.z);
                var meetPoint = ClashAt(TMeet);
                var px = new AnimationCurve(); var py = new AnimationCurve(); var pz = new AnimationCurve();
                var rx = new AnimationCurve(); var ry = new AnimationCurve(); var rz = new AnimationCurve();
                var sc = new AnimationCurve();
                Vector3 prevEuler = Vector3.zero;
                bool first = true;
                for (float t = 0f; t <= TSwap + 0.2f; t += 1f / 30f)
                {
                    Vector3 h = t < TFire ? src : t < TMeet ? Vector3.Lerp(src, meetPoint, Mathf.SmoothStep(0f, 1f, (t - TFire) / (TMeet - TFire))) : ClashAt(t);
                    var dir = src - h;
                    var aim = dir.sqrMagnitude > 1e-4f ? dir.normalized : (src - meetPoint).normalized;
                    var e = Quaternion.FromToRotation(Vector3.up, aim).eulerAngles;
                    if (!first) e = new Vector3(prevEuler.x + Mathf.DeltaAngle(prevEuler.x, e.x), prevEuler.y + Mathf.DeltaAngle(prevEuler.y, e.y), prevEuler.z + Mathf.DeltaAngle(prevEuler.z, e.z));
                    prevEuler = e; first = false;
                    float length = t < TFire || t > TSwap + 0.1f ? 0f : dir.magnitude / KlausBeamLength;
                    px.AddKey(t, h.x); py.AddKey(t, h.y); pz.AddKey(t, h.z);
                    rx.AddKey(t, e.x); ry.AddKey(t, e.y); rz.AddKey(t, e.z);
                    sc.AddKey(t, length);
                }
                Bind(clip, names[i], typeof(Transform), "m_LocalPosition.x", px);
                Bind(clip, names[i], typeof(Transform), "m_LocalPosition.y", py);
                Bind(clip, names[i], typeof(Transform), "m_LocalPosition.z", pz);
                Bind(clip, names[i], typeof(Transform), "localEulerAnglesRaw.x", rx);
                Bind(clip, names[i], typeof(Transform), "localEulerAnglesRaw.y", ry);
                Bind(clip, names[i], typeof(Transform), "localEulerAnglesRaw.z", rz);
                BindUniformScale(clip, names[i], sc);
            }

            var clashLight = new List<float> { 0f, 0f, TMeet - 0.02f, 0f, TMeet, 35f, TMeet + 0.35f, 6f };
            clashLight.AddRange(Flicker(TMeet + 0.4f, TUlt - 0.05f, 0.06f, 3.5f, 7f, 3));
            clashLight.AddRange(new[] { TUlt, 30f });
            clashLight.AddRange(Flicker(TUlt + 0.3f, TBlast - 0.1f, 0.05f, 8f, 14f, 5));
            clashLight.AddRange(new[] { TBlast, 25f, TSwap - 0.2f, 0f });
            Bind(clip, "Clash/Light", typeof(Light), "m_Intensity", Lin(clashLight.ToArray()));

            Bind(clip, "Lights/Rim", typeof(Light), "m_Intensity", Lin(0f, 7f, 6.4f + Shift, 7f, 6.9f + Shift, 0f));
            Bind(clip, "Lights/Heaven", typeof(Light), "m_Intensity", Smooth(0f, 0f, 11.8f + Shift, 0f, 18f + Shift, 10f, TEnd, 11f));
            Bind(clip, "Lights/Key", typeof(Light), "m_Intensity", Lin(0f, 1.15f, 6.0f + Shift, 1.15f, 6.2f + Shift, 2.8f, 7.5f + Shift, 0.7f, 12f + Shift, 0.7f, 20f + Shift, 1.2f));
            Bind(clip, "Lights/Key", typeof(Light), "m_Color.r", Smooth(0f, 0.72f, 13f + Shift, 0.72f, 21f + Shift, 1f));
            Bind(clip, "Lights/Key", typeof(Light), "m_Color.g", Smooth(0f, 0.72f, 13f + Shift, 0.72f, 21f + Shift, 0.93f));
            Bind(clip, "Lights/Key", typeof(Light), "m_Color.b", Smooth(0f, 0.9f, 13f + Shift, 0.9f, 21f + Shift, 0.82f));
            Bind(clip, "Lights/PlayerKey", typeof(Light), "m_Intensity", Smooth(0f, 2.5f, TQte, 2.5f, TQteLoop, 4f, TUlt, 4f, TUlt + 0.1f, 5f, TSwap, 0f));
            Bind(clip, "Lights/Resonance", typeof(Light), "m_Intensity", Lin(0f, 0f, TQte + 0.2f, 0f, TQteLoop, 2.5f, TQteEnd, 3.5f, TUlt + 0.05f, 8f, TStruggleEnd, 4f, TSwap, 0f));

            // The grey curse lifts a little with the resonance and the light strike, the rest snaps away on the purification.
            Bind(clip, "Volumes/Curse", typeof(Volume), "weight", Lin(0f, 1f, TQte, 1f, TQteLoop, 0.8f, TUlt, 0.8f, TUlt + 0.3f, 0.6f, TBlast, 1f, TLightStrike, 1f, TPurify - 0.05f, 0.7f, TPurify + 0.6f, 0f));
            Bind(clip, "Volumes/Warm", typeof(Volume), "weight", Smooth(0f, 0f, 14f + Shift, 0f, TPurify - 0.05f, 0.25f, TPurify + 1.2f, 1f));
            Bind(clip, "Volumes/Impact", typeof(Volume), "weight", Lin(0f, 0f, TMeet - 0.02f, 0f, TMeet + 0.03f, 0.75f, TMeet + 0.6f, 0f, TQte - 0.02f, 0f, TQte + 0.04f, 0.35f, TQte + 0.6f, 0f,
                TUlt - 0.02f, 0f, TUlt + 0.04f, 0.6f, TUlt + 0.5f, 0.15f, TStruggleEnd, 0.1f, 6.0f + Shift, 0.3f, TBlast + 0.05f, 1f, 6.8f + Shift, 0.6f, 7.6f + Shift, 0f,
                TLightStrike - 0.02f, 0f, TLightStrike + 0.05f, 0.35f, TLightStrike + 0.6f, 0f, TPurify - 0.02f, 0f, TPurify + 0.05f, 0.5f, TPurify + 0.8f, 0f));
            return SaveClip(clip);
        }

        static AnimationClip BuildUiClip()
        {
            var clip = new AnimationClip { name = "UI_BossDefeat" };
            Bind(clip, "BlackFade", typeof(Image), "m_Color.a", Lin(0f, 1f, 0.35f, 0f, 24.2f + Shift, 0f, TEnd - 0.1f, 1f));
            Bind(clip, "WhiteFlash", typeof(Image), "m_Color.a", Lin(0f, 0f, TUlt - 0.02f, 0f, TUlt + 0.03f, 0.4f, TUlt + 0.3f, 0f, TBlast - 0.1f, 0f, TWhiteFull, 1f, TWhiteHoldEnd, 1f, 8.6f + Shift, 0f,
                TPurify - 0.02f, 0f, TPurify + 0.06f, 0.3f, TPurify + 0.7f, 0f, TEnd, 0f));
            return SaveClip(clip);
        }
    }
}
