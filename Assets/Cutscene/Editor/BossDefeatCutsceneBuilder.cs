using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Timeline;
using UnityEngine.UI;
using static AiHackathon.Cutscene.Editor.CutsceneKit;

namespace AiHackathon.Cutscene.Editor
{
    /// <summary>
    /// Generates the boss defeat cutscene scene + Timeline from the Tripo boss models.
    /// Beam struggle -> blast (hood blown off, model swap under the white flash) -> collapse -> light returns -> stands up.
    /// Re-run from Tools/Cutscene to rebuild after tweaking the numbers below.
    /// </summary>
    public static class BossDefeatCutsceneBuilder
    {
        const string Root = "Assets/Cutscene";
        const string ScenePath = Root + "/BossDefeatCutscene.unity";
        const string TimelinePath = Root + "/BossDefeat.playable";

        const string HoodedMat = "Assets/TripoModels/fantasy_character_3d_model_1/Materials/fantasy_character_3d_model_1.mat";
        const string UnhoodedFbx = "Assets/TripoModels/anime_character_3d_model/anime_character_3d_model.fbx";
        const string UnhoodedMat = "Assets/TripoModels/anime_character_3d_model/Materials/anime_character_3d_model.mat";
        const string UnhoodedTex = "Assets/TripoModels/anime_character_3d_model/anime_character_3d_model.fbm/anime_character_3d_model_BaseColor.jpg";

        // Story beats (seconds)
        const float TStruggleEnd = 5.4f;
        const float TBlast = 6.15f;
        const float TSwap = 6.85f;
        // White flash is fully opaque from TWhiteFull to TWhiteHoldEnd; effects are switched off inside that window.
        const float TWhiteFull = TBlast + 0.25f;
        const float TWhiteHoldEnd = 7.25f;
        const float TFxEnd = TWhiteFull + 0.05f;
        // Cast clip: arm pulls back ~1.0s and thrusts forward at 1.22s, held until ~2.1s.
        const float CastThrust = 1.22f;
        const float CastHeld = 1.82f;
        const float TStandStart = 12.8f;
        // Lying-to-kneeling crossfade length.
        const float StandBlend = 1.4f;
        // Purification: light strikes the fallen child, then the curse breaks as he finishes standing (cut to shot G).
        const float TLightStrike = 12.0f;
        const float TPurify = 17.2f;
        const float TEnd = 25.5f;

        // Both beams fire at TFire; their heads meet at the clash point at TMeet.
        // TFire is when the boss's arm thrusts out in the cast clip (src 1.22s, see HoodedCastIn).
        const float TFire = 0.7f;
        const float TMeet = 1.1f;
        // The players' beam comes in from off-screen left.
        const float GoldSourceX = -3.6f;


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

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            BuildEnvironment();

            var hooded = SpawnCharacter(HoodedFbx, "Boss_Hooded", true);
            PinHoodToHead(hooded.GetComponentInChildren<SkinnedMeshRenderer>());
            var unhooded = SpawnCharacter(UnhoodedFbx, "Boss_Unhooded", false);
            var castClip = FindClip(HoodedFbx, "Young mage casts");
            var hitClip = FindClip(HoodedFbx, "Young mage gets hit");
            var defeatClip = FindClip(UnhoodedFbx, "Young mage loses strength");
            var standClip = FindClip(UnhoodedFbx, "Young mage kneeling");

            // Wand tip with the arm held out after the thrust = where the boss beam starts.
            // Sampled mid-hold: the slow hold drifts the tip a few centimetres either side of this.
            castClip.SampleAnimation(hooded.gameObject, CastHeld + 0.14f);
            var tip = WandTip(hooded.GetComponentInChildren<SkinnedMeshRenderer>(), "Right_Hand");
            // Clash roughly midway so both beams start equally long (= equally thick).
            var clashX0 = tip.x - 1.8f;

            // The defeat clip ends with the knees ~30cm further forward than the stand clip starts. Wrap the
            // unhooded model in a rig whose child slides by that gap during the crossfade, so the knees stay planted.
            var unhoodedRig = new GameObject("Boss_Unhooded_Rig");
            var unhoodedRigAnimator = unhoodedRig.AddComponent<Animator>();
            var unhoodedOffset = new GameObject("Offset").transform;
            unhoodedOffset.SetParent(unhoodedRig.transform, false);
            unhooded.transform.SetParent(unhoodedOffset, true);
            var kneeGap = KneeGap(unhooded, defeatClip, standClip);

