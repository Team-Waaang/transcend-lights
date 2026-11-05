using System;
using TMPro;
using UnityEngine;

namespace AiHackathon.Parchment
{
    /// <summary>
    /// Reveals a TextMeshPro string one character at a time: each character fades in while rising a few units.
    /// The caller decides when each character starts (see <see cref="ParchmentTitleCard"/>); this only animates the mesh.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TMP_Text))]
    public class ParchmentTextReveal : MonoBehaviour
    {
        [Tooltip("Seconds a single character takes to fade in.")]
        public float fadeTime = 0.18f;
        [Tooltip("Canvas units a character rises while it fades in.")]
        public float rise = 4f;

        [NonSerialized] public bool unscaledTime = true;

        TMP_Text text;
        float[] shownAt = Array.Empty<float>();   // start time per character, +inf = not shown yet
        bool animating;

        public TMP_Text Text => text ? text : text = GetComponent<TMP_Text>();
        public int Count => shownAt.Length;
        public bool IsAnimating => animating;
        float Now => unscaledTime ? Time.unscaledTime : Time.time;

        /// <summary>Set the string with every character hidden.</summary>
        public void Prepare(string value)
        {
            Text.text = value ?? string.Empty;
            Text.ForceMeshUpdate();
            shownAt = new float[Text.textInfo.characterCount];
            Array.Fill(shownAt, float.PositiveInfinity);
            animating = true;
            Apply();
        }

        public char CharAt(int i) => Text.textInfo.characterInfo[i].character;

        /// <summary>True when character <paramref name="i"/> is the last one of its word.</summary>
        public bool IsWordEnd(int i) => i + 1 >= Count || char.IsWhiteSpace(CharAt(i + 1));

        public void Reveal(int i)
        {
            if (i < 0 || i >= Count || !float.IsPositiveInfinity(shownAt[i])) return;
            shownAt[i] = Now;
            animating = true;
        }

        /// <summary>Start every character that has not started yet; <paramref name="instant"/> skips their fade.</summary>
        public void RevealAll(bool instant = false)
        {
            float t = instant ? Now - fadeTime : Now;
            for (int i = 0; i < shownAt.Length; i++)
                if (float.IsPositiveInfinity(shownAt[i])) shownAt[i] = t;
            animating = true;
        }

        void LateUpdate()
        {
            if (animating) Apply();
        }

        void Apply()
        {
            var t = Text;
            t.ForceMeshUpdate();   // fresh, fully visible mesh every frame; we only ever subtract from it
            var info = t.textInfo;
            float now = Now, fade = Mathf.Max(fadeTime, 1e-4f);
            bool done = true;
            int n = Mathf.Min(info.characterCount, shownAt.Length);
            for (int i = 0; i < n; i++)
            {
                var c = info.characterInfo[i];
                if (!c.isVisible) continue;
                float k = Mathf.Clamp01((now - shownAt[i]) / fade);
                if (k < 1f) done = false;
                float e = 1f - (1f - k) * (1f - k);
                var mesh = info.meshInfo[c.materialReferenceIndex];
                for (int j = 0; j < 4; j++)
                {
                    int v = c.vertexIndex + j;
                    mesh.colors32[v].a = (byte)(mesh.colors32[v].a * e);
                    mesh.vertices[v].y -= rise * (1f - e);
                }
            }
            t.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32 | TMP_VertexDataUpdateFlags.Vertices);
            if (done) animating = false;
        }
    }
}
