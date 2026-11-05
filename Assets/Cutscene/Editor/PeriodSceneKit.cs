using System.Collections.Generic;
using System.IO;
using AiHackathon.Game;
using AiHackathon.Game.Editor;
using AiHackathon.Parchment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Timeline;
using UnityEngine.UI;
using Waaang;
using Waaang.EditorTools;
using static AiHackathon.Cutscene.Editor.CutsceneKit;

namespace AiHackathon.Cutscene.Editor
{
    /// <summary>
    /// What every period scene shares: the rune classroom, Amelia and Nox as the game's casters (parked for the
    /// intro, staff in the lead hand), a rune stone ahead of each, the play camera, cinema UI, the parchment card,
    /// the intro director and the GameManager / RunePeriod pair. A period builder calls <see cref="Begin"/>, adds
    /// its own timeline, shots and effects, then <see cref="Finish"/>.
    /// </summary>
    internal static class PeriodSceneKit
    {
        internal const string CardPrefab = "Assets/Parchment/ParchmentTitleCard.prefab";
        internal const string ProfileDir = "Assets/Game/Profiles";

        // Layout (world), framed like the Game scene's duo shot but closer.
        // Nox sits a touch further back: her stance reads forward of Amelia's at the same depth.
        internal static readonly Vector3 AmeliaPos = new Vector3(-0.78f, 0f, 0f);
        internal static readonly Vector3 NoxPos = new Vector3(0.78f, 0f, -0.08f);
        internal static readonly Vector3[] StonePos = { new Vector3(-0.75f, 0.16f, 2.3f), new Vector3(0.75f, 0.16f, 2.3f) };
        // Play camera: closer than the Game scene's duo shot, so the upper bodies and the runes fill more of the frame.
        internal static readonly Vector3 PlayCamBack = new Vector3(0f, 0.32f, -1.15f);
        internal const float PlayCamPitch = 12f;
        internal const float PlayCamFov = 52f;
        internal static readonly Color NoxColor = new Color(0.45f, 0.75f, 1f);

        internal struct Classroom
        {
            public Transform root, windows, shafts;
            public Color fog;
        }

        internal sealed class Context
        {
            public UnityEngine.SceneManagement.Scene scene, previous;
            public bool keepOpenScenes;
            public Classroom room;
            public Camera cam;
            public WandPointerRig[] rigs;
            public CasterProfile[] profiles;
            public Animator[] animators;
            public RuneStoneFx[] stones;
            public Vector3[] runeCenters;
            public Color[] runeColors;
            public Animator ui;
            public ParchmentTitleCard card;
            public TimelineAsset timeline;
            public PlayableDirector director;
            public PeriodIntro intro;
            public GameManager game;
            public RunePeriod period;
            public Vector3 playCam, playCamLook;
            public Animator Amelia => animators[0];
            public Animator Nox => animators[1];
        }

        /// <summary>
        /// Bakes the runes and clips, opens a new scene (additively when <paramref name="keepOpenScenes"/>), and
        /// builds everything the periods share. Lane i starts its rune sequence at (i + laneOffset).
        /// <paramref name="buildRoom"/> replaces the rune classroom (it gets the texture folder);
        /// without <paramref name="runeStones"/> there are no stones and no <see cref="RunePeriod"/>: the period
        /// brings its own game (and sets each player's <see cref="GameManager.Player.startAim"/>).
        /// </summary>
        internal static Context Begin(string root, string timelinePath, int laneOffset, bool keepOpenScenes,
            System.Func<string, Classroom> buildRoom = null, bool runeStones = true)
        {
            PeriodPoseClips.Build();
            // Only periods with stones bake them (baking rewrites the stone prefab and rune materials).
            var runePaths = new List<string>();
            if (runeStones)
            {
                runePaths = RuneStoneBaker.BakeAll().ConvertAll(AssetDatabase.GetAssetPath);
                RuneStoneBaker.BuildStonePrefab();
            }
            EnsureProfiles();
            AssetDatabase.SaveAssets();

            AssetRoot = root;
            PrepareFolders();
            EnsureFolder(root + "/Textures");
            var c = new Context { keepOpenScenes = keepOpenScenes };
            c.previous = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            c.scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, keepOpenScenes ? NewSceneMode.Additive : NewSceneMode.Single);
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(c.scene);
            // A single-mode NewScene unloads assets nothing in a scene references, which destroys the definitions
            // and profiles loaded before it; load them again by path from here on.
            var runes = runePaths.ConvertAll(AssetDatabase.LoadAssetAtPath<RuneDefinition>);
            c.profiles = new[]
            {
                AssetDatabase.LoadAssetAtPath<CasterProfile>(ProfileDir + "/Amelia.asset"),
                AssetDatabase.LoadAssetAtPath<CasterProfile>(ProfileDir + "/Nox.asset"),
            };
            c.room = buildRoom != null ? buildRoom(root + "/Textures") : BuildClassroom(root + "/Textures");
            c.cam = BuildCamera();
            c.cam.backgroundColor = c.room.fog;

