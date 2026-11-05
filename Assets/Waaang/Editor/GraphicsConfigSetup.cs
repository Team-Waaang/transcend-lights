using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Waaang.EditorTools
{
    /// <summary>
    /// Builds what the High graphics tier switches to: a "PC High" quality level with its own URP asset and renderer
    /// (copies of the PC ones with higher settings), plus the GraphicsConfig asset. The PC level, its assets and the
    /// per-platform defaults are left untouched, so the Default tier renders exactly as before.
    /// Safe to re-run: existing High assets are kept (same GUIDs) and their settings re-applied.
    /// </summary>
    public static class GraphicsConfigSetup
    {
        const string PcAsset = "Assets/Settings/PC_RPAsset.asset";
        const string PcRenderer = "Assets/Settings/PC_Renderer.asset";
        const string HighAsset = "Assets/Settings/PC_High_RPAsset.asset";
        const string HighRenderer = "Assets/Settings/PC_High_Renderer.asset";
        const string ConfigPath = "Assets/Waaang/Resources/GraphicsConfig.asset";

        [MenuItem("Waaang/Graphics/Set Up High Quality")]
        public static void SetUp()
        {
            var renderer = CopyOnce(PcRenderer, HighRenderer);
            var asset = CopyOnce(PcAsset, HighAsset);
            ConfigureRenderer(renderer);
            ConfigureAsset(asset, renderer);
            AddQualityLevel(asset);
            var config = EnsureConfig();
            AssetDatabase.SaveAssets();
            Selection.activeObject = config;
            Debug.Log("[GraphicsConfig] High tier ready: quality level '" + GraphicsConfig.HighQualityLevelName + "' -> " + HighAsset + ". Set the tier on " + ConfigPath + ".");
        }

        [MenuItem("Waaang/Graphics/Select Graphics Config")]
        static void Select()
        {
            Selection.activeObject = EnsureConfig();
            EditorGUIUtility.PingObject(Selection.activeObject);
        }

        static Object CopyOnce(string from, string to)
        {
            if (AssetDatabase.LoadMainAssetAtPath(to) == null && !AssetDatabase.CopyAsset(from, to))
                throw new System.Exception("Could not copy " + from + " to " + to);
            return AssetDatabase.LoadMainAssetAtPath(to);
        }

        static void ConfigureRenderer(Object renderer)
        {
            foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(renderer)))
            {
                if (sub is not ScreenSpaceAmbientOcclusion ssao)
                    continue;
                var so = new SerializedObject(ssao);
                // Full-resolution, high sample count, high-quality blur, normals from the depth-normals prepass.
                so.FindProperty("m_Settings.Downsample").boolValue = false;
                so.FindProperty("m_Settings.Source").enumValueIndex = 1;
                so.FindProperty("m_Settings.NormalSamples").enumValueIndex = 2;
                so.FindProperty("m_Settings.Samples").enumValueIndex = 0;
                so.FindProperty("m_Settings.BlurQuality").enumValueIndex = 0;
                so.FindProperty("m_Settings.Intensity").floatValue = 0.5f;
                so.FindProperty("m_Settings.Radius").floatValue = 0.35f;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            EditorUtility.SetDirty(renderer);
        }

        static void ConfigureAsset(Object asset, Object renderer)
        {
            var so = new SerializedObject(asset);
            var list = so.FindProperty("m_RendererDataList");
            list.arraySize = 1;
            list.GetArrayElementAtIndex(0).objectReferenceValue = renderer;
            so.FindProperty("m_DefaultRendererIndex").intValue = 0;

            // Anti-aliasing and colour: 4x MSAA, 64-bit HDR buffer (less banding in glows), HDR colour grading.
            so.FindProperty("m_MSAA").intValue = 4;
            so.FindProperty("m_SupportsHDR").boolValue = true;
            so.FindProperty("m_HDRColorBufferPrecision").intValue = 1;
            so.FindProperty("m_ColorGradingMode").intValue = 1;
            so.FindProperty("m_ColorGradingLutSize").intValue = 64;
            so.FindProperty("m_OpaqueDownsampling").intValue = 0;
            so.FindProperty("m_RenderScale").floatValue = 1f;

            // Shadows: 4096 maps, 4 cascades, high soft-shadow filtering, larger per-tier sizes for local lights.
            so.FindProperty("m_MainLightShadowsSupported").boolValue = true;
            so.FindProperty("m_MainLightShadowmapResolution").intValue = 4096;
            so.FindProperty("m_AdditionalLightShadowsSupported").boolValue = true;
            so.FindProperty("m_AdditionalLightsShadowmapResolution").intValue = 4096;
            so.FindProperty("m_AdditionalLightsShadowResolutionTierLow").intValue = 512;
            so.FindProperty("m_AdditionalLightsShadowResolutionTierMedium").intValue = 1024;
            so.FindProperty("m_AdditionalLightsShadowResolutionTierHigh").intValue = 2048;
            so.FindProperty("m_ShadowCascadeCount").intValue = 4;
            so.FindProperty("m_SoftShadowsSupported").boolValue = true;
            so.FindProperty("m_SoftShadowQuality").intValue = 3;

            // Lighting: more per-pixel local lights per object, larger cookie atlas.
            so.FindProperty("m_AdditionalLightsRenderingMode").intValue = 1;
            so.FindProperty("m_AdditionalLightsPerObjectLimit").intValue = 8;
            so.FindProperty("m_AdditionalLightsCookieResolution").intValue = 4096;
            so.FindProperty("m_ReflectionProbeBlending").boolValue = true;
            so.FindProperty("m_ReflectionProbeBoxProjection").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
        }

        static void AddQualityLevel(Object asset)
        {
            var settings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset")[0];
            var so = new SerializedObject(settings);
            var levels = so.FindProperty("m_QualitySettings");
            int pc = -1, high = -1;
            for (int i = 0; i < levels.arraySize; i++)
            {
                var name = levels.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue;
                if (name == "PC") pc = i;
                if (name == GraphicsConfig.HighQualityLevelName) high = i;
            }
            if (high < 0)
            {
                if (pc < 0)
                    throw new System.Exception("No 'PC' quality level to copy.");
                // Duplicate PC at the end so existing level indices (and per-platform defaults) do not shift.
                levels.InsertArrayElementAtIndex(pc);
                levels.MoveArrayElement(pc + 1, levels.arraySize - 1);
                high = levels.arraySize - 1;
                levels.GetArrayElementAtIndex(high).FindPropertyRelative("name").stringValue = GraphicsConfig.HighQualityLevelName;
            }
            var level = levels.GetArrayElementAtIndex(high);
            level.FindPropertyRelative("customRenderPipeline").objectReferenceValue = asset;
            level.FindPropertyRelative("anisotropicTextures").intValue = 2;
            level.FindPropertyRelative("skinWeights").intValue = 4;
            level.FindPropertyRelative("lodBias").floatValue = 2f;
            level.FindPropertyRelative("realtimeReflectionProbes").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static GraphicsConfig EnsureConfig()
        {
            var config = AssetDatabase.LoadAssetAtPath<GraphicsConfig>(ConfigPath);
            if (config != null)
                return config;
            config = ScriptableObject.CreateInstance<GraphicsConfig>();
            config.tier = GraphicsTier.Default;
            AssetDatabase.CreateAsset(config, ConfigPath);
            return config;
        }
    }
}
