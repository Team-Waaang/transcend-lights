using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace AiHackathon.Game
{
    /// <summary>
    /// A rune stone whose carved rune glows. It shows one <see cref="RuneDefinition"/> at a time
    /// (<see cref="SetRune"/>, or <see cref="ChangeRune"/> with a sink-and-rise swap).
    /// A cutscene timeline animates <see cref="cutsceneGlow"/> / <see cref="cutsceneHover"/>; gameplay plays
    /// <see cref="PlaySuccess"/> (spins up and flares) or <see cref="PlayFail"/> (stalls, sputters out, dust).
    /// </summary>
    [ExecuteAlways]
    public class RuneStoneFx : MonoBehaviour
    {
        public RuneDefinition rune;

        [Header("Parts (wired by the builder)")]
        [Tooltip("Rotated / scaled by the result motions. The rune model sits under it.")]
        public Transform spin;
        public Transform model;
        public Renderer stone;
        public Light runeLight;
        public BoxCollider hitBox;
        public RuneTrace trace;
        public GameObject successFx;
        public GameObject failFx;

        [Header("Look")]
        public float lightIntensity = 0.12f;
        [Tooltip("Glow the rune keeps while waiting to be traced.")]
        public float idleGlow = 0.35f;

        [Header("Events")]
        public UnityEvent onSuccess = new UnityEvent();
        public UnityEvent onFail = new UnityEvent();

        [Header("Animated by the cutscene")]
        public float cutsceneGlow;
        public float cutsceneHover;

        /// <summary>Extra glow set by game logic, e.g. a memory period showing the rune before the lights go out.</summary>
        [System.NonSerialized] public float revealGlow;
        /// <summary>Multiplies the stone's base colour (paint included); a memory period darkens it to hide the rune.</summary>
        [System.NonSerialized] public float shade = 1f;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        float resultGlow;
        float flicker = 1f;
        float size = 1f;
        Vector3 shake;
        float spinAngle;
        Coroutine running;
        MaterialPropertyBlock block;
        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        public bool IsPlaying => running != null;

        void Awake()
        {
            // A stone saved without its model (or whose model was lost) rebuilds it from its rune.
            if (Application.isPlaying && model == null && rune != null) SetRune(rune);
        }
        Color GlowColor => rune != null ? rune.glowColor : new Color(1f, 0.72f, 0.28f) * 1.8f;

        /// <summary>Shows <paramref name="definition"/> on this stone right away (edit mode too).</summary>
        public void SetRune(RuneDefinition definition)
        {
            rune = definition;
            if (model != null)
            {
                if (Application.isPlaying) Destroy(model.gameObject);
                else DestroyImmediate(model.gameObject);
                model = null;
                stone = null;
            }
            if (definition == null || definition.model == null || spin == null) return;
            var go = Instantiate(definition.model, spin, false);
            go.name = "Model";
            model = go.transform;
            model.localPosition = definition.modelPosition;
            model.localRotation = definition.modelRotation;
            model.localScale = Vector3.one * definition.modelScale;
            stone = go.GetComponentInChildren<Renderer>();
            if (stone != null && definition.material != null) stone.sharedMaterial = definition.material;
            foreach (var c in go.GetComponentsInChildren<Collider>()) DestroyImmediate(c);
            if (runeLight != null)
            {
                runeLight.transform.localPosition = definition.runeCenter + Vector3.back * 0.9f;
                var c = definition.glowColor;
                float max = Mathf.Max(c.r, Mathf.Max(c.g, c.b, 0.001f));
                runeLight.color = new Color(c.r / max, c.g / max, c.b / max);
            }
            if (hitBox != null)
            {
                hitBox.center = definition.bounds.center;
                hitBox.size = definition.bounds.size;
            }
            // Bursts that belong on the rune itself follow it from stone to stone.
            foreach (var fx in new[] { successFx, failFx })
            {
                var at = fx != null ? fx.transform.Find("AtRune") : null;
                if (at != null) at.localPosition = definition.runeCenter + Vector3.back * 0.08f;
            }
            if (trace != null) trace.SetRune(definition);
        }

        /// <summary>The stone sinks away in a puff of dust and comes back carrying <paramref name="next"/>.</summary>
        public void ChangeRune(RuneDefinition next) => Run(Change(next));

        public void PlaySuccess() => Run(Success());
        public void PlayFail() => Run(Fail());

        void Run(IEnumerator routine)
        {
            if (!Application.isPlaying) return;
            if (running != null) StopCoroutine(running);
            resultGlow = 0f; flicker = 1f; shake = Vector3.zero; spinAngle = 0f; size = 1f;
            running = StartCoroutine(Wrap(routine));
        }

        IEnumerator Wrap(IEnumerator routine)
        {
            yield return routine;
            running = null;
        }

        static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

        // Lifts a little, turns once with an ease that rushes the middle, the rune flares and a soft column of light
        // rises; then it settles back down still lit.
        IEnumerator Success()
        {
            Burst(successFx);
            onSuccess.Invoke();
            const float dur = 1.6f;
            for (float t = 0f; t < dur; t += Time.deltaTime)
            {
                float k = t / dur;
                spinAngle = 360f * Smooth(Smooth(k));
                resultGlow = Mathf.Sin(Mathf.Clamp01(k * 1.6f) * Mathf.PI * 0.5f) * 1.0f * (1f - 0.45f * Smooth((k - 0.6f) / 0.4f));
                shake = Vector3.up * (0.12f * Mathf.Sin(Mathf.Clamp01(k) * Mathf.PI));
                yield return null;
            }
            spinAngle = 0f;
            shake = Vector3.zero;
            Settle(successFx);
            for (float t = 0f; t < 0.8f; t += Time.deltaTime)
            {
                resultGlow = Mathf.Lerp(0.55f, 0.35f, Smooth(t / 0.8f));
                yield return null;
            }
            resultGlow = 0.35f;
        }

        // Tries to turn, stalls and twists back; the rune sputters and goes dark, a puff of grey dust, then the
        // idle glow creeps back so the player can try again.
        IEnumerator Fail()
        {
            onFail.Invoke();
            const float dur = 1.5f;
            bool puffed = false;
            for (float t = 0f; t < dur; t += Time.deltaTime)
            {
                float k = t / dur;
                spinAngle = 55f * Smooth(k / 0.3f) * (1f - Smooth((k - 0.35f) / 0.4f))
                            - 12f * Mathf.Sin(Mathf.Clamp01((k - 0.6f) / 0.4f) * Mathf.PI);
                float s = Mathf.Clamp01((k - 0.3f) / 0.5f);
                shake = new Vector3(Mathf.Sin(t * 70f), 0f, Mathf.Sin(t * 53f + 1f)) * (0.012f * (1f - s) * Smooth(k / 0.3f));
                flicker = k < 0.25f ? 1f : Mathf.Clamp01((Mathf.PerlinNoise(t * 22f, 0.3f) > 0.45f ? 1f : 0.15f) * (1f - Smooth((k - 0.3f) / 0.4f)));
                resultGlow = 0.5f * (1f - Smooth(k / 0.6f));
                if (!puffed && k > 0.38f) { puffed = true; Burst(failFx); }
                if (puffed && k > 0.75f) Settle(failFx);
                yield return null;
            }
            shake = Vector3.zero;
            spinAngle = 0f;
            for (float t = 0f; t < 1.2f; t += Time.deltaTime)
            {
                flicker = Smooth(t / 1.2f);
                yield return null;
            }
            flicker = 1f;
        }

        IEnumerator Change(RuneDefinition next)
        {
            float glowFrom = resultGlow;
            for (float t = 0f; t < 0.4f; t += Time.deltaTime)
            {
                float k = Smooth(t / 0.4f);
                size = 1f - k;
                spinAngle = -90f * k;
                resultGlow = glowFrom * (1f - k);
                yield return null;
            }
            SetRune(next);
            resultGlow = 0f;
            for (float t = 0f; t < 0.55f; t += Time.deltaTime)
            {
                float k = t / 0.55f;
                // Ease out with a little overshoot.
                float back = 1f + 2.2f * Mathf.Pow(k - 1f, 3f) + 1.2f * Mathf.Pow(k - 1f, 2f);
                size = back;
                spinAngle = 90f * (1f - Smooth(k));
                yield return null;
            }
            size = 1f;
            spinAngle = 0f;
        }

        static void Burst(GameObject fx)
        {
            if (fx == null) return;
            fx.SetActive(false);
            fx.SetActive(true);
            foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true))
            {
                ps.Clear(true);
                ps.Play(true);
            }
        }

        static void Settle(GameObject fx)
        {
            if (fx == null) return;
            foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true))
                if (ps.isEmitting) ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
        }

        // Recolours the success effect to the rune's own colour, keeping each layer's alpha.
        static void TintFx(GameObject fx, Color hdr)
        {
            if (fx == null) return;
            float max = Mathf.Max(hdr.r, Mathf.Max(hdr.g, hdr.b, 0.001f));
            var c = new Color(hdr.r / max, hdr.g / max, hdr.b / max);
            foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                var start = main.startColor;
                if (start.mode == ParticleSystemGradientMode.Color)
                    main.startColor = new Color(c.r, c.g, c.b, start.color.a);
                else if (start.mode == ParticleSystemGradientMode.TwoColors)
                    main.startColor = new ParticleSystem.MinMaxGradient(new Color(c.r, c.g, c.b, start.colorMin.a), new Color(c.r, c.g, c.b, start.colorMax.a));
            }
        }

        void LateUpdate()
        {
            float glow = (Mathf.Max(cutsceneGlow, 0f) + resultGlow + Mathf.Max(revealGlow, 0f)) * flicker;
            if (stone != null)
            {
                block ??= new MaterialPropertyBlock();
                stone.GetPropertyBlock(block);
                block.SetColor(EmissionColor, GlowColor * glow);
                block.SetColor(BaseColor, new Color(shade, shade, shade, 1f));
                stone.SetPropertyBlock(block);
            }
            if (runeLight != null) runeLight.intensity = lightIntensity * glow;
            if (spin != null)
            {
                spin.localPosition = Vector3.up * cutsceneHover + shake;
                spin.localRotation = Quaternion.Euler(0f, spinAngle, 0f);
                spin.localScale = Vector3.one * Mathf.Max(0.001f, size);
            }
        }
    }
}
