#if UNITY_EDITOR || UNITY_ANDROID
using System.Collections.Generic;
using System.Threading.Tasks;
using AiHackathon.Parchment;
using PhonePointer;
using TMPro;
using UnityEngine;

namespace Waaang.Phone
{
    // Presentation only: the SDK continues to own AR, calibration and the v2 input protocol.
    public sealed class WaaangPhoneControllerUI : MonoBehaviour
    {
        public PhoneController controller;
        public Camera arCamera;
        public RectTransform safeArea;
        public ParchmentTitleCard registrationCard;
        public GameObject gameplay, settingsPanel;
        public UnityEngine.UI.RawImage wandImage;
        public UnityEngine.UI.Button settingsButton, closeButton, backdropButton;
        public UnityEngine.UI.Button centerButton, registerButton, cameraButton, modeButton, exportButton;
        public UnityEngine.UI.Slider sensitivity;
        public TMP_Text stateLabel, hintLabel, sensitivityLabel, modeLabel, cameraLabel, diagnosticLabel;
        public GameObject wandAsset, noxWandAsset;
        public Texture wandTexture, noxWandTexture;
        public Shader wandShader;
        [Header("Touch VFX (Assets/VFX_Klaus)")]
        [Tooltip("Bursts played together at the wand tip on every cast pulse.")]
        public GameObject[] tipEffects;
        [Tooltip("Smaller sparkle played at points along the wand shaft.")]
        public GameObject shaftEffect;
        public float tipEffectScale = .3f, shaftEffectScale = .14f;
        [Range(0, 3)] public int shaftPoints = 2;
        [Tooltip("How far particle hues are shifted toward the player's color (Amelia gold / Nox blue).")]
        [Range(0, 1)] public float playerTint = 1f;
        public bool CanCast => !settingsPanel.activeSelf && controller.Connected && controller.Registered &&
            controller.Tracking && controller.Calibrated && !controller.ModeSwitchPending;

        readonly List<Material> ownedMaterials = new List<Material>();
        RenderTexture previewTexture;
        GameObject previewWorld;
        Transform wandPivot;
        int displayedSlot = -1;
        Camera previewCamera;
        // Several volleys of the same effects so a new pulse never cuts off the previous one's fade.
        ParticleSystem[][] volleys;
        int nextVolley;
        const int VolleyCount = 3;
        Quaternion restRotation;
        Rect previousSafeArea;
        Vector2Int previousScreen;
        string instruction, exportResult;
        float recoil, nextPulse;
        bool held, bound, recoveryCamera;
        Task<string> export;
        const int PreviewLayer = 30;

        void Start()
        {
            if (!controller || !arCamera || !safeArea || !registrationCard || !gameplay || !settingsPanel ||
                !wandImage || !settingsButton || !closeButton || !backdropButton || !centerButton ||
                !registerButton || !cameraButton || !modeButton || !exportButton || !sensitivity ||
                !stateLabel || !hintLabel || !sensitivityLabel || !modeLabel || !cameraLabel || !diagnosticLabel ||
                !wandAsset || !noxWandAsset || !noxWandTexture || !wandShader ||
                tipEffects == null || tipEffects.Length == 0 || System.Array.Exists(tipEffects, e => !e))
            {
                Debug.LogError("Waaang phone prefab has missing bindings.", this);
                enabled = false;
                return;
            }
            settingsButton.onClick.AddListener(OpenSettings);
            closeButton.onClick.AddListener(CloseSettings);
            backdropButton.onClick.AddListener(CloseSettings);
            centerButton.onClick.AddListener(Center);
            registerButton.onClick.AddListener(Register);
            cameraButton.onClick.AddListener(ToggleCamera);
            modeButton.onClick.AddListener(ToggleMode);
            exportButton.onClick.AddListener(Export);
            sensitivity.minValue = PointerMath.MinSensitivity;
            sensitivity.maxValue = PointerMath.MaxSensitivity;
            sensitivity.SetValueWithoutNotify(controller.Sensitivity);
            sensitivity.onValueChanged.AddListener(ChangeSensitivity);
            settingsPanel.SetActive(false);
            arCamera.cullingMask &= ~(1 << PreviewLayer);
            BuildWandPreview();
            bound = true;
            Refresh();
        }

