using System.Threading.Tasks;
using UnityEngine;

namespace PhonePointer
{
    // Presentation bindings only. Layout, fonts, colors and touch controls live in the prefab.
    [RequireComponent(typeof(PhoneController))]
    public sealed class PhoneControllerUI : MonoBehaviour
    {
        [Header("Scene / prefab bindings")]
        [SerializeField] PhoneController controller;
        [SerializeField] GameObject canvasObject, diagnostics;
        [SerializeField] RectTransform safeArea;
        [SerializeField] PhonePointerHoldButton hold;
        [SerializeField] UnityEngine.UI.Button select, center, register, cameraButton, exportButton, resetButton, diagnosticsButton, closeButton;
        [SerializeField] UnityEngine.UI.Button modeButton;
        [SerializeField] UnityEngine.UI.Text modeLabel;
        [SerializeField] UnityEngine.UI.Slider sensitivity;
        [SerializeField] UnityEngine.UI.Text status, message, steps, guide, gain, selectLabel, cameraLabel, exportResult, gainHint, diagnosticStatus;
        [Header("Font fallback (embedded font data)")]
        [SerializeField] Font fallbackFont;
        [Header("Status colors")]
        [SerializeField] Color readyColor=new Color(.12f,.86f,.72f), waitingColor=new Color(1f,.75f,.36f);
        Rect lastSafeArea;
        Vector2Int lastSize;
        Task<string> export;
        bool bound;

