using UnityEngine;

namespace AiHackathon.Game
{
    /// <summary>
    /// One seal of the phase 2 barrier QTE (<see cref="BarrierQte"/>): a turning rune disc in its owner's colour that
    /// faces the camera. Only its owner's laser counts; held on it for <see cref="hp"/> seconds it breaks (the
    /// barrier takes it), and if <see cref="Window"/> runs out first it expires (the curse gets through there).
    /// It pulses faster and whitens as time runs out.
    /// </summary>
    public class QteSeal : MonoBehaviour
    {
        [Tooltip("Seconds of the owner's laser it takes to break.")]
        public float hp = 0.1f;
        public Transform disc;
        public Renderer[] tinted = new Renderer[0];
        public Light lit;

        public int Owner { get; private set; }
        public Color Color { get; private set; } = Color.white;
        public float Window { get; private set; } = 2f;
        public bool Broken { get; private set; }
        public bool Expired { get; private set; }
        public float Progress => Mathf.Clamp01((Time.time - born) / Window);

        float born, hit, baseLight;
        Vector3 baseScale;
        MaterialPropertyBlock block;

        void Awake()
        {
            baseScale = transform.localScale;
            if (lit != null) baseLight = lit.intensity;
            block = new MaterialPropertyBlock();
        }

        public void Arm(int owner, Color color, float window)
        {
            Owner = owner;
            Color = color;
            Window = Mathf.Max(0.3f, window);
            born = Time.time;
            if (lit != null) lit.color = color;
            Tint(1f);
            Face();
        }

        /// <summary>Laser on it for <paramref name="seconds"/>; true on the frame it breaks.</summary>
        public bool Shoot(float seconds)
        {
            if (Broken || Expired) return false;
            hp -= seconds;
            hit = 1f;
            if (hp > 0f) return false;
            Broken = true;
            return true;
        }

        void Update()
        {
            if (!Broken && !Expired && Time.time - born >= Window) Expired = true;
            hit = Mathf.Max(0f, hit - Time.deltaTime * 6f);
            float k = Progress;
            float appear = Mathf.Clamp01((Time.time - born) / 0.18f);
            float pop = 1f - (1f - appear) * (1f - appear) + 0.25f * Mathf.Sin(appear * Mathf.PI);
            // Urgent in the last third: a quicker, bigger pulse.
            float urgency = Mathf.Clamp01((k - 0.65f) / 0.35f);
            float pulse = 1f + (0.04f + 0.08f * urgency) * Mathf.Sin(Time.time * Mathf.Lerp(5f, 16f, urgency));
            transform.localScale = baseScale * (pop * pulse * (1f + 0.25f * hit));
            if (disc != null) disc.Rotate(Vector3.forward, Mathf.Lerp(70f, 220f, urgency) * Time.deltaTime, Space.Self);
            if (lit != null) lit.intensity = baseLight * (1f + 1.5f * hit + 0.6f * urgency);
            Tint(1f + 0.8f * hit + 0.5f * urgency, urgency);
            Face();
        }

        void Face()
        {
            var cam = Camera.main;
            if (cam != null) transform.rotation = cam.transform.rotation;
        }

        void Tint(float intensity, float whiten = 0f)
        {
            if (block == null) block = new MaterialPropertyBlock();
            var c = Color.Lerp(Color, Color.white, 0.45f * whiten) * (2f * intensity);
            foreach (var r in tinted)
            {
                if (r == null) continue;
                // Keep each layer's own opacity (the glow is fainter than the disc).
                c.a = r.sharedMaterial != null && r.sharedMaterial.HasProperty("_BaseColor") ? r.sharedMaterial.GetColor("_BaseColor").a : 1f;
                r.GetPropertyBlock(block);
                block.SetColor("_BaseColor", c);
                r.SetPropertyBlock(block);
            }
        }
    }
}