            var fx = BuildFx(tip, clashX0, unhooded.GetComponentInChildren<SkinnedMeshRenderer>(true), out var controlled);
            var cam = BuildCamera();
            var ui = BuildUi(cam);

            var hoodedHead = FindBone(hooded.transform, "Head");
            var unhoodedHead = FindBone(unhooded.transform, "Head");
            var unhoodedChest = FindBone(unhooded.transform, "UpperChest");
            var shots = new[]
            {
                new Shot { Name = "A_ClashWide", Start = 0f, End = 3.2f, From = new Vector3(-0.85f, 0.55f, -3.0f), To = new Vector3(-1.15f, 0.62f, -2.7f), LookPoint = new Vector3(-1.1f, 0.68f, 0f), Fov = 38f, Shake = 0.5f, Kicks = new[] { TMeet, 2.1f, 2.7f } },
                new Shot { Name = "B_BossClose", Start = 3.2f, End = TStruggleEnd, From = new Vector3(-0.62f, 0.45f, -1.38f), To = new Vector3(-0.5f, 0.5f, -1.2f), LookPoint = Vector3.Lerp(hoodedHead.position, tip, 0.45f), Fov = 46f, Shake = 0.7f, StrongShake = true, Kicks = new[] { 3.9f, 4.6f } },
                new Shot { Name = "C_Overwhelm", Start = TStruggleEnd, End = TSwap, From = new Vector3(-2.3f, 0.3f, 1.3f), To = new Vector3(-1.8f, 0.36f, 1.1f), LookPoint = new Vector3(0f, 0.7f, 0f), Fov = 42f, Shake = 1.0f, StrongShake = true, Kicks = new[] { TStruggleEnd + 0.05f, TBlast } },
                new Shot { Name = "D_Collapse", Start = TSwap, End = 10.5f, From = new Vector3(-0.6f, 0.45f, -1.9f), To = new Vector3(-0.45f, 0.38f, -1.65f), LookPoint = new Vector3(-0.25f, 0.33f, 0f), Fov = 40f, Shake = 0.25f },
                new Shot { Name = "E_SilenceHigh", Start = 10.5f, End = 13.8f, From = new Vector3(-2.2f, 2.3f, -1.9f), To = new Vector3(-1.7f, 1.7f, -1.45f), LookPoint = new Vector3(0f, 0.15f, 0f), Fov = 38f, Shake = 0.15f, Kicks = new[] { TLightStrike + 0.02f } },
                new Shot { Name = "F_LiftHead", Start = 13.8f, End = 17.2f, From = new Vector3(-1.12f, 0.22f, -0.12f), To = new Vector3(-0.99f, 0.62f, -0.16f), LookAt = unhoodedHead, LookOffset = new Vector3(0f, 0.03f, 0f), Fov = 30f, Shake = 0.2f },
                new Shot { Name = "G_StandUp", Start = 17.2f, End = 21.7f, From = new Vector3(-1.25f, 0.55f, -0.95f), To = new Vector3(-1.45f, 0.62f, -0.3f), LookAt = unhoodedChest, LookOffset = new Vector3(0f, 0.12f, 0f), Fov = 34f, Shake = 0.15f, Kicks = new[] { TPurify + 0.02f } },
                new Shot { Name = "H_Finale", Start = 20.9f, End = TEnd, From = new Vector3(-1.5f, 0.65f, -0.2f), To = new Vector3(-3.4f, 1.0f, -0.9f), LookPoint = new Vector3(0f, 0.5f, 0f), Fov = 38f, Shake = 0.1f },
            };
            var camRoot = new GameObject("Cameras").transform;
            var vcams = new List<(Shot, Animator, CinemachineCamera, AnimationClip)>();
            foreach (var s in shots) vcams.Add(BuildShotCamera(camRoot, s));

            // Timeline
            AssetDatabase.DeleteAsset(TimelinePath);
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            AssetDatabase.CreateAsset(timeline, TimelinePath);
            var directorGo = new GameObject("Cutscene Director");
            var director = directorGo.AddComponent<PlayableDirector>();
            director.playableAsset = timeline;
            director.playOnAwake = true;
            director.extrapolationMode = DirectorWrapMode.Hold;

