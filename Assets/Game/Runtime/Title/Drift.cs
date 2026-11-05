using UnityEngine;

namespace AiHackathon.Game
{
    /// <summary>Slides an object along <see cref="velocity"/>, wrapping it back within ±<see cref="span"/>/2 on that line (drifting clouds).</summary>
    public class Drift : MonoBehaviour
    {
        public Vector3 velocity = new Vector3(0.3f, 0f, 0f);
        public float span = 120f;

        Vector3 origin;
        float offset;

        void Awake() => origin = transform.position;

        void Update()
        {
            float speed = velocity.magnitude;
            if (speed < 1e-5f) return;
            offset = Mathf.Repeat(offset + speed * Time.deltaTime + span * 0.5f, span) - span * 0.5f;
            transform.position = origin + velocity / speed * offset;
        }
    }
}
