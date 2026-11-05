using UnityEngine;

namespace AiHackathon.Cutscene
{
    /// <summary>Constant rotation about a local axis, with an optional slow wobble (rune rings, magic circles).</summary>
    public class Spin : MonoBehaviour
    {
        public Vector3 axis = Vector3.up;
        [Tooltip("Degrees per second.")]
        public float speed = 30f;
        [Tooltip("Degrees of wobble about local X.")]
        public float wobble;
        public float wobblePeriod = 3f;

        Quaternion rest;

        void Awake() => rest = transform.localRotation;

        void Update()
        {
            float w = wobble != 0f ? Mathf.Sin(Time.time / Mathf.Max(0.1f, wobblePeriod) * Mathf.PI * 2f) * wobble : 0f;
            transform.localRotation = rest * Quaternion.AngleAxis(w, Vector3.right) * Quaternion.AngleAxis(Time.time * speed, axis);
        }
    }
}