            var charGroup = timeline.CreateTrack<GroupTrack>(null, "Characters");
            var hoodedAnim = timeline.CreateTrack<AnimationTrack>(charGroup, "Hooded Anim");
            director.SetGenericBinding(hoodedAnim, hooded);
            // Wind-up and thrust land the arm on TFire, then the held pose plays at a crawl until the hit.
            var windup = hoodedAnim.CreateClip(castClip);
            windup.start = 0; windup.clipIn = CastThrust - TFire;
            windup.duration = CastHeld - windup.clipIn;
            var hold = hoodedAnim.CreateClip(castClip);
            hold.start = windup.end; hold.clipIn = CastHeld; hold.timeScale = 0.06f;
            hold.duration = 6.1f - hold.start;
            ((AnimationPlayableAsset)hold.asset).loop = AnimationPlayableAsset.LoopMode.Off;
            // Holding the beam: the boss trembles with effort, easing while he gains ground (3.9s) and shaking hardest as he is overwhelmed.
            AddTremble(timeline, hoodedAnim, hooded, castClip, CastHeld + 0.14f, TFire + 0.3f, 5.95f, 2.2f, 11f,
                Smooth(TFire, 0.6f, 3.2f, 0.8f, 3.9f, 0.5f, 4.6f, 1f, TStruggleEnd, 1.6f, 5.95f, 2f));
            var hit = hoodedAnim.CreateClip(hitClip);
            hit.start = 5.85f; hit.duration = TSwap + 0.1f - 5.85f; hit.clipIn = 0.6f; hit.timeScale = 0.8f;

            var unhoodedAnim = timeline.CreateTrack<AnimationTrack>(charGroup, "Unhooded Anim");
            director.SetGenericBinding(unhoodedAnim, unhooded);
            var defeat = unhoodedAnim.CreateClip(defeatClip);
            defeat.start = TSwap; defeat.duration = TStandStart + StandBlend - TSwap; defeat.clipIn = 0.25f; defeat.timeScale = 0.75f;
            ((AnimationPlayableAsset)defeat.asset).loop = AnimationPlayableAsset.LoopMode.Off;
            var stand = unhoodedAnim.CreateClip(standClip);
            stand.start = TStandStart; stand.duration = TEnd - TStandStart; stand.timeScale = 0.7f;
            ((AnimationPlayableAsset)stand.asset).loop = AnimationPlayableAsset.LoopMode.Off;
            // Same ease-in-out as the clips' default mix curves, so offset and blend weight move together.
            var slide = new AnimationClip { name = "Unhooded_KneeAlign" };
            Bind(slide, "Offset", typeof(Transform), "m_LocalPosition.x", Smooth(0f, 0f, TStandStart, 0f, TStandStart + StandBlend, kneeGap.x));
            Bind(slide, "Offset", typeof(Transform), "m_LocalPosition.y", Lin(0f, 0f, TEnd, 0f));
            Bind(slide, "Offset", typeof(Transform), "m_LocalPosition.z", Smooth(0f, 0f, TStandStart, 0f, TStandStart + StandBlend, kneeGap.z));
            var slideTrack = timeline.CreateTrack<AnimationTrack>(charGroup, "Unhooded Knee Align");
            director.SetGenericBinding(slideTrack, unhoodedRigAnimator);
            var slideClip = slideTrack.CreateClip(SaveClip(slide));
            slideClip.start = 0; slideClip.duration = TEnd;

            Activation(timeline, charGroup, director, "Hooded Active", hooded.gameObject, 0, TSwap);
            Activation(timeline, charGroup, director, "Unhooded Active", unhooded.gameObject, TSwap, TEnd);

            var camGroup = timeline.CreateTrack<GroupTrack>(null, "Cameras");
            var cmTrack = timeline.CreateTrack<CinemachineTrack>(camGroup, "Cinemachine");
            director.SetGenericBinding(cmTrack, cam.GetComponent<CinemachineBrain>());
            foreach (var (s, rig, vcam, moveClip) in vcams)
            {
                var c = cmTrack.CreateClip<CinemachineShot>();
                c.displayName = s.Name; c.start = s.Start; c.duration = s.End - s.Start;
                var shot = (CinemachineShot)c.asset;
                shot.VirtualCamera.exposedName = GUID.Generate().ToString();
                director.SetReferenceValue(shot.VirtualCamera.exposedName, vcam);

                var move = timeline.CreateTrack<AnimationTrack>(camGroup, s.Name + " Move");
                director.SetGenericBinding(move, rig);
                var mc = move.CreateClip(moveClip);
                mc.start = s.Start; mc.duration = s.End - s.Start;
            }

