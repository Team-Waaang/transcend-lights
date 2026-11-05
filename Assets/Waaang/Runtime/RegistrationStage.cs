using UnityEngine;

namespace Waaang
{
    // A private render world keeps pairing/recovery independent of the game camera and lighting.
    public sealed class RegistrationStage : MonoBehaviour
    {
        public GameObject stoneAsset;
        public Material stoneMaterial;
        public Texture View => target;
        public bool Revealing => stoneVisible && Time.unscaledTime - revealedAt < RevealSeconds;
        public const float RevealSeconds = 1.35f;
        // Measured by matching the supplied crop against this FBX's orthographic front projection.
        static readonly Vector3 ReferenceCenter = new Vector3(.062966f, .439382f, 0f);
        const float ReferenceWidth = .646558f;
        const int StageLayer = 29;
        Camera stageCamera;
        GameObject world, stoneMotion;
        RenderTexture target;
        Vector3 stonePosition;
        float stoneScale, revealedAt;
        bool stoneVisible;
        int lastWidth, lastHeight;

        public void Build()
        {
            if (world || !stoneAsset || !stoneMaterial) return;
            world = new GameObject("Registration Chamber");
            world.transform.SetParent(transform, false);
            world.transform.localPosition = new Vector3(5000, 5000, 0);
            var cameraObject = new GameObject("Registration Camera", typeof(Camera));
            cameraObject.transform.SetParent(world.transform, false);
            cameraObject.transform.localPosition = new Vector3(0, 0, 10);
            cameraObject.transform.localRotation = Quaternion.Euler(0, 180, 0);
            stageCamera = cameraObject.GetComponent<Camera>();
            stageCamera.orthographic = true;
            stageCamera.orthographicSize = 4.5f;
            stageCamera.nearClipPlane = .1f; stageCamera.farClipPlane = 40f;
            stageCamera.clearFlags = CameraClearFlags.SolidColor;
            stageCamera.backgroundColor = Backdrop;
            stageCamera.cullingMask = 1 << StageLayer;
            stageCamera.allowHDR = false; stageCamera.allowMSAA = true;

            var key = new GameObject("Chamber Key", typeof(Light));
            key.transform.SetParent(world.transform, false);
            key.transform.localRotation = Quaternion.Euler(38, -28, 0);
            var light = key.GetComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.4f;
            light.color = new Color(.6f, .75f, 1f); light.cullingMask = 1 << StageLayer;
            stoneMotion = new GameObject("Runic Stone Reveal");
            stoneMotion.transform.SetParent(world.transform, false);
            var model = Instantiate(stoneAsset, stoneMotion.transform, false);
            model.name = "Runic Stone / Original FBX";
            foreach (var renderer in model.GetComponentsInChildren<Renderer>(true)) renderer.sharedMaterial = stoneMaterial;
            stoneMotion.SetActive(false);
            foreach (var node in world.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = StageLayer;
            Resize();
        }

        static readonly Color Backdrop = new Color(.008f, .015f, .025f, 1f);

        /// <param name="overGame">Clear to transparent so only the stone is drawn over the running game.</param>
        public void Present(bool visible, bool marker, bool overGame = false)
        {
            Build();
            if (!world) return;
            stageCamera.backgroundColor = overGame ? new Color(Backdrop.r, Backdrop.g, Backdrop.b, 0f) : Backdrop;
            world.SetActive(visible);
            if (!visible) { stoneVisible = false; return; }
            Resize();
            if (marker && !stoneVisible) revealedAt = Time.unscaledTime;
            stoneVisible = marker;
            stoneMotion.SetActive(marker);
        }

        void LateUpdate()
        {
            if (!world || !world.activeSelf) return;
            Resize();
            if (stoneVisible)
            {
                float t = Mathf.Clamp01((Time.unscaledTime - revealedAt) / RevealSeconds);
                float ease = 1f - Mathf.Pow(1f - t, 3f);
                stoneMotion.transform.localPosition = stonePosition + Vector3.down * (1f - ease) * .9f;
                stoneMotion.transform.localScale = Vector3.one * stoneScale * Mathf.Lerp(.78f, 1f, ease);
                stoneMotion.transform.localRotation = Quaternion.Euler(0, Mathf.Lerp(32f, 0f, ease), 0);
                // No idle bob/rotation: AR needs a stationary, front-facing reference after the reveal.
            }
        }

        void Resize()
        {
            if (!stageCamera || Screen.width <= 0 || Screen.height <= 0) return;
            if (lastWidth == Screen.width && lastHeight == Screen.height && target) return;
            lastWidth = Screen.width; lastHeight = Screen.height;
            if (target) { stageCamera.targetTexture = null; target.Release(); Destroy(target); }
            float ratio = Mathf.Min(1f, 1920f / Mathf.Max(Screen.width, Screen.height));
            // ARGB32 keeps the alpha channel, so the stone can be laid over the game (Present overGame).
            target = new RenderTexture(Mathf.Max(1, Mathf.RoundToInt(Screen.width * ratio)), Mathf.Max(1, Mathf.RoundToInt(Screen.height * ratio)), 24, RenderTextureFormat.ARGB32);
            target.name = "Registration Chamber"; target.antiAliasing = 4; target.Create();
            stageCamera.targetTexture = target;
            stageCamera.aspect = (float)Screen.width / Screen.height;
            Rect reference;
            if (!PhonePointer.ScreenMarkerLayout.TryGetRects(Screen.width, Screen.height, out _, out reference)) return;
            stoneScale = reference.width * (stageCamera.orthographicSize * 2f / Screen.height) / ReferenceWidth;
            stonePosition = -ReferenceCenter * stoneScale;
        }

        void OnDestroy()
        {
            if (world) Destroy(world);
            if (target) { target.Release(); Destroy(target); }
        }
    }
}
