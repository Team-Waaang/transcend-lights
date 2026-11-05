using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace AiHackathon.Game
{
    /// <summary>
    /// A black camera that always exists behind every other one (depth -100, renders no layers). Periods switch their
    /// play rig off while a cutscene scene loads additively, and a scene's camera can appear a frame after the old
    /// one is gone; without this the Game view shows Unity's "No cameras rendering" in those gaps.
    /// It is untagged so <see cref="Camera.main"/> never picks it.
    /// </summary>
    public static class FallbackCamera
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            var go = new GameObject("Fallback Camera") { hideFlags = HideFlags.DontSave | HideFlags.NotEditable };
            Object.DontDestroyOnLoad(go);
            var cam = go.AddComponent<Camera>();
            cam.depth = -100f;
            cam.cullingMask = 0;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.useOcclusionCulling = false;
            cam.allowHDR = false;
            cam.allowMSAA = false;
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;
            data.renderShadows = false;
            data.requiresColorTexture = false;
            data.requiresDepthTexture = false;
        }
    }
}
