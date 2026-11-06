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

        // Spawn burst: the share of the arc covered in the first half second.
        const float BurstSeconds = 0.5f;
        const float BurstReach = 0.3f;

        Vector3 from, control;
        Transform target;
        Vector3 targetOffset;
        float travel = 5f, t;
        float hitFlash;
        Vector3 baseScale;
        bool done;

        public bool Done => done;
        /// <summary>What it flies at (a player's chest).</summary>
        public Transform Target => target;

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
            GameAudio.Play("orb_launch", transform);
            baseScale = transform.localScale;
            t = 0f;
        }

        void Update()
        {
            if (done) return;
            t += Time.deltaTime / travel;
            var end = target != null ? target.position + targetOffset : from;
            // A fast burst out along the arc for the first BurstSeconds (the volley fans out), easing to a near stop;
            // then the rest of the way slow at first, faster as it closes in. Total travel time is unchanged.
            float elapsed = t * travel;
            float e;
            if (elapsed < BurstSeconds)
            {
                float u = elapsed / BurstSeconds;
                e = BurstReach * (1f - (1f - u) * (1f - u) * (1f - u));
            }
            else
            {
                float k = Mathf.Clamp01((elapsed - BurstSeconds) / Mathf.Max(0.01f, travel - BurstSeconds));
                e = BurstReach + (1f - BurstReach) * (k * k * (1.6f - 0.6f * k));
            }
            var a = Vector3.Lerp(from, control, e);
            var b = Vector3.Lerp(control, end, e);
            transform.position = Vector3.Lerp(a, b, e) + Vector3.up * (Mathf.Sin(Time.time * 7f + GetInstanceID()) * 0.03f);
            hitFlash = Mathf.Max(0f, hitFlash - Time.deltaTime * 6f);
            transform.localScale = baseScale * (1f + 0.35f * hitFlash);
            if (t >= 1f)
            {
                GameAudio.Play("curse_impact", transform);
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
            // Laser contact sizzle; the catalog cooldown keeps a held beam from stacking it every frame.
            if (hp > 0f) { GameAudio.Play("orb_hit", transform); return false; }
            GameAudio.Play("orb_break", transform);
            done = true;
            Popped?.Invoke(this);
            return true;
        }
    }
}
