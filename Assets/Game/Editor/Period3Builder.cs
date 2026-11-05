using System.Collections.Generic;
using System.Linq;
using AiHackathon.Cutscene.Editor;
using AiHackathon.Parchment;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Waaang;
using Waaang.EditorTools;
using static AiHackathon.Cutscene.Editor.CutsceneKit;

namespace AiHackathon.Game.Editor
{
    /// <summary>
    /// Builds the Period 3 boss fight scene: the boss arena, Amelia and Nox facing the boss (the same casters as the
    /// other periods), the boss with its animator, ward and curse-orb templates, and <see cref="BossPeriod"/>, which
    /// plays the three boss cutscenes around the fight. Uses the boss cutscene frame: boss at the origin facing -X,
    /// players on the -X side facing +X. Adds the scene and the cutscenes to Build Settings.
    /// </summary>
    public static class Period3Builder
    {
        public const string Root = "Assets/Game/Period3";
        public const string ScenePath = Root + "/Period3.unity";
        const string ProfileDir = "Assets/Game/Profiles";
        const string CardPrefab = "Assets/Parchment/ParchmentTitleCard.prefab";
        const string Model2Fbx = "Assets/TripoModels/fantasy_character_3d_model_2/fantasy_character_3d_model_2.fbx";

        static readonly Vector3 AmeliaPos = new Vector3(-3.7f, 0f, 0.78f);
        static readonly Vector3 NoxPos = new Vector3(-3.78f, 0f, -0.78f);
        static readonly Quaternion PlayerFacing = Quaternion.Euler(0f, 90f, 0f);
        const float BossScale = 1.3f;
        // Play camera behind the pair, like the Game scene's duo shot.
        static readonly Vector3 CamBack = new Vector3(-1.5f, 0.55f, 0f);
        const float CamPitch = 10f;
        const float CamFov = 56f;

        [MenuItem("Tools/Period 3/Build Period 3 Scene")]
        static void BuildFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build();
        }

