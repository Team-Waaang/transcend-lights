using System;
using System.IO;
using System.Linq;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;

namespace AiHackathon.Cutscene.Editor
{
    /// <summary>
    /// Renders still frames of a cutscene scene's timeline to PNG (Temp/CutscenePreview/), for reviewing shots
    /// without pressing Play: the director is evaluated at each time, Cinemachine is stepped, then the main camera
    /// renders into a 1280x720 target.
    /// </summary>
    public static class CutscenePreview
    {
        public const string OutDir = "Temp/CutscenePreview";

        [MenuItem("Tools/Cutscene/Render Preview Frames Of Open Scene")]
        static void RenderOpenFromMenu()
        {
            var director = UnityEngine.Object.FindAnyObjectByType<PlayableDirector>();
            if (director == null) { Debug.LogWarning("[CutscenePreview] No PlayableDirector in the open scene."); return; }
            int n = 12;
            var times = Enumerable.Range(0, n).Select(i => (float)(director.duration * (i + 0.5) / n)).ToArray();
            Render(EditorSceneManager.GetActiveScene().name, times);
        }

        /// <summary>Opens <paramref name="scenePath"/> and renders it at each time.</summary>
        public static string[] RenderScene(string scenePath, params float[] times)
        {
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
                if (EditorSceneManager.GetSceneAt(i).isDirty)
                    throw new Exception("Open scene '" + EditorSceneManager.GetSceneAt(i).path + "' has unsaved changes; save it first.");
            var previous = EditorSceneManager.GetActiveScene().path;
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            try { return Render(Path.GetFileNameWithoutExtension(scenePath), times); }
            finally
            {
                // Leave the scene as it was on disk, and go back to what was open before.
                if (!string.IsNullOrEmpty(previous)) EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
            }
        }

        /// <summary>
        /// Runs a cutscene builder's static Build() (type name, e.g. "BossEntranceCutsceneBuilder") without touching
        /// unsaved work (a full type name works too, e.g. "AiHackathon.Game.Editor.Period3Builder"): refuses if an open scene is dirty, and reopens the previous scene afterwards.
        /// </summary>
        public static string SafeBuild(string builder)
        {
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
                if (EditorSceneManager.GetSceneAt(i).isDirty)
                    throw new Exception("Open scene '" + EditorSceneManager.GetSceneAt(i).path + "' has unsaved changes; save it first.");
            var previous = EditorSceneManager.GetActiveScene().path;
            var type = typeof(CutscenePreview).Assembly.GetType(builder.Contains(".") ? builder : "AiHackathon.Cutscene.Editor." + builder) ?? throw new Exception("No builder " + builder);
            type.GetMethod("Build", Type.EmptyTypes).Invoke(null, null);
            var built = EditorSceneManager.GetActiveScene().path;
            if (!string.IsNullOrEmpty(previous) && previous != built) EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
            return built;
        }

        /// <summary><see cref="RenderScene"/> with the times as a comma-separated list (for scripted calls).</summary>
        public static string[] RenderSceneCsv(string scenePath, string times) =>
            RenderScene(scenePath, times.Split(',').Select(s => float.Parse(s.Trim(), System.Globalization.CultureInfo.InvariantCulture)).ToArray());

        /// <summary>Renders the open scene at each timeline time; returns the written file paths.</summary>
        public static string[] Render(string prefix, params float[] times)
        {
            Directory.CreateDirectory(OutDir);
            var director = UnityEngine.Object.FindAnyObjectByType<PlayableDirector>();
            var brain = UnityEngine.Object.FindAnyObjectByType<CinemachineBrain>();
            var cam = brain != null ? brain.GetComponent<Camera>() : Camera.main;
            if (cam == null) throw new Exception("No camera in the open scene.");
            if (brain != null) brain.UpdateMethod = CinemachineBrain.UpdateMethods.ManualUpdate;
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            var written = new string[times.Length];
            try
            {
                for (int i = 0; i < times.Length; i++)
                {
                    if (director != null)
                    {
                        // Step up to the frame so particles and blends have history.
                        double from = Math.Max(0.0, times[i] - 0.5);
                        for (double t = from; t < times[i]; t += 1.0 / 30.0) { director.time = t; director.Evaluate(); if (brain != null) brain.ManualUpdate(); }
                        director.time = times[i];
                        director.Evaluate();
                        AiHackathon.Cutscene.BeamTailFollow.ApplyAll();
                    }
                    if (brain != null) { brain.ManualUpdate(); brain.ManualUpdate(); }
                    var prev = cam.targetTexture;
                    cam.targetTexture = rt;
                    // The first render after a jump can still draw meshes and skins at their previous transforms
                    // (a second, stale wand); render once to settle, then capture.
                    cam.Render();
                    cam.Render();
                    cam.targetTexture = prev;
                    var active = RenderTexture.active;
                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                    tex.Apply();
                    RenderTexture.active = active;
                    var path = $"{OutDir}/{prefix}_{times[i]:00.00}.png";
                    File.WriteAllBytes(path, tex.EncodeToPNG());
                    written[i] = Path.GetFullPath(path);
                }
            }
            finally
            {
                if (brain != null) brain.UpdateMethod = CinemachineBrain.UpdateMethods.LateUpdate;
                UnityEngine.Object.DestroyImmediate(tex);
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
            }
            Debug.Log("[CutscenePreview] Wrote " + string.Join(", ", written));
            return written;
        }
    }
}