        void Start()
        {
            ApplyFontFallback();
            if(controller==null) controller=GetComponent<PhoneController>();
            if(controller==null || canvasObject==null || diagnostics==null || safeArea==null || hold==null ||
                select==null || center==null || register==null || cameraButton==null || exportButton==null ||
                resetButton==null || diagnosticsButton==null || closeButton==null || sensitivity==null ||
                status==null || message==null || steps==null || guide==null || gain==null || selectLabel==null ||
                cameraLabel==null || exportResult==null || gainHint==null || diagnosticStatus==null)
            { Debug.LogError("Phone Pointer UI references are missing. Use the PhoneController prefab or assign the Inspector bindings.",this); enabled=false; return; }
            hold.Controller=controller; hold.Button=select;
            center.onClick.AddListener(controller.Center);
            register.onClick.AddListener(controller.RequestRegistration);
            cameraButton.onClick.AddListener(controller.ToggleCamera);
            if(modeButton!=null) modeButton.onClick.AddListener(controller.ToggleTrackingMode);
            resetButton.onClick.AddListener(ResetSensitivity);
            diagnosticsButton.onClick.AddListener(OpenDiagnostics);
            closeButton.onClick.AddListener(CloseDiagnostics);
            exportButton.onClick.AddListener(ExportDiagnostics);
            sensitivity.minValue=PointerMath.MinSensitivity; sensitivity.maxValue=PointerMath.MaxSensitivity;
            sensitivity.SetValueWithoutNotify(controller.Sensitivity);
            sensitivity.onValueChanged.AddListener(ChangeSensitivity);
            diagnostics.SetActive(false); bound=true; ApplySafeArea(); Refresh();
        }
        void ApplyFontFallback()
        {
            if(canvasObject==null) return;
            foreach(var label in canvasObject.GetComponentsInChildren<UnityEngine.UI.Text>(true))
            {
                // Preserve custom Inspector fonts; repair only missing references.
                if(label.font!=null) continue;
                if(fallbackFont==null)
                { Debug.LogError("Phone Pointer UI font missing. Assign the bundled LiberationSans font in the prefab.",label); continue; }
                label.font=fallbackFont;
            }
        }
        void ResetSensitivity() => controller.SetSensitivity(1f);
        void ChangeSensitivity(float value) => controller.SetSensitivity(Mathf.Round(value*20)/20);
        void OpenDiagnostics() { hold.Cancel(); diagnostics.SetActive(true); }
        void CloseDiagnostics() => diagnostics.SetActive(false);
        void ExportDiagnostics() { if(export==null) export=controller.ExportDiagnosticsAsync(); }
        void Update()
        {
            if(!bound) return;
            ApplySafeArea(); Refresh();
            if(export!=null && export.IsCompleted)
            {
                exportResult.text=export.IsCanceled ? "Export canceled." : export.IsFaulted ? "Export failed. Check storage access." : "Saved locally:\n"+export.Result;
                export=null;
            }
            exportButton.interactable=export==null;
        }
        void ApplySafeArea()
        {
            var area=Screen.safeArea; var size=new Vector2Int(Screen.width,Screen.height);
            if(area==lastSafeArea && size==lastSize) return;
            lastSafeArea=area; lastSize=size; if(size.x<=0 || size.y<=0) return;
            safeArea.anchorMin=new Vector2(area.xMin/size.x,area.yMin/size.y);
            safeArea.anchorMax=new Vector2(area.xMax/size.x,area.yMax/size.y);
            safeArea.offsetMin=safeArea.offsetMax=Vector2.zero;
        }
        void Refresh()
        {
            bool ready=controller.Connected && controller.Registered && controller.Tracking && controller.Calibrated;
            select.interactable=ready; if(!ready) hold.Cancel();
            center.interactable=controller.Connected && controller.Registered && controller.Tracking;
            bool air=controller.TrackingMode==PhoneTrackingMode.AirMouse;
            register.interactable=controller.Connected && !controller.ModeSwitchPending && !air;
            cameraButton.interactable=controller.Registered && !air;
            if(modeButton!=null) modeButton.interactable=controller.Connected && controller.AirMouseSupported && !controller.ModeSwitchPending;
            if(modeLabel!=null) modeLabel.text=controller.ModeSwitchPending ? "Switching..." : air ? "Air mouse" : "AR aim";
            bool gainEnabled=!controller.Connected || controller.SensitivitySupported;
            sensitivity.interactable=resetButton.interactable=gainEnabled;
            sensitivity.SetValueWithoutNotify(controller.Sensitivity);
            gain.text=$"Sensitivity  {controller.Sensitivity:F2}x";
            gainHint.text=!gainEnabled ? "PC uses 1x. Update the PC SDK to enable sensitivity." : controller.Connected && !Mathf.Approximately(controller.Sensitivity,controller.AppliedSensitivity) ? "Applying sensitivity to PC..." : "1x matches physical aim. Higher values move the pointer farther.";
            selectLabel.text=controller.Pressing ? "HOLDING" : ready ? "SELECT" : "SELECT / WAIT";
            cameraLabel.text=air ? "Camera off" : controller.CameraVisible ? "Controller view" : "Camera view";
            status.text=controller.ConnectionStatus==ConnectionStatus.Reconnecting ? "RECONNECT" : ready ? "READY" : !controller.Connected ? "CONNECT" : !controller.Registered ? "REGISTER" : !controller.Tracking ? "TRACKING LOST" : "CENTER";
            status.color=ready ? readyColor : waitingColor; message.text=controller.Message;
            diagnosticStatus.text=$"Connection: {controller.ConnectionStatus}\nReason: {controller.DisconnectReason}\nApplied sensitivity: {controller.AppliedSensitivity:F2}x";
            steps.text=!controller.Connected ? "[1 CONNECT]   2 REGISTER   3 CENTER   4 PLAY" : !controller.Registered ? "1 CONNECT   [2 REGISTER]   3 CENTER   4 PLAY" : !controller.Calibrated ? "1 CONNECT   2 REGISTER   [3 CENTER]   4 PLAY" : "1 CONNECT   2 REGISTER   3 CENTER   [4 PLAY]";
            guide.text=controller.ConnectionStatus==ConnectionStatus.Reconnecting ? "Reconnecting automatically..." : !controller.Connected ? "SCAN THE QR INSIDE THE SQUARE\nKeep the entire square in view." : !controller.Registered ? "KEEP THE SAME SQUARE IN VIEW\nRegistering automatically. Hold steady." : !controller.Tracking ? "Uncover the camera and move slowly." : !controller.Calibrated ? "Aim the TOP edge at the monitor center.\nThen tap CENTER." : controller.CameraVisible ? "Camera preview on. Tracking continues." : "Aim with the TOP edge of your phone.\nCENTER is optional fine-tuning.";
            if(air && controller.Connected)
            {
                steps.text=controller.Calibrated ? "CONNECT   AIR MOUSE   [PLAY]" : "CONNECT   AIR MOUSE   [CENTER]";
                guide.text=controller.Calibrated ? "Rotate the TOP edge to move the pointer.\nCENTER resets drift. Translation is not tracked." : "Hold the phone in your aiming posture.\nTap CENTER to set the pointer to the middle.";
            }
        }
        void OnDisable() { hold?.Cancel(); if(canvasObject!=null) canvasObject.SetActive(false); controller?.SaveSensitivity(); }
        void OnEnable() { if(canvasObject!=null) canvasObject.SetActive(true); }
        void OnDestroy()
        {
            hold?.Cancel(); if(!bound) return;
            center.onClick.RemoveListener(controller.Center); register.onClick.RemoveListener(controller.RequestRegistration);
            if(modeButton!=null) modeButton.onClick.RemoveListener(controller.ToggleTrackingMode);
            cameraButton.onClick.RemoveListener(controller.ToggleCamera); resetButton.onClick.RemoveListener(ResetSensitivity);
            diagnosticsButton.onClick.RemoveListener(OpenDiagnostics); closeButton.onClick.RemoveListener(CloseDiagnostics);
            exportButton.onClick.RemoveListener(ExportDiagnostics); sensitivity.onValueChanged.RemoveListener(ChangeSensitivity);
        }
    }
}
