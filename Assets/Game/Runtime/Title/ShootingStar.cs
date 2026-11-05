using UnityEngine;

namespace AiHackathon.Game
{
    /// <summary>
    /// Now and then a shooting star streaks across the sky: the object (with a trail) jumps to a random point high
    /// in the view, flies a short way down and across, then waits for the next one.
    /// </summary>
    [RequireComponent(typeof(TrailRenderer))]
    public class ShootingStar : MonoBehaviour
    {
        public Vector2 interval = new Vector2(5f, 11f);
        public float distance = 34f;
        [Tooltip("Elevation range (degrees) of the start point, seen from the origin.")]
        public Vector2 elevation = new Vector2(13f, 24f);
        [Tooltip("Azimuth range (degrees) round -Z.")]
        public Vector2 azimuth = new Vector2(-26f, 26f);
        public float speed = 26f;
        public Vector2 flight = new Vector2(0.45f, 0.8f);

        TrailRenderer trail;
        float nextAt, endAt;
        Vector3 velocity;
        bool flying;

        void Awake()
        {
            trail = GetComponent<TrailRenderer>();
            trail.emitting = false;
            nextAt = Time.time + Random.Range(1.5f, 4f);
        }

        void Update()
        {
            if (!flying && Time.time >= nextAt)
            {
                float el = Random.Range(elevation.x, elevation.y) * Mathf.Deg2Rad;
                float az = Random.Range(azimuth.x, azimuth.y) * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Sin(az) * Mathf.Cos(el), Mathf.Sin(el), -Mathf.Cos(az) * Mathf.Cos(el));
                trail.emitting = false;
                transform.position = dir * distance;
                trail.Clear();
                // Down and across, away from the middle of the frame.
                float side = Mathf.Sign(az == 0f ? 1f : az);
                velocity = new Vector3(side * Random.Range(0.75f, 1f), -Random.Range(0.35f, 0.6f), 0f).normalized * speed;
                endAt = Time.time + Random.Range(flight.x, flight.y);
                flying = true;
                trail.emitting = true;
            }
            if (!flying) return;
            transform.position += velocity * Time.deltaTime;
            if (Time.time >= endAt)
            {
                flying = false;
                trail.emitting = false;
                nextAt = Time.time + Random.Range(interval.x, interval.y);
            }
        }
    }
}
