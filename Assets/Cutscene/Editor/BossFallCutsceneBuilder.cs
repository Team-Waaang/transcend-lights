using System.Collections.Generic;
using AiHackathon.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Timeline;
using UnityEngine.UI;
using Waaang.EditorTools;
using static AiHackathon.Cutscene.Editor.CutsceneKit;
using static AiHackathon.Cutscene.Editor.PeriodSceneKit;

namespace AiHackathon.Cutscene.Editor
{
    /// <summary>
    /// The way into Period 3, played right before the boss entrance (<see cref="BossPeriod.fallScene"/>): back in
    /// Period 2's stone hall Amelia and Nox cheer, the floor rumbles and cracks with violet light, it gives way and
    /// they drop through the dark, crash down onto the boss court, get up and look round, and the boss slowly takes
    /// shape out of the curse at the seal. Ends on black, where the entrance cutscene picks up.
    ///
    /// Three places in one scene, far apart so they never see each other: the hall at <see cref="HallOrigin"/>
    /// (turned so the girls face +X as in the arena), an empty void at <see cref="VoidCenter"/> for the fall, and the
    /// arena at the origin. Each girl sits in a mover ("Offset" under "&lt;name&gt; Mover") that carries her between them;
    /// her own Animator plays the pose clips (<see cref="PeriodPoseClips.BuildFall"/>).
    /// </summary>
    public static class BossFallCutsceneBuilder
    {
        public const string Root = "Assets/Cutscene/BossFall";
        public const string ScenePath = Root + "/BossFallCutscene.unity";
        const string TimelinePath = Root + "/BossFall.playable";
        const string ProfileDir = "Assets/Game/Profiles";
        const string StandFbx = "Assets/TripoModels/fantasy_character_3d_model_2/fantasy_character_3d_model_2.fbx";

        // Beats (seconds)
        const float TRumble = 3.3f;    // the floor starts to shake
        const float TCrack = 3.55f;    // violet cracks spread under them
        const float TBreak = 4.8f;     // it gives way
        const float TVoid = 5.7f;      // cut: falling through the dark
        const float TArena = 7.9f;     // cut: the boss court
        const float TDrop = 8.4f;      // they come down into frame
        const float TLand = 8.78f;
        const float TOmen = 12.3f;     // the seal stirs
        const float TSmoke = 13.0f;    // curse smoke rises; the boss is inside it
        const float TReveal = 13.6f;   // light finds his silhouette
        const float TGlint = 16.0f;
        const float TFadeOut = 16.35f;
        const float TEnd = 17f;

        static readonly Vector3 HallOrigin = new Vector3(300f, 0f, 0f);
        static readonly Quaternion HallRotation = Quaternion.Euler(0f, 90f, 0f); // hall +Z (where the class faces) → world +X
        static readonly Vector3 VoidCenter = new Vector3(600f, 0f, 0f);
        static readonly Vector3[] ArenaSpots = { BossDefeatCutsceneBuilder.AmeliaPos, BossDefeatCutsceneBuilder.NoxPos };
        // Where they stand in the hall (hall space): Amelia on the left, Nox on the right, as in every period.
        static readonly Vector3[] HallLocal = { new Vector3(-0.78f, 0f, 1f), new Vector3(0.78f, 0f, 0.92f) };
        static readonly Vector3 HoleLocal = new Vector3(0f, 0f, 1f);
        // Where each hangs in the void (offsets from VoidCenter).
        static readonly Vector3[] VoidLocal = { new Vector3(0f, 0.4f, 0.75f), new Vector3(0.25f, -0.15f, -0.8f) };
        static readonly Color Violet = new Color(0.62f, 0.38f, 1f);
        static readonly Color Warm = new Color(1f, 0.84f, 0.62f);

        static Vector3 Hall(Vector3 local) => HallOrigin + HallRotation * local;

        [MenuItem("Tools/Cutscene/Build Boss Fall Cutscene")]
        static void BuildFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build();
        }