        void Update()
        {
            if (!bound) return;
            Refresh();
            if (held && !CanCast) EndCast();
            if (held && Time.unscaledTime >= nextPulse) Pulse();
            recoil = Mathf.MoveTowards(recoil, 0, Time.unscaledDeltaTime * 5f);
            if (wandPivot)
            {
                float breathe = Mathf.Sin(Time.unscaledTime * 1.4f) * .8f;
                wandPivot.localRotation = restRotation * Quaternion.Euler(-recoil * 5f, breathe, 0f);
            }
            if (export != null && export.IsCompleted)
            {
                exportResult = export.IsFaulted || export.IsCanceled
                    ? "기록을 저장하지 못했습니다."
                    : "진단 기록 저장 완료\n" + export.Result;
                export = null;
            }
        }

        void Refresh()
        {
            if (displayedSlot != controller.PlayerSlot)
            {
                EndCast();
                RebuildWand();
            }
            ApplySafeArea();
            ResizePreview();
            bool registered = controller.Connected && controller.Registered;
            bool ready = registered && controller.Tracking && controller.Calibrated;
            if (registered && !controller.Tracking && !controller.CameraVisible &&
                controller.TrackingMode == PhoneTrackingMode.AR)
            {
                controller.ToggleCamera(); recoveryCamera = true;
            }
            if (recoveryCamera && ready)
            {
                if (controller.CameraVisible) controller.ToggleCamera();
                recoveryCamera = false;
            }
            bool cameraPreview = registered && controller.CameraVisible;
            gameplay.SetActive(registered && !cameraPreview);
            if (previewCamera) previewCamera.enabled = gameplay.activeSelf;
            string nextInstruction = !controller.Connected ? "석판을 인식하라" :
                !controller.Registered ? "석판을 인식하라" : !controller.Tracking ? "석판을 다시 비추어라" :
                !controller.Calibrated ? "화면 중앙을 조준하라" : null;
            // The original parchment animation is reused; only its text and visibility change.
            if (instruction != nextInstruction)
            {
                instruction = nextInstruction;
                if (instruction == null) registrationCard.Hide();
                else registrationCard.Show(new TitleCardInfo(instruction,
                    !controller.Connected ? "같은 Wi-Fi에서 PC의 QR을 비추세요" :
                    !controller.Registered ? "등록 이미지를 화면 안에 담아 주세요" :
                    !controller.Tracking ? "카메라를 가리지 말고 천천히 움직이세요" : "왼쪽 아래 중앙 버튼을 눌러 주세요"));
            }
            stateLabel.gameObject.SetActive(registered);
            hintLabel.gameObject.SetActive(registered);
            stateLabel.text = controller.ConnectionStatus == ConnectionStatus.Reconnecting ? "다시 연결하는 중" :
                ready ? (displayedSlot % 2 == 1 ? "녹스의 지팡이" : "아멜리아의 지팡이") : !controller.Connected ? "석판을 찾는 중" :
                !controller.Registered ? "석판을 읽는 중" : "지팡이를 준비하는 중";
            hintLabel.text = held ? "마법을 시전하는 중" : ready ? "화면을 누르면 마법이 펼쳐집니다" : "등록과 중앙 보정을 완료해 주세요";
            centerButton.interactable = controller.Connected && controller.Registered && controller.Tracking;
            bool air = controller.TrackingMode == PhoneTrackingMode.AirMouse;
            registerButton.interactable = controller.Connected && !air && !controller.ModeSwitchPending;
            cameraButton.interactable = controller.Registered && !air;
            modeButton.interactable = controller.Connected && controller.AirMouseSupported && !controller.ModeSwitchPending;
            modeLabel.text = controller.ModeSwitchPending ? "전환 중…" : air ? "조작 방식 · 자이로" : "조작 방식 · AR";
            cameraLabel.text = controller.CameraVisible ? "지팡이 화면으로" : "카메라 보기";
            sensitivity.interactable = !controller.Connected || controller.SensitivitySupported;
            sensitivity.SetValueWithoutNotify(controller.Sensitivity);
            sensitivityLabel.text = "조준 감도   " + controller.Sensitivity.ToString("0.00") + "×";
            if (export == null)
                diagnosticLabel.text = exportResult ?? controller.Message + "\n연결: " + controller.ConnectionStatus;
            exportButton.interactable = export == null;
        }

