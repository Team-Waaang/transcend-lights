#if UNITY_EDITOR || UNITY_ANDROID
using UnityEngine;
using UnityEngine.EventSystems;

namespace Waaang.Phone
{
    // One owning finger prevents a second touch or a settings tap from leaving SELECT held.
    public sealed class PhoneSpellTouch : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public WaaangPhoneControllerUI view;
        int? pointer;
        public void OnPointerDown(PointerEventData data)
        {
            if (pointer.HasValue || data.button != PointerEventData.InputButton.Left || !view) return;
            if (view.BeginCast()) pointer = data.pointerId;
        }
        public void OnPointerUp(PointerEventData data) { if (pointer == data.pointerId) Cancel(); }
        public void OnPointerExit(PointerEventData data) { if (pointer == data.pointerId) Cancel(); }
        void Cancel() { pointer = null; if (view) view.EndCast(); }
        void OnDisable() { Cancel(); }
        void OnApplicationPause(bool paused) { if (paused) Cancel(); }
        void OnApplicationFocus(bool focused) { if (!focused) Cancel(); }
    }
}

#endif