        public static void Build()
        {
            AssetRoot = Root;
            PrepareFolders();
            EnsureFolder(Root + "/Textures");
            var profiles = new[]
            {
                AssetDatabase.LoadAssetAtPath<CasterProfile>(ProfileDir + "/Amelia.asset"),
                AssetDatabase.LoadAssetAtPath<CasterProfile>(ProfileDir + "/Nox.asset"),
            };
            if (profiles[0] == null || profiles[1] == null) throw new System.Exception("Build the Period 1 cutscene first: it creates the caster profiles.");
            PeriodPoseClips.BuildFall();
            AssetRoot = Root;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // The hall first: the arena sets the scene's fog and ambient last (the cutscene runs inside Period 3 anyway).
            var hall = StoneHallBuilder.Build(Root + "/Textures");
            hall.root.SetPositionAndRotation(HallOrigin, HallRotation);
            Object.DestroyImmediate(hall.shafts.gameObject);
            foreach (var r in hall.windows.GetComponentsInChildren<Renderer>())
                r.sharedMaterial.SetColor("_BaseColor", new Color(0.55f, 0.6f, 0.8f));
            var envLoops = BuildEnvironment();
            var cam = BuildCamera();

            // ---- Amelia and Nox, each in a mover ----
            var movers = new Animator[2];
            var players = new Animator[2];
            for (int i = 0; i < 2; i++)
            {
                var rig = WaaangSceneSetup.CreateCaster(profiles[i], cam, Vector3.zero, i);
                rig.toonScene = false;
                rig.gameObject.SetActive(false);
                rig.character.GetComponent<Waaang.CasterPose>().enabled = false;
                rig.character.rotation = BossDefeatCutsceneBuilder.PlayerFacing * rig.character.rotation;
                HoldStaff(rig.character, rig.staff, AssetDatabase.GetAssetPath(profiles[i].characterModel), profiles[i].LeadSide, profiles[i].staffScale);
                var animator = rig.character.gameObject.AddComponent<Animator>();
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                players[i] = animator;
                foreach (var n in new[] { "Hit Marker ", "Hit Light ", "Laser " })
                {
                    var go = GameObject.Find(n + profiles[i].displayName);
                    if (go != null) Object.DestroyImmediate(go);
                }
                var mover = new GameObject(profiles[i].displayName + " Mover");
                movers[i] = mover.AddComponent<Animator>();
                var offset = new GameObject("Offset").transform;
                offset.SetParent(mover.transform, false);
                offset.position = Hall(HallLocal[i]);
                rig.character.SetParent(offset, false);
                rig.character.localPosition = Vector3.zero;
            }

            // ---- The boss, dark at the seal until the light finds him ----
            var boss = SpawnCharacter(HoodedFbx, "Boss_Hooded", false);
            var bossBody = boss.GetComponentInChildren<SkinnedMeshRenderer>(true);
            PinHoodToHead(bossBody);
            var stand = FindClip(StandFbx, "Young mage standing still");
            boss.gameObject.SetActive(true);
            stand.SampleAnimation(boss.gameObject, 2f);
            var bossHead = FindBone(boss.transform, "Head");
            var headPos = bossHead.position;
            boss.gameObject.SetActive(false);

            var fx = BuildFx(headPos, out var controlled, out var debris, out var slabs);
            foreach (var loop in envLoops) controlled.Add((loop, 0f, TEnd));
            var ui = BuildUi(cam);

            var (timeline, director) = CreateTimeline(TimelinePath);
            var chars = timeline.CreateTrack<GroupTrack>(null, "Characters");
            string[] who = { "Amelia", "Nox" };
            for (int i = 0; i < 2; i++)
            {
                var track = timeline.CreateTrack<AnimationTrack>(chars, profiles[i].displayName + " Anim");
                director.SetGenericBinding(track, players[i]);
                // Overlapping clips crossfade.
                Place(track, PeriodPoseClips.Load(who[i] + "_Cheer"), 0f, TRumble + 0.3f);
                Place(track, PeriodPoseClips.Load(who[i] + "_Startle"), TRumble, TBreak + 0.3f - TRumble);
                Place(track, PeriodPoseClips.Load(who[i] + "_Fall"), TBreak, TLand + 0.04f - TBreak, 0.9f * i, 1f, true);
                Place(track, PeriodPoseClips.Load(who[i] + "_Land"), TLand, TEnd - TLand, 0f, i == 0 ? 1f : 0.96f);
                var move = timeline.CreateTrack<AnimationTrack>(chars, profiles[i].displayName + " Move");
                director.SetGenericBinding(move, movers[i]);
                Place(move, MoverClip(i), 0f, TEnd);
            }
            var bossAnim = timeline.CreateTrack<AnimationTrack>(chars, "Boss Anim");
            director.SetGenericBinding(bossAnim, boss);
            Place(bossAnim, stand, TSmoke, TEnd - TSmoke, 0f, 0.7f);
            Activation(timeline, chars, director, "Boss Active", boss.gameObject, TSmoke + 0.25f, TEnd);

            var hole = Hall(HoleLocal);
            var voidMid = VoidCenter + new Vector3(0f, 1.1f, 0f);
            AddShots(timeline, director, cam, new[]
            {
                // In front of the two in the hall, a little low: the celebration.
                new Shot { Name = "A_Cheer", Start = 0f, End = TRumble, From = Hall(new Vector3(0.55f, 1.15f, 3.7f)), To = Hall(new Vector3(0.45f, 1.1f, 3.35f)), LookPoint = Hall(new Vector3(0f, 1.1f, 1f)), Fov = 36f, Shake = 0.05f },
                // Down at the flagstones: the cracks run under their feet.
                new Shot { Name = "B_Rumble", Start = TRumble, End = TBreak, From = Hall(new Vector3(-1.95f, 0.95f, 3.1f)), To = Hall(new Vector3(-1.75f, 0.88f, 2.9f)), LookPoint = Hall(new Vector3(0f, 0.55f, 1f)), Fov = 50f, Shake = 0.35f, StrongShake = true, Kicks = new[] { TCrack, 4.25f }, KickPeak = 1.6f },
                // From above: the floor opens and they drop.
                new Shot { Name = "C_Break", Start = TBreak, End = TVoid, From = Hall(new Vector3(0.3f, 3.3f, 3.1f)), To = Hall(new Vector3(0.25f, 3.45f, 3.0f)), LookPoint = hole + Vector3.down * 0.6f, Fov = 52f, Shake = 0.5f, StrongShake = true, Kicks = new[] { TBreak + 0.02f }, KickPeak = 3f, KickDecay = 0.8f },
                // The fall: alongside them in the dark, rubble rising past.
                new Shot { Name = "D_Void", Start = TVoid, End = TArena, From = voidMid + new Vector3(2.7f, -0.2f, 1.2f), To = voidMid + new Vector3(2.35f, -0.35f, 1.0f), LookPoint = voidMid, Fov = 46f, Shake = 0.45f },
                // The court, low: they crash down.
                new Shot { Name = "E_Land", Start = TArena, End = 10.2f, From = new Vector3(-2.85f, 0.42f, 1.95f), To = new Vector3(-2.95f, 0.46f, 1.8f), LookPoint = new Vector3(-4.62f, 0.75f, 0f), Fov = 46f, Shake = 0.15f, Thud = TLand, Kicks = new[] { TLand + 0.01f }, KickPeak = 2.4f, KickDecay = 0.9f },
                // Facing them from the court: they get up and look round.
                new Shot { Name = "F_Rise", Start = 10.2f, End = TOmen, From = new Vector3(-1.55f, 1.3f, 0.4f), To = new Vector3(-1.75f, 1.25f, 0.28f), LookPoint = new Vector3(-4.62f, 1.05f, 0f), Fov = 40f, Shake = 0.08f },
                // Over their shoulders to the seal: something takes shape in the curse.
                new Shot { Name = "G_Reveal", Start = TOmen, End = TEnd, From = new Vector3(-6.05f, 1.3f, 0.05f), To = new Vector3(-5.8f, 1.27f, 0.03f), LookPoint = new Vector3(0f, 0.9f, 0f), Fov = 32f, FovTo = 19f, Shake = 0.06f, Kicks = new[] { TSmoke + 0.02f }, KickPeak = 0.7f },
            });

            var fxGroup = timeline.CreateTrack<GroupTrack>(null, "FX");
            AddAnimation(timeline, fxGroup, director, "Lights Grading", fx, BuildFxClip(), TEnd);
            AddAnimation(timeline, fxGroup, director, "Fade", ui, BuildUiClip(), TEnd);
            AddAnimation(timeline, fxGroup, director, "Debris", debris, DebrisClip(debris.transform), TEnd);
            Activation(timeline, fxGroup, director, "Slabs Active", slabs.gameObject, TBreak, TVoid);
            AddAnimation(timeline, fxGroup, director, "Slabs Fall", slabs, DebrisClip(slabs.transform), TEnd);
            AddControlled(timeline, fxGroup, director, controlled);
            // The court sleeps until they land; the braziers catch, then the seal stirs and the curse answers.
            BossArena.AddTimelineClip(timeline, fxGroup, director, "Arena_BossFall", TEnd, clip =>
            {
                BossArena.SealCurve(clip, Smooth(0f, 0.04f, TOmen, 0.04f, TSmoke, 0.25f, TReveal + 1f, 0.12f, TEnd, 0.08f));
                BossArena.FireCurve(clip, Smooth(0f, 0.15f, TLand + 1.4f, 0.15f, TLand + 2.2f, 0.8f, TSmoke, 0.6f, TSmoke + 0.4f, 1.4f, TEnd, 0.6f));
                BossArena.CrystalCurve(clip, Smooth(0f, 0.1f, TLand + 1.4f, 0.1f, TLand + 2.4f, 0.35f, TSmoke + 0.3f, 0.8f, TEnd, 0.3f));
            });

            AssetDatabase.SaveAssets();
            director.time = 0;
            director.Evaluate();
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("[Cutscene] Built " + ScenePath);
        }

