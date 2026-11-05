using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AiHackathon.Game.Editor
{
    /// <summary>
    /// Wires the school day: the HUD font and voice bank assets in Resources, and Build Settings in play order
    /// (Title → Connection → 1교시 → 2교시 → 3교시, then the boss cutscenes and the Game test scene).
    /// </summary>
    public static class PeriodSetup
    {
        const string FontsPath = "Assets/Game/Resources/GameGuiFonts.asset";
        const string VoiceBankPath = "Assets/Game/Resources/VoiceBank.asset";
        static readonly (VoiceLine line, string file)[] VoiceFiles =
        {
            (VoiceLine.AmeliaCalm, "amelia/아멜리아 마력 안정"),
            (VoiceLine.AmeliaRuneSuccess, "amelia/아멜리아 룬 제작 성공"),
            (VoiceLine.AmeliaRuneFail, "amelia/아멜리아 룬 제작 실패"),
            (VoiceLine.AmeliaRuneFail2, "amelia/아멜리아 룬 제작 실패2"),
            (VoiceLine.AmeliaHit, "amelia/아멜리아 피격"),
            (VoiceLine.AmeliaHitShort, "amelia/아멜리아 피격 short"),
            (VoiceLine.AmeliaPeriod2Start, "amelia/아멜리아 2교시 시작 대사"),
            (VoiceLine.AmeliaSearch, "amelia/아멜리아 색적"),
            (VoiceLine.AmeliaSkill, "amelia/아멜리아 스킬"),
            (VoiceLine.AmeliaStreak, "amelia/아멜리아 적 연속 처치"),
            (VoiceLine.AmeliaBossDown, "amelia/아멜리아 보스 처치"),
            (VoiceLine.AmeliaGuard, "amelia/아멜리아 방어"),
            (VoiceLine.AmeliaMiss, "amelia/아멜리아 공격 명중 실패"),
            (VoiceLine.AmeliaQteReady, "amelia/아멜리아 회피 QTE 이전 대사"),
            (VoiceLine.AmeliaQteSuccess, "amelia/아멜리아 QTE 성공 "),
            (VoiceLine.AmeliaQteFail, "amelia/아멜리아 QTE 실패"),
            (VoiceLine.KaelaRuneStart, "nox/카엘라 룬 제작"),
            (VoiceLine.KaelaRuneSuccess, "nox/카엘라 룬 제작 성공"),
            (VoiceLine.KaelaRuneFail, "nox/카엘라 룬 실패"),
            (VoiceLine.KaelaPeriod2Start, "nox/카엘라 2교시 진입"),
            (VoiceLine.KaelaSearch, "nox/카엘라 색적"),
            (VoiceLine.KaelaHit, "nox/카엘라 피격"),
            (VoiceLine.KaelaStreak, "nox/카엘라 연속처치"),
            (VoiceLine.KaelaPowerUp1, "nox/카엘라 1교시 강화"),
            (VoiceLine.KaelaPowerUp2, "nox/카엘라 2교시 강화"),
            (VoiceLine.KaelaBeforeUltimate, "nox/카엘라 궁극기 전 대사"),
            (VoiceLine.KaelaQteStart, "nox/카엘라 QTE 시작"),
            (VoiceLine.KaelaQteSuccess, "nox/카엘라 qte 성공"),
            (VoiceLine.KaelaQteFail, "nox/카엘라 qte실패"),
            (VoiceLine.KaelaVictory, "nox/카엘라 승리"),
            (VoiceLine.Ultimate, "nox/어르또메이또비이이이임"),
            (VoiceLine.VillainDefeat, "boss/빌런 패배"),
            (VoiceLine.VillainSkill, "boss/빌런 킄킄킄"),
            (VoiceLine.VillainShield, "boss/빌런 킄"),
            (VoiceLine.VillainPhase2, "boss/빌런 2페이즈각성"),
            (VoiceLine.VillainPush, "boss/빌런 아자자자자잣"),
        };
        static readonly string[] Cutscenes =
        {
            "Assets/Cutscene/BossEntrance/BossEntranceCutscene.unity",
            "Assets/Cutscene/BossPhase2/BossPhase2Cutscene.unity",
            "Assets/Cutscene/BossDefeatCutscene.unity",
        };
        const string GameScene = "Assets/Scenes/Game.unity";

        [MenuItem("Tools/Periods/Set Up School Day (Fonts + Voices + Build Order)")]
        public static string Run()
        {
            EnsureFonts();
            EnsureVoices();
            var order = new List<string> { PeriodFlow.TitleScene, PeriodFlow.ConnectionScene };
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

        /// <summary>Points the voice bank at the clips in Assets/Voice, one per line.</summary>
        public static string EnsureVoices()
        {
            var bank = AssetDatabase.LoadAssetAtPath<VoiceBank>(VoiceBankPath);
            if (bank == null)
            {
                Cutscene.Editor.CutsceneKit.EnsureFolder("Assets/Game/Resources");
                bank = ScriptableObject.CreateInstance<VoiceBank>();
                AssetDatabase.CreateAsset(bank, VoiceBankPath);
            }
            var entries = new List<VoiceBank.Entry>();
            var missing = new List<string>();
            foreach (var (line, file) in VoiceFiles)
            {
                var path = "Assets/Voice/" + file + ".mp3";
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip == null) { missing.Add(path); continue; }
                entries.Add(new VoiceBank.Entry { line = line, clip = clip });
            }
            bank.entries = entries.ToArray();
            EditorUtility.SetDirty(bank);
            AssetDatabase.SaveAssets();
            foreach (var m in missing) Debug.LogWarning("[Periods] Voice clip not found: " + m);
            return entries.Count + " voice lines" + (missing.Count > 0 ? ", missing: " + string.Join(", ", missing) : "");
        }
    }
}