        void ApplySafeArea()
        {
            var screen = new Vector2Int(Screen.width, Screen.height);
            var area = Screen.safeArea;
            if (screen == previousScreen && area == previousSafeArea) return;
            previousScreen = screen; previousSafeArea = area;
            if (screen.x <= 0 || screen.y <= 0) return;
            safeArea.anchorMin = new Vector2(area.xMin / screen.x, area.yMin / screen.y);
            safeArea.anchorMax = new Vector2(area.xMax / screen.x, area.yMax / screen.y);
            safeArea.offsetMin = safeArea.offsetMax = Vector2.zero;
        }

        public bool BeginCast()
        {
            if (!bound || !CanCast || held) return false;
            controller.PressDown(); // SDK also sends one Select and its existing short haptic tick.
            if (!controller.Pressing) return false;
            held = true; Pulse();
            return true;
        }
        public void EndCast()
        {
            held = false;
            if (controller) controller.PressUp();
            if (volleys != null)
                foreach (var volley in volleys)
                    foreach (var system in volley) if (system) system.Stop(false, ParticleSystemStopBehavior.StopEmitting);
        }
        void Pulse()
        {
            recoil = 1f; nextPulse = Time.unscaledTime + .32f;
            if (volleys == null || volleys.Length == 0) return;
            var volley = volleys[nextVolley];
            nextVolley = (nextVolley + 1) % volleys.Length;
            foreach (var system in volley)
            {
                if (!system) continue;
                system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                system.Play(false);
            }
        }
        void OpenSettings() { EndCast(); settingsPanel.SetActive(true); }
        void CloseSettings() { settingsPanel.SetActive(false); controller.SaveSensitivity(); }
        // Bottom-left button on the play screen, outside the settings panel.
        void Center() { EndCast(); controller.Center(); }
        void Register() { EndCast(); controller.RequestRegistration(); CloseSettings(); }
        void ToggleCamera() { EndCast(); recoveryCamera = false; controller.ToggleCamera(); CloseSettings(); }
        void ToggleMode() { EndCast(); controller.ToggleTrackingMode(); }
        void ChangeSensitivity(float value) { EndCast(); controller.SetSensitivity(Mathf.Round(value * 20f) / 20f); }
        void Export() { EndCast(); if (export == null) { exportResult = null; export = controller.ExportDiagnosticsAsync(); } }

        void BuildWandPreview()
        {
            previewWorld = new GameObject("Wand Preview World");
            previewWorld.transform.position = new Vector3(1000f, 1000f, 0f);
            previewWorld.transform.SetParent(transform, false);
            // Independent render texture: never move or replace the SDK AR camera.
            var cameraObject = new GameObject("Wand Preview Camera", typeof(Camera));
            cameraObject.transform.SetParent(previewWorld.transform, false);
            cameraObject.transform.localPosition = new Vector3(0f, 0f, -8f);
            previewCamera = cameraObject.GetComponent<Camera>();
            previewCamera.orthographic = true; previewCamera.orthographicSize = 2.8f;
            previewCamera.clearFlags = CameraClearFlags.SolidColor;
            previewCamera.backgroundColor = new Color(0, 0, 0, 0);
            previewCamera.cullingMask = 1 << PreviewLayer;
            previewCamera.nearClipPlane = .1f; previewCamera.farClipPlane = 20f;
            previewCamera.allowHDR = false; previewCamera.allowMSAA = false;

            RebuildWand();
            ResizePreview();
        }