        // ------------------------------------------------------------------ movement

        /// <summary>
        /// Carries one girl: the hall (Amelia hops while cheering), the drop through the hole, hanging in the void, and
        /// down onto the court. Jumps between places land on camera cuts.
        /// </summary>
        static AnimationClip MoverClip(int i)
        {
            var hall = Hall(HallLocal[i]);
            var voidAt = VoidCenter + VoidLocal[i];
            var arena = ArenaSpots[i];
            var keys = new List<(float t, Vector3 p)>();
            void Key(float t, Vector3 p) => keys.Add((t, p));

            Key(0f, hall);
            if (i == 0)
                foreach (var h in PeriodPoseClips.CheerHops)
                {
                    for (int s = 0; s <= 6; s++)
                    {
                        float u = s / 6f;
                        Key(h + u * PeriodPoseClips.HopAir, hall + Vector3.up * (0.22f * 4f * u * (1f - u)));
                    }
                }
            Key(TBreak + 0.05f, hall);
            // Drop: a short catch on the crumbling edge, then a free fall.
            for (int s = 1; s <= 8; s++)
            {
                float dt = s * 0.1f;
                Key(TBreak + 0.05f + dt, hall + Vector3.down * (0.5f * 9.8f * dt * dt * (i == 0 ? 1f : 0.92f)));
            }
            // The void: drifting slightly while the world rushes up past them.
            Key(TVoid, voidAt + Vector3.up * 0.25f);
            Key(TArena - 0.01f, voidAt - Vector3.up * 0.25f + new Vector3(0f, 0f, 0.08f * (i == 0 ? 1f : -1f)));
            // Out of the dark above the court, faster and faster, onto the flagstones.
            float land = TLand + (i == 0 ? 0f : 0.07f);
            Key(TArena, arena + Vector3.up * 9f);
            for (int s = 0; s <= 6; s++)
            {
                float u = s / 6f;
                float t = Mathf.Lerp(TDrop, land, u);
                Key(t, arena + Vector3.up * (3.2f * (1f - u * u)));
            }
            Key(TEnd, arena);

            var clip = new AnimationClip { name = "Mover_" + (i == 0 ? "Amelia" : "Nox") };
            var x = new AnimationCurve(); var y = new AnimationCurve(); var z = new AnimationCurve();
            foreach (var (t, p) in keys)
            {
                x.AddKey(new Keyframe(t, p.x)); y.AddKey(new Keyframe(t, p.y)); z.AddKey(new Keyframe(t, p.z));
            }
            // Straight lines between keys (and holds before each jump), so the cuts are clean.
            foreach (var c in new[] { x, y, z })
                for (int k = 0; k < c.length; k++)
                {
                    AnimationUtility.SetKeyLeftTangentMode(c, k, AnimationUtility.TangentMode.Linear);
                    AnimationUtility.SetKeyRightTangentMode(c, k, AnimationUtility.TangentMode.Linear);
                }
            Bind(clip, "Offset", typeof(Transform), "m_LocalPosition.x", x);
            Bind(clip, "Offset", typeof(Transform), "m_LocalPosition.y", y);
            Bind(clip, "Offset", typeof(Transform), "m_LocalPosition.z", z);
            return SaveClip(clip);
        }

