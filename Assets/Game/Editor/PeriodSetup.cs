using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AiHackathon.Game.Editor
{
    /// <summary>
    /// Wires the school day: the HUD font asset in Resources, and Build Settings in play order
    /// (Connection → 1교시 → 2교시 → 3교시, then the boss cutscenes and the Game test scene).
    /// </summary>
    public static class PeriodSetup
    {
        const string FontsPath = "Assets/Game/Resources/GameGuiFonts.asset";
        static readonly string[] Cutscenes =
        {
            "Assets/Cutscene/BossEntrance/BossEntranceCutscene.unity",
            "Assets/Cutscene/BossPhase2/BossPhase2Cutscene.unity",
            "Assets/Cutscene/BossDefeatCutscene.unity",
        };
        const string GameScene = "Assets/Scenes/Game.unity";

        [MenuItem("Tools/Periods/Set Up School Day (Fonts + Build Order)")]
        public static string Run()
        {
            EnsureFonts();
            var order = new List<string> { PeriodFlow.ConnectionScene };
            order.AddRange(PeriodFlow.Periods.Select(p => p.scene));
            order.AddRange(Cutscenes);
            order.Add(GameScene);
            var scenes = new List<EditorBuildSettingsScene>();
            foreach (var path in order)
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) != null) scenes.Add(new EditorBuildSettingsScene(path, true));
            // Keep anything else already listed (after ours).
            foreach (var s in EditorBuildSettings.scenes)
                if (!scenes.Any(x => x.path == s.path)) scenes.Add(s);
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();
            var summary = string.Join(" → ", scenes.Select(s => System.IO.Path.GetFileNameWithoutExtension(s.path)));
            Debug.Log("[Periods] Build order: " + summary);
            return summary;
        }

        static void EnsureFonts()
        {
            var fonts = AssetDatabase.LoadAssetAtPath<GameGuiFonts>(FontsPath);
            if (fonts == null)
            {
                Cutscene.Editor.CutsceneKit.EnsureFolder("Assets/Game/Resources");
                fonts = ScriptableObject.CreateInstance<GameGuiFonts>();
                AssetDatabase.CreateAsset(fonts, FontsPath);
            }
            if (fonts.title == null) fonts.title = AssetDatabase.LoadAssetAtPath<Font>(Cutscene.Editor.BossDefeatCutsceneBuilder.TitleFontPath);
            if (fonts.body == null) fonts.body = AssetDatabase.LoadAssetAtPath<Font>(Cutscene.Editor.BossDefeatCutsceneBuilder.BodyFontPath);
            EditorUtility.SetDirty(fonts);
        }
    }
}
