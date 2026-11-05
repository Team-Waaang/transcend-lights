using UnityEngine;
using UnityEngine.InputSystem;
using Waaang;

namespace AiHackathon.Game
{
    /// <summary>
    /// Reads a player's phone (or the mouse test stand-in) directly, without a <see cref="WandPointerRig"/>: for
    /// moments when the casters are not live, e.g. the resonance QTE inside the boss defeat cutscene.
    /// Slot i is the i-th connected phone, as in the rig. With no phone in that slot and <c>mouseTest</c> on,
    /// slot 0 is the left mouse button and slot 1 the right one, both aiming with the cursor.
    /// </summary>
    public static class CasterInput
    {
        public static PhonePointer.PhoneSession Session(int slot)
        {
            var link = PhoneLink.Instance;
            var receiver = link != null ? link.Receiver : null;
            if (receiver == null) return null;
            int index = 0;
            var sessions = receiver.Sessions;
            for (int i = 0; i < sessions.Count; i++)
            {
                if (sessions[i].ConnectionStatus != PhonePointer.ConnectionStatus.Connected) continue;
                if (index == slot) return sessions[i];
                index++;
            }
            return null;
        }

        /// <summary>True while the player's trigger (phone button / mouse button) is held.</summary>
        public static bool Pressed(int slot, bool mouseTest)
        {
            var session = Session(slot);
            if (session != null) return session.Model.Pressed;
            if (!mouseTest || Mouse.current == null) return false;
            return slot == 1 ? Mouse.current.rightButton.isPressed : Mouse.current.leftButton.isPressed;
        }

        /// <summary>The player's pointer in viewport space (0..1, origin bottom-left).</summary>
        public static bool TryGetPointer(int slot, bool mouseTest, out Vector2 uv)
        {
            var session = Session(slot);
            if (session != null) return session.TryGetPointerNormalized(out uv);
            uv = new Vector2(0.5f, 0.5f);
            if (!mouseTest || Mouse.current == null) return false;
            var m = Mouse.current.position.ReadValue();
            uv = new Vector2(Mathf.Clamp01(m.x / Mathf.Max(1f, Screen.width)), Mathf.Clamp01(m.y / Mathf.Max(1f, Screen.height)));
            return true;
        }
    }
}