        // ------------------------------------------------------------------ effects

        static Animator BuildFx(Vector3 bossHead, out List<(GameObject, float, float)> controlled, out Animator debris, out Animator slabs)
        {
            controlled = new List<(GameObject, float, float)>();
            var fx = new GameObject("FX");
            var animator = fx.AddComponent<Animator>();
            var lights = StandardLights(fx.transform);
            var hole = Hall(HoleLocal);

            // Hall: a warm key over the two (the room is theirs again) and a cool fill from the windows.
            var warm = Light(lights, "Hall Warm", LightType.Point, Warm, 0f, 7f);
            warm.transform.position = Hall(new Vector3(0.4f, 2.6f, 2.4f));
            var cool = Light(lights, "Hall Cool", LightType.Spot, new Color(0.6f, 0.68f, 1f), 0f, 12f);
            cool.transform.position = Hall(new Vector3(-1.5f, 3.4f, 6f));
            cool.transform.LookAt(hole + Vector3.up);
            cool.spotAngle = 60f;
            var crack = Light(lights, "Crack Glow", LightType.Point, Violet, 0f, 4f);
            crack.transform.position = hole + Vector3.up * 0.15f;
            // Void: violet glow welling up from below them.
            var below = Light(lights, "Void Glow", LightType.Point, Violet, 0f, 9f);
            below.transform.position = VoidCenter + Vector3.down * 3f;
            var voidKey = Light(lights, "Void Key", LightType.Point, new Color(0.75f, 0.75f, 1f), 0f, 8f);
            voidKey.transform.position = VoidCenter + new Vector3(2f, 2.2f, 1.5f);
            // Arena: the seal's omen, and a light on his face for the last look.
            var omen = Light(lights, "Omen", LightType.Point, Violet, 0f, 3.5f);
            omen.transform.position = new Vector3(0f, 0.2f, 0f);
            var face = Light(lights, "Face", LightType.Point, Violet, 0f, 1.2f);
            face.transform.position = bossHead + BossRotation * Vector3.forward * 0.35f;

            var volumes = StandardVolumes(fx.transform);
            Volume(volumes, "Warm", 2, 0f, Profile("FallWarm", p =>
            {
                var ca = p.Add<ColorAdjustments>(); ca.saturation.Override(12f); ca.colorFilter.Override(new Color(1f, 0.95f, 0.88f)); ca.postExposure.Override(0.25f);
            }));
            Volume(volumes, "Dread", 4, 0f, Profile("FallDread", p =>
            {
                var ca = p.Add<ColorAdjustments>(); ca.saturation.Override(-45f); ca.colorFilter.Override(new Color(0.8f, 0.7f, 1f)); ca.contrast.Override(20f); ca.postExposure.Override(-0.1f);
                var v = p.Add<Vignette>(); v.intensity.Override(0.5f); v.color.Override(new Color(0.12f, 0f, 0.2f));
            }));

            var vfx = new GameObject("VFX").transform;
            vfx.SetParent(fx.transform);
            var floorY = Vector3.up * 0.012f;
            // Cracks of violet light spreading under them, then the hole.
            var crackMat = Additive("FallCrackGlow", PaintTexture(Root + "/Textures/crack_glow.png", 512, CrackPixel), new Color(1f, 1f, 1f, 1f));
            var crackQuad = FloorQuad(vfx, "Crack", hole + floorY, 3.4f, crackMat);
            var holeMat = AlphaBlend("FallHole", PaintTexture(Root + "/Textures/hole.png", 256, HolePixel));
            FloorQuad(vfx, "Hole", hole + floorY * 1.5f, 3.1f, holeMat);
            // Grit shaken off the ceiling, and a cloud of it when the floor goes.
            var gritBurst = Motes(vfx, "Break Grit", new Color(0.62f, 0.58f, 0.54f, 0.8f), 260f, 0.035f, -2.5f);
            gritBurst.transform.position = hole + Vector3.up * 0.6f;
            var gritShape = gritBurst.GetComponent<ParticleSystem>().shape;
            gritShape.scale = new Vector3(2.6f, 1.2f, 2.6f);
            controlled.Add((gritBurst, TBreak - 0.05f, TBreak + 0.6f));
            var falling = Motes(vfx, "Falling Grit", new Color(0.7f, 0.66f, 0.6f, 0.7f), 60f, 0.02f, -1.2f);
            falling.transform.position = hole + Vector3.up * 2.6f;
            controlled.Add((falling, TRumble, TVoid));

            // Void: streaks rushing up past them, motes, rubble rising.
            controlled.Add((Streaks(vfx, VoidCenter), TVoid - 0.2f, TArena));
            var voidMotes = Motes(vfx, "Void Motes", new Color(0.75f, 0.6f, 1f, 0.8f), 70f, 0.03f, 3.5f);
            voidMotes.transform.position = VoidCenter + Vector3.down * 1f;
            controlled.Add((voidMotes, TVoid - 0.2f, TArena));

            // Landing: a puff of grit under each of them.
            for (int i = 0; i < 2; i++)
            {
                var dust = Motes(vfx, "Land Dust " + i, new Color(0.5f, 0.48f, 0.52f, 0.75f), 320f, 0.03f, 0.35f);
                dust.transform.position = ArenaSpots[i] + Vector3.up * 0.12f;
                var dustShape = dust.GetComponent<ParticleSystem>().shape;
                dustShape.scale = new Vector3(0.9f, 0.15f, 0.9f);
                var dustMain = dust.GetComponent<ParticleSystem>().main;
                dustMain.prewarm = false;
                dustMain.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
                float t = TLand + (i == 0 ? 0f : 0.07f);
                controlled.Add((dust, t - 0.02f, t + 0.25f));
            }
            var ash = Motes(vfx, "AshMotes", new Color(0.7f, 0.68f, 0.76f, 0.7f), 45f, 0.018f, 0.06f);
            controlled.Add((ash, TArena, TEnd));

            // The seal stirs: petals of curse, then a smoke pillar he stands inside.
            controlled.Add((Vfx(vfx, "HCFX_Appear_03", Vector3.zero, 0.1f), TOmen, TSmoke + 1f));
            var smoke = Tint(Vfx(vfx, "HCFX_Smoke_02_Floor", Vector3.zero, 0.1f, 0.6f), new Color(0.28f, 0.18f, 0.38f));
            controlled.Add((smoke, TSmoke - 0.1f, TEnd));
            controlled.Add((FloorRing(vfx, 0.22f, Violet), TSmoke, TSmoke + 1.4f));
            controlled.Add((CurseWispsAt(vfx), TReveal, TEnd));

            debris = BuildDebris(fx.transform, hole);
            slabs = BuildSlabs(fx.transform, hole);
            crackQuad.SetActive(true);
            return animator;
        }

