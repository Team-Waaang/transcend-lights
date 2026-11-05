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
            EnsureVoiceBank();
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

        const string VoiceBankPath = "Assets/Game/Resources/VoiceBank.asset";

        /// <summary>Which file in Assets/Voice each line plays.</summary>
        static readonly (VoiceLine line, string file)[] VoiceFiles =
        {
            (VoiceLine.AmeliaCalm, "amelia/아멜리아 마력 안정.mp3"),
            (VoiceLine.AmeliaRuneOk, "amelia/아멜리아 룬 제작 성공.mp3"),
            (VoiceLine.AmeliaRuneFail1, "amelia/아멜리아 룬 제작 실패.mp3"),
            (VoiceLine.AmeliaRuneFail2, "amelia/아멜리아 룬 제작 실패2.mp3"),
            (VoiceLine.AmeliaHurt, "amelia/아멜리아 피격.mp3"),
            (VoiceLine.AmeliaHurtShort, "amelia/아멜리아 피격 short.mp3"),
            (VoiceLine.AmeliaPeriod2, "amelia/아멜리아 2교시 시작 대사.mp3"),
            (VoiceLine.AmeliaSearch, "amelia/아멜리아 색적.mp3"),
            (VoiceLine.AmeliaSkill, "amelia/아멜리아 스킬.mp3"),
            (VoiceLine.AmeliaCombo, "amelia/아멜리아 적 연속 처치.mp3"),
            (VoiceLine.AmeliaBossDown, "amelia/아멜리아 보스 처치.mp3"),
            (VoiceLine.NoxRune, "nox/카엘라 룬 제작.mp3"),
            (VoiceLine.NoxRuneOk, "nox/카엘라 룬 제작 성공.mp3"),
            (VoiceLine.NoxRuneFail, "nox/카엘라 룬 실패.mp3"),
            (VoiceLine.NoxPeriod2, "nox/카엘라 2교시 진입.mp3"),
            (VoiceLine.NoxSearch, "nox/카엘라 색적.mp3"),
            (VoiceLine.NoxHurt, "nox/카엘라 피격.mp3"),
            (VoiceLine.NoxCombo, "nox/카엘라 연속처치.mp3"),
            (VoiceLine.NoxBoost1, "nox/카엘라 1교시 강화.mp3"),
            (VoiceLine.NoxBoost2, "nox/카엘라 2교시 강화.mp3"),
            (VoiceLine.NoxPreUltimate, "nox/카엘라 궁극기 전 대사.mp3"),
            (VoiceLine.UltimateBeam, "nox/어르또메이또비이이이임.mp3"),
            (VoiceLine.BossDefeat, "boss/빌런 패배.mp3"),
            (VoiceLine.BossSkill, "boss/빌런 킄킄킄.mp3"),
            (VoiceLine.BossShield, "boss/빌런 킄.mp3"),
            (VoiceLine.BossPhase2, "boss/빌런 2페이즈각성.mp3"),
        };

        /// <summary>Creates or refreshes Resources/VoiceBank from Assets/Voice (keeps hand-tuned volumes).</summary>
        [MenuItem("Tools/Periods/Build Voice Bank")]
        public static string EnsureVoiceBank()
        {
            var bank = AssetDatabase.LoadAssetAtPath<VoiceBank>(VoiceBankPath);
            if (bank == null)
            {
                Cutscene.Editor.CutsceneKit.EnsureFolder("Assets/Game/Resources");
                bank = ScriptableObject.CreateInstance<VoiceBank>();
                AssetDatabase.CreateAsset(bank, VoiceBankPath);
            }
            var old = bank.entries ?? new VoiceBank.Entry[0];
            var entries = new List<VoiceBank.Entry>();
            var missing = new List<string>();
            foreach (var (line, file) in VoiceFiles)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Voice/" + file);
                if (clip == null) missing.Add(file);
                var prev = old.FirstOrDefault(e => e.line == line);
                entries.Add(new VoiceBank.Entry { line = line, clip = clip, volume = prev != null ? prev.volume : 1f });
            }
            bank.entries = entries.ToArray();
            EditorUtility.SetDirty(bank);
            AssetDatabase.SaveAssets();
            var result = $"VoiceBank: {entries.Count - missing.Count}/{entries.Count} clips" + (missing.Count > 0 ? "; missing " + string.Join(", ", missing) : "");
            if (missing.Count > 0) Debug.LogWarning("[Periods] " + result); else Debug.Log("[Periods] " + result);
            return result;
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
