using UnityEngine;

namespace AiHackathon.Cutscene
{
    /// <summary>
    /// Keeps a Klaus beam rig (pivot = beam head, +Y back toward the caster, uniform scale = length / 20) attached
    /// to a moving caster: after the timeline has placed the head, it turns the rig toward <see cref="tail"/> and
    /// sizes it to reach. A zero scale from the timeline means "beam off" and is left alone.
    /// </summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(200)]
    public class BeamTailFollow : MonoBehaviour
    {
        public Transform tail;
        public const float BeamLength = 20f;

        void LateUpdate() => Apply();

        public void Apply()
        {
            if (tail == null || transform.localScale.x <= 1e-4f) return;
            var dir = tail.position - transform.position;
            if (dir.sqrMagnitude < 1e-6f) return;
            transform.rotation = Quaternion.FromToRotation(Vector3.up, dir);
            transform.localScale = Vector3.one * (dir.magnitude / BeamLength);
        }

        /// <summary>For still renders right after a timeline Evaluate (no LateUpdate in between).</summary>
        public static void ApplyAll()
        {
            foreach (var f in FindObjectsByType<BeamTailFollow>(FindObjectsSortMode.None)) f.Apply();
        }
    }
}