        static GameObject CurseWispsAt(Transform parent)
        {
            var wisps = Motes(parent, "Curse Wisps", new Color(0.62f, 0.38f, 1f, 0.85f), 30f, 0.035f, 0.5f);
            wisps.transform.position = new Vector3(0f, 0.6f, 0f);
            var shape = wisps.GetComponent<ParticleSystem>().shape;
            shape.scale = new Vector3(0.9f, 1.2f, 0.9f);
            return wisps;
        }

        static GameObject FloorQuad(Transform parent, string name, Vector3 at, float size, Material material)
        {
            var q = Block(parent, name, PrimitiveType.Quad, at, Vector3.one * size, material, Quaternion.Euler(90f, 0f, 0f));
            Object.DestroyImmediate(q.GetComponent<Collider>());
            var r = q.GetComponent<Renderer>();
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return q;
        }

        static Material AlphaBlend(string name, Texture texture)
        {
            var path = $"{AssetRoot}/Materials/{name}.mat";
            AssetDatabase.DeleteAsset(path);
            var m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetTexture("_BaseMap", texture);
            m.SetColor("_BaseColor", Color.white);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        /// <summary>Stretched streaks shooting up past the camera: the sense of falling.</summary>
        static GameObject Streaks(Transform parent, Vector3 at)
        {
            var go = Motes(parent, "Fall Streaks", new Color(0.8f, 0.75f, 1f, 0.55f), 90f, 0.025f, 0f);
            go.transform.position = at + Vector3.down * 4f;
            var ps = go.GetComponent<ParticleSystem>();
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.8f);
            main.prewarm = true;
            var shape = ps.shape;
            shape.scale = new Vector3(6f, 1f, 6f);
            var vel = ps.velocityOverLifetime;
            vel.y = new ParticleSystem.MinMaxCurve(12f, 18f);
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.12f;
            renderer.lengthScale = 2f;
            return go;
        }

