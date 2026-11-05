using TMPro;
using UnityEngine;

namespace AiHackathon.Game
{
    /// <summary>
    /// The title logo: letters rise and fade in one after another, coloured along the word from Amelia's gold through
    /// white to Nox's blue; every few seconds a band of light sweeps across them, and the whole line floats a little.
    /// Works on the TMP mesh (vertex colours and positions), so the material's glow and the scene's bloom stay on.
    /// </summary>
    [RequireComponent(typeof(TMP_Text))]
    public class TitleLogo : MonoBehaviour
    {
        public Color left = new Color(1f, 0.7f, 0.34f);
        public Color middle = new Color(1f, 0.97f, 0.92f);
        public Color right = new Color(0.6f, 0.83f, 1f);
        [Range(0f, 1f), Tooltip("Letters rest at this brightness; the sweep lifts them to full.")]
        public float rest = 0.9f;

        [Header("Reveal")]
        public float perLetter = 0.075f;
        public float letterTime = 0.7f;
        [Tooltip("Rise in canvas units while a letter fades in.")]
        public float rise = 26f;

        [Header("Sweep")]
        public float sweepEvery = 6.5f;
        public float sweepTime = 1.5f;
        [Tooltip("Width of the light band, in letters.")]
        public float sweepWidth = 2.6f;

        [Header("Float")]
        public float floatAmount = 5f;
        public float floatPeriod = 7f;

        TMP_Text text;
        float startAt = float.PositiveInfinity;
        Vector3[][] baseVertices;

        void Awake()
        {
            text = GetComponent<TMP_Text>();
        }

        /// <summary>Starts the reveal <paramref name="delay"/> seconds from now.</summary>
        public void Play(float delay)
        {
            startAt = Time.unscaledTime + delay;
        }

        /// <summary>Shows every letter now.</summary>
        public void Finish()
        {
            float done = Time.unscaledTime - (text != null ? text.textInfo.characterCount : 20) * perLetter - letterTime;
            startAt = Mathf.Min(startAt, done);
        }

        void LateUpdate()
        {
            if (text == null) return;
            text.ForceMeshUpdate();
            var info = text.textInfo;
            int count = info.characterCount;
            if (count == 0) return;
            float now = Time.unscaledTime;
            float t = now - startAt;

            // Sweep position in letters (-width .. count + width), or off when it isn't sweeping.
            float sinceReveal = t - (count * perLetter + letterTime);
            float sweep = float.NaN;
            if (sinceReveal > 0f)
            {
                float k = Mathf.Repeat(sinceReveal, sweepEvery) / sweepTime;
                if (k < 1f) sweep = Mathf.Lerp(-sweepWidth, count + sweepWidth, Mathf.SmoothStep(0f, 1f, k));
            }
            float bob = floatAmount * Mathf.Sin(now / Mathf.Max(0.1f, floatPeriod) * Mathf.PI * 2f);

            int visibleIndex = 0, visibleCount = 0;
            for (int i = 0; i < count; i++) if (info.characterInfo[i].isVisible) visibleCount++;
            for (int i = 0; i < count; i++)
            {
                var ch = info.characterInfo[i];
                if (!ch.isVisible) continue;
                float u = visibleCount > 1 ? visibleIndex / (visibleCount - 1f) : 0.5f;
                visibleIndex++;
                var baseColor = u < 0.5f ? Color.Lerp(left, middle, u * 2f) : Color.Lerp(middle, right, u * 2f - 1f);

                float local = Mathf.Clamp01((t - i * perLetter) / letterTime);
                float appear = 1f - (1f - local) * (1f - local) * (1f - local);
                float light = rest;
                if (!float.IsNaN(sweep))
                {
                    float d = Mathf.Abs(i - sweep) / sweepWidth;
                    light = Mathf.Lerp(rest, 1f, Mathf.Clamp01(1f - d * d));
                }
                // Each letter flares a touch as it lands.
                light = Mathf.Min(1f, light + 0.25f * Mathf.Sin(local * Mathf.PI));
                var c = new Color(baseColor.r * light, baseColor.g * light, baseColor.b * light, appear);
                var c32 = (Color32)c;

                int mat = ch.materialReferenceIndex, v = ch.vertexIndex;
                var colors = info.meshInfo[mat].colors32;
                var verts = info.meshInfo[mat].vertices;
                var lift = new Vector3(0f, bob - rise * (1f - appear), 0f);
                for (int k = 0; k < 4; k++)
                {
                    colors[v + k] = c32;
                    verts[v + k] += lift;
                }
            }
            text.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32 | TMP_VertexDataUpdateFlags.Vertices);
        }
    }
}
