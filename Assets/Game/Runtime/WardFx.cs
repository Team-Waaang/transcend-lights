using UnityEngine;

namespace AiHackathon.Game
{
    /// <summary>
    /// The boss's ward: a force-field shell (AiHackathon/Ward shader) with rune rings, a seal under it and crackling
    /// energy. Raise() pops it up with an overshoot, Hit() sends a ripple from the laser's hit point and throws sparks,
    /// Strength cracks and darkens the cells as it weakens, Break() dissolves it cell by cell in a burst, Drop() fades it.
    /// </summary>
    public class WardFx : MonoBehaviour
    {
        public Renderer shell;
        public Collider hitCollider;
        [Tooltip("Shown with the ward (rings, seal, energy); they scale in and out with it.")]
        public Transform dressing;
        public GameObject raiseFx, sparkFx, breakFx, shardFx;
        public Light glow;
        public float glowIntensity = 2.5f;

        public bool Up { get; private set; }
        public bool Visible => gameObject.activeSelf;

        /// <summary>0..1 ward health; cracks show as it drops.</summary>
        public float Strength { get; set; } = 1f;

        enum Mode { Hidden, Raising, Holding, Breaking, Dropping }
        Mode mode = Mode.Hidden;
        float modeTime, hit, lastSpark = -1f;
        Vector3 hitPos;
        Vector3 baseScale = Vector3.one;
        MaterialPropertyBlock block;
        static readonly int StrengthId = Shader.PropertyToID("_Strength");
        static readonly int HitId = Shader.PropertyToID("_Hit");
        static readonly int HitPosId = Shader.PropertyToID("_HitPos");
        static readonly int DissolveId = Shader.PropertyToID("_Dissolve");
        static readonly int AlphaId = Shader.PropertyToID("_Alpha");

        void Awake()
        {
            baseScale = transform.localScale;
            block = new MaterialPropertyBlock();
            // Saved inactive by the builder; the first Raise() activates it (and runs this).
        }

        public void Raise()
        {
            GameAudio.Play("ward_raise", transform);
            gameObject.SetActive(true);
            mode = Mode.Raising;
            modeTime = 0f;
            Strength = 1f;
            hit = 0f;
            Up = true;
            if (hitCollider != null) hitCollider.enabled = true;
            FxUtil.Burst(raiseFx, transform.position);
        }

        public void Hit(Vector3 point)
        {
            if (!Up) return;
            hit = 1f;
            hitPos = point;
            if (Time.time - lastSpark > 0.09f)
            {
                lastSpark = Time.time;
                GameAudio.Play("ward_hit", transform);
                FxUtil.Burst(sparkFx, point, 1.5f);
            }
        }

        public void Break()
        {
            if (!Up) return;
            GameAudio.Play("ward_break", transform);
            Up = false;
            if (hitCollider != null) hitCollider.enabled = false;
            mode = Mode.Breaking;
            modeTime = 0f;
            FxUtil.Burst(breakFx, transform.position);
            FxUtil.Burst(shardFx, transform.position);
        }

        public void Drop()
        {
            if (!Visible) return;
            GameAudio.Play("ward_expire", transform);
            Up = false;
            if (hitCollider != null) hitCollider.enabled = false;
            mode = Mode.Dropping;
            modeTime = 0f;
        }

        /// <summary>Gone at once (phase end).</summary>
        public void Clear()
        {
            Up = false;
            mode = Mode.Hidden;
            gameObject.SetActive(false);
        }

        void Update()
        {
            GameAudio.Loop("ward_loop", transform, Up ? Mathf.Lerp(0.4f, 1f, Strength) : 0f);
            modeTime += Time.deltaTime;
            hit = Mathf.Max(0f, hit - Time.deltaTime * 2.2f);
            float scale = 1f, dissolve = 0f, alpha = 1f;
            switch (mode)
            {
                case Mode.Raising:
                {
                    // Elastic pop: overshoot then settle.
                    float k = Mathf.Clamp01(modeTime / 0.55f);
                    scale = 1f - Mathf.Exp(-7f * k) * Mathf.Cos(k * 11f);
                    alpha = Mathf.Clamp01(modeTime / 0.2f);
                    if (k >= 1f) mode = Mode.Holding;
                    break;
                }
                case Mode.Holding:
                    scale = 1f + 0.015f * Mathf.Sin(Time.time * 3.1f) + 0.04f * hit;
                    break;
                case Mode.Breaking:
                {
                    float k = Mathf.Clamp01(modeTime / 0.5f);
                    scale = 1f + 0.18f * k;
                    dissolve = k;
                    if (k >= 1f) Clear();
                    break;
                }
                case Mode.Dropping:
                {
                    float k = Mathf.Clamp01(modeTime / 0.35f);
                    scale = 1f - 0.4f * k * k;
                    alpha = 1f - k;
                    if (k >= 1f) Clear();
                    break;
                }
            }
            transform.localScale = baseScale * scale;
            if (dressing != null) dressing.localScale = Vector3.one * Mathf.Clamp01(alpha) * (mode == Mode.Breaking ? 1f - dissolve : 1f);
            if (glow != null) glow.intensity = glowIntensity * alpha * (1f - dissolve) * (0.7f + 0.3f * Strength) + hit * 3f;
            if (shell != null)
            {
                shell.GetPropertyBlock(block);
                block.SetFloat(StrengthId, Strength);
                block.SetFloat(HitId, hit);
                block.SetVector(HitPosId, hitPos);
                block.SetFloat(DissolveId, dissolve);
                block.SetFloat(AlphaId, alpha);
                shell.SetPropertyBlock(block);
            }
        }
    }

    /// <summary>One-shot effects from disabled templates.</summary>
    public static class FxUtil
    {
        public static GameObject Burst(GameObject template, Vector3 at, float life = 3f)
        {
            if (template == null) return null;
            var fx = Object.Instantiate(template, template.transform.parent);
            fx.transform.position = at;
            fx.SetActive(true);
            foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>()) ps.Play(true);
            Object.Destroy(fx, life);
            return fx;
        }
    }
}