        // ------------------------------------------------------------------ rubble

        struct Chunk { public Vector3 from, velocity, spin, euler; public float start; }
        static readonly Dictionary<Transform, Chunk[]> chunkPlans = new Dictionary<Transform, Chunk[]>();

        /// <summary>Stones round the hole that tumble in after them, and rubble floating up past them in the void.</summary>
        static Animator BuildDebris(Transform parent, Vector3 hole)
        {
            var root = new GameObject("Debris");
            root.transform.SetParent(parent);
            var animator = root.AddComponent<Animator>();
            var stone = LitMaterial("FallRubble", new Color(0.5f, 0.47f, 0.44f), 0.1f);
            var rng = new System.Random(7);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var plan = new List<Chunk>();
            for (int i = 0; i < 16; i++)
            {
                float a = i / 16f * Mathf.PI * 2f + R(-0.15f, 0.15f);
                float r = R(1.15f, 1.6f);
                var p = hole + new Vector3(Mathf.Cos(a) * r, 0.06f, Mathf.Sin(a) * r);
                var go = Block(root.transform, "Chunk " + i, PrimitiveType.Cube, p, new Vector3(R(0.12f, 0.3f), R(0.08f, 0.16f), R(0.12f, 0.28f)), stone);
                Object.DestroyImmediate(go.GetComponent<Collider>());
                var inward = (hole - p).normalized;
                plan.Add(new Chunk { from = p, velocity = inward * R(0.4f, 1.1f) + Vector3.up * R(0f, 0.6f), spin = new Vector3(R(-300f, 300f), R(-200f, 200f), R(-300f, 300f)),
                    euler = new Vector3(0f, R(0f, 360f), 0f), start = TBreak + R(0f, 0.3f) });
            }
            // Void rubble: drifting up past the camera (they fall faster than it).
            for (int i = 0; i < 14; i++)
            {
                var p = VoidCenter + new Vector3(R(-1.5f, 1.8f), R(-6f, -2f), R(-2.2f, 2.2f));
                var go = Block(root.transform, "Void Rock " + i, PrimitiveType.Cube, p, Vector3.one * R(0.1f, 0.35f), stone);
                Object.DestroyImmediate(go.GetComponent<Collider>());
                plan.Add(new Chunk { from = p, velocity = Vector3.up * R(2.5f, 4.5f), spin = new Vector3(R(-90f, 90f), R(-90f, 90f), R(-90f, 90f)),
                    euler = new Vector3(R(0f, 360f), R(0f, 360f), 0f), start = -(TVoid - 0.2f) });
            }
            chunkPlans[root.transform] = plan.ToArray();
            return animator;
        }

        /// <summary>The floor under them, in slabs, falling with them (switched on when the floor breaks).</summary>
        static Animator BuildSlabs(Transform parent, Vector3 hole)
        {
            var root = new GameObject("Slabs");
            root.transform.SetParent(parent);
            var animator = root.AddComponent<Animator>();
            var stone = LitMaterial("FallSlab", new Color(0.62f, 0.58f, 0.54f), 0.15f);
            var rng = new System.Random(11);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var plan = new List<Chunk>();
            int n = 0;
            for (float x = -1.2f; x <= 1.21f; x += 0.6f)
                for (float z = -1.2f; z <= 1.21f; z += 0.6f)
                {
                    if (x * x + z * z > 1.5f * 1.5f) continue;
                    var p = hole + new Vector3(x + R(-0.08f, 0.08f), -0.05f, z + R(-0.08f, 0.08f));
                    var go = Block(root.transform, "Slab " + n++, PrimitiveType.Cube, p, new Vector3(R(0.45f, 0.58f), 0.1f, R(0.45f, 0.58f)), stone);
                    Object.DestroyImmediate(go.GetComponent<Collider>());
                    plan.Add(new Chunk { from = p, velocity = new Vector3(x, 0f, z) * R(0.1f, 0.4f), spin = new Vector3(R(-120f, 120f), R(-60f, 60f), R(-120f, 120f)),
                        euler = new Vector3(0f, R(-8f, 8f), 0f), start = TBreak + 0.02f + R(0f, 0.12f) });
                }
            root.SetActive(false);
            chunkPlans[root.transform] = plan.ToArray();
            return animator;
        }

