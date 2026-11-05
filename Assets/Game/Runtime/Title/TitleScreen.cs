using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace AiHackathon.Game
{
    /// <summary>
    /// The main screen: the live terrace scene comes up out of black, the logo writes itself in letter by letter,
    /// then the ornament, the prompt and the credit. Enter / Space / a click (once the logo has begun) flashes the
    /// screen, pushes the camera in and hands over to the connection screen through <see cref="PeriodFlow"/>.
    /// </summary>
    public class TitleScreen : MonoBehaviour
    {
        public TitleLogo logo;
        public CanvasGroup ornament, prompt, credit;
        public Image black, flash;
        public TitleCamera cameraRig;
        [Tooltip("Scene the title hands over to.")]
        public string nextScene = PeriodFlow.ConnectionScene;

        [Header("Timing (s)")]
        public float fadeIn = 2.2f;
        public float logoAt = 1.1f;
        public float ornamentAt = 2.5f;
        public float promptAt = 3.3f;

        float t0, startedAt = -1f;

        void OnEnable()
        {
            t0 = Time.unscaledTime;
            if (logo != null) logo.Play(logoAt);
            Apply(0f);
        }

        void Update()
        {
            float t = Time.unscaledTime - t0;
            if (startedAt < 0f && t > 0.6f && Pressed())
            {
                startedAt = Time.unscaledTime;
                // A press during the reveal finishes it at once, so the flash lands on the whole logo.
                if (logo != null) logo.Finish();
                if (cameraRig != null) cameraRig.Push();
            }
            Apply(t);
            if (startedAt >= 0f && Time.unscaledTime - startedAt > 0.45f && !handedOver)
            {
                handedOver = true;
                PeriodFlow.Go(nextScene, "", "");
            }
        }

        bool handedOver;

        static bool Pressed()
        {
            var k = Keyboard.current;
            var m = Mouse.current;
            return (k != null && (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame || k.spaceKey.wasPressedThisFrame))
                || (m != null && m.leftButton.wasPressedThisFrame);
        }

        void Apply(float t)
        {
            if (black != null) SetAlpha(black, 1f - Smooth01(t / fadeIn));
            if (ornament != null) ornament.alpha = Smooth01((t - ornamentAt) / 1.2f);
            if (credit != null) credit.alpha = 0.5f * Smooth01((t - promptAt - 0.6f) / 1.5f);
            if (prompt != null)
            {
                float on = Smooth01((t - promptAt) / 1f);
                // A slow breath, quickened into a bright blink once the player has pressed.
                float breath = 0.62f + 0.38f * Mathf.Sin((t - promptAt) * 2.1f);
                if (startedAt >= 0f) breath = Mathf.Repeat((Time.unscaledTime - startedAt) * 8f, 1f) < 0.5f ? 1f : 0.35f;
                prompt.alpha = on * breath;
            }
            if (flash != null)
            {
                float f = startedAt < 0f ? 0f : Time.unscaledTime - startedAt;
                SetAlpha(flash, startedAt < 0f ? 0f : 0.55f * Mathf.Clamp01(f / 0.06f) * (1f - Smooth01((f - 0.06f) / 0.5f)));
            }
        }

        static float Smooth01(float x) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(x));

        static void SetAlpha(Graphic g, float a)
        {
            var c = g.color;
            c.a = a;
            g.color = c;
            g.enabled = a > 0.001f;
        }
    }
}