            // The same casters as the Game scene, parked for the intro: rigs off, staff in the lead hand.
            c.rigs = new[]
            {
                WaaangSceneSetup.CreateCaster(c.profiles[0], c.cam, AmeliaPos, 0),
                WaaangSceneSetup.CreateCaster(c.profiles[1], c.cam, NoxPos, 1),
            };
            c.animators = new Animator[2];
            for (int i = 0; i < 2; i++)
            {
                var rig = c.rigs[i];
                rig.toonScene = false;   // the classroom keeps its own materials
                rig.mouseMode = true;
                rig.gameObject.SetActive(false);
                rig.character.GetComponent<CasterPose>().enabled = false;
                HoldStaff(rig.character, rig.staff, AssetDatabase.GetAssetPath(c.profiles[i].characterModel), c.profiles[i].LeadSide, c.profiles[i].staffScale);
                var animator = rig.character.gameObject.AddComponent<Animator>();
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                c.animators[i] = animator;
            }

            // A stone ahead of each player; each lane walks the runes from a different start.
            int stoneCount = runeStones ? 2 : 0;
            c.stones = new RuneStoneFx[stoneCount];
            c.runeCenters = new Vector3[stoneCount];
            c.runeColors = new Color[stoneCount];
            var sequences = new RuneDefinition[2][];
            var aims = new Transform[2];
            for (int i = 0; i < stoneCount; i++)
            {
                var seq = new RuneDefinition[runes.Count];
                for (int k = 0; k < runes.Count; k++) seq[k] = runes[(k + i + laneOffset) % runes.Count];
                sequences[i] = seq;
                c.stones[i] = RuneStoneBaker.PlaceStone("RuneStone " + c.profiles[i].displayName, StonePos[i], seq.Length > 0 ? seq[0] : null);
                var def = c.stones[i].rune;
                c.runeCenters[i] = c.stones[i].transform.TransformPoint(def != null ? def.runeCenter : new Vector3(0f, 0.6f, 0f));
                c.runeColors[i] = def != null ? def.glowColor : new Color(1f, 0.78f, 0.35f);
                var aim = new GameObject("AimPoint").transform;
                aim.SetParent(c.stones[i].transform, false);
                aim.position = c.runeCenters[i];
                aims[i] = aim;
            }

            c.ui = BuildUi(c.cam);
            Panel(c.ui.transform, "ExitFade", new Color(0f, 0f, 0f, 0f), Vector2.zero, Vector2.one);
            var cardRoot = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(CardPrefab));
            c.card = cardRoot.GetComponentInChildren<ParchmentTitleCard>(true);

            (c.timeline, c.director) = CreateTimeline(timelinePath);
            c.intro = c.director.gameObject.AddComponent<PeriodIntro>();
            c.intro.card = c.card;
            c.intro.exitFade = c.ui.transform.Find("ExitFade").GetComponent<Image>();