        /// <summary>Ballistic paths for a debris group: at rest until its start, then thrown and falling (or rising, for the void rubble).</summary>
        static AnimationClip DebrisClip(Transform root)
        {
            var plan = chunkPlans[root];
            var clip = new AnimationClip { name = root.name + "_BossFall" };
            for (int i = 0; i < plan.Length; i++)
            {
                var c = plan[i];
                var path = root.GetChild(i).name;
                var curves = new AnimationCurve[6];
                for (int k = 0; k < 6; k++) curves[k] = new AnimationCurve();
                bool rising = c.start < 0f;
                float t0 = rising ? -c.start : c.start;
                void Sample(float t)
                {
                    float dt = Mathf.Max(0f, t - t0);
                    var p = rising ? c.from + c.velocity * dt : c.from + c.velocity * dt + Vector3.down * (0.5f * 9.8f * dt * dt);
                    var e = c.euler + c.spin * dt;
                    curves[0].AddKey(t, p.x); curves[1].AddKey(t, p.y); curves[2].AddKey(t, p.z);
                    curves[3].AddKey(t, e.x); curves[4].AddKey(t, e.y); curves[5].AddKey(t, e.z);
                }
                Sample(0f);
                float until = rising ? TArena : t0 + 1.2f;
                for (float t = t0; t <= until + 1e-3f; t += 0.05f) Sample(t);
                Sample(TEnd);
                string[] props = { "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z", "localEulerAnglesRaw.x", "localEulerAnglesRaw.y", "localEulerAnglesRaw.z" };
                for (int k = 0; k < 6; k++) Bind(clip, path, typeof(Transform), props[k], curves[k]);
            }
            return SaveClip(clip);
        }

        // ------------------------------------------------------------------ painted textures

        static List<(Vector2 a, Vector2 b, float w)> crackSegments;

        /// <summary>Jagged branching cracks from the middle, white-hot cores in violet glow.</summary>
        static Color CrackPixel(float u, float v)
        {
            if (crackSegments == null)
            {
                crackSegments = new List<(Vector2, Vector2, float)>();
                var rng = new System.Random(5);
                float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
                void Branch(Vector2 p, float angle, float width, int steps, int depth)
                {
                    for (int s = 0; s < steps; s++)
                    {
                        angle += R(-0.55f, 0.55f);
                        var q = p + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * R(0.025f, 0.05f);
                        crackSegments.Add((p, q, width));
                        if (depth < 2 && rng.NextDouble() < 0.18) Branch(q, angle + R(-1.1f, 1.1f), width * 0.6f, steps / 2, depth + 1);
                        p = q;
                        width *= 0.9f;
                        if ((p - new Vector2(0.5f, 0.5f)).magnitude > 0.47f) break;
                    }
                }
                for (int b = 0; b < 9; b++) Branch(new Vector2(0.5f, 0.5f), b / 9f * Mathf.PI * 2f + R(-0.2f, 0.2f), 0.009f, 14, 0);
            }
            var x = new Vector2(u, v);
            float core = 0f, glow = 0f;
            foreach (var (a, b, w) in crackSegments)
            {
                var ab = b - a;
                float h = Mathf.Clamp01(Vector2.Dot(x - a, ab) / ab.sqrMagnitude);
                float d = (x - a - ab * h).magnitude;
                core = Mathf.Max(core, Mathf.Exp(-(d / w) * (d / w) * 2f));
                glow = Mathf.Max(glow, Mathf.Exp(-(d / (w * 4f)) * (d / (w * 4f))));
            }
            float edge = Mathf.Clamp01((0.5f - (x - new Vector2(0.5f, 0.5f)).magnitude) / 0.08f);
            var c = Color.Lerp(new Color(0.65f, 0.4f, 1f), Color.white, core * 0.8f);
            return new Color(c.r, c.g, c.b, Mathf.Clamp01(core + 0.45f * glow) * edge);
        }

        /// <summary>A ragged black opening with a faint violet lip.</summary>
        static Color HolePixel(float u, float v)
        {
            var d = new Vector2(u - 0.5f, v - 0.5f);
            float a = Mathf.Atan2(d.y, d.x);
            float r = 0.41f + 0.035f * Mathf.Sin(5f * a + 1f) + 0.025f * Mathf.Sin(11f * a + 2f) + 0.015f * Mathf.Sin(23f * a + 0.5f);
            float m = d.magnitude;
            float alpha = Mathf.Clamp01((r - m) * 120f);
            float lip = Mathf.Exp(-Mathf.Pow((r - m) / 0.02f, 2f));
            return new Color(0.22f * lip, 0.1f * lip, 0.35f * lip, alpha);
        }

        static Texture2D PaintTexture(string path, int size, System.Func<float, float, Color> paint)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    tex.SetPixel(x, y, paint((x + 0.5f) / size, (y + 0.5f) / size));
            tex.Apply();
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.alphaIsTransparency = true;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ------------------------------------------------------------------ animated lights, grading, fades

