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
    /// Shared building blocks for the generated boss cutscenes: arena, characters, shot cameras, VFX handling,
    /// grading volumes, curve and Timeline helpers. Each builder sets <see cref="AssetRoot"/> before building.
    /// </summary>
    internal static class CutsceneKit
    {
        /// <summary>Folder that receives this cutscene's generated clips, materials, meshes and volume profiles.</summary>
        internal static string AssetRoot = "Assets/Cutscene";

        internal const string HoodedFbx = "Assets/TripoModels/fantasy_character_3d_model_1/fantasy_character_3d_model_1.fbx";
        internal const string VfxDir = "Assets/VFX_Klaus/Prefabs/Hyper Casual FX Vol.2/";
        internal const string MspDir = "Assets/MasterStylizedProjectiles/Projectiles/";
        const string NoiseMild = "Packages/com.unity.cinemachine/Presets/Noise/Handheld_normal_mild.asset";
        const string NoiseStrong = "Packages/com.unity.cinemachine/Presets/Noise/Handheld_normal_strong.asset";

        // Boss stands at the origin looking toward the players (-X).
        internal static readonly Quaternion BossRotation = Quaternion.Euler(0f, -90f, 0f);
        // HCFX_Beam_* prefabs: impact at the pivot, laser running 20 units up local +Y.
        internal const float KlausBeamLength = 20f;

        internal static void PrepareFolders()
        {
            foreach (var sub in new[] { "", "/Anim", "/Materials", "/Volumes", "/Meshes" }) EnsureFolder(AssetRoot + sub);
        }

        internal struct Shot
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
            public float KickPeak;      // > 0: spike amplitude, otherwise Shake * 2.5 + 0.8
            public float KickDecay;     // > 0: seconds to settle back to Shake, otherwise 0.6
            public float Thud;          // absolute time of one vertical ground thump, 0 = none
            public float FovTo;         // > 0: lens zooms from Fov to this over the shot
            public bool SnapIn;         // accelerate (ease-in) the move and zoom, for a punch-in that ends on a cut
        }

        internal static Material RimMaterial(string name, Material source)
        {
            var path = $"{AssetRoot}/Materials/{name}_Rim.mat";
            AssetDatabase.DeleteAsset(path);
            var m = new Material(source);
            m.SetFloat("_UseRim", 1f);
            m.SetColor("_RimColor", new Color(0.85f, 0.75f, 1f, 1f));
            m.SetFloat("_RimBorder", 0.6f);
            m.SetFloat("_RimBlur", 0.25f);
            m.SetFloat("_RimFresnelPower", 2.5f);
            m.SetFloat("_RimMainStrength", 0f);
            m.SetFloat("_RimShadowMask", 0.3f);
            BossToon(m);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        // ---------- The storybook grade ("PP", commit 347d3e6) ----------
        // Kept in code: the profiles and boss materials are rebuilt from here, which had dropped that look.

        /// <summary>The boss's toon shading: plum outline, lavender cel shadow with a violet terminator, rim shade.</summary>
        internal static void BossToon(Material m)
        {
            var outline = Shader.Find("Hidden/lilToonOutline");
            if (outline != null && m.shader != outline) m.shader = outline;
            m.SetFloat("_OutlineWidth", 0.13f);
            m.SetColor("_OutlineColor", new Color(0.12f, 0.07f, 0.16f, 1f));
            m.SetFloat("_OutlineEnableLighting", 0.55f);
            m.SetFloat("_ShadowStrength", 0.75f);
            m.SetFloat("_ShadowBorder", 0.45f);
            m.SetFloat("_ShadowBlur", 0.25f);
            m.SetFloat("_ShadowReceive", 0.5f);
            m.SetColor("_ShadowColor", new Color(0.7f, 0.64f, 0.88f, 1f));
            m.SetColor("_ShadowBorderColor", new Color(0.75f, 0.4f, 0.8f, 1f));
            m.SetFloat("_ShadowBorderRange", 0.07f);
            m.SetColor("_Shadow2ndColor", new Color(0.68f, 0.66f, 0.79f, 0f));
            m.SetFloat("_UseRimShade", 1f);
            m.SetColor("_RimShadeColor", new Color(0.45f, 0.36f, 0.72f, 1f));
            m.SetFloat("_RimShadeBorder", 0.6f);
            m.SetFloat("_RimShadeBlur", 0.4f);
            m.SetFloat("_RimShadeFresnelPower", 1.5f);
            m.SetFloat("_RimDirStrength", 0.6f);
            m.SetColor("_RimIndirColor", new Color(0.6f, 0.65f, 1f, 0.8f));
            m.SetFloat("_RimIndirBorder", 0.55f);
            m.SetFloat("_RimIndirBlur", 0.15f);
        }

        /// <summary>Base grade of the boss cutscenes and Period 3: ACES, warm highlights over cool shadows, a lift.</summary>
        internal static void CutsceneBaseGrade(VolumeProfile p)
        {
            p.Add<Tonemapping>().mode.Override(TonemappingMode.ACES);
            var b = p.Add<Bloom>(); b.threshold.Override(0.9f); b.intensity.Override(1.15f); b.scatter.Override(0.72f);
            b.tint.Override(new Color(1f, 0.94f, 0.9f)); b.highQualityFiltering.Override(true);
            var v = p.Add<Vignette>(); v.intensity.Override(0.3f); v.smoothness.Override(0.45f); v.color.Override(new Color(0.03f, 0.02f, 0.06f));
            var ca = p.Add<ColorAdjustments>(); ca.contrast.Override(10f); ca.postExposure.Override(0.45f); ca.saturation.Override(6f);
            var g = p.Add<FilmGrain>(); g.type.Override(FilmGrainLookup.Thin1); g.intensity.Override(0.14f);
            var lgg = p.Add<LiftGammaGain>(); lgg.lift.Override(new Vector4(1f, 1f, 1.02f, 0.02f)); lgg.gain.Override(new Vector4(1.02f, 1f, 0.99f, 0f));
            var st = p.Add<SplitToning>(); st.shadows.Override(new Color(0.46f, 0.47f, 0.56f)); st.highlights.Override(new Color(0.58f, 0.53f, 0.48f)); st.balance.Override(-10f);
            var wb = p.Add<WhiteBalance>(); wb.temperature.Override(-2f); wb.tint.Override(0f);
        }

        /// <summary>The curse grade on top of it: muted rather than grey, cool shadows.</summary>
        internal static void CutsceneCurseGrade(VolumeProfile p)
        {
            var ca = p.Add<ColorAdjustments>(); ca.saturation.Override(-40f); ca.colorFilter.Override(new Color(0.94f, 0.93f, 1f)); ca.postExposure.Override(0f);
            var v = p.Add<Vignette>(); v.intensity.Override(0.42f); v.color.Override(new Color(0.05f, 0.02f, 0.1f));
            var st = p.Add<SplitToning>(); st.shadows.Override(new Color(0.47f, 0.46f, 0.56f)); st.highlights.Override(new Color(0.57f, 0.53f, 0.5f)); st.balance.Override(-15f);
        }

        /// <summary>Base grade of the Period 1 and 2 classrooms: warm, bright and a little saturated.</summary>
        internal static void PeriodBaseGrade(VolumeProfile p)
        {
            p.Add<Tonemapping>().mode.Override(TonemappingMode.ACES);
            var b = p.Add<Bloom>(); b.threshold.Override(0.9f); b.intensity.Override(0.8f); b.scatter.Override(0.75f);
            b.tint.Override(new Color(1f, 0.92f, 0.8f)); b.highQualityFiltering.Override(true);
            var v = p.Add<Vignette>(); v.intensity.Override(0.3f); v.smoothness.Override(0.45f); v.color.Override(new Color(0.1f, 0.06f, 0.03f));
            var ca = p.Add<ColorAdjustments>(); ca.contrast.Override(12f); ca.saturation.Override(16f); ca.postExposure.Override(0.45f);
            var wb = p.Add<WhiteBalance>(); wb.temperature.Override(12f); wb.tint.Override(2f);
            var g = p.Add<FilmGrain>(); g.type.Override(FilmGrainLookup.Thin1); g.intensity.Override(0.1f);
            var lgg = p.Add<LiftGammaGain>(); lgg.lift.Override(new Vector4(0.99f, 1f, 1.02f, 0.01f)); lgg.gain.Override(new Vector4(1.02f, 1f, 0.97f, 0f));
            var st = p.Add<SplitToning>(); st.shadows.Override(new Color(0.47f, 0.49f, 0.53f)); st.highlights.Override(new Color(0.6f, 0.54f, 0.45f)); st.balance.Override(-5f);
        }

        /// <summary>Period 2's night: cool and dim, but not drained.</summary>
        internal static void Period2DarkGrade(VolumeProfile p)
        {
            var ca = p.Add<ColorAdjustments>(); ca.saturation.Override(-8f); ca.contrast.Override(10f); ca.postExposure.Override(0.35f);
            var wb = p.Add<WhiteBalance>(); wb.temperature.Override(-18f); wb.tint.Override(3f);
            var v = p.Add<Vignette>(); v.intensity.Override(0.4f); v.color.Override(new Color(0.01f, 0.02f, 0.06f));
            var b = p.Add<Bloom>(); b.intensity.Override(1.1f); b.threshold.Override(0.82f); b.tint.Override(new Color(0.88f, 0.94f, 1f));
            var st = p.Add<SplitToning>(); st.shadows.Override(new Color(0.42f, 0.46f, 0.58f)); st.highlights.Override(new Color(0.6f, 0.54f, 0.44f)); st.balance.Override(-20f);
        }

        /// <summary>Rewrites an existing profile asset in place (scenes keep referencing it) with <paramref name="fill"/>.</summary>
        internal static bool RefillProfile(string path, Action<VolumeProfile> fill)
        {
            var p = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (p == null) return false;
            foreach (var c in p.components.ToArray())
            {
                AssetDatabase.RemoveObjectFromAsset(c);
                UnityEngine.Object.DestroyImmediate(c, true);
            }
            p.components.Clear();
            fill(p);
            foreach (var c in p.components)
            {
                c.name = c.GetType().Name;
                c.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(c, p);
            }
            EditorUtility.SetDirty(p);
            return true;
        }

        /// <summary>
        /// Puts the storybook grade back on the profiles and boss materials already in the project, without rebuilding
        /// any scene. The builders produce the same from now on.
        /// </summary>
        [MenuItem("Tools/Cutscene/Restore Storybook Grade")]
        public static string RestoreStorybookGrade()
        {
            var done = new List<string>();
            string[] cutsceneDirs = { "Assets/Cutscene/BossEntrance", "Assets/Cutscene/BossPhase2", "Assets/Cutscene", "Assets/Game/Period3", "Assets/Cutscene/BossFall" };
            foreach (var dir in cutsceneDirs)
            {
                if (RefillProfile(dir + "/Volumes/CutsceneBase.asset", CutsceneBaseGrade)) done.Add(dir + " Base");
                if (RefillProfile(dir + "/Volumes/CutsceneCurse.asset", CutsceneCurseGrade)) done.Add(dir + " Curse");
            }
            if (RefillProfile("Assets/Cutscene/Period1/Volumes/Period1Base.asset", PeriodBaseGrade)) done.Add("Period1Base");
            if (RefillProfile("Assets/Cutscene/Period2/Volumes/Period2Base.asset", PeriodBaseGrade)) done.Add("Period2Base");
            if (RefillProfile("Assets/Cutscene/Period2/Volumes/Period2Dark.asset", Period2DarkGrade)) done.Add("Period2Dark");
            foreach (var guid in AssetDatabase.FindAssets("t:Material Boss", new[] { "Assets/Cutscene", "Assets/Game/Period3" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith("_Rim.mat")) continue;
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                BossToon(m);
                EditorUtility.SetDirty(m);
                done.Add(path);
            }
            AssetDatabase.SaveAssets();
            return string.Join("\n", done);
        }

        internal static Material LitMaterial(string name, Color color, float smoothness)
        {
            var path = $"{AssetRoot}/Materials/{name}.mat";
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

        /// <summary>
        /// The Period 3 boss arena (<see cref="BossArena"/>) with its fog and ambient. Returns the looping
        /// environment effects (brazier flames, ground mist): add them to the timeline's controlled list so they
        /// play in previews and scrubbing too.
        /// </summary>
        internal static List<GameObject> BuildEnvironment() => BossArena.Build();

        /// <summary>
        /// The wand is part of the Tripo body mesh (no bone of its own): its tip is the vertex rigidly
        /// skinned to the hand that lies farthest from the hand bone, in the current pose.
        /// </summary>
        internal static Vector3 WandTip(SkinnedMeshRenderer body, string handBone)
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
        /// (the hood hem still follows the shoulders; hand and wand vertices are left alone). The same copy also closes
        /// his right hand on the wand (<see cref="PeriodPoseClips.CloseBossHandOnWand"/>).
        /// </summary>
        internal static void PrepareBossBody(SkinnedMeshRenderer body)
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
            PeriodPoseClips.CloseBossHandOnWand(mesh, AssetDatabase.GetAssetPath(src));
            var path = $"{AssetRoot}/Meshes/{mesh.name}.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            body.sharedMesh = mesh;
        }

        /// <summary>World-space horizontal gap from the knees at the start of <paramref name="to"/> to the knees at the end of <paramref name="from"/>.</summary>
        internal static Vector3 KneeGap(Animator character, AnimationClip from, AnimationClip to)
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

        internal static Animator SpawnCharacter(string fbx, string name, bool active)
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

        internal static Transform BeamRig(Transform parent, string name, Vector3 head, Vector3 towardCaster)
        {
            var rig = new GameObject(name).transform;
            rig.SetParent(parent);
            rig.SetPositionAndRotation(head, Quaternion.FromToRotation(Vector3.up, towardCaster));
            rig.localScale = Vector3.zero;
            return rig;
        }

        internal static GameObject KlausBeam(Transform rig, string prefab, float scale)
        {
            var beam = Vfx(rig, prefab, Vector3.zero, scale);
            // Rising smoke and the dark inner stroke pile up into mud when the beam re-fires every half second.
            Mute(beam, "smoke_up", "hit_smoke", "laser_black");
            return beam;
        }

        internal static Light Light(Transform parent, string name, LightType type, Color color, float intensity, float range)
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

        internal static void Volume(Transform parent, string name, int priority, float weight, VolumeProfile profile)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent);
            var v = go.AddComponent<Volume>();
            v.isGlobal = true;
            v.priority = priority;
            v.weight = weight;
            v.sharedProfile = profile;
        }

        internal static VolumeProfile Profile(string name, Action<VolumeProfile> fill)
        {
            var path = $"{AssetRoot}/Volumes/{name}.asset";
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

        internal static GameObject Vfx(Transform parent, string prefab, Vector3 position, float scale, float alpha = 1f)
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

        internal static void Mute(GameObject vfx, params string[] layers)
        {
            foreach (var ps in vfx.GetComponentsInChildren<ParticleSystem>(true))
                if (Array.IndexOf(layers, ps.name) >= 0) ps.gameObject.SetActive(false);
        }

        /// <summary>
        /// Turns a one-shot effect into one that re-emits every <paramref name="period"/> seconds,
        /// letting earlier particles live out their lifetime instead of being cut when a clip restarts.
        /// </summary>
        internal static GameObject Looping(GameObject vfx, float period)
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
        internal static GameObject Tint(GameObject vfx, Color color)
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

        internal static ParticleSystem.MinMaxGradient Fade(ParticleSystem.MinMaxGradient g, float alpha)
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
        internal static GameObject CurseWisps(Transform parent, SkinnedMeshRenderer body, float start, float end)
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

        internal static GameObject Motes(Transform parent, string name, Color color, float rate, float size, float rise)
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

        internal static Camera BuildCamera()
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

        internal static Animator BuildUi(Camera cam)
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

        internal static void Panel(Transform parent, string name, Color color, Vector2 min, Vector2 max)
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

        internal static (Shot, Animator, CinemachineCamera, AnimationClip) BuildShotCamera(Transform parent, Shot s)
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
            AnimationCurve Move(float a, float b) => s.SnapIn ? EaseIn(dur, a, b) : Lin(0, a, dur, b);
            Bind(clip, "Cam", typeof(Transform), "m_LocalPosition.x", Move(s.From.x, s.To.x));
            Bind(clip, "Cam", typeof(Transform), "m_LocalPosition.y", s.Thud > 0f ? ThudY(s, dur) : Move(s.From.y, s.To.y));
            Bind(clip, "Cam", typeof(Transform), "m_LocalPosition.z", Move(s.From.z, s.To.z));
            if (s.FovTo > 0) Bind(clip, "Cam", typeof(CinemachineCamera), "Lens.FieldOfView", Move(s.Fov, s.FovTo));
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
                        float peak = s.KickPeak > 0f ? s.KickPeak : s.Shake * 2.5f + 0.8f;
                        float back = s.KickDecay > 0f ? s.KickDecay : 0.6f;
                        keys.AddRange(new[] { t - 0.02f, s.Shake, t + 0.04f, peak, t + back, s.Shake });
                    }
                    keys.AddRange(new[] { dur, s.Shake });
                    Bind(clip, "Cam", typeof(CinemachineBasicMultiChannelPerlin), "AmplitudeGain", Lin(keys.ToArray()));
                }
            }
            return (s, animator, vcam, SaveClip(clip));
        }

        internal static List<float> Flicker(float from, float to, float step, float min, float max, int seed)
        {
            var rng = new System.Random(seed);
            var keys = new List<float>();
            for (float t = from; t < to; t += step) keys.AddRange(new[] { t, Mathf.Lerp(min, max, (float)rng.NextDouble()) });
            return keys;
        }

        /// <summary>Maps the values of (time, value) pairs, starting at pair index <paramref name="fromPair"/>.</summary>
        internal static float[] MapValues(float[] tv, Func<float, float> f, int fromPair)
        {
            var mapped = new List<float>();
            for (int i = fromPair * 2; i < tv.Length; i += 2) mapped.AddRange(new[] { tv[i], f(tv[i + 1]) });
            return mapped.ToArray();
        }

        internal static float[] Concat(params float[][] parts)
        {
            var all = new List<float>();
            foreach (var p in parts) all.AddRange(p);
            return all.ToArray();
        }

        // Animating one position axis makes the Animator own the whole vector, so the other two are pinned to the beam line.
        internal static void BindPosition(AnimationClip clip, string path, AnimationCurve x, Vector3 line, float end)
        {
            Bind(clip, path, typeof(Transform), "m_LocalPosition.x", x);
            Bind(clip, path, typeof(Transform), "m_LocalPosition.y", Lin(0f, line.y, end, line.y));
            Bind(clip, path, typeof(Transform), "m_LocalPosition.z", Lin(0f, line.z, end, line.z));
        }

        internal static void BindUniformScale(AnimationClip clip, string path, AnimationCurve s)
        {
            Bind(clip, path, typeof(Transform), "m_LocalScale.x", s);
            Bind(clip, path, typeof(Transform), "m_LocalScale.y", s);
            Bind(clip, path, typeof(Transform), "m_LocalScale.z", s);
        }

        /// <summary>One ground hit: the camera drops and springs back on top of the shot's vertical move.</summary>
        static AnimationCurve ThudY(Shot s, float dur)
        {
            float LinY(float t) => Mathf.Lerp(s.From.y, s.To.y, dur <= 0f ? 0f : Mathf.Clamp01(t / dur));
            float lt = Mathf.Clamp(s.Thud - s.Start, 0.05f, dur - 0.05f);
            return Lin(0f, s.From.y, lt - 0.02f, LinY(lt - 0.02f), lt + 0.05f, LinY(lt + 0.05f) - 0.02f, lt + 0.16f, LinY(lt + 0.16f) + 0.006f, lt + 0.55f, LinY(lt + 0.55f), dur, s.To.y);
        }

        /// <summary>Accelerating curve from <paramref name="a"/> to <paramref name="b"/> over <paramref name="dur"/> (flat start, steep finish).</summary>
        internal static AnimationCurve EaseIn(float dur, float a, float b) =>
            new AnimationCurve(new Keyframe(0f, a, 0f, 0f), new Keyframe(dur, b, 2.5f * (b - a) / dur, 0f));

        /// <summary>Letterbox bars slam shut over 0.12s from <paramref name="close"/>, cutting the frame to black.</summary>
        internal static void BindShutter(AnimationClip clip, float close)
        {
            Bind(clip, "LetterboxTop", typeof(RectTransform), "m_AnchorMin.y", Lin(0f, 0.88f, close, 0.88f, close + 0.12f, 0.5f));
            Bind(clip, "LetterboxBottom", typeof(RectTransform), "m_AnchorMax.y", Lin(0f, 0.12f, close, 0.12f, close + 0.12f, 0.5f));
        }

        internal static AnimationCurve Lin(params float[] tv) => Curve(tv, AnimationUtility.TangentMode.Linear);
        internal static AnimationCurve Smooth(params float[] tv) => Curve(tv, AnimationUtility.TangentMode.ClampedAuto);

        internal static AnimationCurve Curve(float[] tv, AnimationUtility.TangentMode mode)
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

        internal static void Bind(AnimationClip clip, string path, Type type, string property, AnimationCurve curve) =>
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, type, property), curve);

        internal static AnimationClip SaveClip(AnimationClip clip)
        {
            var path = $"{AssetRoot}/Anim/{clip.name}.anim";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(clip, path);
            return clip;
        }

        // ---------- Timeline helpers ----------

        internal static void Activation(TimelineAsset timeline, TrackAsset parent, PlayableDirector director, string name, GameObject target, float start, float end)
        {
            var track = timeline.CreateTrack<ActivationTrack>(parent, name);
            track.postPlaybackState = ActivationTrack.PostPlaybackState.LeaveAsIs;
            director.SetGenericBinding(track, target);
            var c = track.CreateDefaultClip();
            c.start = start;
            c.duration = end - start;
        }

        internal static void Control(ControlTrack track, PlayableDirector director, GameObject target, float start, float end)
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

        internal static AnimationClip FindClip(string fbx, string prefix)
        {
            foreach (var a in AssetDatabase.LoadAllAssetsAtPath(fbx))
                if (a is AnimationClip c && c.name.StartsWith(prefix)) return c;
            throw new Exception($"Clip '{prefix}...' not found in {fbx}");
        }

        internal static Transform FindBone(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            throw new Exception($"Bone {name} not found under {root.name}");
        }

        internal static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }

        /// <summary>
        /// Grading shared by the boss cutscenes: Base (always on), Curse (grey, weight 1) and Impact (hit punch, weight 0),
        /// under <paramref name="parent"/> so an FX animator can drive their weights by "Volumes/&lt;name&gt;".
        /// </summary>
        internal static Transform StandardVolumes(Transform parent)
        {
            var volumes = new GameObject("Volumes").transform;
            volumes.SetParent(parent);
            Volume(volumes, "Base", 0, 1f, Profile("CutsceneBase", CutsceneBaseGrade));
            Volume(volumes, "Curse", 1, 1f, Profile("CutsceneCurse", CutsceneCurseGrade));
            Volume(volumes, "Impact", 3, 0f, Profile("CutsceneImpact", p =>
            {
                var b = p.Add<Bloom>(); b.intensity.Override(5f); b.threshold.Override(0.6f);
                p.Add<ChromaticAberration>().intensity.Override(0.8f);
                p.Add<LensDistortion>().intensity.Override(-0.25f);
                p.Add<ColorAdjustments>().postExposure.Override(0.5f);
            }));
            return volumes;
        }

        /// <summary>Switches off the MasterStylizedProjectiles explosion layers that fill the screen (white core sphere, giant flare/glow, smoke).</summary>
        internal static void MuteScreenFill(GameObject explosion) =>
            Mute(explosion, "Core", "Flare", "Light", "Light (1)", "ScreenSmoke", "Smoke (1)", "Smoke (2)", "Smoke (3)", "Smoke (4)");

        /// <summary>Thin shockwave ring on the floor at the origin (HCFX_Energy_04 without its big glow), recoloured.</summary>
        internal static GameObject FloorRing(Transform parent, float scale, Color color)
        {
            var ring = Tint(Vfx(parent, "HCFX_Energy_04", new Vector3(0f, 0.02f, 0f), scale, 0.55f), color);
            Mute(ring, "glow_view");
            return ring;
        }

        /// <summary>Key, purple rim and an optional face light under "Lights/", matching the defeat cutscene's look.</summary>
        internal static Transform StandardLights(Transform parent)
        {
            var lights = new GameObject("Lights").transform;
            lights.SetParent(parent);
            var key = Light(lights, "Key", LightType.Directional, new Color(0.72f, 0.72f, 0.9f), 1.15f, 0f);
            key.transform.rotation = Quaternion.Euler(38f, 55f, 0f);
            key.shadows = LightShadows.Soft;
            var rim = Light(lights, "Rim", LightType.Spot, new Color(0.65f, 0.3f, 1f), 7f, 6f);
            rim.transform.position = new Vector3(1.8f, 1.6f, 0.9f);
            rim.transform.LookAt(new Vector3(0f, 0.6f, 0f));
            rim.spotAngle = 55f;
            return lights;
        }

        /// <summary>Spike pairs for a punch curve: base value, a quick rise to <paramref name="peak"/> at each time, decay over <paramref name="decay"/>.</summary>
        internal static float[] Pulses(float baseValue, float peak, float decay, params float[] times)
        {
            var keys = new List<float> { 0f, baseValue };
            foreach (var t in times) keys.AddRange(new[] { t - 0.02f, baseValue, t + 0.04f, peak, t + decay, baseValue });
            return keys.ToArray();
        }

        /// <summary>
        /// Brings a held pose to life: an override track (masked to the upper body) that layers heavy breathing on top of
        /// <paramref name="pose"/> sampled at <paramref name="poseTime"/>, plus a flinch at each time in <paramref name="jolts"/>.
        /// Use it where the base track only crawls through a pose, so the base barely moves under the override.
        /// </summary>
        internal static void AddBreathing(TimelineAsset timeline, AnimationTrack baseTrack, Animator model, AnimationClip pose, float poseTime,
            float start, float end, float breathPeriod, float breathDegrees, float joltDegrees, params float[] jolts)
        {
            var right = model.transform.rotation * Vector3.right;    // pitch axis (bow forward / straighten)
            var forward = model.transform.rotation * Vector3.forward; // roll axis for the shoulder shrug
            // (bone, axis, breath gain, jolt gain): the chest heaves most.
            var rig = new (string bone, Vector3 axis, float breath, float jolt)[]
            {
                ("Spine", right, 0.4f, 0.5f), ("Chest", right, 1f, 0.8f), ("UpperChest", right, 0.8f, 0.6f),
                ("Neck", right, -0.5f, 0.2f), ("Head", right, -0.4f, 0.2f),
                ("Left_Shoulder", forward, 0.9f, 1f), ("Right_Shoulder", forward, -0.9f, -1f),
            };
            AddPoseLayer(timeline, baseTrack, model, pose, poseTime, start, end, "Breathing", 30f,
                rig.Select(r => (r.bone, r.axis)).ToArray(), (i, t) =>
                {
                    float angle = rig[i].breath * breathDegrees * Mathf.Sin(t / breathPeriod * Mathf.PI * 2f);
                    foreach (var j in jolts)
                    {
                        float dt = t - j;
                        if (dt < -0.06f || dt > 0.6f) continue;
                        float k = dt < 0f ? 1f + dt / 0.06f : Mathf.Exp(-dt * 7f); // quick hit, ~0.4s settle
                        angle += rig[i].jolt * joltDegrees * k;
                    }
                    return angle;
                });
        }

        /// <summary>
        /// Straining tremble for a held, effortful pose (e.g. pushing a beam): fast noise on the spine, head and wand arm,
        /// scaled over time by <paramref name="intensity"/> (absolute time -> multiplier of <paramref name="degrees"/>).
        /// </summary>
        internal static void AddTremble(TimelineAsset timeline, AnimationTrack baseTrack, Animator model, AnimationClip pose, float poseTime,
            float start, float end, float degrees, float frequency, AnimationCurve intensity)
        {
            var right = model.transform.rotation * Vector3.right;
            var up = model.transform.rotation * Vector3.up;
            var forward = model.transform.rotation * Vector3.forward;
            var rig = new (string bone, Vector3 axis, float gain)[]
            {
                ("Spine", right, 0.4f), ("Chest", right, 0.6f), ("Chest", up, 0.4f), ("UpperChest", right, 0.6f),
                ("Neck", right, 0.5f), ("Head", right, 0.7f), ("Head", up, 0.5f),
                ("Right_Shoulder", forward, 0.7f), ("Right_UpperArm", right, 1f), ("Right_UpperArm", up, 0.8f),
                ("Right_LowerArm", right, 0.8f), ("Left_UpperArm", right, 0.5f),
            };
            AddPoseLayer(timeline, baseTrack, model, pose, poseTime, start, end, "Tremble", 60f,
                rig.Select(r => (r.bone, r.axis)).ToArray(),
                (i, t) => rig[i].gain * degrees * intensity.Evaluate(t) * (Mathf.PerlinNoise(i * 7.31f, t * frequency) - 0.5f) * 2f);
        }

        /// <summary>
        /// Override layer (avatar-masked to the listed bones) that holds <paramref name="pose"/> and rotates each listed
        /// (bone, world axis) entry by <paramref name="angle"/>(entry index, absolute time) degrees. A bone may appear
        /// more than once to move it about several axes.
        /// </summary>
        internal static void AddPoseLayer(TimelineAsset timeline, AnimationTrack baseTrack, Animator model, AnimationClip pose, float poseTime,
            float start, float end, string name, float sampleRate, (string bone, Vector3 axis)[] entries, Func<int, float, float> angle)
        {
            var root = model.transform;
            bool wasActive = model.gameObject.activeSelf;
            model.gameObject.SetActive(true);
            pose.SampleAnimation(model.gameObject, poseTime);
            var clip = new AnimationClip { name = $"{name}_{model.name}_{start:0.0}" };
            float dur = end - start;
            var paths = new List<string>();
            foreach (var boneName in entries.Select(e => e.bone).Distinct())
            {
                var bone = FindBone(root, boneName);
                var path = AnimationUtility.CalculateTransformPath(bone, root);
                paths.Add(path);
                var baseLocal = bone.localRotation;
                var mine = Enumerable.Range(0, entries.Length).Where(i => entries[i].bone == boneName)
                    .Select(i => (index: i, localAxis: Quaternion.Inverse(bone.rotation) * entries[i].axis)).ToArray();
                var curves = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
                for (float t = 0f; t <= dur + 1e-4f; t += 1f / sampleRate)
                {
                    var q = baseLocal;
                    foreach (var (index, localAxis) in mine) q *= Quaternion.AngleAxis(angle(index, start + t), localAxis);
                    curves[0].AddKey(t, q.x); curves[1].AddKey(t, q.y); curves[2].AddKey(t, q.z); curves[3].AddKey(t, q.w);
                }
                string[] comps = { "x", "y", "z", "w" };
                for (int c = 0; c < 4; c++) Bind(clip, path, typeof(Transform), "m_LocalRotation." + comps[c], curves[c]);
            }
            model.gameObject.SetActive(wasActive);

            // One mask per layer: several layers in one cutscene must not overwrite each other's mask asset.
            var mask = new AvatarMask { name = $"UpperBody{name}_{model.name}_{start:0.0}" };
            mask.AddTransformPath(root, true);
            for (int i = 0; i < mask.transformCount; i++) mask.SetTransformActive(i, paths.Contains(mask.GetTransformPath(i)));
            var maskPath = $"{AssetRoot}/Anim/{mask.name}.mask";
            AssetDatabase.DeleteAsset(maskPath);
            AssetDatabase.CreateAsset(mask, maskPath);

            // An AnimationTrack parented to an AnimationTrack is an override layer on the same binding.
            var layer = timeline.CreateTrack<AnimationTrack>(baseTrack, name);
            layer.avatarMask = mask;
            layer.applyAvatarMask = true;
            var c2 = layer.CreateClip(SaveClip(clip));
            c2.start = start; c2.duration = dur;
            c2.easeInDuration = Mathf.Min(0.35f, dur * 0.3f);
            c2.easeOutDuration = Mathf.Min(0.35f, dur * 0.3f);
        }

        // ---------- Timeline assembly ----------

        internal static (TimelineAsset, PlayableDirector) CreateTimeline(string path)
        {
            AssetDatabase.DeleteAsset(path);
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            AssetDatabase.CreateAsset(timeline, path);
            var director = new GameObject("Cutscene Director").AddComponent<PlayableDirector>();
            director.playableAsset = timeline;
            director.playOnAwake = true;
            director.extrapolationMode = DirectorWrapMode.Hold;
            return (timeline, director);
        }

        /// <summary>One Cinemachine shot clip plus a move track per shot camera.</summary>
        internal static void AddShots(TimelineAsset timeline, PlayableDirector director, Camera cam, IEnumerable<Shot> shots)
        {
            var camRoot = new GameObject("Cameras").transform;
            var camGroup = timeline.CreateTrack<GroupTrack>(null, "Cameras");
            var cmTrack = timeline.CreateTrack<CinemachineTrack>(camGroup, "Cinemachine");
            director.SetGenericBinding(cmTrack, cam.GetComponent<CinemachineBrain>());
            foreach (var s in shots)
            {
                var (_, rig, vcam, moveClip) = BuildShotCamera(camRoot, s);
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
        }

        /// <summary>One control track per VFX object; repeated entries become several clips on that track.</summary>
        internal static void AddControlled(TimelineAsset timeline, TrackAsset group, PlayableDirector director, IEnumerable<(GameObject, float, float)> controlled)
        {
            var tracks = new Dictionary<GameObject, ControlTrack>();
            foreach (var (go, start, end) in controlled)
            {
                if (!tracks.TryGetValue(go, out var track))
                    tracks[go] = track = timeline.CreateTrack<ControlTrack>(group, go.name);
                Control(track, director, go, start, end);
            }
        }

        internal static AnimationTrack AddAnimation(TimelineAsset timeline, TrackAsset group, PlayableDirector director, string name, Animator target, AnimationClip clip, float duration)
        {
            var track = timeline.CreateTrack<AnimationTrack>(group, name);
            director.SetGenericBinding(track, target);
            var c = track.CreateClip(clip);
            c.start = 0; c.duration = duration;
            return track;
        }

        /// <summary>Clip on <paramref name="track"/> from <paramref name="start"/>, beginning <paramref name="clipIn"/> into the source.</summary>
        internal static TimelineClip Place(AnimationTrack track, AnimationClip clip, float start, float duration, float clipIn = 0f, float speed = 1f, bool loop = false)
        {
            var c = track.CreateClip(clip);
            c.start = start; c.duration = duration; c.clipIn = clipIn; c.timeScale = speed;
            ((AnimationPlayableAsset)c.asset).loop = loop ? AnimationPlayableAsset.LoopMode.On : AnimationPlayableAsset.LoopMode.Off;
            return c;
        }
    }
}