            var fxGroup = timeline.CreateTrack<GroupTrack>(null, "FX");
            var fxTrack = timeline.CreateTrack<AnimationTrack>(fxGroup, "Beams Lights Grading");
            director.SetGenericBinding(fxTrack, fx);
            var fxc = fxTrack.CreateClip(BuildFxClip(tip, clashX0));
            fxc.start = 0; fxc.duration = TEnd;
            var uiTrack = timeline.CreateTrack<AnimationTrack>(fxGroup, "Fade Flash");
            director.SetGenericBinding(uiTrack, ui);
            var uic = uiTrack.CreateClip(BuildUiClip());
            uic.start = 0; uic.duration = TEnd;
            // One track per VFX object; repeated bursts become several clips on that track.
            var controlTracks = new Dictionary<GameObject, ControlTrack>();
            foreach (var (go, start, end) in controlled)
            {
                if (!controlTracks.TryGetValue(go, out var track))
                    controlTracks[go] = track = timeline.CreateTrack<ControlTrack>(fxGroup, go.name);
                Control(track, director, go, start, end);
            }

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


        static Animator BuildFx(Vector3 tip, float clashX0, SkinnedMeshRenderer unhoodedBody, out List<(GameObject, float, float)> controlled)
        {
            controlled = new List<(GameObject, float, float)>();
            var fx = new GameObject("FX");
            var animator = fx.AddComponent<Animator>();

            // Beams: each rig's pivot is the beam head (the clash point) and its +Y points back at the caster,
            // so the prefab's impact burst lands on the clash and uniform scale = length / 20.
            // The one-shot lasers are switched to looping emitters so they re-fire in place every half second
            // and each shot fades out on its own instead of being cut off.
            var head = new Vector3(clashX0, tip.y, tip.z);
            var purpleRig = BeamRig(fx.transform, "BeamPurple", head, Vector3.right);
            controlled.Add((Looping(KlausBeam(purpleRig, "HCFX_Beam_04", 1f), 0.5f), TFire, TFxEnd));
            var goldRig = BeamRig(fx.transform, "BeamGold", head, Vector3.left);
            controlled.Add((Looping(KlausBeam(goldRig, "HCFX_Beam_03", 1f), 0.5f), TFire, TSwap));
            // Final push: a fatter gold layer firing rapidly on top (extra thickness only, keep the boss readable).
            var surge = KlausBeam(goldRig, "HCFX_Beam_03", 1.25f);
            Mute(surge, "hit_impact_view", "hit_impact_floor");
            controlled.Add((Looping(surge, 0.25f), TStruggleEnd, TSwap));

            // Wand: one muzzle flash as the arm thrusts out, then a steady glow while the beam is held.
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
            // Clash point: a looping lightning core plus gold/purple impact bursts that speed up once the gold beam takes over.
            controlled.Add((Vfx(clash, MspDir + "LightningExplosion/Prefabs/LightningExplosionCharge", Vector3.zero, 0.18f), TMeet, TFxEnd));
            var sparks = Vfx(clash, MspDir + "YellowSwordBeam/Prefabs/Par_YellowSwordBeam_Hit", Vector3.zero, 0.22f);
            var rapidSparks = Vfx(clash, MspDir + "YellowSwordBeam/Prefabs/Par_YellowSwordBeam_Hit", Vector3.zero, 0.22f);
            var purpleBurst = Vfx(clash, MspDir + "Shoot_Purple/Prefabs/Par_PurpleShoot_Hit", Vector3.zero, 0.2f);
            // Their smoke/backdrop layers stack into a muddy cloud when re-fired this often.
            foreach (var go in new[] { sparks, rapidSparks, purpleBurst }) Mute(go, "Par_Smoke", "BG");
            controlled.Add((Looping(sparks, 0.45f), TMeet, TStruggleEnd + 0.6f));
            controlled.Add((Looping(rapidSparks, 0.2f), TStruggleEnd, TFxEnd));
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
            var chest = new Vector3(0f, 0.55f, 0f) + BossRotation * Vector3.forward * 0.15f;
            controlled.Add((Vfx(vfx, "HCFX_Appear_03", Vector3.zero, 0.12f), 0f, TSwap));
            // Blast effects end while the screen is still fully white so nothing pops out as the flash fades.
            controlled.Add((Vfx(vfx, "HCFX_Explosion_02", chest, 0.25f), TBlast, TWhiteHoldEnd));
            controlled.Add((Vfx(vfx, MspDir + "LightningExplosion/Prefabs/LightningExplosionHit", chest, 0.22f), TBlast - 0.05f, TWhiteHoldEnd));
            controlled.Add((Vfx(vfx, "HCFX_Flash_05", chest, 0.3f), TBlast - 0.05f, TWhiteHoldEnd));
            // ---- Purification ----
            // The curse seeps off the fallen body and thins out as the light takes over.
            controlled.Add((CurseWisps(vfx, unhoodedBody, 8.5f, TPurify), 8.5f, TPurify));
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
            controlled.Add((spirits, 13.8f, TPurify));
            // Standing up completes the purification: burst, floor shockwave, and the colour snaps back.
            var purifyBurst = Vfx(vfx, MspDir + "EnergyExplosion/Prefabs/EnergyExplosionHit", new Vector3(0.2f, 0.45f, 0f), 0.13f);
            Mute(purifyBurst, "Core", "Flare", "Light", "Light (1)", "ScreenSmoke", "Smoke (1)", "Smoke (4)"); // layers that fill the screen and hide the child
            controlled.Add((purifyBurst, TPurify - 0.05f, TPurify + 2.5f));
            // Cartoon light stripes only while the light is still fighting the curse; the cut at TPurify hides their end.
            controlled.Add((Vfx(vfx, "HCFX_Blessed_01_Big", Vector3.zero, 0.35f, 0.45f), 12f, TPurify));
            // After the purification the light settles into faint warm-white shafts instead of saturated yellow.
            var shafts = Tint(Vfx(vfx, "HCFX_Blessed_01_Big", new Vector3(0.15f, 0f, 0f), 0.4f, 0.22f), new Color(1f, 0.95f, 0.86f));
            Mute(shafts, "particle_star");
            controlled.Add((shafts, TPurify, TEnd));
            controlled.Add((Motes(vfx, "AshMotes", new Color(0.7f, 0.68f, 0.76f, 0.7f), 45f, 0.018f, 0.06f), 0f, TEnd));
            controlled.Add((Motes(vfx, "LightMotes", new Color(1f, 0.96f, 0.88f, 0.45f), 22f, 0.014f, 0.08f), 13.5f, TEnd));
            return animator;
        }


