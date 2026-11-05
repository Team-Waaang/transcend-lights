using System;
using UnityEngine;

namespace AiHackathon.Game
{
    /// <summary>
    /// A curse orb the boss throws at the players. It arcs from the wand to its target over <see cref="travel"/>
    /// seconds; a laser held on it for <see cref="hp"/> seconds pops it. If it arrives, <see cref="Reached"/> fires.
    /// </summary>
    public class CurseOrb : MonoBehaviour
    {
        [Tooltip("Seconds of laser it takes to pop.")]
        public float hp = 0.3f;
        [Tooltip("Radius of the laser hitbox (bigger than the visible orb: the laser follows the wand, which lags the pointer).")]
        public float hitRadius = 0.45f;

        public event Action<CurseOrb> Popped;
        public event Action<CurseOrb> Reached;

        Vector3 from, control;
        Transform target;
        Vector3 targetOffset;
        float travel = 5f, t;
        float hitFlash;
        Vector3 baseScale;
        bool done;

        public bool Done => done;

        void Awake()
        {
            var sphere = GetComponent<SphereCollider>();
            if (sphere != null) sphere.radius = Mathf.Max(sphere.radius, hitRadius / Mathf.Max(1e-4f, transform.lossyScale.x));
        }

        /// <summary>Arcs to <paramref name="to"/> (+offset) through a control point lifted by <paramref name="arc"/>.</summary>
        public void Launch(Vector3 start, Transform to, Vector3 offset, float seconds, Vector3 arc)
        {
            from = start;
            target = to;
            targetOffset = offset;
            travel = Mathf.Max(0.5f, seconds);
            var end = to.position + offset;
            control = Vector3.Lerp(start, end, 0.45f) + arc;
            transform.position = start;
            baseScale = transform.localScale;
            t = 0f;
        }

        void Update()
        {
            if (done) return;
            t += Time.deltaTime / travel;
            var end = target != null ? target.position + targetOffset : from;
            float k = Mathf.Clamp01(t);
            // Slow out of the wand, faster as it closes in.
            float e = k * k * (1.6f - 0.6f * k);
            var a = Vector3.Lerp(from, control, e);
            var b = Vector3.Lerp(control, end, e);
            transform.position = Vector3.Lerp(a, b, e) + Vector3.up * (Mathf.Sin(Time.time * 7f + GetInstanceID()) * 0.03f);
            hitFlash = Mathf.Max(0f, hitFlash - Time.deltaTime * 6f);
            transform.localScale = baseScale * (1f + 0.35f * hitFlash);
            if (t >= 1f)
            {
                done = true;
                Reached?.Invoke(this);
            }
        }

        /// <summary>Laser damage for <paramref name="seconds"/>; returns true when this pops it.</summary>
        public bool Damage(float seconds)
        {
            if (done) return false;
            hp -= seconds;
            hitFlash = 1f;
            if (hp > 0f) return false;
            done = true;
            Popped?.Invoke(this);
            return true;
        }
    }
}
