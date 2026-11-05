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
        const string CardPrefab = "Assets/Parchment/ParchmentTitleCard.prefab";
        const string ProfileDir = "Assets/Game/Profiles";

        // Layout (world) as in the Game scene: the two players 2.4 m apart facing +Z, each with her own stone ahead.
        // Nox sits a touch further back: her stance reads forward of Amelia's at the same depth.
        static readonly Vector3 AmeliaPos = new Vector3(-0.78f, 0f, 0f);
        static readonly Vector3 NoxPos = new Vector3(0.78f, 0f, -0.08f);
        static readonly Vector3[] StonePos = { new Vector3(-0.75f, 0.16f, 2.3f), new Vector3(0.75f, 0.16f, 2.3f) };
        // Play camera: closer than the Game scene's duo shot, so the upper bodies and the runes fill more of the frame.
        static readonly Vector3 PlayCamBack = new Vector3(0f, 0.32f, -1.15f);
        const float PlayCamPitch = 12f;
        const float PlayCamFov = 52f;

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
            PeriodPoseClips.Build();
            var runePaths = RuneStoneBaker.BakeAll().ConvertAll(AssetDatabase.GetAssetPath);
            RuneStoneBaker.BuildStonePrefab();

            var ameliaProfile = EnsureProfile("Amelia", PeriodPoseClips.AmeliaFbx, "Assets/Waaang/Materials/Amelia_Rim.mat",
                PeriodPoseClips.AmeliaWandFbx, "Assets/Waaang/Materials/Wand.mat", 1f, false,
                new Color(1f, 0.78f, 0.38f), new Color(0f, 0f, 0f, 0f), new Color(1.5f, 0.78f, 0.28f), new Color(1f, 0.78f, 0.45f),
                "Assets/VFX_Klaus/Prefabs/Hyper Casual FX Vol.2/HCFX_Beam_04.prefab");
            var noxColor = new Color(0.45f, 0.75f, 1f);
            var noxProfile = EnsureProfile("Nox", PeriodPoseClips.NoxFbx, "Assets/Waaang/Materials/Nox_Rim.mat",
                PeriodPoseClips.NoxWandFbx, "Assets/Waaang/Materials/NoxWand.mat", WaaangSceneSetup.NoxStaffScale, true,
                noxColor, new Color(0f, 0f, 0f, 0f), new Color(0.45f, 0.85f, 1.6f), Color.Lerp(noxColor, Color.white, 0.3f),
                "Assets/VFX_Klaus/Prefabs/Hyper Casual FX Vol.2/HCFX_Beam_02.prefab");

            AssetDatabase.SaveAssets();
            AssetRoot = Root;
            PrepareFolders();
            EnsureFolder(Root + "/Textures");
            var previous = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, keepOpenScenes ? NewSceneMode.Additive : NewSceneMode.Single);
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
            // A single-mode NewScene unloads assets nothing in a scene references, which destroys the definitions
            // BakeAll returned; load them again by path from here on.
            var runes = runePaths.ConvertAll(AssetDatabase.LoadAssetAtPath<RuneDefinition>);
            ameliaProfile = AssetDatabase.LoadAssetAtPath<CasterProfile>(ProfileDir + "/Amelia.asset");
            noxProfile = AssetDatabase.LoadAssetAtPath<CasterProfile>(ProfileDir + "/Nox.asset");
            var fogColor = BuildClassroom();
            var cam = BuildCamera();
            cam.backgroundColor = fogColor;

            // The same casters as the Game scene, parked for the intro: rigs off, staff in the lead hand.
            var rigs = new[]
            {
                WaaangSceneSetup.CreateCaster(ameliaProfile, cam, AmeliaPos, 0),
                WaaangSceneSetup.CreateCaster(noxProfile, cam, NoxPos, 1),
            };
            var profiles = new[] { ameliaProfile, noxProfile };
            var animators = new Animator[2];
            for (int i = 0; i < 2; i++)
            {
                var rig = rigs[i];
                rig.toonScene = false;   // the classroom keeps its own materials
                rig.mouseMode = true;
                rig.gameObject.SetActive(false);
                rig.character.GetComponent<CasterPose>().enabled = false;
                HoldStaff(rig.character, rig.staff, AssetDatabase.GetAssetPath(profiles[i].characterModel), profiles[i].LeadSide, profiles[i].staffScale);
                var animator = rig.character.gameObject.AddComponent<Animator>();
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animators[i] = animator;
            }
            var amelia = animators[0];
            var nox = animators[1];
            // Amelia's palm writes the circle in gold; the tip of Nox's wand leaves her blue.
            var palmTrail = Trail(FindBone(amelia.transform, "Left_Hand"), PeriodPoseClips.PalmLocal(PeriodPoseClips.AmeliaFbx, "Left_"), "PalmTrail", Gold, 0.06f, 1.4f);
            var tipTrail = Trail(rigs[1].staff.Find("Staff Tip"), Vector3.zero, "WandTrail", noxColor, 0.055f, 0.7f);

            // A stone ahead of each player; each lane walks the runes from a different start.
            var stones = new RuneStoneFx[2];
            var sequences = new RuneDefinition[2][];
            var runeCenters = new Vector3[2];
            var runeColors = new Color[2];
            var aims = new Transform[2];
            for (int i = 0; i < 2; i++)
            {
                var seq = new RuneDefinition[runes.Count];
                for (int k = 0; k < runes.Count; k++) seq[k] = runes[(k + i) % runes.Count];
                sequences[i] = seq;
                stones[i] = RuneStoneBaker.PlaceStone("RuneStone " + profiles[i].displayName, StonePos[i], seq.Length > 0 ? seq[0] : null);
                var def = stones[i].rune;
                runeCenters[i] = stones[i].transform.TransformPoint(def != null ? def.runeCenter : new Vector3(0f, 0.6f, 0f));
                runeColors[i] = def != null ? def.glowColor : Gold;
                var aim = new GameObject("AimPoint").transform;
                aim.SetParent(stones[i].transform, false);
                aim.position = runeCenters[i];
                aims[i] = aim;
            }

            var fx = BuildFx(runeCenters, runeColors, out var controlled);
            var ui = BuildUi(cam);
            Panel(ui.transform, "ExitFade", new Color(0f, 0f, 0f, 0f), Vector2.zero, Vector2.one);
            var exitFade = ui.transform.Find("ExitFade").GetComponent<Image>();

            var cardRoot = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(CardPrefab));
            var card = cardRoot.GetComponentInChildren<ParchmentTitleCard>(true);

            var (timeline, director) = CreateTimeline(TimelinePath);
            var intro = director.gameObject.AddComponent<PeriodIntro>();
            intro.card = card;
            intro.title = "룬";
            intro.subtitle = "손끝이 떨리면, 룬도 떨린다.";
            intro.showAt = TCardIn;
            intro.hideAt = TCardOut;
            intro.skipTo = TReady;
            intro.exitFade = exitFade;

            var gameGo = new GameObject("Game");
            var game = gameGo.AddComponent<GameManager>();
            game.intro = intro;
            game.players = new GameManager.Player[2];
            for (int i = 0; i < 2; i++)
                game.players[i] = new GameManager.Player
                {
                    profile = profiles[i], character = rigs[i].character, rig = rigs[i], pose = rigs[i].character.GetComponent<CasterPose>(),
                    cutsceneAnimator = animators[i], staff = rigs[i].staff, startAim = aims[i],
                };
            var period = gameGo.AddComponent<RunePeriod>();
            period.game = game;
            period.lanes = new RunePeriod.Lane[2];
            for (int i = 0; i < 2; i++) period.lanes[i] = new RunePeriod.Lane { stone = stones[i], sequence = sequences[i] };

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
            AddAnimation(timeline, trails, director, "Palm Trail", palmTrail, EmitClip("Trail_Palm", 9.3f, 11.0f), TEnd);
            AddAnimation(timeline, trails, director, "Wand Trail", tipTrail, EmitClip("Trail_Wand", 9.75f, 10.6f), TEnd);

            var stoneGroup = timeline.CreateTrack<GroupTrack>(null, "Rune Stones");
            var stoneClip = BuildStoneClip();
            for (int i = 0; i < 2; i++)
                AddAnimation(timeline, stoneGroup, director, "Stone " + profiles[i].displayName, stones[i].gameObject.AddComponent<Animator>(), stoneClip, TEnd);

            // The last shot lands exactly on the Game scene's duo camera, so play starts from that view.
            float headY = FindBone(amelia.transform, "Head").position.y;
            var duo = new Vector3(0f, headY, 0f) + PlayCamBack;
            var duoLook = duo + Quaternion.Euler(PlayCamPitch, 0f, 0f) * Vector3.forward * 4f;
            AddShots(timeline, director, cam, new[]
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
                // Pulls back out between the two and rises into the duo framing behind them.
                new Shot { Name = "F_ToGame", Start = 15.2f, End = TEnd, From = new Vector3(0f, 1.3f, -0.3f), To = duo, LookPoint = duoLook, Fov = 50f, FovTo = PlayCamFov, Shake = 0.03f },
            });

            var fxGroup = timeline.CreateTrack<GroupTrack>(null, "FX");
            AddAnimation(timeline, fxGroup, director, "Lights Grading", fx, BuildFxClip(), TEnd);
            AddAnimation(timeline, fxGroup, director, "Fade Letterbox", ui, BuildUiClip(), TEnd);
            AddControlled(timeline, fxGroup, director, controlled);

            AssetDatabase.SaveAssets();
            director.time = 0;
            director.Evaluate();
            foreach (var stone in stones)
                if (stone.model == null)
                    Debug.LogError("[Cutscene] " + stone.name + " has no rune model.");
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("[Cutscene] Built " + ScenePath);
            if (keepOpenScenes && previous.IsValid())
            {
                UnityEngine.SceneManagement.SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        // ------------------------------------------------------------------ characters

        /// <summary>
        /// Creates the profile asset the first time with these defaults; later builds only fill missing references,
        /// so edits made in the inspector stay.
        /// </summary>
        static CasterProfile EnsureProfile(string name, string fbx, string body, string wandFbx, string wandMat, float staffScale, bool leftHanded,
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
        static Animator Trail(Transform parent, Vector3 localPosition, string name, Color color, float width, float time)
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

        static AnimationClip EmitClip(string name, float on, float off)
        {
            var clip = new AnimationClip { name = name };
            Bind(clip, "", typeof(TrailRenderer), "m_Emitting", Curve(new[] { 0f, 0f, on, 1f, off, 0f, TEnd, 0f }, AnimationUtility.TangentMode.Constant));
            // Off outside its beat: a stopped trail still draws its last point, which bloomed into a white ball on
            // the wand tip during play.
            Bind(clip, "", typeof(TrailRenderer), "m_Enabled", Curve(new[] { 0f, 0f, on - 0.05f, 1f, off + 1.5f, 0f, TEnd, 0f }, AnimationUtility.TangentMode.Constant));
            return SaveClip(clip);
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

        // ------------------------------------------------------------------ classroom

        /// <summary>Sunlit rune classroom: wood floor, a navy rug with a gold ring under a stone dais, a wall of tall
        /// windows behind the stone, columns with school banners and bookshelves along the sides.</summary>
        static Color BuildClassroom()
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
            foreach (float x in new[] { -2.3f, 0f, 2.3f })
            {
                Block(env, "Window", PrimitiveType.Quad, new Vector3(x, 2.45f, 5.19f), new Vector3(1.3f, 3.0f, 1f), glass);
                Block(env, "Mullion V", PrimitiveType.Cube, new Vector3(x, 2.45f, 5.17f), new Vector3(0.06f, 3.0f, 0.04f), dark);
                Block(env, "Mullion H", PrimitiveType.Cube, new Vector3(x, 2.9f, 5.17f), new Vector3(1.3f, 0.06f, 0.04f), dark);
                Block(env, "Mullion H2", PrimitiveType.Cube, new Vector3(x, 1.9f, 5.17f), new Vector3(1.3f, 0.06f, 0.04f), dark);
                Block(env, "Sill", PrimitiveType.Cube, new Vector3(x, 0.9f, 5.12f), new Vector3(1.5f, 0.08f, 0.2f), stoneMat);
                Block(env, "Arch", PrimitiveType.Cylinder, new Vector3(x, 3.95f, 5.18f), new Vector3(1.3f, 0.01f, 1.3f), glass, Quaternion.Euler(90f, 0f, 0f));
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
            var shaftTex = ShaftTexture(Root + "/Textures/shaft.png");
            var shaftMat = Additive("SunShaft", shaftTex, new Color(1f, 0.9f, 0.72f, 0.075f));
            foreach (float x in new[] { -2.3f, 0f, 2.3f })
            for (int k = 0; k < 2; k++)
            {
                var q = Block(env, "Shaft", PrimitiveType.Quad, new Vector3(x * 0.85f, 2.2f, 3.2f), new Vector3(1.2f, 5.2f, 1f), shaftMat,
                    // Two quads crossed about the shaft's own axis, so it reads from any side.
                    Quaternion.Euler(-52f, 180f, 0f) * Quaternion.Euler(0f, 90f * k, 0f));
                q.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            }

            // Lasers hit the room (strokes on walls and floor), but not the light shafts, and a flat cylinder's
            // capsule collider would balloon into a sphere: drop those.
            foreach (var col in env.GetComponentsInChildren<Collider>())
                if (col.name == "Shaft" || (col is CapsuleCollider && col.transform.localScale.y < 0.2f))
                    Object.DestroyImmediate(col);
            return fog;
        }

        static GameObject Block(Transform parent, string name, PrimitiveType type, Vector3 position, Vector3 scale, Material material, Quaternion? rotation = null)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent);
            go.transform.SetPositionAndRotation(position, rotation ?? Quaternion.identity);
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go;
        }

        static Material Unlit(string name, Color color)
        {
            var path = $"{AssetRoot}/Materials/{name}.mat";
            AssetDatabase.DeleteAsset(path);
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            m.SetColor("_BaseColor", color);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static Material Additive(string name, Texture texture, Color color)
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

        static Texture2D ShaftTexture(string path)
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
            Volume(volumes, "Base", 0, 1f, Profile("Period1Base", p =>
            {
                p.Add<Tonemapping>().mode.Override(TonemappingMode.Neutral);
                var b = p.Add<Bloom>(); b.threshold.Override(1.1f); b.intensity.Override(0.5f); b.scatter.Override(0.65f);
                var v = p.Add<Vignette>(); v.intensity.Override(0.24f); v.smoothness.Override(0.5f);
                var ca = p.Add<ColorAdjustments>(); ca.contrast.Override(12f); ca.saturation.Override(8f); ca.postExposure.Override(0f);
                p.Add<WhiteBalance>().temperature.Override(8f);
            }));
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
                stoneBase.position = StonePos[i];
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