        // ---------- Animation curves ----------

        static AnimationClip BuildFxClip(Vector3 tip, float clashX0)
        {
            var clip = new AnimationClip { name = "FX_BossDefeat" };

            // Clash point after contact: see-saws, the boss briefly gains ground (3.9s), then the gold beam drives it into the boss.
            float[] clashKeys =
            {
                TMeet, clashX0, 1.6f, clashX0 + 0.06f, 2.1f, clashX0 - 0.1f, 2.7f, clashX0 + 0.14f, 3.2f, clashX0 - 0.05f,
                3.9f, clashX0 - 0.22f, 4.6f, clashX0 + 0.08f, 5.0f, clashX0 - 0.04f, TStruggleEnd, clashX0 + 0.03f, TBlast, tip.x - 0.02f,
            };
            var clash = Concat(new[] { 0f, clashX0 }, clashKeys);
            BindPosition(clip, "Clash", Smooth(clash), tip, TEnd);

            // Beam heads fly from each caster to the clash point (TFire..TMeet), then ride it. Scale = head-to-caster length / 20.
            var purpleHead = Concat(new[] { 0f, tip.x, TFire, tip.x }, clashKeys);
            BindPosition(clip, "BeamPurple", Smooth(purpleHead), tip, TEnd);
            var purpleLength = Concat(new[] { 0f, 0f }, MapValues(purpleHead, x => Mathf.Max(0f, tip.x - x) / KlausBeamLength, 1), new[] { TBlast + 0.05f, 0f });
            BindUniformScale(clip, "BeamPurple", Smooth(purpleLength));

            var goldHead = Concat(new[] { 0f, GoldSourceX, TFire, GoldSourceX }, clashKeys, new[] { TSwap, tip.x - 0.02f });
            BindPosition(clip, "BeamGold", Smooth(goldHead), tip, TEnd);
            var goldLength = Concat(new[] { 0f, 0f }, MapValues(goldHead, x => (x - GoldSourceX) / KlausBeamLength, 1), new[] { TSwap + 0.1f, 0f });
            BindUniformScale(clip, "BeamGold", Smooth(goldLength));

            var clashLight = new List<float> { 0f, 0f, TMeet - 0.02f, 0f, TMeet, 35f, TMeet + 0.35f, 6f };
            clashLight.AddRange(Flicker(TMeet + 0.4f, TBlast - 0.1f, 0.06f, 3.5f, 7f, 3));
            clashLight.AddRange(new[] { TBlast, 25f, TSwap - 0.2f, 0f });
            Bind(clip, "Clash/Light", typeof(Light), "m_Intensity", Lin(clashLight.ToArray()));

            Bind(clip, "Lights/Rim", typeof(Light), "m_Intensity", Lin(0f, 7f, 6.4f, 7f, 6.9f, 0f));
            Bind(clip, "Lights/Heaven", typeof(Light), "m_Intensity", Smooth(0f, 0f, 11.8f, 0f, 18f, 10f, TEnd, 11f));
            Bind(clip, "Lights/Key", typeof(Light), "m_Intensity", Lin(0f, 1.15f, 6.0f, 1.15f, 6.2f, 2.8f, 7.5f, 0.7f, 12f, 0.7f, 20f, 1.2f));
            Bind(clip, "Lights/Key", typeof(Light), "m_Color.r", Smooth(0f, 0.72f, 13f, 0.72f, 21f, 1f));
            Bind(clip, "Lights/Key", typeof(Light), "m_Color.g", Smooth(0f, 0.72f, 13f, 0.72f, 21f, 0.93f));
            Bind(clip, "Lights/Key", typeof(Light), "m_Color.b", Smooth(0f, 0.9f, 13f, 0.9f, 21f, 0.82f));

            // The grey curse lifts a little with the light strike and the rest snaps away on the purification burst.
            Bind(clip, "Volumes/Curse", typeof(Volume), "weight", Lin(0f, 1f, TLightStrike, 1f, TPurify - 0.05f, 0.7f, TPurify + 0.6f, 0f));
            Bind(clip, "Volumes/Warm", typeof(Volume), "weight", Smooth(0f, 0f, 14f, 0f, TPurify - 0.05f, 0.25f, TPurify + 1.2f, 1f));
            Bind(clip, "Volumes/Impact", typeof(Volume), "weight", Lin(0f, 0f, TMeet - 0.02f, 0f, TMeet + 0.03f, 0.75f, TMeet + 0.6f, 0f, TStruggleEnd, 0f, 6.0f, 0.3f, TBlast + 0.05f, 1f, 6.8f, 0.6f, 7.6f, 0f,
                TLightStrike - 0.02f, 0f, TLightStrike + 0.05f, 0.35f, TLightStrike + 0.6f, 0f, TPurify - 0.02f, 0f, TPurify + 0.05f, 0.5f, TPurify + 0.8f, 0f));
            return SaveClip(clip);
        }

        static AnimationClip BuildUiClip()
        {
            var clip = new AnimationClip { name = "UI_BossDefeat" };
            Bind(clip, "BlackFade", typeof(Image), "m_Color.a", Lin(0f, 1f, 0.35f, 0f, 24.2f, 0f, TEnd - 0.1f, 1f));
            Bind(clip, "WhiteFlash", typeof(Image), "m_Color.a", Lin(0f, 0f, TBlast - 0.1f, 0f, TWhiteFull, 1f, TWhiteHoldEnd, 1f, 8.6f, 0f,
                TPurify - 0.02f, 0f, TPurify + 0.06f, 0.3f, TPurify + 0.7f, 0f, TEnd, 0f));
            return SaveClip(clip);
        }

    }
}