        void RebuildWand()
        {
            if (!previewWorld) return;
            if (wandPivot) { wandPivot.gameObject.SetActive(false); Destroy(wandPivot.gameObject); }
            volleys = null;
            foreach (var material in ownedMaterials) if (material) Destroy(material);
            ownedMaterials.Clear();
            displayedSlot = controller.PlayerSlot;
            bool nox = displayedSlot % 2 == 1;
            wandPivot = new GameObject("Wand Motion").transform;
            wandPivot.SetParent(previewWorld.transform, false);
            var wand = Instantiate(nox ? noxWandAsset : wandAsset, wandPivot, false);
            wand.name = nox ? "Nox Wand" : "Amelia Wand";
            // The source meshes are diagonal in XY. Align their actual shaft, not their AABB.
            // These rotations come from the principal axis of each imported mesh.
            wand.transform.localRotation = Quaternion.Euler(0f, 0f, nox ? -33.43f : -35.15f);
            var renderers = wand.GetComponentsInChildren<Renderer>(true);
            Bounds bounds = BoundsOf(renderers);
            wand.transform.localScale *= 3.7f / Mathf.Max(.001f, bounds.size.y);
            bounds = BoundsOf(renderers);
            wand.transform.position += wandPivot.position - bounds.center;
            foreach (var renderer in renderers)
            {
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    var material = new Material(wandShader);
                    material.mainTexture = nox ? noxWandTexture : wandTexture;
                    ownedMaterials.Add(material); materials[i] = material;
                }
                renderer.sharedMaterials = materials;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            bounds = BoundsOf(renderers);
            // Touch effect: VFX_Klaus (Hyper Casual FX Vol.2) prefabs with their own URP materials.
            // Tip bursts on every pulse, small sparkles along the shaft.
            Color tint = nox ? new Color(.35f, .65f, 1f, 1f) : new Color(1f, .72f, .32f, 1f);
            float front = bounds.center.z - Mathf.Max(.15f, bounds.extents.z + .05f);
            var tipPosition = new Vector3(bounds.center.x, bounds.max.y - .1f, front);
            volleys = new ParticleSystem[VolleyCount][];
            nextVolley = 0;
            for (int v = 0; v < VolleyCount; v++)
            {
                var systems = new List<ParticleSystem>();
                foreach (var effect in tipEffects)
                    SpawnEffect(effect, "Tip", tipPosition, tipEffectScale, tint, systems);
                if (shaftEffect)
                    for (int point = 1; point <= shaftPoints; point++)
                    {
                        float t = point / (shaftPoints + 1f);
                        SpawnEffect(shaftEffect, "Shaft " + point, new Vector3(bounds.center.x,
                            Mathf.Lerp(bounds.min.y + .12f, bounds.max.y - .1f, .35f + .55f * t), front),
                            shaftEffectScale, tint, systems);
                    }
                volleys[v] = systems.ToArray();
            }
            foreach (var node in previewWorld.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = PreviewLayer;
            restRotation = Quaternion.identity;
            wandPivot.localRotation = restRotation;
        }
        void SpawnEffect(GameObject prefab, string label, Vector3 position, float scale, Color tint, List<ParticleSystem> output)
        {
            var effect = Instantiate(prefab, wandPivot, false).transform;
            effect.name = prefab.name + " / " + label;
            effect.position = position;
            effect.localRotation = Quaternion.identity;
            effect.localScale = Vector3.one * scale;
            foreach (var light in effect.GetComponentsInChildren<Light>(true)) light.enabled = false;
            foreach (var system in effect.GetComponentsInChildren<ParticleSystem>(true))
            {
                system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = system.main;
                // Pooled and replayed by Pulse(): never loop, never self-destroy.
                main.playOnAwake = false; main.loop = false;
                main.stopAction = ParticleSystemStopAction.None;
                main.useUnscaledTime = true;
                main.simulationSpace = ParticleSystemSimulationSpace.Local;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                main.startColor = Tinted(main.startColor, tint);
                var overLifetime = system.colorOverLifetime;
                if (overLifetime.enabled) overLifetime.color = Tinted(overLifetime.color, tint);
                output.Add(system);
            }
            // Keep VFX_Klaus materials (URP Shader Graph); only shadows are irrelevant here.
            foreach (var renderer in effect.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }
        // Hue shift toward the player color: keeps each particle's brightness/saturation (white cores stay white).
        Color Shift(Color c, Color tint)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            Color.RGBToHSV(tint, out float th, out _, out _);
            h = Mathf.Repeat(Mathf.LerpAngle(h * 360f, th * 360f, playerTint) / 360f, 1f);
            var result = Color.HSVToRGB(h, s, v, true); result.a = c.a;
            return result;
        }
        Gradient Shift(Gradient g, Color tint)
        {
            if (g == null) return null;
            var keys = g.colorKeys;
            for (int i = 0; i < keys.Length; i++) keys[i].color = Shift(keys[i].color, tint);
            var result = new Gradient { mode = g.mode };
            result.SetKeys(keys, g.alphaKeys);
            return result;
        }
        ParticleSystem.MinMaxGradient Tinted(ParticleSystem.MinMaxGradient source, Color tint)
        {
            switch (source.mode)
            {
                case ParticleSystemGradientMode.Color: return new ParticleSystem.MinMaxGradient(Shift(source.color, tint));
                case ParticleSystemGradientMode.TwoColors: return new ParticleSystem.MinMaxGradient(Shift(source.colorMin, tint), Shift(source.colorMax, tint));
                case ParticleSystemGradientMode.Gradient:
                case ParticleSystemGradientMode.RandomColor:
                    { var r = new ParticleSystem.MinMaxGradient(Shift(source.gradient, tint)); r.mode = source.mode; return r; }
                case ParticleSystemGradientMode.TwoGradients:
                    return new ParticleSystem.MinMaxGradient(Shift(source.gradientMin, tint), Shift(source.gradientMax, tint));
                default: return source;
            }
        }
        static Bounds BoundsOf(Renderer[] renderers)
        {
            if (renderers.Length == 0) return new Bounds(Vector3.zero, Vector3.one);
            var result = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) result.Encapsulate(renderers[i].bounds);
            return result;
        }
        void ResizePreview()
        {
            if (!previewCamera || !wandImage) return;
            // Render at half phone width, capped for mobile; preserve the RawImage's actual aspect.
            var rect = wandImage.rectTransform.rect;
            float aspect = rect.width > 0 && rect.height > 0 ? rect.width / rect.height : (float)Screen.width / Mathf.Max(1, Screen.height);
            int width = Mathf.Clamp(Screen.width / 2, 256, 640);
            int height = Mathf.Clamp(Mathf.RoundToInt(width / Mathf.Max(.1f, aspect)), 256, 1536);
            if (previewTexture && previewTexture.width == width && previewTexture.height == height) return;
            if (previewTexture) { previewTexture.Release(); Destroy(previewTexture); }
            previewTexture = new RenderTexture(width, height, 16, RenderTextureFormat.ARGB32);
            previewTexture.name = "Waaang Phone Wand";
            previewTexture.Create(); previewCamera.targetTexture = previewTexture;
            wandImage.texture = previewTexture;
        }

        void OnApplicationPause(bool paused) { if (paused) { EndCast(); controller?.SaveSensitivity(); } }
        void OnApplicationFocus(bool focused) { if (!focused) EndCast(); }
        void OnDisable() { EndCast(); if (previewCamera) previewCamera.enabled = false; }
        void OnEnable() { if (previewCamera) previewCamera.enabled = true; }
        void OnDestroy()
        {
            EndCast();
            if (bound)
            {
                settingsButton.onClick.RemoveListener(OpenSettings); closeButton.onClick.RemoveListener(CloseSettings);
                backdropButton.onClick.RemoveListener(CloseSettings); centerButton.onClick.RemoveListener(Center);
                registerButton.onClick.RemoveListener(Register); cameraButton.onClick.RemoveListener(ToggleCamera);
                modeButton.onClick.RemoveListener(ToggleMode); exportButton.onClick.RemoveListener(Export);
                sensitivity.onValueChanged.RemoveListener(ChangeSensitivity);
            }
            if (previewWorld) Destroy(previewWorld);
            if (previewTexture) { previewTexture.Release(); Destroy(previewTexture); }
            foreach (var material in ownedMaterials) if (material) Destroy(material);
        }
    }
}

#endif
