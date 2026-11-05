using UnityEngine;

namespace AiHackathon.Game
{
    /// <summary>Candle-like flicker of a light's intensity (and an optional glow's scale) round its starting value.</summary>
    public class LightFlicker : MonoBehaviour
    {
        public float amount = 0.18f;
        public float speed = 2.2f;
        public Transform glow;

        Light lit;
        float baseIntensity, seed;
        Vector3 glowScale;

        void Awake()
        {
            lit = GetComponent<Light>();
            if (lit != null) baseIntensity = lit.intensity;
            if (glow != null) glowScale = glow.localScale;
            seed = Random.value * 100f;
        }

        void Update()
        {
            float n = Mathf.PerlinNoise(seed, Time.time * speed) * 2f - 1f;
            n += 0.35f * (Mathf.PerlinNoise(seed + 7f, Time.time * speed * 3.1f) * 2f - 1f);
            if (lit != null) lit.intensity = baseIntensity * (1f + amount * n);
            if (glow != null) glow.localScale = glowScale * (1f + amount * 0.5f * n);
        }
    }
}
