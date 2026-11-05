using UnityEngine;

namespace AiHackathon.Cutscene
{
    /// <summary>
    /// Blends the scene's ambient light and fog (and the main camera's clear colour) between a lit and a dark set as
    /// <see cref="darkness"/> goes 0 → 1, so a timeline can "turn the lights off". Runs in edit mode for previews.
    /// </summary>
    [ExecuteAlways]
    public class AmbientFader : MonoBehaviour
    {
        [Range(0f, 1f)] public float darkness;

        [Header("Lit")]
        public Color litSky = new Color(0.7f, 0.66f, 0.62f);
        public Color litEquator = new Color(0.5f, 0.45f, 0.42f);
        public Color litGround = new Color(0.28f, 0.22f, 0.18f);
        public Color litFog = new Color(0.62f, 0.56f, 0.5f);

        [Header("Dark")]
        public Color darkSky = new Color(0.07f, 0.08f, 0.13f);
        public Color darkEquator = new Color(0.04f, 0.045f, 0.075f);
        public Color darkGround = new Color(0.02f, 0.02f, 0.03f);
        public Color darkFog = new Color(0.03f, 0.035f, 0.06f);

        public Camera viewCamera;

        void LateUpdate()
        {
            float k = Mathf.Clamp01(darkness);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Color.Lerp(litSky, darkSky, k);
            RenderSettings.ambientEquatorColor = Color.Lerp(litEquator, darkEquator, k);
            RenderSettings.ambientGroundColor = Color.Lerp(litGround, darkGround, k);
            var fog = Color.Lerp(litFog, darkFog, k);
            RenderSettings.fogColor = fog;
            var cam = viewCamera != null ? viewCamera : Camera.main;
            if (cam != null) cam.backgroundColor = fog;
        }
    }
}
