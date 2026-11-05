using UnityEngine;

namespace AiHackathon.Game
{
    /// <summary>A glow sprite that always faces the camera and breathes in size (wand tips, the palm light, the moon halo).</summary>
    public class GlowSprite : MonoBehaviour
    {
        public float pulse = 0.08f;
        public float period = 2.4f;
        [Tooltip("Optional light that breathes with it.")]
        public Light lit;

        Vector3 baseScale;
        float baseIntensity, seed;

        void Awake()
        {
            baseScale = transform.localScale;
            if (lit != null) baseIntensity = lit.intensity;
            seed = Random.value;
        }

        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam != null) transform.rotation = cam.transform.rotation;
            float k = 1f + pulse * Mathf.Sin((Time.time / Mathf.Max(0.1f, period) + seed) * Mathf.PI * 2f);
            transform.localScale = baseScale * k;
            if (lit != null) lit.intensity = baseIntensity * (0.8f + 0.2f * k);
        }
    }
}
