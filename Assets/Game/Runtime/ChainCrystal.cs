using System;
using UnityEngine;

namespace AiHackathon.Game
{
    /// <summary>
    /// The anchor of the curse chain (<see cref="BossController"/>): a floating curse crystal with a chain of violet
    /// light running from it to the bound player's chest. The other player breaks it with <see cref="hp"/> seconds of
    /// laser; it shakes and brightens as it cracks.
    /// </summary>
    public class ChainCrystal : MonoBehaviour
    {
        public float hp = 1.2f;
        public Transform crystal;
        public LineRenderer chain;
        public Light glow;
        [Tooltip("How far the chain sags (m) and how much it writhes.")]
        public float sag = 0.35f;
        public float writhe = 0.06f;

        public event Action<ChainCrystal> Broken;
        public Transform Target { get; private set; }
        public bool Done { get; private set; }
        public float Fill => maxHp > 0f ? Mathf.Clamp01(hp / maxHp) : 0f;

        float maxHp, hitFlash, baseGlow, appear;
        Vector3 home, baseScale;

        void Awake()
        {
            if (crystal == null) crystal = transform;
            baseScale = crystal.localScale;
            if (glow != null) baseGlow = glow.intensity;
        }

        public void Bind(Transform target, float seconds)
        {
            Target = target;
            hp = maxHp = Mathf.Max(0.1f, seconds);
            home = transform.position;
            appear = 0f;
        }

        public void Damage(float seconds)
        {
            if (Done) return;
            hp -= seconds;
            hitFlash = 1f;
            if (hp > 0f) return;
            Done = true;
            Broken?.Invoke(this);
        }

        void Update()
        {
            float t = Time.time;
            appear = Mathf.Min(1f, appear + Time.deltaTime / 0.3f);
            hitFlash = Mathf.Max(0f, hitFlash - Time.deltaTime * 5f);
            float cracked = 1f - Fill;
            var shake = new Vector3(Mathf.Sin(t * 53f), Mathf.Sin(t * 47f + 1f), Mathf.Sin(t * 41f + 2f)) * (0.012f * hitFlash + 0.006f * cracked);
            transform.position = home + Vector3.up * (0.05f * Mathf.Sin(t * 2f)) + shake;
            crystal.Rotate(Vector3.up, 60f * Time.deltaTime, Space.World);
            float pop = 1f - (1f - appear) * (1f - appear) + 0.15f * Mathf.Sin(appear * Mathf.PI);
            crystal.localScale = baseScale * pop * (1f + 0.15f * hitFlash);
            if (glow != null) glow.intensity = baseGlow * (1f + 1.5f * hitFlash + cracked);

            if (chain == null || Target == null) return;
            int n = chain.positionCount;
            var a = crystal.position;
            var b = Target.position;
            for (int i = 0; i < n; i++)
            {
                float u = i / (n - 1f);
                var p = Vector3.Lerp(a, b, u) + Vector3.down * (sag * 4f * u * (1f - u));
                p += new Vector3(0f, Mathf.Sin(u * 9f - t * 7f), Mathf.Cos(u * 7f - t * 6f)) * (writhe * Mathf.Sin(u * Mathf.PI));
                chain.SetPosition(i, Vector3.Lerp(a, p, appear));
            }
        }
    }
}
