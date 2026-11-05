using System;
using UnityEngine;

namespace AiHackathon.Game
{
    /// <summary>
    /// A false copy of the boss for the clone pattern (<see cref="BossController"/>): it floats, bobs and faces the
    /// players like him, but every second or so it wavers (a quick squash and a sideways jitter): the tell.
    /// A laser held on it for <see cref="hp"/> seconds bursts it, and the burst fires back at whoever shot it.
    /// </summary>
    public class BossDecoy : MonoBehaviour
    {
        public float hp = 0.25f;
        public float hover = 0.14f;
        public float bob = 0.05f;

        /// <summary>Burst by a laser; the int is the shooter (GameManager player index).</summary>
        public event Action<BossDecoy, int> Popped;

        public Transform[] targets = new Transform[0];
        public bool Done { get; private set; }

        Vector3 ground, baseScale;
        float appear, seed, nextWaver, waver;

        void Awake()
        {
            baseScale = transform.localScale;
            seed = UnityEngine.Random.value * 10f;
        }

        /// <summary>Puts it at <paramref name="spot"/> (on the floor) and lets it swell into view.</summary>
        public void Place(Vector3 spot)
        {
            ground = spot;
            appear = 0f;
            nextWaver = Time.time + UnityEngine.Random.Range(0.6f, 1.2f);
            transform.position = spot + Vector3.up * hover;
        }

        public void Damage(float seconds, int shooter)
        {
            if (Done) return;
            hp -= seconds;
            waver = Mathf.Max(waver, 0.6f);
            if (hp > 0f) return;
            Done = true;
            Popped?.Invoke(this, shooter);
        }

        void Update()
        {
            appear = Mathf.Min(1f, appear + Time.deltaTime / 0.22f);
            float t = Time.time + seed;
            transform.position = ground + Vector3.up * (hover + bob * Mathf.Sin(t * 2.4f) + 0.015f * Mathf.Sin(t * 5.3f));

            if (targets.Length > 0)
            {
                var mid = Vector3.zero;
                foreach (var tr in targets) if (tr != null) mid += tr.position;
                mid /= targets.Length;
                var flat = Vector3.ProjectOnPlane(mid - transform.position, Vector3.up);
                if (flat.sqrMagnitude > 0.01f)
                    transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(flat.normalized), 1f - Mathf.Exp(-4f * Time.deltaTime));
            }

            // The tell: now and then it wavers like a reflection in water.
            if (Time.time >= nextWaver)
            {
                waver = 1f;
                nextWaver = Time.time + UnityEngine.Random.Range(0.9f, 1.6f);
            }
            waver = Mathf.Max(0f, waver - Time.deltaTime * 3.2f);
            float w = waver * waver;
            float pop = appear < 1f ? 1f - (1f - appear) * (1f - appear) + 0.12f * Mathf.Sin(appear * Mathf.PI) : 1f;
            var squash = new Vector3(1f + 0.12f * w * Mathf.Sin(t * 40f), 1f - 0.1f * w, 1f + 0.12f * w * Mathf.Cos(t * 37f));
            transform.localScale = Vector3.Scale(baseScale * pop, squash);
            transform.position += transform.right * (0.06f * w * Mathf.Sin(t * 47f));
        }
    }
}