        public static void Build()
        {
            var ameliaProfile = AssetDatabase.LoadAssetAtPath<CasterProfile>(ProfileDir + "/Amelia.asset");
            var noxProfile = AssetDatabase.LoadAssetAtPath<CasterProfile>(ProfileDir + "/Nox.asset");
            if (ameliaProfile == null || noxProfile == null) throw new System.Exception("Build the Period 1 cutscene first: it creates the caster profiles.");
            AssetRoot = Root;
            PrepareFolders();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var playRoot = new GameObject("Period3 Play");
            var envLoops = BossArena.Build(playRoot.transform, colliders: true);

            // ---- Camera ----
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            camGo.transform.SetParent(playRoot.transform);
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = BossArena.FogColor;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 120f;
            cam.fieldOfView = CamFov;
            camGo.AddComponent<AudioListener>();
            var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

            // ---- Players ----
            var profiles = new[] { ameliaProfile, noxProfile };
            var positions = new[] { AmeliaPos, NoxPos };
            var rigs = new WandPointerRig[2];
            for (int i = 0; i < 2; i++)
            {
                var before = new HashSet<GameObject>(scene.GetRootGameObjects());
                var rig = WaaangSceneSetup.CreateCaster(profiles[i], cam, positions[i], i);
                rig.toonScene = false;
                rig.drawTrails = false;
                rig.character.gameObject.AddComponent<CasterMotion>().rig = rig;
                rig.character.rotation = PlayerFacing * rig.character.rotation;
                // Everything the caster added at the scene root goes under the play root, so cutscenes can switch it off.
                foreach (var go in scene.GetRootGameObjects())
                    if (!before.Contains(go) && go != playRoot) go.transform.SetParent(playRoot.transform, true);
                rigs[i] = rig;
            }
            var headA = FindBone(rigs[0].character, "Head").position;
            var headB = FindBone(rigs[1].character, "Head").position;
            var mid = (headA + headB) * 0.5f;
            camGo.transform.position = mid + CamBack;
            camGo.transform.rotation = Quaternion.LookRotation(Vector3.right) * Quaternion.Euler(CamPitch, 0f, 0f);

            // ---- Lights, grading ----
            var lights = StandardLights(playRoot.transform);
            lights.Find("Key").GetComponent<Light>().intensity = 1.0f;
            // Soft fill on the players' backs and the floor in front of them; the rim and key are set for the boss.
            var fill = Light(lights, "Player Fill", LightType.Point, new Color(0.7f, 0.68f, 0.9f), 1.4f, 5f);
            fill.transform.position = mid + new Vector3(-1.2f, 0.9f, 0f);
            fill.shadows = LightShadows.None;
            var bossFill = Light(lights, "Boss Front", LightType.Spot, new Color(0.85f, 0.8f, 1f), 3f, 7f);
            bossFill.transform.position = new Vector3(-2.2f, 2.4f, 0f);
            bossFill.transform.LookAt(new Vector3(0f, 0.8f, 0f));
            bossFill.spotAngle = 40f;
            bossFill.shadows = LightShadows.None;
            var volumes = StandardVolumes(playRoot.transform);
            Volume(volumes, "Phase2", 4, 0f, Profile("Period3Phase2", p =>
            {
                var ca = p.Add<ColorAdjustments>(); ca.saturation.Override(-20f); ca.colorFilter.Override(new Color(0.92f, 0.78f, 1f)); ca.contrast.Override(20f);
                var v = p.Add<Vignette>(); v.intensity.Override(0.45f); v.color.Override(new Color(0.16f, 0f, 0.22f));
                var b = p.Add<Bloom>(); b.intensity.Override(1.6f); b.threshold.Override(0.85f); b.tint.Override(new Color(0.9f, 0.7f, 1f));
            }));
            // The fight reads better a little less grey than the cutscenes' curse grade.
            volumes.Find("Curse").GetComponent<Volume>().weight = 0.75f;

            // ---- Boss ----
            var bossAnimator = SpawnCharacter(HoodedFbx, "Boss", true);
            bossAnimator.transform.SetParent(playRoot.transform, true);
            bossAnimator.transform.localScale = Vector3.one * BossScale;
            var body = bossAnimator.GetComponentInChildren<SkinnedMeshRenderer>();
            PinHoodToHead(body);
            var bossController = BuildBossController();
            bossAnimator.runtimeAnimatorController = bossController;
            var idle = FindClip(HoodedFbx, "Idle stance");
            idle.SampleAnimation(bossAnimator.gameObject, 0f);
            var capsule = bossAnimator.gameObject.AddComponent<CapsuleCollider>();
            capsule.center = new Vector3(0f, 0.55f, 0f);
            capsule.height = 1.15f;
            capsule.radius = 0.24f;
            var hand = FindBone(bossAnimator.transform, "Right_Hand");
            var tipPos = WandTip(body, "Right_Hand");
            var wandTip = new GameObject("WandTip").transform;
            wandTip.SetParent(hand, false);
            wandTip.position = tipPos;
            var chest = FindBone(bossAnimator.transform, "UpperChest");
            var hitLight = Light(chest, "Hit Light", LightType.Point, Color.white, 0f, 1.6f);
            hitLight.transform.localPosition = Vector3.zero;
            hitLight.shadows = LightShadows.None;

            var fx = new GameObject("Boss FX").transform;
            fx.SetParent(playRoot.transform);
            var violet = BossArena.Violet;
            var ward = BuildWard(fx);

            // Curse orbs: the pink-violet orb effect, a light and a short glowing trail.
            var orb = new GameObject("Curse Orb");
            orb.transform.SetParent(fx);
            var orbCol = orb.AddComponent<SphereCollider>();
            orbCol.radius = 0.45f;   // generous: the laser trails the pointer, a tight box was hard to hit
            orb.AddComponent<CurseOrb>();
            var orbVfx = Vfx(orb.transform, "HCFX_Orb_04", Vector3.zero, 0.16f);
            orbVfx.SetActive(true);
            var orbLight = Light(orb.transform, "Light", LightType.Point, new Color(0.8f, 0.4f, 1f), 1.6f, 1.4f);
            orbLight.transform.localPosition = Vector3.zero;
            orbLight.shadows = LightShadows.None;
            var trail = orb.AddComponent<TrailRenderer>();
            trail.time = 0.35f;
            trail.widthMultiplier = 0.16f;
            trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
            var tg = new Gradient();
            tg.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.8f, 1f), 0f), new GradientColorKey(new Color(0.6f, 0.25f, 1f), 1f) },
                new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
            trail.colorGradient = tg;
            trail.sharedMaterial = AdditiveMat("OrbTrail", null, new Color(1.6f, 1.2f, 2f, 1f));
            trail.shadowCastingMode = ShadowCastingMode.Off;
            orb.SetActive(false);

            var pop = Tint(Vfx(fx, "HCFX_Hit_04", Vector3.zero, 0.09f), new Color(1f, 0.85f, 1f));
            pop.name = "Orb Pop FX";
            var land = Tint(Vfx(fx, "HCFX_Explosion_02", Vector3.zero, 0.12f, 0.8f), new Color(0.7f, 0.3f, 1f));
            land.name = "Orb Land FX";
            var cast = Tint(Vfx(fx, MspDir + "LightningExplosion/Prefabs/LightningExplosionCharge", Vector3.zero, 0.045f), new Color(0.85f, 0.55f, 1f));
            cast.name = "Cast Charge FX";
            var blinkOut = Tint(Vfx(fx, "HCFX_Smoke_02_Floor", Vector3.zero, 0.1f, 0.7f), new Color(0.3f, 0.15f, 0.42f));
            blinkOut.name = "Blink Out FX";
            var blinkIn = Tint(Vfx(fx, "HCFX_Appear_04", Vector3.zero, 0.12f), violet);
            blinkIn.name = "Blink In FX";
            var roar = Vfx(fx, MspDir + "LightningExplosion/Prefabs/LightningExplosionHit", Vector3.zero, 0.1f);
            MuteScreenFill(roar);
            roar.name = "Roar FX";
            var aura = CurseWisps(fx, body, 0f, 10f);
            foreach (var ps in aura.GetComponentsInChildren<ParticleSystem>(true)) { var m = ps.main; m.loop = true; m.duration = 4f; var e = ps.emission; e.rateOverTime = 90f; }
            aura.name = "Phase2 Aura";

            // He floats: a slowly turning seal of light on the ground under him, and its glow.
            var under = new GameObject("Hover Seal").transform;
            under.SetParent(bossAnimator.transform, false);
            under.localPosition = new Vector3(0f, -0.14f / BossScale + 0.01f, 0f);
            var underQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            underQuad.name = "Seal";
            Object.DestroyImmediate(underQuad.GetComponent<Collider>());
            underQuad.transform.SetParent(under, false);
            underQuad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            underQuad.transform.localScale = Vector3.one * 1.1f;
            underQuad.GetComponent<Renderer>().sharedMaterial = AdditiveMat("HoverSeal", AssetDatabase.LoadAssetAtPath<Texture2D>(MagicCircleTex), new Color(0.9f, 0.5f, 1.6f, 0.5f));
            underQuad.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            underQuad.AddComponent<AiHackathon.Cutscene.Spin>().axis = Vector3.forward;
            underQuad.GetComponent<AiHackathon.Cutscene.Spin>().speed = 18f;
            var underLight = Light(under, "Glow", LightType.Point, violet, 1.4f, 1.6f);
            underLight.transform.localPosition = new Vector3(0f, 0.15f, 0f);
            underLight.shadows = LightShadows.None;

            // Stone spirits he summons: the Period 2 rock creature, charging instead of hiding. A roomy hitbox: the
            // laser trails the pointer.
            var minion = Period2CutsceneBuilder.CreateCreature(fx, "Stone Spirit", Vector3.zero, 0f);
            if (minion != null)
            {
                minion.areaMin = new Vector2(-12f, -12f);
                minion.areaMax = new Vector2(12f, 12f);
                minion.viewSlope = 0f;
                var minionCol = minion.gameObject.AddComponent<SphereCollider>();
                minionCol.radius = 0.42f;
                minionCol.center = new Vector3(0f, 0.36f, 0f);
                minion.reachDistance = 1.0f;
                // A faint curse glow on each, so they read against the dark arena floor.
                var minionGlow = Light(minion.transform, "Curse Glow", LightType.Point, new Color(0.85f, 0.45f, 1f), 1.1f, 1.3f);
                minionGlow.transform.localPosition = new Vector3(0f, 0.9f, 0f);
                minionGlow.shadows = LightShadows.None;
                minion.gameObject.SetActive(false);
            }
            // Where one claws up: just a thin violet crack-ring on the floor; the creature sheds its own stone grit as
            // it rises (BossController). The Klaus smoke carried a hidden pink second colour and rose in a column.
            var summon = new GameObject("Summon FX");
            summon.transform.SetParent(fx);
            FloorRing(summon.transform, 0.1f, violet).SetActive(true);
            summon.SetActive(false);

            // Clones: the same boy, same look and animator, no hover seal; BossDecoy makes it waver now and then.
            var decoyAnim = SpawnCharacter(HoodedFbx, "Boss Decoy", true);
            decoyAnim.transform.SetParent(fx, true);
            decoyAnim.transform.localScale = Vector3.one * BossScale;
            var decoyBody = decoyAnim.GetComponentInChildren<SkinnedMeshRenderer>();
            decoyBody.sharedMesh = body.sharedMesh;
            decoyBody.sharedMaterial = body.sharedMaterial;
            decoyAnim.runtimeAnimatorController = bossController;
            var decoyCol = decoyAnim.gameObject.AddComponent<CapsuleCollider>();
            decoyCol.center = capsule.center;
            decoyCol.height = capsule.height;
            decoyCol.radius = capsule.radius * 1.3f;
            decoyAnim.gameObject.AddComponent<BossDecoy>();
            decoyAnim.gameObject.SetActive(false);

            // The curse chain's anchor: a floating violet crystal and a chain of light to the bound player.
            var crystalRoot = new GameObject("Chain Crystal");
            crystalRoot.transform.SetParent(fx);
            var crystalShape = new GameObject("Crystal").transform;
            crystalShape.SetParent(crystalRoot.transform, false);
            crystalShape.localScale = new Vector3(0.2f, 0.38f, 0.2f);
            var gem = GameObject.CreatePrimitive(PrimitiveType.Cube);
            gem.name = "Gem";
            Object.DestroyImmediate(gem.GetComponent<Collider>());
            gem.transform.SetParent(crystalShape, false);
            gem.transform.localRotation = Quaternion.Euler(45f, 0f, 45f);
            var gemMat = LitMaterial("ChainCrystal", new Color(0.32f, 0.12f, 0.45f), 0.85f);
            gemMat.EnableKeyword("_EMISSION");
            gemMat.SetColor("_EmissionColor", new Color(0.9f, 0.35f, 1.6f));
            gemMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            gem.GetComponent<Renderer>().sharedMaterial = gemMat;
            var gemGlow = Light(crystalRoot.transform, "Glow", LightType.Point, violet, 2f, 2.2f);
            gemGlow.transform.localPosition = Vector3.zero;
            gemGlow.shadows = LightShadows.None;
            var gemAura = Tint(Vfx(crystalRoot.transform, "HCFX_Gathering_Energy", Vector3.zero, 0.07f, 0.6f), violet);
            gemAura.SetActive(true);
            var crystalCol = crystalRoot.AddComponent<SphereCollider>();
            crystalCol.radius = 0.4f;
            var chainGo = new GameObject("Chain");
            chainGo.transform.SetParent(crystalRoot.transform, false);
            var chain = chainGo.AddComponent<LineRenderer>();
            chain.positionCount = 18;
            chain.useWorldSpace = true;
            chain.widthCurve = new AnimationCurve(new Keyframe(0f, 0.06f), new Keyframe(1f, 0.035f));
            var cg = new Gradient();
            cg.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.7f, 1f), 0f), new GradientColorKey(new Color(0.6f, 0.25f, 1f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.85f, 1f) });
            chain.colorGradient = cg;
            chain.numCapVertices = 2;
            chain.sharedMaterial = AdditiveMat("CurseChain", null, new Color(1.4f, 0.7f, 2.2f, 1f));
            chain.shadowCastingMode = ShadowCastingMode.Off;
            var crystalFx = crystalRoot.AddComponent<ChainCrystal>();
            crystalFx.crystal = crystalShape;
            crystalFx.chain = chain;
            crystalFx.glow = gemGlow;
            crystalRoot.SetActive(false);
            var crystalBreak = Tint(Vfx(fx, "HCFX_Hit_04", Vector3.zero, 0.14f), new Color(0.95f, 0.7f, 1f));
            crystalBreak.name = "Crystal Break FX";

            var controller = bossAnimator.gameObject.AddComponent<BossController>();
            controller.minionTemplate = minion != null ? minion.gameObject : null;
            controller.summonFx = summon;
            controller.decoyTemplate = decoyAnim.gameObject;
            controller.crystalTemplate = crystalRoot;
            controller.crystalBreakFx = crystalBreak;
            controller.animator = bossAnimator;
            controller.wandTip = wandTip;
            controller.chest = chest;
            controller.ward = ward;
            controller.orbTemplate = orb;
            controller.orbPopFx = pop;
            controller.orbHitFx = land;
            controller.castFx = cast;
            controller.blinkOutFx = blinkOut;
            controller.blinkInFx = blinkIn;
            controller.roarFx = roar;
            controller.phase2Aura = aura;
            controller.hitLight = hitLight;

            // ---- Players' game manager ----
            var gameGo = new GameObject("Game");
            gameGo.transform.SetParent(playRoot.transform);
            var game = gameGo.AddComponent<GameManager>();
            game.players = new GameManager.Player[2];
            var aim = new GameObject("Start Aim").transform;
            aim.SetParent(playRoot.transform);
            aim.position = new Vector3(0f, 0.8f, 0f);
            for (int i = 0; i < 2; i++)
                game.players[i] = new GameManager.Player
                {
                    profile = profiles[i], character = rigs[i].character, rig = rigs[i], pose = rigs[i].character.GetComponent<CasterPose>(),
                    staff = rigs[i].staff, startAim = aim,
                };

            // ---- Title card ----
            var cardRoot = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(CardPrefab));
            cardRoot.transform.SetParent(playRoot.transform);
            var card = cardRoot.GetComponentInChildren<ParchmentTitleCard>(true);

            // ---- The period ----
            var period = new GameObject("Period3").AddComponent<BossPeriod>();
            period.playRoot = playRoot;
            period.game = game;
            period.boss = controller;
            period.playCamera = cam;
            period.titleFont = AssetDatabase.LoadAssetAtPath<Font>(BossDefeatCutsceneBuilder.TitleFontPath);
            period.bodyFont = AssetDatabase.LoadAssetAtPath<Font>(BossDefeatCutsceneBuilder.BodyFontPath);
            period.card = card;
            period.curseVolume = volumes.Find("Curse").GetComponent<Volume>();
            period.phase2Volume = volumes.Find("Phase2").GetComponent<Volume>();
            period.impactVolume = volumes.Find("Impact").GetComponent<Volume>();
            period.entranceScene = "Assets/Cutscene/BossEntrance/BossEntranceCutscene.unity";
            period.phase2Scene = "Assets/Cutscene/BossPhase2/BossPhase2Cutscene.unity";
            period.defeatScene = BossDefeatCutsceneBuilder.ScenePath;

            // Brazier flames and mist play on their own in play mode.
            foreach (var loop in envLoops) loop.SetActive(true);
            // Saved off: BossPeriod switches it on after the entrance cutscene.
            playRoot.SetActive(false);

            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings(ScenePath, period.entranceScene, period.phase2Scene, period.defeatScene);
            PeriodSetup.Run();
            Debug.Log("[Period3] Built " + ScenePath);
        }

        const string MagicCircleTex = "Assets/VFX_Klaus/Textures/Tex_fx_HCFX_MagieCircle_04.tga";

        /// <summary>
        /// The ward: a force-field shell (AiHackathon/Ward), two rune rings orbiting it on tilted planes, a magic
        /// circle turning on the ground, lightning crackle and gathering energy inside, a glow light; plus the raise,
        /// spark, break and shard bursts. Saved inactive; <see cref="WardFx"/> drives it.
        /// </summary>
        static WardFx BuildWard(Transform parent)
        {
            var violet = BossArena.Violet;
            float radius = 0.75f * BossScale;
            var root = new GameObject("Ward");
            root.transform.SetParent(parent);
            root.transform.position = new Vector3(0f, 0.72f * BossScale, 0f);

            var shell = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            shell.name = "Shell";
            shell.transform.SetParent(root.transform, false);
            shell.transform.localScale = Vector3.one * radius * 2f;
            var shellMat = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/Ward.mat");
            if (shellMat == null)
            {
                shellMat = new Material(Shader.Find("AiHackathon/Ward"));
                AssetDatabase.CreateAsset(shellMat, Root + "/Materials/Ward.mat");
            }
            shell.GetComponent<Renderer>().sharedMaterial = shellMat;
            shell.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

            var dressing = new GameObject("Dressing").transform;
            dressing.SetParent(root.transform, false);
            var ringMat = AdditiveMat("WardRuneRing", RuneRingTexture(Root + "/Textures/runering.png"), new Color(1.1f, 0.7f, 2.2f, 0.85f));
            (Vector3 euler, float speed, float scale)[] rings = { (new Vector3(72f, 0f, 18f), 42f, 1.22f), (new Vector3(108f, 50f, -14f), -60f, 1.36f), (new Vector3(90f, 0f, 0f), 25f, 1.5f) };
            for (int i = 0; i < rings.Length; i++)
            {
                var pivot = new GameObject("Rune Ring " + i).transform;
                pivot.SetParent(dressing, false);
                pivot.localRotation = Quaternion.Euler(rings[i].euler);
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                q.name = "Ring";
                Object.DestroyImmediate(q.GetComponent<Collider>());
                q.transform.SetParent(pivot, false);
                q.transform.localScale = Vector3.one * radius * 2f * rings[i].scale;
                q.GetComponent<Renderer>().sharedMaterial = ringMat;
                q.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                var spin = q.AddComponent<AiHackathon.Cutscene.Spin>();
                spin.axis = Vector3.forward;
                spin.speed = rings[i].speed;
                spin.wobble = i == 2 ? 0f : 4f;
            }
            // The flat third ring sits low, as a band round his feet.
            dressing.Find("Rune Ring 2").localPosition = new Vector3(0f, -radius * 0.82f, 0f);
            var circle = GameObject.CreatePrimitive(PrimitiveType.Quad);
            circle.name = "Ground Circle";
            Object.DestroyImmediate(circle.GetComponent<Collider>());
            circle.transform.SetParent(dressing, false);
            circle.transform.localPosition = new Vector3(0f, -0.72f * BossScale + 0.03f, 0f);
            circle.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            circle.transform.localScale = Vector3.one * radius * 3.2f;
            circle.GetComponent<Renderer>().sharedMaterial = AdditiveMat("WardCircle", AssetDatabase.LoadAssetAtPath<Texture2D>(MagicCircleTex), new Color(1f, 0.55f, 2f, 0.9f));
            circle.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            circle.AddComponent<AiHackathon.Cutscene.Spin>().axis = Vector3.forward;
            circle.GetComponent<AiHackathon.Cutscene.Spin>().speed = -30f;

            var crackle = Tint(Vfx(dressing, MspDir + "LightningExplosion/Prefabs/LightningExplosionCharge", Vector3.zero, 0.11f), new Color(0.85f, 0.6f, 1f));
            crackle.SetActive(true);
            var swirl = Tint(Vfx(dressing, "HCFX_Gathering_Energy", Vector3.zero, 0.2f, 0.75f), violet);
            Mute(swirl, "dust_floor", "get_pop");
            Looping(swirl, 1.4f).SetActive(true);
            var glow = Light(root.transform, "Glow", LightType.Point, new Color(0.75f, 0.45f, 1f), 2.5f, 3.2f);
            glow.transform.localPosition = Vector3.zero;
            glow.shadows = LightShadows.None;

            var raise = Tint(Vfx(parent, "HCFX_Appear_04", Vector3.zero, 0.16f), violet);
            raise.name = "Ward Raise FX";
            var spark = Vfx(parent, MspDir + "Shoot_Purple/Prefabs/Par_PurpleShoot_Hit", Vector3.zero, 0.07f);
            Mute(spark, "Par_Smoke", "BG");
            spark.name = "Ward Spark FX";
            var shatter = Vfx(parent, MspDir + "LightningExplosion/Prefabs/LightningExplosionHit", Vector3.zero, 0.12f);
            MuteScreenFill(shatter);
            shatter.name = "Ward Break FX";
            var shards = Tint(Vfx(parent, "HCFX_Explosion_03", Vector3.zero, 0.2f), new Color(1f, 0.7f, 1f));
            shards.name = "Ward Shards FX";

            var fx = root.AddComponent<WardFx>();
            fx.shell = shell.GetComponent<Renderer>();
            fx.hitCollider = shell.GetComponent<Collider>();
            fx.dressing = dressing;
            fx.raiseFx = raise;
            fx.sparkFx = spark;
            fx.breakFx = shatter;
            fx.shardFx = shards;
            fx.glow = glow;
            root.SetActive(false);
            return fx;
        }

        static Material AdditiveMat(string name, Texture texture, Color color)
        {
            var path = Root + "/Materials/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 2f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.One);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetTexture("_BaseMap", texture);
            m.SetColor("_BaseColor", color);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>A band of rune glyphs between two thin rings (transparent elsewhere).</summary>
        static Texture2D RuneRingTexture(string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) return existing;
            EnsureFolder(Root + "/Textures");
            const int n = 512, glyphs = 28;
            float Line(float d, float w) => Mathf.Clamp01(1f - (d - w) / (w * 1.5f));
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true);
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                var p = new Vector2((x + 0.5f) / n * 2f - 1f, (y + 0.5f) / n * 2f - 1f);
                float r = p.magnitude, a = 0f;
                a = Mathf.Max(a, Line(Mathf.Abs(r - 0.975f), 0.008f));
                a = Mathf.Max(a, Line(Mathf.Abs(r - 0.8f), 0.006f));
                if (r > 0.815f && r < 0.96f)
                {
                    float th = (Mathf.Atan2(p.y, p.x) / (Mathf.PI * 2f) + 1f) % 1f * glyphs;
                    int cell = Mathf.FloorToInt(th);
                    float lu = (th - cell - 0.5f) * (Mathf.PI * 2f * r / glyphs) / 0.13f;
                    float lv = (r - 0.8875f) / 0.06f;
                    uint h = (uint)(cell * 2654435761u + 31u);
                    float g = 0f; const float w = 0.12f;
                    if ((h & 1) != 0) g = Mathf.Max(g, Line(Mathf.Abs(lu), w) * (Mathf.Abs(lv) < 0.85f ? 1f : 0f));
                    if ((h & 2) != 0) g = Mathf.Max(g, Line(Mathf.Abs(lv - 0.4f), w) * (Mathf.Abs(lu) < 0.4f ? 1f : 0f));
                    if ((h & 4) != 0) g = Mathf.Max(g, Line(Mathf.Abs(lu - lv * 0.45f), w) * (lv > -0.1f && lv < 0.85f ? 1f : 0f));
                    if ((h & 8) != 0) g = Mathf.Max(g, Line(Mathf.Abs(lu + lv * 0.45f), w) * (lv < 0.1f && lv > -0.85f ? 1f : 0f));
                    if ((h & 16) != 0) g = Mathf.Max(g, Line(Mathf.Abs(new Vector2(lu, lv + 0.3f).magnitude - 0.25f), w * 0.8f));
                    if ((h & 3) == 0) g = Mathf.Max(g, Line(Mathf.Abs(lu), w));
                    a = Mathf.Max(a, g);
                }
                a = Mathf.Max(a, Mathf.Clamp01(1f - Mathf.Abs(r - 0.89f) / 0.1f) * 0.12f);
                px[y * n + x] = new Color(1f, 1f, 1f, r > 1f ? 0f : a);
            }
            tex.SetPixels(px);
            tex.Apply();
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Boss states played by <see cref="AiHackathon.Game.BossController"/> through CrossFade.</summary>
        static RuntimeAnimatorController BuildBossController()
        {
            var path = Root + "/Boss.controller";
            AssetDatabase.DeleteAsset(path);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(path);
            var sm = ctrl.layers[0].stateMachine;
            (string state, string fbx, string clip)[] states =
            {
                ("Idle", HoodedFbx, "Idle stance"),
                ("Cast", HoodedFbx, "Young mage casts"),
                ("Summon", HoodedFbx, "Young mage raises the wand"),
                ("Guard", HoodedFbx, "Young mage quickly raises"),
                ("Hit", HoodedFbx, "Young mage gets hit"),
                ("Roar", Model2Fbx, "Young mage throws both arms"),
                ("Push", HoodedFbx, "Young mage pushes"),
            };
            foreach (var (state, fbx, clip) in states)
            {
                var s = sm.AddState(state);
                s.motion = FindClip(fbx, clip);
                if (state == "Idle") sm.defaultState = s;
            }
            return ctrl;
        }

        static void AddToBuildSettings(params string[] paths)
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            foreach (var p in paths)
                if (!scenes.Any(s => s.path == p)) scenes.Add(new EditorBuildSettingsScene(p, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
