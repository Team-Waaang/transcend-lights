using UnityEngine;

namespace AiHackathon.Game
{
    /// <summary>
    /// The main screen's camera: drifts slowly round its framing (incommensurate periods, so the motion never visibly
    /// repeats), breathes its field of view, and on <see cref="Push"/> eases forward into the scene.
    /// </summary>
    public class TitleCamera : MonoBehaviour
    {
        public Vector3 lookAt = new Vector3(0f, 1.3f, 0f);
        public Vector3 drift = new Vector3(0.14f, 0.05f, 0.08f);
        public Vector3 driftPeriods = new Vector3(19f, 13f, 23f);
        public float roll = 0.35f;
        public float fovBreath = 0.4f;

        [Header("Push in")]
        public float pushDistance = 0.7f;
        public float pushFov = 5f;
        public float pushTime = 1.1f;

        Vector3 home;
        float baseFov, pushedAt = -1f;
        Camera cam;

        void Awake()
        {
            cam = GetComponent<Camera>();
            home = transform.position;
            baseFov = cam != null ? cam.fieldOfView : 35f;
        }

        public void Push()
        {
            if (pushedAt < 0f) pushedAt = Time.unscaledTime;
        }

        void LateUpdate()
        {
            float t = Time.unscaledTime;
            float W(float period, float phase) => Mathf.Sin((t / Mathf.Max(0.1f, period) + phase) * Mathf.PI * 2f);
            var offset = new Vector3(drift.x * W(driftPeriods.x, 0f), drift.y * W(driftPeriods.y, 0.3f), drift.z * W(driftPeriods.z, 0.6f));
            float push = 0f;
            if (pushedAt >= 0f)
            {
                float k = Mathf.Clamp01((t - pushedAt) / pushTime);
                push = k * k * k;
            }
            var pos = home + offset;
            var forward = (lookAt - pos).normalized;
            pos += forward * (pushDistance * push);
            // The look point drifts at a third of the camera, so the frame turns as well as slides.
            var look = lookAt + offset * 0.33f;
            transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos) * Quaternion.Euler(0f, 0f, roll * W(29f, 0.15f)));
            if (cam != null) cam.fieldOfView = baseFov + fovBreath * W(17f, 0.8f) - pushFov * push;
        }
    }
}
