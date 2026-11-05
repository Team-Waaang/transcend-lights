using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Waaang
{
    public enum GraphicsTier
    {
        /// <summary>The project as authored: the platform's default quality level, nothing touched.</summary>
        Default,
        /// <summary>Denser/softer shadows, more lights, MSAA, HDR grading, high-quality bloom and a softer toon look.</summary>
        High,
    }

    /// <summary>
    /// Global graphics setting, edited in the inspector on <c>Assets/Waaang/Resources/GraphicsConfig.asset</c>.
    /// Default leaves everything as authored. High switches to the "PC High" quality level (its own URP asset and
    /// renderer, made by <c>Waaang/Graphics/Set Up High Quality</c>) and, on every scene load, upgrades lights,
    /// cameras and bloom and softens lilToon materials. Changing the tier in Play mode re-applies it live.
    /// </summary>
    [CreateAssetMenu(menuName = "Waaang/Graphics Config", fileName = "GraphicsConfig")]
    public class GraphicsConfig : ScriptableObject
    {
        public const string ResourcePath = "GraphicsConfig";
        public const string HighQualityLevelName = "PC High";

        [Tooltip("Default = the project as it is now. High = finer shadows and lighting, lighter toon shading.")]
        public GraphicsTier tier = GraphicsTier.Default;

        [Header("High: lights")]
        [Tooltip("Lights that already cast shadows switch to soft shadows at the high resolution tier.")]
        public bool upgradeShadowedLights = true;
        [Tooltip("This many of the strongest point/spot lights that are lit at scene load and cast no shadow start casting soft shadows. Costly with moving lights; 0 keeps the authored set.")]
        [Range(0, 4)] public int extraShadowedLights = 0;

        [Header("High: camera and post")]
        [Tooltip("Dither the final image to hide banding in dark gradients and bloom.")]
        public bool cameraDithering = true;
        [Tooltip("Bloom with high-quality (bicubic) upsampling: smoother, less blocky glows.")]
        public bool highQualityBloom = true;

        [Header("High: softer toon (lilToon)")]
        [Tooltip("Minimum blur of the light/shadow terminator. Authored materials use ~0.035 (hard cel edge).")]
        [Range(0f, 1f)] public float toonShadowBlur = 0.22f;
        [Tooltip("Multiplier on the toon shadow strength.")]
        [Range(0f, 1f)] public float toonShadowStrength = 0.8f;
        [Tooltip("How much cast shadows (from other objects) darken toon materials. Authored materials use 0.")]
        [Range(0f, 1f)] public float toonReceiveShadows = 0.85f;
        [Tooltip("Multiplier on the outline width.")]
        [Range(0f, 1f)] public float toonOutlineScale = 0.7f;

        static GraphicsConfig current;
        static int defaultQualityLevel = -1;

        // lilToon originals -> softened clones, and back.
        static readonly Dictionary<Material, Material> cloneOf = new();
        static readonly Dictionary<Material, Material> originalOf = new();
        // Runtime-created toon materials (ToonLook) softened in place, with a copy of their authored values.
        static readonly Dictionary<Material, Material> runtimeSnapshot = new();
        static readonly Dictionary<Light, LightShadows> lights = new();
        static readonly Dictionary<UniversalAdditionalCameraData, bool> cameras = new();
        static readonly List<GameObject> volumes = new();
        static VolumeProfile bloomProfile;

        static readonly int ShadowBlur = Shader.PropertyToID("_ShadowBlur");
        static readonly int Shadow2ndBlur = Shader.PropertyToID("_Shadow2ndBlur");
        static readonly int ShadowStrength = Shader.PropertyToID("_ShadowStrength");
        static readonly int ShadowReceive = Shader.PropertyToID("_ShadowReceive");
        static readonly int Shadow2ndReceive = Shader.PropertyToID("_Shadow2ndReceive");
        static readonly int OutlineWidth = Shader.PropertyToID("_OutlineWidth");

        /// <summary>The config asset, or null if it has not been created.</summary>
        public static GraphicsConfig Current
        {
            get
            {
                if (current == null)
                    current = Resources.Load<GraphicsConfig>(ResourcePath);
                return current;
            }
        }

        public static bool High => Current != null && Current.tier == GraphicsTier.High;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            current = null;
            defaultQualityLevel = -1;
            cloneOf.Clear();
            originalOf.Clear();
            runtimeSnapshot.Clear();
            lights.Clear();
            cameras.Clear();
            volumes.Clear();
            bloomProfile = null;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            defaultQualityLevel = QualitySettings.GetQualityLevel();
            ApplyQualityLevel();
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (High)
                ApplyScene(scene);
        }

        void OnValidate()
        {
            if (!Application.isPlaying || this != Current || defaultQualityLevel < 0)
                return;
            ApplyQualityLevel();
            RestoreAll();
            if (tier == GraphicsTier.High)
            {
                for (int i = 0; i < SceneManager.sceneCount; i++)
                    ApplyScene(SceneManager.GetSceneAt(i));
                foreach (var m in new List<Material>(runtimeSnapshot.Keys))
                    if (m != null) Soften(m);
            }
        }

        static void ApplyQualityLevel()
        {
            int target = defaultQualityLevel;
            if (High && !Application.isMobilePlatform)
            {
                int high = System.Array.IndexOf(QualitySettings.names, HighQualityLevelName);
                if (high >= 0)
                    target = high;
                else
                    Debug.LogWarning("[GraphicsConfig] Quality level '" + HighQualityLevelName + "' is not available on this platform (excluded, or not set up: Waaang/Graphics/Set Up High Quality). Scene upgrades still apply.");
            }
            if (target >= 0 && QualitySettings.GetQualityLevel() != target)
            {
                QualitySettings.SetQualityLevel(target, true);
                // A level excluded for the current platform (e.g. the editor on the Android build target) is refused.
                if (QualitySettings.GetQualityLevel() != target)
                    Debug.LogWarning("[GraphicsConfig] Quality level '" + QualitySettings.names[target] + "' is excluded for this platform; using '" + QualitySettings.names[QualitySettings.GetQualityLevel()] + "'. Scene upgrades still apply.");
            }
        }

        // ---------- Per scene ----------

        static void ApplyScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return;
            var config = Current;
            var roots = scene.GetRootGameObjects();
            var sceneLights = new List<Light>();
            for (int i = 0; i < roots.Length; i++)
            {
                SoftenRenderers(roots[i]);
                sceneLights.AddRange(roots[i].GetComponentsInChildren<Light>(true));
                if (config.cameraDithering)
                {
                    foreach (var data in roots[i].GetComponentsInChildren<UniversalAdditionalCameraData>(true))
                    {
                        if (!cameras.ContainsKey(data))
                            cameras[data] = data.dithering;
                        data.dithering = true;
                    }
                }
            }
            UpgradeLights(sceneLights, config);
            if (config.highQualityBloom)
                AddBloomVolume(scene);
        }

        static void SoftenRenderers(GameObject root)
        {
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is ParticleSystemRenderer || renderer is LineRenderer || renderer is TrailRenderer)
                    continue;
                var mats = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (!IsToon(m) || originalOf.ContainsKey(m) || runtimeSnapshot.ContainsKey(m))
                        continue;
                    if (!cloneOf.TryGetValue(m, out var clone) || clone == null)
                    {
                        clone = new Material(m) { name = m.name + " (High)" };
                        Soften(clone);
                        cloneOf[m] = clone;
                        originalOf[clone] = m;
                    }
                    mats[i] = clone;
                    changed = true;
                }
                if (changed)
                    renderer.sharedMaterials = mats;
            }
        }

        static void UpgradeLights(List<Light> sceneLights, GraphicsConfig config)
        {
            var candidates = new List<Light>();
            foreach (var light in sceneLights)
            {
                if (light == null || light.type == LightType.Rectangle || light.type == LightType.Disc)
                    continue;
                if (light.shadows != LightShadows.None)
                {
                    if (config.upgradeShadowedLights)
                        SetShadows(light);
                }
                else if ((light.type == LightType.Point || light.type == LightType.Spot) && light.isActiveAndEnabled && light.intensity > 0f)
                {
                    candidates.Add(light);
                }
            }
            candidates.Sort((a, b) => (b.intensity * b.range).CompareTo(a.intensity * a.range));
            for (int i = 0; i < Mathf.Min(config.extraShadowedLights, candidates.Count); i++)
                SetShadows(candidates[i]);
        }

        // Shadow map sizes come from the "PC High" URP asset (4096 atlases, larger per-tier resolutions).
        static void SetShadows(Light light)
        {
            if (!lights.ContainsKey(light))
                lights[light] = light.shadows;
            light.shadows = LightShadows.Soft;
        }

        static void AddBloomVolume(Scene scene)
        {
            if (bloomProfile == null)
            {
                bloomProfile = ScriptableObject.CreateInstance<VolumeProfile>();
                bloomProfile.name = "Graphics High";
                // Only the filtering is overridden; intensity, threshold and tint still come from the scene's volumes.
                var bloom = bloomProfile.Add<Bloom>(false);
                bloom.highQualityFiltering.Override(true);
            }
            var go = new GameObject("Graphics High Volume");
            SceneManager.MoveGameObjectToScene(go, scene);
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 1000f;
            volume.weight = 1f;
            volume.sharedProfile = bloomProfile;
            volumes.Add(go);
        }

        // ---------- Toon ----------

        static bool IsToon(Material m)
        {
            return m != null && m.shader != null && m.shader.name.StartsWith("lilToon") && m.HasProperty(ShadowBlur);
        }

        static void Soften(Material m)
        {
            var c = Current;
            if (m.HasProperty(ShadowBlur)) m.SetFloat(ShadowBlur, Mathf.Max(m.GetFloat(ShadowBlur), c.toonShadowBlur));
            if (m.HasProperty(Shadow2ndBlur)) m.SetFloat(Shadow2ndBlur, Mathf.Max(m.GetFloat(Shadow2ndBlur), c.toonShadowBlur * 1.5f));
            if (m.HasProperty(ShadowStrength)) m.SetFloat(ShadowStrength, m.GetFloat(ShadowStrength) * c.toonShadowStrength);
            if (m.HasProperty(ShadowReceive)) m.SetFloat(ShadowReceive, Mathf.Max(m.GetFloat(ShadowReceive), c.toonReceiveShadows));
            if (m.HasProperty(Shadow2ndReceive)) m.SetFloat(Shadow2ndReceive, Mathf.Max(m.GetFloat(Shadow2ndReceive), c.toonReceiveShadows));
            if (m.HasProperty(OutlineWidth)) m.SetFloat(OutlineWidth, m.GetFloat(OutlineWidth) * c.toonOutlineScale);
        }

        /// <summary>Called by <see cref="ToonLook"/> for each lilToon material it creates at runtime.</summary>
        public static void OnToonMaterialCreated(Material material)
        {
            if (material == null || runtimeSnapshot.ContainsKey(material))
                return;
            runtimeSnapshot[material] = new Material(material);
            if (High)
                Soften(material);
        }

        // ---------- Back to Default (live toggle in Play mode) ----------

        static void RestoreAll()
        {
            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                var scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                    {
                        var mats = renderer.sharedMaterials;
                        bool changed = false;
                        for (int i = 0; i < mats.Length; i++)
                        {
                            if (mats[i] != null && originalOf.TryGetValue(mats[i], out var original))
                            {
                                mats[i] = original;
                                changed = true;
                            }
                        }
                        if (changed) renderer.sharedMaterials = mats;
                    }
                }
            }
            foreach (var clone in originalOf.Keys)
                if (clone != null) Destroy(clone);
            cloneOf.Clear();
            originalOf.Clear();

            foreach (var pair in runtimeSnapshot)
                if (pair.Key != null && pair.Value != null) pair.Key.CopyPropertiesFromMaterial(pair.Value);

            foreach (var pair in lights)
                if (pair.Key != null) pair.Key.shadows = pair.Value;
            lights.Clear();

            foreach (var pair in cameras)
                if (pair.Key != null) pair.Key.dithering = pair.Value;
            cameras.Clear();

            foreach (var go in volumes)
                if (go != null) Destroy(go);
            volumes.Clear();
        }
    }
}