        static AnimationClip BuildFxClip()
        {
            var clip = new AnimationClip { name = "FX_BossFall" };
            // Hall lights only while they are there.
            Bind(clip, "Lights/Hall Warm", typeof(Light), "m_Intensity", Smooth(0f, 2.2f, TRumble, 2.2f, TBreak, 1.2f, TVoid, 0f, TEnd, 0f));
            Bind(clip, "Lights/Hall Cool", typeof(Light), "m_Intensity", Smooth(0f, 6f, TBreak, 5f, TVoid, 0f, TEnd, 0f));
            Bind(clip, "Lights/Crack Glow", typeof(Light), "m_Intensity", Smooth(0f, 0f, TCrack, 0f, TBreak - 0.1f, 3f, TBreak + 0.15f, 6f, TVoid, 2f));
            Bind(clip, "VFX/Crack", typeof(Transform), "m_LocalScale.x", Smooth(0f, 0.05f, TCrack, 0.05f, TBreak, 3.4f, TEnd, 3.4f));
            Bind(clip, "VFX/Crack", typeof(Transform), "m_LocalScale.y", Smooth(0f, 0.05f, TCrack, 0.05f, TBreak, 3.4f, TEnd, 3.4f));
            Bind(clip, "VFX/Crack", typeof(Transform), "m_LocalScale.z", Lin(0f, 1f, TEnd, 1f));
            Bind(clip, "VFX/Hole", typeof(Transform), "m_LocalScale.x", Lin(0f, 0f, TBreak - 0.01f, 0f, TBreak + 0.2f, 3.1f, TEnd, 3.1f));
            Bind(clip, "VFX/Hole", typeof(Transform), "m_LocalScale.y", Lin(0f, 0f, TBreak - 0.01f, 0f, TBreak + 0.2f, 3.1f, TEnd, 3.1f));
            Bind(clip, "VFX/Hole", typeof(Transform), "m_LocalScale.z", Lin(0f, 1f, TEnd, 1f));
            Bind(clip, "Lights/Void Glow", typeof(Light), "m_Intensity", Smooth(0f, 0f, TVoid - 0.1f, 0f, TVoid + 0.3f, 3f, TArena - 0.3f, 8f, TArena, 0f));
            Bind(clip, "Lights/Void Key", typeof(Light), "m_Intensity", Smooth(0f, 0f, TVoid - 0.1f, 0f, TVoid + 0.2f, 2.5f, TArena, 2.5f, TArena + 0.01f, 0f));
            // Arena: dim until they are down; the omen breathes, the rim and the face light find him.
            Bind(clip, "Lights/Key", typeof(Light), "m_Intensity", Smooth(0f, 0.5f, TArena, 0.5f, TLand + 0.5f, 0.9f, TOmen, 0.9f, TReveal, 0.7f));
            Bind(clip, "Lights/Rim", typeof(Light), "m_Intensity", Smooth(0f, 0f, TReveal, 0f, TReveal + 2.4f, 5f, TGlint, 7f, TEnd, 7f));
            Bind(clip, "Lights/Omen", typeof(Light), "m_Intensity", Smooth(0f, 0f, TOmen, 0f, TOmen + 0.5f, 2.2f, TOmen + 0.9f, 1f, TSmoke, 3.5f, TReveal + 1.5f, 1.2f, TEnd, 0.8f));
            Bind(clip, "Lights/Face", typeof(Light), "m_Intensity", Smooth(0f, 0f, TGlint - 0.4f, 0f, TGlint, 2.6f, TEnd, 2.2f));
            // Warm in the hall, the curse grade in the dark, dread as he appears.
            Bind(clip, "Volumes/Warm", typeof(Volume), "weight", Smooth(0f, 1f, TRumble, 1f, TBreak, 0.3f, TVoid, 0f, TEnd, 0f));
            Bind(clip, "Volumes/Curse", typeof(Volume), "weight", Smooth(0f, 0f, TRumble, 0f, TBreak, 0.4f, TVoid, 1f, TEnd, 1f));
            Bind(clip, "Volumes/Dread", typeof(Volume), "weight", Smooth(0f, 0f, TSmoke - 0.1f, 0f, TReveal + 1.8f, 1f, TEnd, 1f));
            Bind(clip, "Volumes/Impact", typeof(Volume), "weight", Lin(Pulses(0f, 0.35f, 0.5f, TBreak, TLand)));
            return SaveClip(clip);
        }

        static AnimationClip BuildUiClip()
        {
            var clip = new AnimationClip { name = "UI_BossFall" };
            // From black; a beat of black between the fall and the court; to black for the entrance cutscene.
            Bind(clip, "BlackFade", typeof(Image), "m_Color.a", Lin(0f, 1f, 0.8f, 0f, TArena - 0.25f, 0f, TArena - 0.05f, 1f, TArena + 0.3f, 1f, TDrop - 0.05f, 0f, TFadeOut, 0f, TEnd - 0.1f, 1f, TEnd, 1f));
            Bind(clip, "WhiteFlash", typeof(Image), "m_Color.a", Lin(Pulses(0f, 0.18f, 0.35f, TBreak + 0.02f)));
            return SaveClip(clip);
        }
    }
}
