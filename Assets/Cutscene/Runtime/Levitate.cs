using UnityEngine;

namespace AiHackathon.Cutscene
{
    /// <summary>Slow bob and tumble for rubble the curse holds in the air (boss arena).</summary>
    public class Levitate : MonoBehaviour
    {
        public float amplitude = 0.06f;
        [Tooltip("Seconds per bob.")]
        public float period = 4f;
        [Tooltip("Degrees per second about the object's own up axis.")]
        public float spin = 8f;
        [Range(0f, 1f)] public float phase;

        Vector3 origin;

        void Awake() => origin = transform.localPosition;

        void Update()
        {
            float t = (Time.time / Mathf.Max(0.1f, period) + phase) * Mathf.PI * 2f;
            transform.localPosition = origin + Vector3.up * (Mathf.Sin(t) * amplitude);
            transform.Rotate(Vector3.up, spin * Time.deltaTime, Space.Self);
        }
    }
}