            var gameGo = new GameObject("Game");
            c.game = gameGo.AddComponent<GameManager>();
            c.game.intro = c.intro;
            c.game.players = new GameManager.Player[2];
            for (int i = 0; i < 2; i++)
                c.game.players[i] = new GameManager.Player
                {
                    profile = c.profiles[i], character = c.rigs[i].character, rig = c.rigs[i], pose = c.rigs[i].character.GetComponent<CasterPose>(),
                    cutsceneAnimator = c.animators[i], staff = c.rigs[i].staff, startAim = aims[i],
                };
            if (runeStones)
            {
                c.period = gameGo.AddComponent<RunePeriod>();
                c.period.game = c.game;
                c.period.lanes = new RunePeriod.Lane[2];
                for (int i = 0; i < 2; i++) c.period.lanes[i] = new RunePeriod.Lane { stone = c.stones[i], sequence = sequences[i] };
            }

            // The last shot of every intro lands on this camera, so play starts from that view.
            float headY = FindBone(c.Amelia.transform, "Head").position.y;
            c.playCam = new Vector3(0f, headY, 0f) + PlayCamBack;
            c.playCamLook = c.playCam + Quaternion.Euler(PlayCamPitch, 0f, 0f) * Vector3.forward * 4f;
            return c;
        }

        internal static void Finish(Context c, string scenePath)
        {
            AssetDatabase.SaveAssets();
            c.director.time = 0;
            c.director.Evaluate();
            foreach (var stone in c.stones)
                if (stone.model == null)
                    Debug.LogError("[Cutscene] " + stone.name + " has no rune model.");
            EditorSceneManager.SaveScene(c.scene, scenePath);
            Debug.Log("[Cutscene] Built " + scenePath);
            if (c.keepOpenScenes && c.previous.IsValid())
            {
                UnityEngine.SceneManagement.SceneManager.SetActiveScene(c.previous);
                EditorSceneManager.CloseScene(c.scene, true);
            }
        }

        internal static void EnsureProfiles()
        {
            EnsureProfile("Amelia", PeriodPoseClips.AmeliaFbx, "Assets/Waaang/Materials/Amelia_Rim.mat",
                PeriodPoseClips.AmeliaWandFbx, "Assets/Waaang/Materials/Wand.mat", 1f, false,
                new Color(1f, 0.78f, 0.38f), new Color(0f, 0f, 0f, 0f), new Color(1.5f, 0.78f, 0.28f), new Color(1f, 0.78f, 0.45f),
                "Assets/VFX_Klaus/Prefabs/Hyper Casual FX Vol.2/HCFX_Beam_04.prefab");
            EnsureProfile("Nox", PeriodPoseClips.NoxFbx, "Assets/Waaang/Materials/Nox_Rim.mat",
                PeriodPoseClips.NoxWandFbx, "Assets/Waaang/Materials/NoxWand.mat", WaaangSceneSetup.NoxStaffScale, true,
                NoxColor, new Color(0f, 0f, 0f, 0f), new Color(0.45f, 0.85f, 1.6f), Color.Lerp(NoxColor, Color.white, 0.3f),
                "Assets/VFX_Klaus/Prefabs/Hyper Casual FX Vol.2/HCFX_Beam_02.prefab");
        }

        /// <summary>
        /// Creates the profile asset the first time with these defaults; later builds only fill missing references,
        /// so edits made in the inspector stay.
        /// </summary>
        internal static CasterProfile EnsureProfile(string name, string fbx, string body, string wandFbx, string wandMat, float staffScale, bool leftHanded,
            Color player, Color beamTint, Color trail, Color tipLight, string beam)
        {
            EnsureFolder(ProfileDir);
            var path = ProfileDir + "/" + name + ".asset";
            var p = AssetDatabase.LoadAssetAtPath<CasterProfile>(path);
            if (p == null)
            {
                p = ScriptableObject.CreateInstance<CasterProfile>();
                p.displayName = name;
                p.staffScale = staffScale;
                p.leftHanded = leftHanded;
                p.playerColor = player;
                p.beamTint = beamTint;
                p.trailColor = trail;
                p.tipLightColor = tipLight;
                AssetDatabase.CreateAsset(p, path);
            }
            if (p.characterModel == null) p.characterModel = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            if (p.bodyMaterial == null) p.bodyMaterial = AssetDatabase.LoadAssetAtPath<Material>(body);
            if (p.wandModel == null) p.wandModel = AssetDatabase.LoadAssetAtPath<GameObject>(wandFbx);
            if (p.wandMaterial == null) p.wandMaterial = AssetDatabase.LoadAssetAtPath<Material>(wandMat);
            if (p.idle == null) p.idle = PeriodPoseClips.Load("Caster_Idle");
            if (p.beamPrefab == null)
            {
                // These beams carry their own colours (gold spiral, blue core), so drop any old tint with them.
                p.beamPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(beam);
                p.beamTint = beamTint;
            }
            EditorUtility.SetDirty(p);
            return p;
        }

        /// <summary>
        /// Moves the game rig's staff into the lead fist for the intro, where the keyed clips close the hand: the
        /// lead grip on the fist, the shaft along it. GameManager hands it back to the rig when play starts.
        /// </summary>
        internal static void HoldStaff(Transform character, Transform staff, string fbx, string side, float staffScale)
        {
            PeriodPoseClips.HandGrip(fbx, side, out var point, out var axis);
            var hand = FindBone(character, side + "Hand");
            var grip = staff.Find("Right Grip").localPosition;
            staff.SetParent(hand, false);
            staff.localScale = Vector3.one * staffScale;
            var rot = Quaternion.FromToRotation(Vector3.up, axis);
            staff.localRotation = rot;
            staff.localPosition = point - rot * (grip * staffScale);
        }

        /// <summary>A trail under <paramref name="parent"/>; its own Animator lets the timeline switch emitting.</summary>
        internal static Animator Trail(Transform parent, Vector3 localPosition, string name, Color color, float width, float time)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            var trail = go.AddComponent<TrailRenderer>();
            trail.time = time;
            trail.minVertexDistance = 0.008f;
            trail.widthMultiplier = width;
            trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(color, 0.25f), new GradientColorKey(color, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0f, 1f) });
            trail.colorGradient = g;
            trail.emitting = false;
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.sharedMaterial = Additive("TrailAdditive", null, new Color(2.2f, 2.2f, 2.2f, 1f));
            return go.AddComponent<Animator>();
        }

        internal static AnimationClip EmitClip(string name, float on, float off, float end)
        {
            var clip = new AnimationClip { name = name };
            Bind(clip, "", typeof(TrailRenderer), "m_Emitting", Curve(new[] { 0f, 0f, on, 1f, off, 0f, end, 0f }, AnimationUtility.TangentMode.Constant));
            // Off outside its beat: a stopped trail still draws its last point, which bloomed into a white ball on
            // the wand tip during play.
            Bind(clip, "", typeof(TrailRenderer), "m_Enabled", Curve(new[] { 0f, 0f, on - 0.05f, 1f, off + 1.5f, 0f, end, 0f }, AnimationUtility.TangentMode.Constant));
            return SaveClip(clip);
        }

        // ------------------------------------------------------------------ classroom

        /// <summary>Sunlit rune classroom: wood floor, a navy rug with a gold ring under a stone dais, a wall of tall
        /// windows behind the stone, columns with school banners and bookshelves along the sides.</summary>
        internal static Classroom BuildClassroom(string textureDir)
        {
            var fog = new Color(0.62f, 0.56f, 0.5f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = 0.02f;
            RenderSettings.fogColor = fog;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.7f, 0.66f, 0.62f);
            RenderSettings.ambientEquatorColor = new Color(0.5f, 0.45f, 0.42f);
            RenderSettings.ambientGroundColor = new Color(0.28f, 0.22f, 0.18f);
            RenderSettings.skybox = null;

            var env = new GameObject("Classroom").transform;
            var wood = LitMaterial("FloorWood", new Color(0.36f, 0.25f, 0.17f), 0.4f);
            var stoneMat = LitMaterial("Stone", new Color(0.6f, 0.57f, 0.53f), 0.15f);
            var wall = LitMaterial("Wall", new Color(0.66f, 0.6f, 0.54f), 0.05f);
            var dark = LitMaterial("DarkWood", new Color(0.22f, 0.14f, 0.09f), 0.25f);
            var navy = LitMaterial("Navy", new Color(0.12f, 0.13f, 0.3f), 0.1f);
            var gold = LitMaterial("GoldTrim", new Color(0.85f, 0.65f, 0.28f), 0.6f);

            Block(env, "Floor", PrimitiveType.Cube, new Vector3(0f, -0.05f, 0.6f), new Vector3(10f, 0.1f, 12f), wood);
            Block(env, "Wall Back", PrimitiveType.Cube, new Vector3(0f, 2.4f, 5.3f), new Vector3(10f, 4.8f, 0.2f), wall);
            Block(env, "Wall Left", PrimitiveType.Cube, new Vector3(-4.8f, 2.4f, 0.6f), new Vector3(0.2f, 4.8f, 12f), wall);
            Block(env, "Wall Right", PrimitiveType.Cube, new Vector3(4.8f, 2.4f, 0.6f), new Vector3(0.2f, 4.8f, 12f), wall);
            Block(env, "Wall Behind", PrimitiveType.Cube, new Vector3(0f, 2.4f, -5.2f), new Vector3(10f, 4.8f, 0.2f), wall);

            // Dark wood wainscot and a moulding band round the room, a chalkboard on the wall behind the students.
            Block(env, "Wainscot Back", PrimitiveType.Cube, new Vector3(0f, 0.5f, 5.17f), new Vector3(9.8f, 1.0f, 0.06f), dark);
            Block(env, "Wainscot Left", PrimitiveType.Cube, new Vector3(-4.67f, 0.5f, 0.6f), new Vector3(0.06f, 1.0f, 11.8f), dark);
            Block(env, "Wainscot Right", PrimitiveType.Cube, new Vector3(4.67f, 0.5f, 0.6f), new Vector3(0.06f, 1.0f, 11.8f), dark);
            Block(env, "Wainscot Behind", PrimitiveType.Cube, new Vector3(0f, 0.5f, -5.07f), new Vector3(9.8f, 1.0f, 0.06f), dark);
            Block(env, "Moulding Behind", PrimitiveType.Cube, new Vector3(0f, 3.9f, -5.07f), new Vector3(9.8f, 0.12f, 0.08f), dark);
            Block(env, "Moulding Left", PrimitiveType.Cube, new Vector3(-4.66f, 3.9f, 0.6f), new Vector3(0.08f, 0.12f, 11.8f), dark);
            Block(env, "Moulding Right", PrimitiveType.Cube, new Vector3(4.66f, 3.9f, 0.6f), new Vector3(0.08f, 0.12f, 11.8f), dark);
            var board = LitMaterial("Chalkboard", new Color(0.07f, 0.13f, 0.1f), 0.1f);
            var chalk = Unlit("Chalk", new Color(0.82f, 0.84f, 0.8f));
            Block(env, "Board Frame", PrimitiveType.Cube, new Vector3(0f, 1.85f, -5.05f), new Vector3(4.0f, 1.75f, 0.06f), dark);
            Block(env, "Board", PrimitiveType.Cube, new Vector3(0f, 1.85f, -5.01f), new Vector3(3.8f, 1.55f, 0.04f), board);
            Block(env, "Board Ledge", PrimitiveType.Cube, new Vector3(0f, 1.02f, -4.97f), new Vector3(3.9f, 0.05f, 0.12f), dark);
            // Chalk: a rune (vertical stroke and two branches) and a few lines of notes.
            Block(env, "Chalk Rune Stem", PrimitiveType.Cube, new Vector3(-1.1f, 1.85f, -4.985f), new Vector3(0.035f, 0.9f, 0.005f), chalk, Quaternion.Euler(0f, 0f, 8f));
            Block(env, "Chalk Rune Arm 1", PrimitiveType.Cube, new Vector3(-0.92f, 2.13f, -4.985f), new Vector3(0.035f, 0.4f, 0.005f), chalk, Quaternion.Euler(0f, 0f, -55f));
            Block(env, "Chalk Rune Arm 2", PrimitiveType.Cube, new Vector3(-0.92f, 1.9f, -4.985f), new Vector3(0.035f, 0.4f, 0.005f), chalk, Quaternion.Euler(0f, 0f, -55f));
            for (int i = 0; i < 5; i++)
                Block(env, "Chalk Note", PrimitiveType.Cube, new Vector3(0.35f + (i % 2) * 0.12f, 2.3f - i * 0.2f, -4.985f), new Vector3(1.2f - i * 0.15f, 0.02f, 0.005f), chalk);
            foreach (float x in new[] { -3.3f, 3.3f })
            {
                Block(env, "Banner Behind", PrimitiveType.Cube, new Vector3(x, 2.7f, -5.06f), new Vector3(0.62f, 1.7f, 0.02f), navy);
                Block(env, "Banner Behind Stripe", PrimitiveType.Cube, new Vector3(x, 2.0f, -5.045f), new Vector3(0.62f, 0.07f, 0.01f), gold);
                Block(env, "Banner Behind Star", PrimitiveType.Cube, new Vector3(x, 2.85f, -5.045f), new Vector3(0.16f, 0.16f, 0.01f), gold, Quaternion.Euler(0f, 0f, 45f));
            }

            // A rug and dais under each stone.
            foreach (var p in StonePos)
            {
                Block(env, "Rug Ring", PrimitiveType.Cylinder, new Vector3(p.x, 0.003f, p.z), new Vector3(1.5f, 0.003f, 1.5f), gold);
                Block(env, "Rug", PrimitiveType.Cylinder, new Vector3(p.x, 0.006f, p.z), new Vector3(1.4f, 0.003f, 1.4f), navy);
                Block(env, "Dais", PrimitiveType.Cylinder, new Vector3(p.x, 0.08f, p.z), new Vector3(0.95f, 0.08f, 0.95f), stoneMat);
                Block(env, "Dais Top", PrimitiveType.Cylinder, new Vector3(p.x, 0.162f, p.z), new Vector3(0.82f, 0.004f, 0.82f), gold);
            }

            // Window wall: three tall bright panes with mullions, columns between them.
            var glass = Unlit("WindowLight", new Color(1.9f, 1.75f, 1.45f));
            var windows = new GameObject("Windows").transform;
            windows.SetParent(env);
            int pane = 0;
            foreach (float x in new[] { -2.3f, 0f, 2.3f })
            {
                Block(windows, "Window " + pane, PrimitiveType.Quad, new Vector3(x, 2.45f, 5.19f), new Vector3(1.3f, 3.0f, 1f), glass);
                Block(env, "Mullion V", PrimitiveType.Cube, new Vector3(x, 2.45f, 5.17f), new Vector3(0.06f, 3.0f, 0.04f), dark);
                Block(env, "Mullion H", PrimitiveType.Cube, new Vector3(x, 2.9f, 5.17f), new Vector3(1.3f, 0.06f, 0.04f), dark);
                Block(env, "Mullion H2", PrimitiveType.Cube, new Vector3(x, 1.9f, 5.17f), new Vector3(1.3f, 0.06f, 0.04f), dark);
                Block(env, "Sill", PrimitiveType.Cube, new Vector3(x, 0.9f, 5.12f), new Vector3(1.5f, 0.08f, 0.2f), stoneMat);
                Block(windows, "Arch " + pane, PrimitiveType.Cylinder, new Vector3(x, 3.95f, 5.18f), new Vector3(1.3f, 0.01f, 1.3f), glass, Quaternion.Euler(90f, 0f, 0f));
                pane++;
            }
            foreach (float x in new[] { -3.45f, -1.15f, 1.15f, 3.45f })
            {
                Block(env, "Column", PrimitiveType.Cylinder, new Vector3(x, 2.4f, 5.0f), new Vector3(0.42f, 2.4f, 0.42f), stoneMat);
                Block(env, "Column Base", PrimitiveType.Cube, new Vector3(x, 0.12f, 5.0f), new Vector3(0.6f, 0.24f, 0.6f), stoneMat);
            }
            foreach (float x in new[] { -3.45f, 3.45f })
            {
                Block(env, "Banner", PrimitiveType.Cube, new Vector3(x, 3.1f, 4.74f), new Vector3(0.62f, 1.7f, 0.02f), navy);
                Block(env, "Banner Stripe", PrimitiveType.Cube, new Vector3(x, 2.4f, 4.725f), new Vector3(0.62f, 0.07f, 0.01f), gold);
                Block(env, "Banner Star", PrimitiveType.Cube, new Vector3(x, 3.25f, 4.725f), new Vector3(0.16f, 0.16f, 0.01f), gold, Quaternion.Euler(0f, 0f, 45f));
            }

            // Bookshelves down both side walls.
            var bookColors = new[] { new Color(0.45f, 0.12f, 0.12f), new Color(0.15f, 0.25f, 0.45f), new Color(0.2f, 0.35f, 0.2f), new Color(0.55f, 0.42f, 0.18f) };
            for (int side = -1; side <= 1; side += 2)
            for (int i = 0; i < 4; i++)
            {
                float z = -2.4f + i * 1.6f;
                float x = side * 4.5f;
                Block(env, "Shelf", PrimitiveType.Cube, new Vector3(x, 1.25f, z), new Vector3(0.45f, 2.5f, 1.45f), dark);
                for (int row = 0; row < 4; row++)
                {
                    var books = LitMaterial("Books" + ((i + row) % 4), bookColors[(i + row) % 4], 0.2f);
                    Block(env, "Books", PrimitiveType.Cube, new Vector3(x - side * 0.08f, 0.42f + row * 0.55f, z), new Vector3(0.32f, 0.36f, 1.3f), books);
                }
            }

            // Sun shafts slanting in from the windows over the stone toward the students.
            var shaftTex = ShaftTexture(textureDir + "/shaft.png");
            var shaftMat = Additive("SunShaft", shaftTex, new Color(1f, 0.9f, 0.72f, 0.075f));
            var shafts = new GameObject("Sun Shafts").transform;
            shafts.SetParent(env);
            foreach (float x in new[] { -2.3f, 0f, 2.3f })
            for (int k = 0; k < 2; k++)
            {
                var q = Block(shafts, "Shaft", PrimitiveType.Quad, new Vector3(x * 0.85f, 2.2f, 3.2f), new Vector3(1.2f, 5.2f, 1f), shaftMat,
                    // Two quads crossed about the shaft's own axis, so it reads from any side.
                    Quaternion.Euler(-52f, 180f, 0f) * Quaternion.Euler(0f, 90f * k, 0f));
                q.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            }

            // Lasers hit the room (strokes on walls and floor), but not the light shafts, and a flat cylinder's
            // capsule collider would balloon into a sphere: drop those.
            foreach (var col in env.GetComponentsInChildren<Collider>())
                if (col.name == "Shaft" || (col is CapsuleCollider && col.transform.localScale.y < 0.2f))
                    Object.DestroyImmediate(col);
            return new Classroom { root = env, windows = windows, shafts = shafts, fog = fog };
        }

        internal static GameObject Block(Transform parent, string name, PrimitiveType type, Vector3 position, Vector3 scale, Material material, Quaternion? rotation = null)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent);
            go.transform.SetPositionAndRotation(position, rotation ?? Quaternion.identity);
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go;
        }

        internal static Material Unlit(string name, Color color)
        {
            var path = $"{AssetRoot}/Materials/{name}.mat";
            AssetDatabase.DeleteAsset(path);
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            m.SetColor("_BaseColor", color);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        internal static Material Additive(string name, Texture texture, Color color)
        {
            var path = $"{AssetRoot}/Materials/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            var m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
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
            if (texture != null) m.SetTexture("_BaseMap", texture);
            m.SetColor("_BaseColor", color);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        internal static Texture2D ShaftTexture(string path)
        {
            const int w = 64, h = 256;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = x / (w - 1f), v = y / (h - 1f);
                float a = Mathf.Pow(Mathf.Sin(u * Mathf.PI), 2f) * Mathf.Sin(v * Mathf.PI) * Mathf.Lerp(0.55f, 1f, v);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

    }
}
