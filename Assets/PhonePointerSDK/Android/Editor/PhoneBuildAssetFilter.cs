using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace PhonePointer.Editor
{
    // Resources folders are copied into every player. These files are used by the PC game or the
    // slab test scene, not by the phone controller, but they were inflating the phone APK.
    public sealed class PhoneBuildAssetFilter : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        public int callbackOrder => 0;

        const string HoldingFolder = "Assets/PhoneBuildExcluded";
        static string Sentinel => Path.Combine(Path.GetDirectoryName(Application.dataPath), "Library", "PhoneBuildExcluded.txt");
        static readonly string[] UnusedInPhoneBuild =
        {
            "Assets/Game/Resources/Audio",
            "Assets/Waaang/Resources/RunicRegistrationStage.prefab",
            "Assets/PhonePointerSDK/Resources/RuneSlabFont.ttf",
            "Assets/PhonePointerSDK/Resources/RuneSlabFont-OFL.txt",
            "Assets/PhonePointerSDK/Resources/ScreenMarker.png",
        };

        const string SessionKey = "PhonePointer.PhoneBuildExcluded";

        [InitializeOnLoadMethod]
        static void RestoreIfInterrupted()
        {
            if (BuildPipeline.isBuildingPlayer || SessionState.GetBool(SessionKey, false)) return;
            Restore();
        }

        public void OnPreprocessBuild(BuildReport report)
        {
            if (!IsPhoneApk(report)) return;
            SessionState.SetBool(SessionKey, true);
            if (!AssetDatabase.IsValidFolder(HoldingFolder))
                AssetDatabase.CreateFolder("Assets", "PhoneBuildExcluded");
            var moved = new List<string>();
            try
            {
                foreach (var source in UnusedInPhoneBuild)
                {
                    if (string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(source))) continue;
                    var destination = HoldingFolder + "/" + Path.GetFileName(source);
                    var error = AssetDatabase.MoveAsset(source, destination);
                    if (!string.IsNullOrEmpty(error))
                        throw new BuildFailedException("Could not exclude " + source + ": " + error);
                    moved.Add(source + "|" + destination);
                    File.WriteAllLines(Sentinel, moved);
                }
            }
            catch
            {
                Restore();
                throw;
            }
            if (moved.Count > 0)
                Debug.Log("Phone build excluded " + moved.Count + " unused Resources assets.");
        }

        public void OnPostprocessBuild(BuildReport report) => Restore();

        static bool IsPhoneApk(BuildReport report)
        {
            var path = report.summary.outputPath ?? "";
            return report.summary.platform == BuildTarget.Android
                && path.EndsWith("WaaangPhone.apk", StringComparison.OrdinalIgnoreCase);
        }

        static void Restore()
        {
            if (!File.Exists(Sentinel)) return;
            foreach (var line in File.ReadAllLines(Sentinel))
            {
                var split = line.IndexOf('|');
                if (split <= 0) continue;
                var source = line.Substring(0, split);
                var destination = line.Substring(split + 1);
                if (string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(destination))) continue;
                var folder = Path.GetDirectoryName(source)?.Replace('\\', '/');
                if (!string.IsNullOrEmpty(folder) && !AssetDatabase.IsValidFolder(folder))
                    Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(Application.dataPath), folder));
                var error = AssetDatabase.MoveAsset(destination, source);
                if (!string.IsNullOrEmpty(error)) Debug.LogError("Could not restore " + source + ": " + error);
            }
            File.Delete(Sentinel);
            if (AssetDatabase.IsValidFolder(HoldingFolder)) AssetDatabase.DeleteAsset(HoldingFolder);
            SessionState.EraseBool(SessionKey);
        }
    }
}
