using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace AiHackathon.Parchment
{
    [Serializable]
    public struct TitleCardInfo
    {
        [Tooltip("Large title in the middle, e.g. 룬")]
        public string title;
        [Tooltip("Optional small line under the title")]
        public string subtitle;

        public TitleCardInfo(string title, string subtitle = "")
        {
            this.title = title;
            this.subtitle = subtitle;
        }
    }

    /// <summary>
    /// Period title card drawn as a parchment scroll: two rolls unwind outward from the center, a gold glint sweeps
    /// the sheet and the title is written one character at a time. Closing reverses it.
    /// Port of animation/parchment-dialogue.html.
    /// </summary>
    /// <remarks>
    /// <c>yield return card.Play(info);</c> runs the whole open → hold → close sequence.
    /// <see cref="Show"/> / <see cref="Hide"/> leave the timing to the caller. <see cref="Advance"/> skips ahead.
    /// </remarks>
    public class ParchmentTitleCard : MonoBehaviour
    {
        public enum Phase { Closed, Opening, Open, Closing }

        [Header("Geometry (canvas units)")]
        [Tooltip("Diameter of a roll while the sheet is fully rolled up.")]
        public float rollDiameter = 68f;
        [Tooltip("How much thinner a roll gets once the paper has unwound from it.")]
        [Range(0f, 0.6f)] public float unwoundShrink = 0.3f;
        [Tooltip("Roll surface travel per unit of unwound paper.")]
        public float spinRatio = 1f;

        [Header("Timing (seconds)")]
        public float riseDelay = 0.2f;
        public float openDuration = 0.95f;
        public float closeDuration = 0.62f;
        [Tooltip("How long Play() keeps the card open after the text is written.")]
        public float holdDuration = 1.8f;
        public float titleCharInterval = 0.12f;
        [Tooltip("Fade + rise of the whole card. Closing plays the same motion backwards.")]
        public float appearDuration = 0.32f;
        public bool useUnscaledTime = true;

        [Header("Sound (optional)")]
        public AudioSource audioSource;
        public AudioClip unrollClip;
        public AudioClip rollUpClip;
        [Tooltip("Played cueDelay seconds after the scroll has opened, e.g. \"지팡이를 들어라!\"")]
        public AudioClip cueClip;
        public float cueDelay = 0.35f;

        [Header("Events")]
        public UnityEvent onOpened = new();
        public UnityEvent onRevealed = new();
        public UnityEvent onClosed = new();

        [Header("Parts (wired by the builder)")]
        public CanvasGroup group;
        public RectTransform shadow;
        public RectTransform sheetClip;
        public RectTransform glint;
        public CanvasGroup content;
        public ParchmentTextReveal titleReveal;
        public ParchmentTextReveal subtitleReveal;
        public RectTransform ornamentLine;
        public RectTransform ornamentDiamond;
        public RectTransform rollL, rollR;
        public RectTransform capL, capR;
        public RawImage rollTexL, rollTexR;
        public Image rollMaskL, rollMaskR;
        [Tooltip("Canvas size of one tile of the roll texture.")]
        public Vector2 rollTile = new(46f, 128f);
        [Tooltip("Bottom slice border of the roll mask sprite, in its pixels.")]
        public float rollMaskBorder = 32f;

        public Phase CurrentPhase => phase;
        public bool IsBusy => runner.Count > 0;

        Phase phase = Phase.Closed;
        float P;                     // 0 rolled up … 1 unrolled (overshoots while it settles)
        bool shown, ornamentOn;
        float appearK, ornamentK, glintK = 1f;
        bool skipTyping, skipHold, holding;
        Vector2 basePos;
        readonly Stack<IEnumerator> runner = new();
        int runId, finishedRun;

        float Dt => useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        float Width => ((RectTransform)transform).rect.width;
        float Height => ((RectTransform)transform).rect.height;

        void Awake()
        {
            basePos = ((RectTransform)transform).anchoredPosition;
            foreach (var r in new[] { titleReveal, subtitleReveal })
                if (r) r.unscaledTime = useUnscaledTime;
            ResetClosed();
        }

        void OnDisable()
        {
            runner.Clear();
            ResetClosed();
        }

        // ── Public API ───────────────────────────────────────────────

        /// <summary>Open, write the text, hold, then roll back up. Yield on the result to wait for the end.</summary>
        public CustomYieldInstruction Play(TitleCardInfo info, float hold = -1f)
        {
            int id = Begin(Sequence(info, hold < 0f ? holdDuration : hold, true));
            return new WaitUntil(() => finishedRun >= id || runId != id);
        }

        /// <summary>Open with this text and stay open until <see cref="Hide"/>. Re-rolls first if already open.</summary>
        public void Show(TitleCardInfo info) => Begin(Sequence(info, 0f, false));

        public void Hide()
        {
            if (phase == Phase.Closed || phase == Phase.Closing) return;
            Begin(Close());
        }

        /// <summary>Finish the writing if it is still going, otherwise roll the card up.</summary>
        public void Advance()
        {
            switch (phase)
            {
                case Phase.Opening: skipTyping = true; break;
                case Phase.Open:
                    if (holding) skipHold = true;
                    else Hide();
                    break;
            }
        }

        /// <summary>Lay the card out fully open with this text, without animating. For editor previews.</summary>
        public void PreviewOpen(TitleCardInfo info)
        {
            basePos = ((RectTransform)transform).anchoredPosition;
            P = 1f; appearK = ornamentK = 1f; glintK = 1f;
            shown = ornamentOn = true;
            Layout(P);
            ApplyVisuals();
            if (titleReveal) titleReveal.Text.text = info.title;
            if (subtitleReveal) subtitleReveal.Text.text = info.subtitle;
        }

        // ── Sequencing ──────────────────────────────────────────────
        // A small hand-run coroutine stack instead of StartCoroutine, so cancelling also stops every nested step
        // and the whole sequence follows the chosen time scale.

        int Begin(IEnumerator routine)
        {
            runner.Clear();
            int id = ++runId;
            runner.Push(Tracked(routine, id));
            return id;
        }

        IEnumerator Tracked(IEnumerator routine, int id)
        {
            yield return routine;
            finishedRun = id;
        }

        void Update()
        {
            Step();
            float dt = Dt;
            appearK = Mathf.MoveTowards(appearK, shown ? 1f : 0f, dt / Mathf.Max(appearDuration, 1e-4f));
            ornamentK = Mathf.MoveTowards(ornamentK, ornamentOn ? 1f : 0f, dt / 0.45f);
            if (glintK < 1f) glintK = Mathf.Min(1f, glintK + dt / 0.9f);
            ApplyVisuals();
        }

        void Step()
        {
            while (runner.Count > 0)
            {
                var top = runner.Peek();
                if (!top.MoveNext()) { runner.Pop(); continue; }
                if (top.Current is IEnumerator nested) { runner.Push(nested); continue; }
                break;   // null (or anything else): wait one frame
            }
        }

        IEnumerator Wait(float seconds, bool skippable = false)
        {
            for (float t = 0f; t < seconds; t += Dt)
            {
                if (skippable && skipTyping) yield break;
                yield return null;
            }
        }

        IEnumerator Sequence(TitleCardInfo info, float hold, bool autoClose)
        {
            if (phase != Phase.Closed)
            {
                yield return Close();
                yield return Wait(0.12f);
            }
            yield return Open(info);
            if (!autoClose) yield break;
            holding = true;
            skipHold = false;
            for (float t = 0f; t < hold && !skipHold; t += Dt) yield return null;
            holding = false;
            yield return Close();
        }

        IEnumerator Open(TitleCardInfo info)
        {
            phase = Phase.Opening;
            skipTyping = false;
            titleReveal.Prepare(info.title);
            subtitleReveal.Prepare(info.subtitle);
            content.alpha = 1f;
            ornamentOn = false;
            shown = true;

            if (P < 0.02f) yield return Wait(riseDelay);   // rise in rolled up first
            PlayClip(unrollClip);
            yield return Tween(1f, openDuration, k => EaseOutBack(k, 1.25f));   // overshoot a little and settle

            glintK = 0f;
            onOpened.Invoke();
            if (cueClip) StartCoroutine(CueAfterDelay());

            yield return Wait(0.12f, true);
            yield return Type(titleReveal, _ => titleCharInterval);
            ornamentOn = true;
            yield return Wait(0.15f, true);
            yield return Type(subtitleReveal, SubtitleDelay);

            titleReveal.RevealAll();
            subtitleReveal.RevealAll();
            ornamentOn = true;
            phase = Phase.Open;
            onRevealed.Invoke();
        }

        IEnumerator Close()
        {
            phase = Phase.Closing;
            holding = false;
            titleReveal.RevealAll(true);
            subtitleReveal.RevealAll(true);
            float a0 = content.alpha;
            for (float t = 0f; t < 0.17f; t += Dt)
            {
                content.alpha = Mathf.Lerp(a0, 0f, t / 0.17f);
                yield return null;
            }
            content.alpha = 0f;
            ornamentOn = false;

            // Mirror of opening: there the card starts appearing riseDelay before the unroll, so here it starts
            // disappearing as many seconds before the roll-up ends as the two overlapped when opening.
            PlayClip(rollUpClip);
            float overlap = Mathf.Clamp(appearDuration - riseDelay, 0f, closeDuration);
            yield return Tween(0f, closeDuration, EaseInOutCubic, closeDuration - overlap, () => shown = false);
            shown = false;
            while (appearK > 0f) yield return null;
            phase = Phase.Closed;
            onClosed.Invoke();
        }

        IEnumerator Type(ParchmentTextReveal reveal, Func<int, float> delay)
        {
            for (int i = 0; i < reveal.Count; i++)
            {
                if (skipTyping) { reveal.RevealAll(); yield break; }
                reveal.Reveal(i);
                if (char.IsWhiteSpace(reveal.CharAt(i))) continue;
                yield return Wait(delay(i), true);
            }
        }

        float SubtitleDelay(int i)
        {
            char c = subtitleReveal.CharAt(i);
            if (".,!?…~".IndexOf(c) >= 0) return 0.2f;
            return subtitleReveal.IsWordEnd(i) ? 0.07f : 0.038f;
        }

        IEnumerator Tween(float to, float duration, Func<float, float> ease, float markAt = float.PositiveInfinity, Action mark = null)
        {
            float from = P;
            for (float t = 0f; t < duration; t += Dt)
            {
                if (mark != null && t >= markAt) { mark(); mark = null; }
                P = Mathf.LerpUnclamped(from, to, ease(t / duration));
                Layout(P);
                yield return null;
            }
            P = to;
            Layout(P);
        }

        IEnumerator CueAfterDelay()
        {
            int id = runId;
            if (useUnscaledTime) yield return new WaitForSecondsRealtime(cueDelay);
            else yield return new WaitForSeconds(cueDelay);
            if (id == runId && phase != Phase.Closing && phase != Phase.Closed) PlayClip(cueClip);
        }

        void PlayClip(AudioClip clip)
        {
            if (!clip) return;
            if (!audioSource) audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.PlayOneShot(clip);
        }

        // ── Layout ──────────────────────────────────────────────────

        void ResetClosed()
        {
            phase = Phase.Closed;
            P = 0f;
            shown = ornamentOn = holding = false;
            appearK = ornamentK = 0f;
            glintK = 1f;
            if (content) content.alpha = 1f;
            if (titleReveal) titleReveal.Prepare(string.Empty);
            if (subtitleReveal) subtitleReveal.Prepare(string.Empty);
            Layout(P);
            ApplyVisuals();
        }

        /// <summary>Place the rolls and the visible part of the sheet for unroll amount <paramref name="p"/>.</summary>
        public void Layout(float p)
        {
            if (!sheetClip || !rollL || !rollR) return;
            float W = Width, H = Height, D = rollDiameter;
            float t = Mathf.Max(0f, p);
            float d = D * (1f - unwoundShrink * Mathf.Min(t, 1f));   // the roll thins as paper leaves it
            float r = d * 0.5f;
            float cap = d * 0.34f;
            float half = t * (W * 0.5f - D);                           // center → inner edge of each roll

            // Show the paper up to a little inside each roll (enough to hide the seam), but never more than the gap
            // between them: CanvasGroup alpha fades every layer on its own, so paper left under a roll would show
            // through it while the card fades. Fully rolled up, nothing of the sheet remains.
            float tuck = Mathf.Min(r * 0.5f, half);
            sheetClip.sizeDelta = new Vector2(2f * (half + tuck), sheetClip.sizeDelta.y);

            float spin = half * spinRatio;                             // the surface turns by the paper it let out
            PlaceRoll(rollL, capL, rollTexL, rollMaskL, -(half + r), spin, d, cap, H);
            PlaceRoll(rollR, capR, rollTexR, rollMaskR, half + r, -spin, d, cap, H);

            if (shadow) shadow.sizeDelta = new Vector2(2f * (half + d) + 30f, shadow.sizeDelta.y);
        }

        void PlaceRoll(RectTransform roll, RectTransform cap, RawImage tex, Image mask, float x, float spin, float d, float capH, float H)
        {
            float h = H + capH * 0.5f;
            roll.anchoredPosition = new Vector2(x, 0f);
            roll.sizeDelta = new Vector2(d, h);
            if (cap) cap.sizeDelta = new Vector2(0f, capH);
            if (tex) tex.uvRect = new Rect(spin / rollTile.x, 0f, d / rollTile.x, h / rollTile.y);
            if (mask) mask.pixelsPerUnitMultiplier = rollMaskBorder / Mathf.Max(0.5f, capH * 0.5f);   // keeps the rounded bottom capH/2 tall
        }

        void ApplyVisuals()
        {
            var rt = (RectTransform)transform;

            // Whole card: fade in, rise 26 units and grow from 96 %. appearK runs linearly both ways and the
            // curves depend only on it, so disappearing is exactly the appearing motion played backwards.
            float e = EaseOutBack(appearK, 1.4f);
            if (group) group.alpha = Mathf.Clamp01(appearK / 0.7f);
            rt.anchoredPosition = basePos + new Vector2(0f, -26f * (1f - e));
            rt.localScale = Vector3.one * Mathf.LerpUnclamped(0.96f, 1f, e);

            float o = 1f - Mathf.Pow(1f - ornamentK, 3f);
            if (ornamentLine) ornamentLine.localScale = new Vector3(o, 1f, 1f);
            if (ornamentDiamond) ornamentDiamond.localScale = Vector3.one * EaseOutBack(ornamentK, 2f);

            if (glint)
            {
                bool on = glintK < 1f;
                if (glint.gameObject.activeSelf != on) glint.gameObject.SetActive(on);
                if (on)
                {
                    float sweep = Width * 0.5f + glint.rect.width;
                    float g = 1f - (1f - glintK) * (1f - glintK);
                    glint.anchoredPosition = new Vector2(Mathf.Lerp(-sweep, sweep, g), 0f);
                }
            }
        }

        static float EaseOutBack(float k, float s)
        {
            float x = k - 1f;
            return 1f + (s + 1f) * x * x * x + s * x * x;
        }

        static float EaseInOutCubic(float k) => k < 0.5f ? 4f * k * k * k : 1f - Mathf.Pow(-2f * k + 2f, 3f) / 2f;
    }
}
