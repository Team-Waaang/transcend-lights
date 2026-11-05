using UnityEngine;
using UnityEngine.EventSystems;

namespace PhonePointer
{
    public sealed class PhonePointerHoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public PhoneController Controller;
        public UnityEngine.UI.Button Button;
        int? pointer;
        public void OnPointerDown(PointerEventData data)
        {
            if(pointer.HasValue || data.button!=PointerEventData.InputButton.Left || Button==null || !Button.IsInteractable() || Controller==null) return;
            pointer=data.pointerId; Controller.PressDown();
        }
        public void OnPointerUp(PointerEventData data) { if(pointer==data.pointerId) Cancel(); }
        public void OnPointerExit(PointerEventData data) { if(pointer==data.pointerId) Cancel(); }
        public void Cancel() { if(pointer.HasValue) { pointer=null; Controller?.PressUp(); } }
        void OnDisable() => Cancel();
        void OnApplicationPause(bool paused) { if(paused) Cancel(); }
        void OnApplicationFocus(bool focused) { if(!focused) Cancel(); }
    }
}
