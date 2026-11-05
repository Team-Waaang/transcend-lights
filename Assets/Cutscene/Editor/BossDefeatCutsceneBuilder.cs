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

        const string HoodedFbx = "Assets/TripoModels/fantasy_character_3d_model_1/fantasy_character_3d_model_1.fbx";
        const string HoodedMat = "Assets/TripoModels/fantasy_character_3d_model_1/Materials/fantasy_character_3d_model_1.mat";
        const string UnhoodedFbx = "Assets/TripoModels/anime_character_3d_model/anime_character_3d_model.fbx";
        const string UnhoodedMat = "Assets/TripoModels/anime_character_3d_model/Materials/anime_character_3d_model.mat";
        const string UnhoodedTex = "Assets/TripoModels/anime_character_3d_model/anime_character_3d_model.fbm/anime_character_3d_model_BaseColor.jpg";
        const string VfxDir = "Assets/VFX_Klaus/Prefabs/Hyper Casual FX Vol.2/";
        const string MspDir = "Assets/MasterStylizedProjectiles/Projectiles/";
        const string NoiseMild = "Packages/com.unity.cinemachine/Presets/Noise/Handheld_normal_mild.asset";
        const string NoiseStrong = "Packages/com.unity.cinemachine/Presets/Noise/Handheld_normal_strong.asset";

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

        // Boss stands at the origin looking toward the players (-X).
        static readonly Quaternion BossRotation = Quaternion.Euler(0f, -90f, 0f);
        // Both beams fire at TFire; their heads meet at the clash point at TMeet.
        // TFire is when the boss's arm thrusts out in the cast clip (src 1.22s, see HoodedCastIn).
        const float TFire = 0.7f;
        const float TMeet = 1.1f;
        // The players' beam comes in from off-screen left.
        const float GoldSourceX = -3.6f;
        // HCFX_Beam_* prefabs: impact at the pivot, laser running 20 units up local +Y.
        const float KlausBeamLength = 20f;

        struct Shot
        {
            public string Name;
            public float Start, End;
            public Vector3 From, To;
            public Vector3 LookPoint;   // used when LookAt is null
            public Transform LookAt;
            public Vector3 LookOffset;
            public float Fov;
            public float Shake;
            public bool StrongShake;
            public float[] Kicks;       // absolute times of camera shake spikes
        }

        [MenuItem("Tools/Cutscene/Build Boss Defeat Cutscene")]
        static void BuildFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build();
        }

        public static void Build()
        {
            EnsureFolder(Root);
            EnsureFolder(Root + "/Anim");
            EnsureFolder(Root + "/Materials");
            EnsureFolder(Root + "/Volumes");
            EnsureFolder(Root + "/Meshes");
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

        static Material RimMaterial(string name, Material source)
        {
            var path = $"{Root}/Materials/{name}_Rim.mat";
            AssetDatabase.DeleteAsset(path);
            var m = new Material(source);
            m.SetFloat("_UseRim", 1f);
            m.SetColor("_RimColor", new Color(0.85f, 0.75f, 1f, 1f));
            m.SetFloat("_RimBorder", 0.6f);
            m.SetFloat("_RimBlur", 0.25f);
            m.SetFloat("_RimFresnelPower", 2.5f);
            m.SetFloat("_RimMainStrength", 0f);
            m.SetFloat("_RimShadowMask", 0.3f);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static Material LitMaterial(string name, Color color, float smoothness)
        {
            var path = $"{Root}/Materials/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(m);
            return m;
        }

        // ---------- Scene content ----------

        static void BuildEnvironment()
        {
            var fogColor = new Color(0.12f, 0.1f, 0.15f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = 0.09f;
            RenderSettings.fogColor = fogColor;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.42f, 0.4f, 0.5f);
            RenderSettings.skybox = null;

            var env = new GameObject("Environment").transform;
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.SetParent(env);
            ground.transform.localScale = new Vector3(4f, 1f, 4f);
            ground.GetComponent<Renderer>().sharedMaterial = LitMaterial("Ground", new Color(0.13f, 0.12f, 0.15f), 0.15f);

            // Ruined pillars of the arena, mostly behind the boss so the camera side stays open.
            var stone = LitMaterial("Stone", new Color(0.24f, 0.22f, 0.27f), 0.05f);
            var rng = new System.Random(7);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            for (int i = 0; i < 9; i++)
            {
                float angle = Mathf.Lerp(-125f, 125f, i / 8f) + R(-8f, 8f);
                float radius = R(4.2f, 6.5f);
                var pos = Quaternion.Euler(0f, angle, 0f) * Vector3.right * radius;
                var pillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                pillar.name = "Pillar_" + i;
                pillar.transform.SetParent(env);
                float h = R(1.2f, 3.6f), w = R(0.35f, 0.6f);
                pillar.transform.position = new Vector3(pos.x, h * 0.5f, pos.z);
                pillar.transform.localScale = new Vector3(w, h * 0.5f, w);
                pillar.transform.rotation = Quaternion.Euler(R(-6f, 6f), R(0f, 360f), R(-10f, 10f));
                pillar.GetComponent<Renderer>().sharedMaterial = stone;

                var rubble = GameObject.CreatePrimitive(PrimitiveType.Cube);
                rubble.name = "Rubble_" + i;
                rubble.transform.SetParent(env);
                float s = R(0.25f, 0.5f);
                rubble.transform.position = pos + Quaternion.Euler(0f, R(0f, 360f), 0f) * Vector3.forward * R(0.6f, 1.1f) + Vector3.up * s * 0.3f;
                rubble.transform.localScale = new Vector3(s, s * 0.7f, s * 1.3f);
                rubble.transform.rotation = Quaternion.Euler(R(-20f, 20f), R(0f, 360f), R(-20f, 20f));
                rubble.GetComponent<Renderer>().sharedMaterial = stone;
            }
            foreach (var c in env.GetComponentsInChildren<Collider>()) UnityEngine.Object.DestroyImmediate(c);
        }

        /// <summary>
        /// The wand is part of the Tripo body mesh (no bone of its own): its tip is the vertex rigidly
        /// skinned to the hand that lies farthest from the hand bone, in the current pose.
        /// </summary>
        static Vector3 WandTip(SkinnedMeshRenderer body, string handBone)
        {
            int hand = Array.FindIndex(body.bones, b => b.name == handBone);
            var weights = body.sharedMesh.boneWeights;
            var baked = new Mesh();
            body.BakeMesh(baked, true);
            var verts = baked.vertices;
            var handPos = body.bones[hand].position;
            var tip = handPos;
            float best = 0f;
            for (int i = 0; i < verts.Length; i++)
            {
                if (weights[i].boneIndex0 != hand || weights[i].weight0 < 0.95f) continue;
                var p = body.transform.TransformPoint(verts[i]);
                float d = (p - handPos).sqrMagnitude;
                if (d > best) { best = d; tip = p; }
            }
            UnityEngine.Object.DestroyImmediate(baked);
            return tip;
        }

        /// <summary>
        /// Tripo's auto-rig lets shoulder/arm bones pull on the hood, so raising the wand arm drags the hood off the
        /// hair. On a cutscene-only mesh copy, shift those influences onto the head, ramping from neck to head height
        /// (the hood hem still follows the shoulders; hand and wand vertices are left alone).
        /// </summary>
        static void PinHoodToHead(SkinnedMeshRenderer body)
        {
            var src = body.sharedMesh;
            var bones = body.bones;
            var bind = src.bindposes;
            int Find(string n) => Array.FindIndex(bones, b => b.name == n);
            Vector3 BindPos(int i) => bind[i].inverse.MultiplyPoint(Vector3.zero);
            int head = Find("Head");
            var up = (BindPos(head) - BindPos(Find("Hips"))).normalized; // Tripo meshes are Z-up in mesh space
            float from = Vector3.Dot(BindPos(Find("Neck")), up) + 0.01f;
            float to = Vector3.Dot(BindPos(head), up) + 0.03f;
            var pulled = new HashSet<int>(new[] { "Left_Shoulder", "Right_Shoulder", "Left_UpperArm", "Right_UpperArm", "Left_LowerArm", "Right_LowerArm", "Spine", "Chest", "UpperChest" }.Select(Find).Where(i => i >= 0));
            var hands = new HashSet<int>(Enumerable.Range(0, bones.Length).Where(i => bones[i].name.Contains("Hand") || bones[i].name.Contains("Thumb") || bones[i].name.Contains("Index") || bones[i].name.Contains("Middle") || bones[i].name.Contains("Ring") || bones[i].name.Contains("Pinky")));

            var mesh = UnityEngine.Object.Instantiate(src);
            mesh.name = src.name + "_HoodPinned";
            var verts = mesh.vertices;
            var weights = mesh.boneWeights;
            for (int v = 0; v < verts.Length; v++)
            {
                float f = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, Vector3.Dot(verts[v], up)));
                if (f <= 0f) continue;
                var w = weights[v];
                var influence = new Dictionary<int, float>();
                void Add(int bone, float amount) { if (amount > 0f) influence[bone] = influence.TryGetValue(bone, out var a) ? a + amount : amount; }
                Add(w.boneIndex0, w.weight0); Add(w.boneIndex1, w.weight1); Add(w.boneIndex2, w.weight2); Add(w.boneIndex3, w.weight3);
                if (influence.Where(kv => hands.Contains(kv.Key)).Sum(kv => kv.Value) > 0.3f) continue; // wand
                float moved = 0f;
                foreach (var b in influence.Keys.Where(pulled.Contains).ToList())
                {
                    float take = influence[b] * f;
                    influence[b] -= take;
                    moved += take;
                }
                if (moved <= 0f) continue;
                Add(head, moved);
                var top = influence.Where(kv => kv.Value > 0f).OrderByDescending(kv => kv.Value).Take(4).ToArray();
                float total = top.Sum(kv => kv.Value);
                var nw = new BoneWeight();
                if (top.Length > 0) { nw.boneIndex0 = top[0].Key; nw.weight0 = top[0].Value / total; }
                if (top.Length > 1) { nw.boneIndex1 = top[1].Key; nw.weight1 = top[1].Value / total; }
                if (top.Length > 2) { nw.boneIndex2 = top[2].Key; nw.weight2 = top[2].Value / total; }
                if (top.Length > 3) { nw.boneIndex3 = top[3].Key; nw.weight3 = top[3].Value / total; }
                weights[v] = nw;
            }
            mesh.boneWeights = weights;
            var path = $"{Root}/Meshes/{mesh.name}.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            body.sharedMesh = mesh;
        }

        /// <summary>World-space horizontal gap from the knees at the start of <paramref name="to"/> to the knees at the end of <paramref name="from"/>.</summary>
        static Vector3 KneeGap(Animator character, AnimationClip from, AnimationClip to)
        {
            var go = character.gameObject;
            bool wasActive = go.activeSelf;
            go.SetActive(true);
            var left = FindBone(go.transform, "Left_LowerLeg");
            var right = FindBone(go.transform, "Right_LowerLeg");
            from.SampleAnimation(go, from.length);
            var end = (left.position + right.position) * 0.5f;
            to.SampleAnimation(go, 0f);
            var start = (left.position + right.position) * 0.5f;
            go.SetActive(wasActive);
            var gap = end - start;
            gap.y = 0f;
            return gap;
        }

        static Animator SpawnCharacter(string fbx, string name, bool active)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(fbx));
            go.name = name;
            go.transform.SetPositionAndRotation(Vector3.zero, BossRotation);
            // Cutscene-only copy of the toon material with a rim light, so faceted shapes still read in the grey grade.
            var body = go.GetComponentInChildren<SkinnedMeshRenderer>();
            body.sharedMaterial = RimMaterial(name, body.sharedMaterial);
            if (!go.TryGetComponent<Animator>(out var animator)) animator = go.AddComponent<Animator>();
            animator.runtimeAnimatorController = null;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            go.SetActive(active);
            return animator;
        }

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

        static Transform BeamRig(Transform parent, string name, Vector3 head, Vector3 towardCaster)
        {
            var rig = new GameObject(name).transform;
            rig.SetParent(parent);
            rig.SetPositionAndRotation(head, Quaternion.FromToRotation(Vector3.up, towardCaster));
            rig.localScale = Vector3.zero;
            return rig;
        }

        static GameObject KlausBeam(Transform rig, string prefab, float scale)
        {
            var beam = Vfx(rig, prefab, Vector3.zero, scale);
            // Rising smoke and the dark inner stroke pile up into mud when the beam re-fires every half second.
            Mute(beam, "smoke_up", "hit_smoke", "laser_black");
            return beam;
        }

        static Light Light(Transform parent, string name, LightType type, Color color, float intensity, float range)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent);
            var l = go.AddComponent<Light>();
            l.type = type;
            l.color = color;
            l.intensity = intensity;
            if (range > 0) l.range = range;
            return l;
        }

        static void Volume(Transform parent, string name, int priority, float weight, VolumeProfile profile)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent);
            var v = go.AddComponent<Volume>();
            v.isGlobal = true;
            v.priority = priority;
            v.weight = weight;
            v.sharedProfile = profile;
        }

        static VolumeProfile Profile(string name, Action<VolumeProfile> fill)
        {
            var path = $"{Root}/Volumes/{name}.asset";
            AssetDatabase.DeleteAsset(path);
            var p = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(p, path);
            fill(p);
            foreach (var c in p.components)
            {
                c.name = c.GetType().Name;
                c.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(c, p);
            }
            EditorUtility.SetDirty(p);
            return p;
        }

        static GameObject Vfx(Transform parent, string prefab, Vector3 position, float scale, float alpha = 1f)
        {
            var path = (prefab.StartsWith("Assets/") ? prefab : VfxDir + prefab) + ".prefab";
            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = Vector3.one * scale;
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                if (alpha < 1f) main.startColor = Fade(main.startColor, alpha);
            }
            go.SetActive(false);
            return go;
        }

        static void Mute(GameObject vfx, params string[] layers)
        {
            foreach (var ps in vfx.GetComponentsInChildren<ParticleSystem>(true))
                if (Array.IndexOf(layers, ps.name) >= 0) ps.gameObject.SetActive(false);
        }

        /// <summary>
        /// Turns a one-shot effect into one that re-emits every <paramref name="period"/> seconds,
        /// letting earlier particles live out their lifetime instead of being cut when a clip restarts.
        /// </summary>
        static GameObject Looping(GameObject vfx, float period)
        {
            foreach (var ps in vfx.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.loop = true;
                main.duration = period;
            }
            return vfx;
        }

        /// <summary>Recolours every layer of an effect to <paramref name="color"/>, keeping each layer's alpha.</summary>
        static GameObject Tint(GameObject vfx, Color color)
        {
            Color C(Color c) => new Color(color.r, color.g, color.b, c.a);
            Gradient G(Gradient src)
            {
                var keys = src.colorKeys.Select(k => new GradientColorKey(color, k.time)).ToArray();
                var g = new Gradient { mode = src.mode };
                g.SetKeys(keys, src.alphaKeys);
                return g;
            }
            foreach (var ps in vfx.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                var sc = main.startColor;
                switch (sc.mode)
                {
                    case ParticleSystemGradientMode.Color: main.startColor = new ParticleSystem.MinMaxGradient(C(sc.color)); break;
                    case ParticleSystemGradientMode.TwoColors: main.startColor = new ParticleSystem.MinMaxGradient(C(sc.colorMin), C(sc.colorMax)); break;
                    case ParticleSystemGradientMode.TwoGradients: main.startColor = new ParticleSystem.MinMaxGradient(G(sc.gradientMin), G(sc.gradientMax)); break;
                    default: var r = new ParticleSystem.MinMaxGradient(G(sc.gradient)); r.mode = sc.mode; main.startColor = r; break;
                }
            }
            return vfx;
        }

        static ParticleSystem.MinMaxGradient Fade(ParticleSystem.MinMaxGradient g, float alpha)
        {
            Color F(Color c) { c.a *= alpha; return c; }
            Gradient FG(Gradient src)
            {
                var keys = src.alphaKeys;
                for (int i = 0; i < keys.Length; i++) keys[i].alpha *= alpha;
                var dst = new Gradient { mode = src.mode };
                dst.SetKeys(src.colorKeys, keys);
                return dst;
            }
            switch (g.mode)
            {
                case ParticleSystemGradientMode.Color: return new ParticleSystem.MinMaxGradient(F(g.color));
                case ParticleSystemGradientMode.TwoColors: return new ParticleSystem.MinMaxGradient(F(g.colorMin), F(g.colorMax));
                case ParticleSystemGradientMode.TwoGradients: return new ParticleSystem.MinMaxGradient(FG(g.gradientMin), FG(g.gradientMax));
                default:
                    var r = new ParticleSystem.MinMaxGradient(FG(g.gradient));
                    r.mode = g.mode;
                    return r;
            }
        }

        /// <summary>Purple curse embers rising off the skinned body; emission tapers to zero by <paramref name="end"/>.</summary>
        static GameObject CurseWisps(Transform parent, SkinnedMeshRenderer body, float start, float end)
        {
            var go = new GameObject("CurseWisps");
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = false;
            main.duration = end - start;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.2f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.1f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.85f, 0.6f, 1f, 0.9f), new Color(0.6f, 0.32f, 0.95f, 0.8f));
            main.maxParticles = 600;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = ps.emission;
            emission.rateOverTime = new ParticleSystem.MinMaxCurve(130f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.35f, 1f), new Keyframe(1f, 0f)));
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.SkinnedMeshRenderer;
            shape.skinnedMeshRenderer = body;
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
            vel.y = new ParticleSystem.MinMaxCurve(0.2f, 0.45f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.25f;
            noise.frequency = 1.2f;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 1.6f)));
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0.6f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = fade;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = ((UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline).defaultParticleMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            go.SetActive(false);
            return go;
        }

        static GameObject Motes(Transform parent, string name, Color color, float rate, float size, float rise)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent);
            go.transform.position = new Vector3(0f, 1.2f, 0f);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.prewarm = true;
            main.duration = 5f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 7f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.4f, size);
            main.startColor = color;
            main.maxParticles = 400;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = ps.emission;
            emission.rateOverTime = rate;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(7f, 2.4f, 7f);
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-0.03f, 0.03f);
            vel.y = new ParticleSystem.MinMaxCurve(rise * 0.5f, rise);
            vel.z = new ParticleSystem.MinMaxCurve(-0.03f, 0.03f);
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.15f;
            noise.frequency = 0.4f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f) });
            col.color = fade;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = ((UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline).defaultParticleMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            go.SetActive(false);
            return go;
        }

        static Camera BuildCamera()
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = RenderSettings.fogColor;
            cam.nearClipPlane = 0.02f;
            go.AddComponent<AudioListener>();
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            var brain = go.AddComponent<CinemachineBrain>();
            brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f);
            return cam;
        }

        static Animator BuildUi(Camera cam)
        {
            var go = new GameObject("CinemaUI");
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 0.1f;
            // Draw after every particle (sorting order beats render queue) so the white flash really hides the blast.
            canvas.sortingOrder = 1000;
            go.AddComponent<CanvasScaler>();
            var animator = go.AddComponent<Animator>();
            Panel(go.transform, "WhiteFlash", new Color(1f, 0.98f, 0.94f, 0f), Vector2.zero, Vector2.one);
            Panel(go.transform, "LetterboxTop", Color.black, new Vector2(0f, 0.88f), Vector2.one);
            Panel(go.transform, "LetterboxBottom", Color.black, Vector2.zero, new Vector2(1f, 0.12f));
            Panel(go.transform, "BlackFade", Color.black, Vector2.zero, Vector2.one);
            return animator;
        }

        static void Panel(Transform parent, string name, Color color, Vector2 min, Vector2 max)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = min; rt.anchorMax = max;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
        }

        static (Shot, Animator, CinemachineCamera, AnimationClip) BuildShotCamera(Transform parent, Shot s)
        {
            var rig = new GameObject("Shot_" + s.Name);
            rig.transform.SetParent(parent);
            var animator = rig.AddComponent<Animator>();
            var camGo = new GameObject("Cam");
            camGo.transform.SetParent(rig.transform);
            camGo.transform.position = s.From;
            var vcam = camGo.AddComponent<CinemachineCamera>();
            var lens = LensSettings.Default;
            lens.FieldOfView = s.Fov;
            lens.NearClipPlane = 0.02f;
            lens.FarClipPlane = 100f;
            vcam.Lens = lens;

            var dur = s.End - s.Start;
            var clip = new AnimationClip { name = "Cam_" + s.Name };
            Bind(clip, "Cam", typeof(Transform), "m_LocalPosition.x", Lin(0, s.From.x, dur, s.To.x));
            Bind(clip, "Cam", typeof(Transform), "m_LocalPosition.y", Lin(0, s.From.y, dur, s.To.y));
            Bind(clip, "Cam", typeof(Transform), "m_LocalPosition.z", Lin(0, s.From.z, dur, s.To.z));
            if (s.LookAt != null)
            {
                vcam.LookAt = s.LookAt;
                var composer = camGo.AddComponent<CinemachineRotationComposer>();
                composer.Damping = Vector2.zero;
                composer.TargetOffset = s.LookOffset;
                var composition = composer.Composition;
                composition.DeadZone.Enabled = false;
                composer.Composition = composition;
                camGo.transform.LookAt(s.LookAt.position + s.LookOffset);
            }
            else
            {
                var e0 = Quaternion.LookRotation(s.LookPoint - s.From).eulerAngles;
                var e1 = Quaternion.LookRotation(s.LookPoint - s.To).eulerAngles;
                e1 = new Vector3(e0.x + Mathf.DeltaAngle(e0.x, e1.x), e0.y + Mathf.DeltaAngle(e0.y, e1.y), 0f);
                camGo.transform.rotation = Quaternion.Euler(e0);
                Bind(clip, "Cam", typeof(Transform), "localEulerAnglesRaw.x", Lin(0, e0.x, dur, e1.x));
                Bind(clip, "Cam", typeof(Transform), "localEulerAnglesRaw.y", Lin(0, e0.y, dur, e1.y));
                Bind(clip, "Cam", typeof(Transform), "localEulerAnglesRaw.z", Lin(0, 0, dur, 0));
            }
            if (s.Shake > 0)
            {
                var noise = camGo.AddComponent<CinemachineBasicMultiChannelPerlin>();
                noise.NoiseProfile = AssetDatabase.LoadAssetAtPath<NoiseSettings>(s.StrongShake ? NoiseStrong : NoiseMild);
                noise.AmplitudeGain = s.Shake;
                noise.FrequencyGain = 1f;
                if (s.Kicks != null)
                {
                    var keys = new List<float> { 0f, s.Shake };
                    foreach (var k in s.Kicks)
                    {
                        float t = k - s.Start;
                        keys.AddRange(new[] { t - 0.02f, s.Shake, t + 0.03f, s.Shake * 2.5f + 0.8f, t + 0.6f, s.Shake });
                    }
                    keys.AddRange(new[] { dur, s.Shake });
                    Bind(clip, "Cam", typeof(CinemachineBasicMultiChannelPerlin), "AmplitudeGain", Lin(keys.ToArray()));
                }
            }
            return (s, animator, vcam, SaveClip(clip));
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
            BindPosition(clip, "Clash", Smooth(clash), tip);

            // Beam heads fly from each caster to the clash point (TFire..TMeet), then ride it. Scale = head-to-caster length / 20.
            var purpleHead = Concat(new[] { 0f, tip.x, TFire, tip.x }, clashKeys);
            BindPosition(clip, "BeamPurple", Smooth(purpleHead), tip);
            var purpleLength = Concat(new[] { 0f, 0f }, MapValues(purpleHead, x => Mathf.Max(0f, tip.x - x) / KlausBeamLength, 1), new[] { TBlast + 0.05f, 0f });
            BindUniformScale(clip, "BeamPurple", Smooth(purpleLength));

            var goldHead = Concat(new[] { 0f, GoldSourceX, TFire, GoldSourceX }, clashKeys, new[] { TSwap, tip.x - 0.02f });
            BindPosition(clip, "BeamGold", Smooth(goldHead), tip);
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

        static List<float> Flicker(float from, float to, float step, float min, float max, int seed)
        {
            var rng = new System.Random(seed);
            var keys = new List<float>();
            for (float t = from; t < to; t += step) keys.AddRange(new[] { t, Mathf.Lerp(min, max, (float)rng.NextDouble()) });
            return keys;
        }

        /// <summary>Maps the values of (time, value) pairs, starting at pair index <paramref name="fromPair"/>.</summary>
        static float[] MapValues(float[] tv, Func<float, float> f, int fromPair)
        {
            var mapped = new List<float>();
            for (int i = fromPair * 2; i < tv.Length; i += 2) mapped.AddRange(new[] { tv[i], f(tv[i + 1]) });
            return mapped.ToArray();
        }

        static float[] Concat(params float[][] parts)
        {
            var all = new List<float>();
            foreach (var p in parts) all.AddRange(p);
            return all.ToArray();
        }

        // Animating one position axis makes the Animator own the whole vector, so the other two are pinned to the beam line.
        static void BindPosition(AnimationClip clip, string path, AnimationCurve x, Vector3 line)
        {
            Bind(clip, path, typeof(Transform), "m_LocalPosition.x", x);
            Bind(clip, path, typeof(Transform), "m_LocalPosition.y", Lin(0f, line.y, TEnd, line.y));
            Bind(clip, path, typeof(Transform), "m_LocalPosition.z", Lin(0f, line.z, TEnd, line.z));
        }

        static void BindUniformScale(AnimationClip clip, string path, AnimationCurve s)
        {
            Bind(clip, path, typeof(Transform), "m_LocalScale.x", s);
            Bind(clip, path, typeof(Transform), "m_LocalScale.y", s);
            Bind(clip, path, typeof(Transform), "m_LocalScale.z", s);
        }

        static AnimationCurve Lin(params float[] tv) => Curve(tv, AnimationUtility.TangentMode.Linear);
        static AnimationCurve Smooth(params float[] tv) => Curve(tv, AnimationUtility.TangentMode.ClampedAuto);

        static AnimationCurve Curve(float[] tv, AnimationUtility.TangentMode mode)
        {
            var c = new AnimationCurve();
            for (int i = 0; i < tv.Length; i += 2) c.AddKey(tv[i], tv[i + 1]);
            for (int i = 0; i < c.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(c, i, mode);
                AnimationUtility.SetKeyRightTangentMode(c, i, mode);
            }
            return c;
        }

        static void Bind(AnimationClip clip, string path, Type type, string property, AnimationCurve curve) =>
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, type, property), curve);

        static AnimationClip SaveClip(AnimationClip clip)
        {
            var path = $"{Root}/Anim/{clip.name}.anim";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(clip, path);
            return clip;
        }

        // ---------- Timeline helpers ----------

        static void Activation(TimelineAsset timeline, TrackAsset parent, PlayableDirector director, string name, GameObject target, float start, float end)
        {
            var track = timeline.CreateTrack<ActivationTrack>(parent, name);
            track.postPlaybackState = ActivationTrack.PostPlaybackState.LeaveAsIs;
            director.SetGenericBinding(track, target);
            var c = track.CreateDefaultClip();
            c.start = start;
            c.duration = end - start;
        }

        static void Control(ControlTrack track, PlayableDirector director, GameObject target, float start, float end)
        {
            var c = track.CreateDefaultClip();
            c.displayName = target.name;
            c.start = start;
            c.duration = end - start;
            var asset = (ControlPlayableAsset)c.asset;
            asset.sourceGameObject.exposedName = GUID.Generate().ToString();
            director.SetReferenceValue(asset.sourceGameObject.exposedName, target);
            asset.updateParticle = true;
            asset.active = true;
            asset.postPlayback = ActivationControlPlayable.PostPlaybackState.Inactive;
        }

        // ---------- Lookups ----------

        static AnimationClip FindClip(string fbx, string prefix)
        {
            foreach (var a in AssetDatabase.LoadAllAssetsAtPath(fbx))
                if (a is AnimationClip c && c.name.StartsWith(prefix)) return c;
            throw new Exception($"Clip '{prefix}...' not found in {fbx}");
        }

        static Transform FindBone(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            throw new Exception($"Bone {name} not found under {root.name}");
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }
}
