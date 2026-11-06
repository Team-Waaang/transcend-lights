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
    /// Period 2 ("암흑"): a lesson in the academy's old stone gallery (<see cref="StoneHallBuilder"/>). The sun
    /// flickers and the lights cut out; Amelia flinches and looks into the dark, Nox calmly lights the tip of her wand
    /// and sweeps the room with it, Amelia steadies herself and lights hers. With only the two wand lights left the
    /// parchment card unrolls over the dark hall, where the rubble that lay about by day has crystals glowing in it,
    /// and the camera rises into play: <see cref="LightHuntPeriod"/>. Each player shines her wand light where she
    /// points; the rock creatures roaming the dark startle and scurry from it, and one kept lit for about three
    /// seconds turns to stone (<see cref="StoneCreature"/>, Petrify shader).
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
        // The panes carry the night-sky art: by day it burns out white, by night it shows, dimmed.
        static readonly Color NightGlass = new Color(0.55f, 0.6f, 0.8f);
        static readonly Color DayGlass = new Color(1.9f, 1.75f, 1.45f);

        const string CreatureFbx = "Assets/TripoModels/rocky_creature_3d_model/rocky_creature_3d_model.fbx";
        const string CreatureTexture = "Assets/TripoModels/rocky_creature_3d_model/rocky_creature_3d_model.fbm/rocky_creature_3d_model_BaseColor.jpg";
        const float CreatureHeight = 0.72f;
        // Turns the model so it walks along its +Z.
        const float CreatureYaw = 0f;
        // Where they lie (as rubble, by day) and start from; facing (degrees).
        static readonly Vector4[] CreatureSpawns =
        {
            new Vector4(-1.5f, 0f, 4.1f, 150f), new Vector4(1.9f, 0f, 5.6f, 220f), new Vector4(-4.4f, 0f, 7.0f, 100f),
            new Vector4(4.6f, 0f, 9.3f, 260f), new Vector4(0.4f, 0f, 9.4f, 180f),
        };
        static readonly Vector2 HuntMin = new Vector2(-5.9f, 3.3f);
        static readonly Vector2 HuntMax = new Vector2(5.9f, 10.6f);
        static readonly Color[] WandLightColors = { new Color(1f, 0.83f, 0.58f), new Color(0.62f, 0.82f, 1f) };

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
            drawn.Clear();
            var c = PeriodSceneKit.Begin(Root, TimelinePath, 1, keepOpenScenes, StoneHallBuilder.Build, false);
            var timeline = c.timeline;
            var director = c.director;
            // The hall's outer shell does not shadow the sun and moon (they would never get in otherwise); the
            // columns, rubble and statues still do.
            foreach (var r in c.room.root.GetComponentsInChildren<Renderer>())
                if (r.name.StartsWith("Wall") || r.name == "Ceiling" || r.name.StartsWith("Cornice") || r.name.StartsWith("Plinth Course"))
                    r.shadowCastingMode = ShadowCastingMode.Off;

            c.intro.title = "암흑";
            c.intro.subtitle = "어둠 속에서 움직이는 것을 빛으로 붙잡아라.";
            c.intro.showAt = TCardIn;
            c.intro.hideAt = TCardOut;
            c.intro.skipTo = TReady;

            BuildHunt(c);

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
                new Shot { Name = "A_Hall", Start = 0f, End = 3.6f, From = new Vector3(2.2f, 2.5f, 5.6f), To = new Vector3(1.9f, 2.3f, 5.1f), LookPoint = new Vector3(0f, 0.8f, 0.4f), Fov = 52f, Shake = 0.06f, Kicks = new[] { TBlackout }, KickPeak = 0.5f, KickDecay = 0.6f },
                new Shot { Name = "B_Amelia", Start = 3.5f, End = 6.5f, From = new Vector3(-1.0f, 1.32f, 1.85f), To = new Vector3(-0.95f, 1.3f, 1.62f), LookPoint = new Vector3(-0.78f, 1.28f, 0f), Fov = 38f, Shake = 0.05f },
                new Shot { Name = "C_Nox", Start = 6.4f, End = 9.4f, From = new Vector3(1.0f, 1.3f, 1.78f), To = new Vector3(0.95f, 1.33f, 1.56f), LookPoint = new Vector3(0.7f, 1.32f, -0.08f), Fov = 40f, Shake = 0.05f },
                // Both in front of the stones, their two lights in the dark.
                new Shot { Name = "D_Both", Start = 9.3f, End = 12.1f, From = new Vector3(0f, 1.0f, 2.0f), To = new Vector3(0f, 1.05f, 1.75f), LookPoint = new Vector3(0f, 1.25f, 0f), Fov = 58f, Shake = 0.05f },
                // Low into the dark hall under the title card: the "rubble" glints with crystals and breathes.
                new Shot { Name = "E_Dark", Start = 12.0f, End = 16.0f, From = new Vector3(0.3f, 0.9f, 1.0f), To = new Vector3(0.1f, 0.78f, 2.0f), LookPoint = new Vector3(-0.6f, 0.35f, 6.0f), Fov = 44f, Shake = 0.05f },
                new Shot { Name = "F_ToGame", Start = 15.4f, End = TEnd, From = new Vector3(0f, 1.3f, -0.3f), To = c.playCam, LookPoint = c.playCamLook, Fov = 50f, FovTo = PeriodSceneKit.PlayCamFov, Shake = 0.03f },
            });

            var fxGroup = timeline.CreateTrack<GroupTrack>(null, "FX");
            AddAnimation(timeline, fxGroup, director, "Lights Grading", fx, BuildFxClip(), TEnd);
            AddAnimation(timeline, fxGroup, director, "Fade Letterbox", c.ui, BuildUiClip(), TEnd);
            AddControlled(timeline, fxGroup, director, controlled);

            PeriodSceneKit.Finish(c, ScenePath);
        }

        // ------------------------------------------------------------------ the hunt

        /// <summary>
        /// Play for this period: the rigs aim without lasers, a <see cref="WandLight"/> on each, the rock creatures
        /// lying about the hall, and the <see cref="LightHuntPeriod"/> that ties them together.
        /// </summary>
        static LightHuntPeriod BuildHunt(PeriodSceneKit.Context c)
        {
            var coneMat = new Material(Shader.Find("AiHackathon/LightCone"));
            coneMat.SetFloat("_DustAmount", 0.4f);
            coneMat = SaveMaterial(coneMat, "WandBeamShaft");

            var lights = new WandLight[2];
            for (int i = 0; i < 2; i++)
            {
                var rig = c.rigs[i];
                rig.showBeam = false;
                var light = rig.gameObject.AddComponent<WandLight>();
                light.rig = rig;
                light.color = WandLightColors[i];
                light.coneMaterial = coneMat;
                lights[i] = light;

                // Play opens with each light aimed into the hall either side of the runner.
                var aim = new GameObject(c.profiles[i].displayName + " Start Aim").transform;
                aim.position = new Vector3(i == 0 ? -1.4f : 1.4f, 0.3f, 6.5f);
                c.game.players[i].startAim = aim;
                EditorUtility.SetDirty(rig);
            }

            var hunt = c.game.gameObject.AddComponent<LightHuntPeriod>();
            hunt.game = c.game;
            hunt.lights = lights;
            hunt.creatures = BuildCreatures();
            hunt.clearedText = "2교시 통과!";
            return hunt;
        }

        static Material SaveMaterial(Material m, string name)
        {
            var path = $"{Root}/Materials/{name}.mat";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static StoneCreature[] BuildCreatures()
        {
            var root = new GameObject("Rock Creatures").transform;
            var list = new List<StoneCreature>();
            for (int i = 0; i < CreatureSpawns.Length; i++)
            {
                var spawn = CreatureSpawns[i];
                var creature = CreateCreature(root, "Rock Creature " + (i + 1), new Vector3(spawn.x, 0f, spawn.z), spawn.w);
                if (creature == null) return new StoneCreature[0];
                creature.areaMin = HuntMin;
                creature.areaMax = HuntMax;
                // Inside the play camera's view (16:9): the horizontal half-angle's tangent, less a little.
                float halfTan = Mathf.Tan(PeriodSceneKit.PlayCamFov * 0.5f * Mathf.Deg2Rad) * 16f / 9f;
                creature.viewSlope = halfTan * 0.85f;
                creature.viewApex = new Vector2(0f, PeriodSceneKit.PlayCamBack.z);
                creature.viewMargin = 0.5f;
                // A little variety in pace.
                creature.wanderSpeed = 0.36f + 0.04f * (i % 3);
                creature.fleeSpeed = 0.95f + 0.07f * (i % 3);
                list.Add(creature);
            }
            return list.ToArray();
        }

        /// <summary>
        /// One rock creature (model on the Petrify shader, scaled to <see cref="CreatureHeight"/>, standing on its root)
        /// with its alert, petrify, grit and step effects and its flash light. Also used by Period 3, where the boss
        /// summons them. Materials go under the current <see cref="CutsceneKit.AssetRoot"/>.
        /// </summary>
        internal static StoneCreature CreateCreature(Transform parent, string name, Vector3 position, float yawDegrees)
        {
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(CreatureFbx);
            if (fbx == null)
            {
                Debug.LogError("[Cutscene] Missing " + CreatureFbx);
                return null;
            }
            // One shared material, kept in place (not deleted and remade) so scenes that already use it stay wired.
            var matPath = $"{Root}/Materials/RockCreature.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                EnsureFolder(Root + "/Materials");
                mat = new Material(Shader.Find("AiHackathon/Petrify"));
                AssetDatabase.CreateAsset(mat, matPath);
            }
            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(CreatureTexture));
            EditorUtility.SetDirty(mat);

            // Model-space bounds, to stand it on its feet at the root and scale it to CreatureHeight.
            var probe = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
            probe.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            // From the meshes themselves: a skinned renderer's bounds are padded.
            var bounds = new Bounds();
            bool any = false;
            foreach (var r in probe.GetComponentsInChildren<Renderer>())
            {
                var mesh = r is SkinnedMeshRenderer smr ? smr.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null) continue;
                var mb = mesh.bounds;
                for (int k = 0; k < 8; k++)
                {
                    var corner = r.transform.TransformPoint(mb.center + Vector3.Scale(mb.extents, new Vector3((k & 1) == 0 ? -1 : 1, (k & 2) == 0 ? -1 : 1, (k & 4) == 0 ? -1 : 1)));
                    if (!any) { bounds = new Bounds(corner, Vector3.zero); any = true; }
                    else bounds.Encapsulate(corner);
                }
            }
            Object.DestroyImmediate(probe);
            float scale = CreatureHeight / Mathf.Max(0.01f, bounds.size.y);
            float radius = Mathf.Max(bounds.extents.x, bounds.extents.z) * scale * 0.85f;

            var go = new GameObject(name);
            go.transform.SetParent(parent);
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yawDegrees, 0f));
            var body = new GameObject("Body").transform;
            body.SetParent(go.transform, false);
            var yaw = new GameObject("Facing").transform;
            yaw.SetParent(body, false);
            yaw.localRotation = Quaternion.Euler(0f, CreatureYaw, 0f);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
            model.transform.SetParent(yaw, false);
            model.transform.localScale = Vector3.one * scale;
            model.transform.localPosition = -scale * new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            var renderers = model.GetComponentsInChildren<Renderer>();
            foreach (var r in renderers)
            {
                var mats = new Material[r.sharedMaterials.Length];
                for (int k = 0; k < mats.Length; k++) mats[k] = mat;
                r.sharedMaterials = mats;
                r.shadowCastingMode = ShadowCastingMode.On;
            }
            foreach (var col in model.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(col);

            var creature = go.AddComponent<StoneCreature>();
            creature.body = body;
            creature.renderers = renderers;
            creature.height = CreatureHeight;
            creature.radius = radius;
            creature.alertFx = Vfx(go.transform, "HCFX_Sign_ExclamationMark", new Vector3(0f, CreatureHeight + 0.3f, 0f), 0.12f);
            // Its rising rainbow bubbles read as more magic than alarm.
            Mute(creature.alertFx, "particle_up");
            creature.petrifyFx = PetrifyBurst(go.transform, radius);
            creature.crumbleDust = Grit(go.transform, radius);
            creature.stepDust = StepPuff(go.transform, radius);
            var glow = Light(go.transform, "Petrify Glow", LightType.Point, new Color(1f, 0.78f, 0.45f), 0f, 2.6f);
            glow.transform.localPosition = new Vector3(0f, CreatureHeight * 0.6f, 0f);
            glow.shadows = LightShadows.None;
            creature.flashLight = glow;
            return creature;
        }

        /// <summary>
        /// The moment it sets: a soft cloud of stone dust puffs off the body and settles, a ring of dust rolls out
        /// along the floor, and a few gold glints rise where the light sealed it. One shot; switching the object on
        /// plays it. (The grit chips come from <see cref="StoneCreature.crumbleDust"/>.)
        /// </summary>
        static GameObject PetrifyBurst(Transform parent, float radius)
        {
            var root = new GameObject("Petrify FX");
            root.transform.SetParent(parent, false);
            var dust = new Color(0.58f, 0.55f, 0.5f);

            var cloud = BurstSystem(root.transform, "Dust Cloud", new Vector3(0f, CreatureHeight * 0.45f, 0f), 14,
                new ParticleSystem.MinMaxCurve(1.6f, 2.4f), new ParticleSystem.MinMaxCurve(0.12f, 0.4f), new ParticleSystem.MinMaxCurve(0.3f, 0.55f),
                dust, 0.45f, LitParticle());
            var cloudShape = cloud.shape;
            cloudShape.shapeType = ParticleSystemShapeType.Sphere;
            cloudShape.radius = radius * 0.8f;
            var cloudMain = cloud.main;
            cloudMain.gravityModifier = -0.015f;
            Settle(cloud, 1.8f, 0.6f, 1.5f);

            var ring = BurstSystem(root.transform, "Dust Ring", new Vector3(0f, 0.04f, 0f), 18,
                new ParticleSystem.MinMaxCurve(1.0f, 1.5f), new ParticleSystem.MinMaxCurve(0.45f, 0.8f), new ParticleSystem.MinMaxCurve(0.16f, 0.3f),
                dust, 0.5f, LitParticle());
            var ringShape = ring.shape;
            ringShape.shapeType = ParticleSystemShapeType.Circle;
            ringShape.radius = radius * 0.5f;
            ringShape.radiusThickness = 0f;
            ringShape.rotation = new Vector3(90f, 0f, 0f);
            Settle(ring, 2.5f, 0.5f, 2.2f);

            var soft = ((UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline).defaultParticleMaterial;
            var glintMat = PeriodSceneKit.Additive("PetrifyGlint", soft != null ? soft.GetTexture("_BaseMap") : null, Color.white);
            var glints = BurstSystem(root.transform, "Glints", new Vector3(0f, CreatureHeight * 0.5f, 0f), 10,
                new ParticleSystem.MinMaxCurve(0.7f, 1.3f), new ParticleSystem.MinMaxCurve(0.05f, 0.2f), new ParticleSystem.MinMaxCurve(0.012f, 0.026f),
                new Color(2.2f, 1.6f, 0.8f), 1f, glintMat);
            var glintShape = glints.shape;
            glintShape.shapeType = ParticleSystemShapeType.Sphere;
            glintShape.radius = radius * 0.7f;
            var glintMain = glints.main;
            glintMain.gravityModifier = -0.06f;

            root.SetActive(false);
            return root;
        }

        static ParticleSystem BurstSystem(Transform parent, string name, Vector3 at, int count, ParticleSystem.MinMaxCurve life,
            ParticleSystem.MinMaxCurve speed, ParticleSystem.MinMaxCurve size, Color color, float alpha, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = true;
            main.duration = 0.5f;
            main.startLifetime = life;
            main.startSpeed = speed;
            main.startSize = size;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new Color(color.r, color.g, color.b, 1f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = count * 2;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
            var rotation = ps.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
            var colour = ps.colorOverLifetime;
            colour.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(alpha, 0.12f), new GradientAlphaKey(alpha * 0.6f, 0.5f), new GradientAlphaKey(0f, 1f) });
            colour.color = fade;
            if (material.GetTexture("_BaseMap") is Texture2D t && t.name == "dust_puffs") RandomCell(ps);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            return ps;
        }

        // Dust slows as it spreads and swells as it thins out.
        static void Settle(ParticleSystem ps, float drag, float sizeFrom, float sizeTo)
        {
            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.drag = drag;
            limit.multiplyDragByParticleSize = false;
            limit.multiplyDragByParticleVelocity = true;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, sizeFrom), new Keyframe(1f, sizeTo)));
        }

        /// <summary>Stone grit shed while it hardens, and the burst when it sets: chips falling off the body.</summary>
        static ParticleSystem Grit(Transform parent, float radius)
        {
            var go = new GameObject("Grit");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, CreatureHeight * 0.55f, 0f);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.9f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.035f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.55f, 0.52f, 0.48f), new Color(0.82f, 0.78f, 0.7f));
            main.gravityModifier = 1.4f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 200;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = radius * 0.8f;
            var col = ps.collision;
            col.enabled = true;
            col.type = ParticleSystemCollisionType.World;
            col.mode = ParticleSystemCollisionMode.Collision3D;
            col.quality = ParticleSystemCollisionQuality.Low;
            col.bounce = 0.3f;
            col.dampen = 0.5f;
            RandomCell(ps);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = GritMaterial();
            r.shadowCastingMode = ShadowCastingMode.Off;
            return ps;
        }

        /// <summary>A puff of dust at the feet each time a foot comes down.</summary>
        static ParticleSystem StepPuff(Transform parent, float radius)
        {
            var go = new GameObject("Step Dust");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 0.03f, 0f);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.25f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.16f);
            main.startColor = new Color(0.5f, 0.47f, 0.44f, 0.35f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 60;
            main.playOnAwake = true;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = radius * 0.7f;
            shape.rotation = new Vector3(90f, 0f, 0f);
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(1f, 1.4f)));
            var colour = ps.colorOverLifetime;
            colour.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            colour.color = fade;
            RandomCell(ps);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = LitParticle();
            r.shadowCastingMode = ShadowCastingMode.Off;
            return ps;
        }

        // Lit so grit and dust only show where a light is on them, like the creatures.
        static readonly HashSet<string> drawn = new HashSet<string>();

        static Material LitParticle() => DustMaterial("StoneDust", PuffSheet(Root + "/Textures/dust_puffs.png", false), 0.45f);
        static Material GritMaterial() => DustMaterial("StoneGrit", PuffSheet(Root + "/Textures/grit_chips.png", true), 0f);

        /// <summary>
        /// Lit, alpha-blended particles (so dust only shows where a light falls on it) with a 2x2 sheet of shapes
        /// picked at random per particle, fading softly where they meet the floor.
        /// </summary>
        static Material DustMaterial(string name, Texture2D sheet, float softFade)
        {
            var path = $"{Root}/Materials/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Particles/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetTexture("_BaseMap", sheet);
            // Plain alpha blending: in premultiplied mode the lighting's specular and reflections are added over the
            // whole quad, transparent corners included, and every puff showed as a faint square.
            // URP turns alpha blending into premultiplied when "preserve specular" is on; switch that off.
            m.SetFloat("_BlendModePreserveSpecular", 0f);
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.DisableKeyword("_ALPHAMODULATE_ON");
            m.SetFloat("_Smoothness", 0f);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_SpecularHighlights", 0f);
            m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            m.SetFloat("_EnvironmentReflections", 0f);
            m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            if (softFade > 0f)
            {
                m.SetFloat("_SoftParticlesEnabled", 1f);
                m.SetFloat("_SoftParticlesNearFadeDistance", 0f);
                m.SetFloat("_SoftParticlesFarFadeDistance", softFade);
                m.SetVector("_SoftParticleFadeParams", new Vector4(0f, 1f / softFade, 0f, 0f));
                m.EnableKeyword("_SOFTPARTICLES_ON");
            }
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Picks one of the four cells of the 2x2 sheet per particle.</summary>
        static void RandomCell(ParticleSystem ps)
        {
            var sheet = ps.textureSheetAnimation;
            sheet.enabled = true;
            sheet.numTilesX = 2;
            sheet.numTilesY = 2;
            sheet.animation = ParticleSystemAnimationType.WholeSheet;
            sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
            sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, 3.999f);
            sheet.cycleCount = 1;
        }

        /// <summary>
        /// Draws a 2x2 sheet: soft, lumpy puffs of dust (wispy edges, denser core), or for <paramref name="chips"/>
        /// small irregular stone fragments with a lit top edge.
        /// </summary>
        static Texture2D PuffSheet(string path, bool chips)
        {
            // Drawn once per build; every creature shares it.
            if (!drawn.Add(path)) return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            const int cell = 128, n = cell * 2;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true);
            var px = new Color[n * n];
            for (int c = 0; c < 4; c++)
            {
                int ox = (c % 2) * cell, oy = (c / 2) * cell;
                float seed = 13.7f * (c + 1);
                for (int y = 0; y < cell; y++)
                for (int x = 0; x < cell; x++)
                {
                    float u = (x + 0.5f) / cell * 2f - 1f, v = (y + 0.5f) / cell * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float ang = Mathf.Atan2(v, u);
                    float a;
                    float shade = 1f;
                    if (!chips)
                    {
                        // Lumpy outline from a few angular lobes, wispy noise inside, falling off softly.
                        float lobes = 0.06f * Mathf.Sin(ang * 3f + seed) + 0.04f * Mathf.Sin(ang * 5f + seed * 1.7f);
                        float edge = Mathf.Clamp01(1f - r / (0.82f + lobes));
                        float noise = Mathf.PerlinNoise(u * 2.6f + seed, v * 2.6f + seed * 0.5f) * 0.65f
                                      + Mathf.PerlinNoise(u * 6f + seed * 2f, v * 6f - seed) * 0.35f;
                        a = Mathf.Pow(edge, 1.6f) * Mathf.Lerp(0.45f, 1.1f, noise);
                        shade = Mathf.Lerp(0.85f, 1.05f, noise);
                    }
                    else
                    {
                        // A chunk: a jagged polygon-ish outline, opaque, lighter on its upper side.
                        float jag = 0.18f * Mathf.Sin(ang * 4f + seed) + 0.1f * Mathf.Sin(ang * 7f + seed * 2.3f);
                        float edge = (0.62f + jag) - r;
                        a = Mathf.Clamp01(edge * 14f);
                        shade = Mathf.Lerp(0.7f, 1.1f, Mathf.Clamp01(0.5f + v * 0.6f)) * Mathf.Lerp(0.85f, 1f, Mathf.PerlinNoise(u * 5f + seed, v * 5f));
                    }
                    px[(oy + y) * n + ox + x] = new Color(shade, shade, shade, Mathf.Clamp01(a));
                }
            }
            tex.SetPixels(px);
            tex.Apply();
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
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
            Volume(volumes, "Base", 0, 1f, Profile("Period2Base", PeriodBaseGrade));
            // Night grade: cooler, dimmer, heavier vignette.
            Volume(volumes, "Dark", 1, 0f, Profile("Period2Dark", Period2DarkGrade));
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
