using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Release builds for the README downloads, run in batch mode (one target per run):
///   Unity -batchmode -quit -projectPath . -buildTarget Win64 -executeMethod ReleaseBuilds.Windows
///   Unity -batchmode -quit -projectPath . -buildTarget OSXUniversal -executeMethod ReleaseBuilds.Mac
///   Unity -batchmode -quit -projectPath . -buildTarget Android -executeMethod ReleaseBuilds.PhoneApp
/// The game is every enabled Build Settings scene (title → connection → periods → cutscenes); the phone app is the
/// wand controller scene alone with the ARCore settings. Product names are set for the build only and put back after.
/// Output goes to Builds/ (git-ignored).
/// </summary>
public static class ReleaseBuilds
{
    const string GameName = "Transcend Lights";
    const string PhoneName = "Transcend Lights Wand";
    const string PhoneScene = "Assets/WaaangPhone/Scenes/WaaangPhoneController.unity";

    [MenuItem("Tools/Release/Build Windows")]
    public static void Windows() => Game(BuildTarget.StandaloneWindows64, "Builds/Windows/TranscendLights.exe");

    [MenuItem("Tools/Release/Build macOS")]
    public static void Mac() => Game(BuildTarget.StandaloneOSX, "Builds/macOS/TranscendLights.app");

    [MenuItem("Tools/Release/Build Phone App (Android)")]
    public static void PhoneApp()
    {
        string product = PlayerSettings.productName;
        bool ok = false;
        try
        {
            PhonePointer.Editor.AndroidSetup.Configure();
            PlayerSettings.productName = PhoneName;
            EditorUserBuildSettings.buildAppBundle = false;
            ok = Run(new[] { PhoneScene }, BuildTarget.Android, "Builds/Android/TranscendLightsWand.apk");
        }
        finally
        {
            PlayerSettings.productName = product;
            AssetDatabase.SaveAssets();
        }
        Done(ok);
    }

    static void Game(BuildTarget target, string path)
    {
        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled && s.path != PhoneScene).Select(s => s.path).ToArray();
        string product = PlayerSettings.productName;
        bool ok = false;
        try
        {
            PlayerSettings.productName = GameName;
            ok = Run(scenes, target, path);
        }
        finally
        {
            PlayerSettings.productName = product;
            AssetDatabase.SaveAssets();
        }
        Done(ok);
    }

    // Exit only after the product name is back (exiting inside the build left the build's name saved).
    static void Done(bool ok)
    {
        if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
    }

    static bool Run(string[] scenes, BuildTarget target, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = path,
            target = target,
            options = BuildOptions.None,
        });
        var s = report.summary;
        Debug.Log($"[ReleaseBuilds] {target}: {s.result}, {s.totalSize / (1024 * 1024)} MB, {s.totalTime}, errors {s.totalErrors} → {path}");
        return s.result == BuildResult.Succeeded;
    }
}
