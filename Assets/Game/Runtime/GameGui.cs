using System.Collections.Generic;
using UnityEngine;

namespace AiHackathon.Game
{
    /// <summary>
    /// IMGUI drawing kit for the period HUDs, scaled to 1080p: generated glow/gradient textures, framed gradient bars
    /// with a trailing damage chip and a shine sweep, banners, diamonds, rings and outlined text in the title font.
    /// </summary>
    public static class GameGui
    {
        static Font titleFont, bodyFont;
        static bool fontsLoaded;

        /// <summary>Serif font for titles and call-outs (Resources/GameGuiFonts unless a HUD sets it).</summary>
        public static Font TitleFont
        {
            get { LoadFonts(); return titleFont; }
            set => titleFont = value;
        }

        /// <summary>Font for smaller text.</summary>
        public static Font BodyFont
        {
            get { LoadFonts(); return bodyFont; }
            set => bodyFont = value;
        }

        static void LoadFonts()
        {
            if (fontsLoaded) return;
            fontsLoaded = true;
            var fonts = Resources.Load<GameGuiFonts>("GameGuiFonts");
            if (fonts == null) return;
            if (titleFont == null) titleFont = fonts.title;
            if (bodyFont == null) bodyFont = fonts.body;
        }

        static Texture2D ring, disc, soft, stripe, diamond, banner;
        static readonly Dictionary<long, Texture2D> gradients = new Dictionary<long, Texture2D>();

        public static float Scale => Screen.height / 1080f;

        public static Texture2D Ring => ring != null ? ring : ring = MakeRadial(128, (r, aa) => Mathf.Clamp01((0.98f - r) / aa) * Mathf.Clamp01((r - 0.86f) / aa));
        public static Texture2D Disc => disc != null ? disc : disc = MakeRadial(64, (r, aa) => Mathf.Clamp01((1f - r) / aa));
        /// <summary>Soft glow blob.</summary>
        public static Texture2D Soft => soft != null ? soft : soft = MakeRadial(64, (r, aa) => Mathf.Pow(Mathf.Clamp01(1f - r), 2f));
        public static Texture2D Diamond => diamond != null ? diamond : diamond = MakeDiamond(64);

        /// <summary>Vertical soft stripe (shine sweeps).</summary>
        static Texture2D Stripe
        {
            get
            {
                if (stripe != null) return stripe;
                stripe = New(64, 4);
                for (int x = 0; x < 64; x++)
                {
                    float u = x / 63f;
                    float a = Mathf.Pow(Mathf.Sin(u * Mathf.PI), 3f);
                    for (int y = 0; y < 4; y++) stripe.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
                stripe.Apply();
                return stripe;
            }
        }

        /// <summary>Horizontal band fading out at both ends (message banners).</summary>
        static Texture2D Banner
        {
            get
            {
                if (banner != null) return banner;
                banner = New(128, 32);
                for (int y = 0; y < 32; y++)
                for (int x = 0; x < 128; x++)
                {
                    float u = x / 127f, v = y / 31f;
                    float a = Mathf.Pow(Mathf.Sin(u * Mathf.PI), 0.8f) * Mathf.Pow(Mathf.Sin(v * Mathf.PI), 0.6f);
                    banner.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
                banner.Apply();
                return banner;
            }
        }

        static Texture2D New(int w, int h) => new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };

        static Texture2D MakeRadial(int n, System.Func<float, float, float> alpha)
        {
            var tex = New(n, n);
            float aa = 2f / n;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float r = new Vector2(x + 0.5f - n * 0.5f, y + 0.5f - n * 0.5f).magnitude / (n * 0.5f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha(r, aa)));
            }
            tex.Apply();
            return tex;
        }

        static Texture2D MakeDiamond(int n)
        {
            var tex = New(n, n);
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = (Mathf.Abs(x + 0.5f - n * 0.5f) + Mathf.Abs(y + 0.5f - n * 0.5f)) / (n * 0.5f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01((1f - d) * n * 0.25f)));
            }
            tex.Apply();
            return tex;
        }

        /// <summary>Bar fill: <paramref name="a"/> → <paramref name="b"/> left to right, lit from above with a highlight line.</summary>
        static Texture2D Gradient(Color a, Color b)
        {
            long key = ((long)ColorKey(a) << 32) | (uint)ColorKey(b);
            if (gradients.TryGetValue(key, out var tex) && tex != null) return tex;
            tex = New(256, 32);
            for (int y = 0; y < 32; y++)
            for (int x = 0; x < 256; x++)
            {
                float u = x / 255f, v = y / 31f; // v = 0 bottom
                var c = Color.Lerp(a, b, u);
                float shade = Mathf.Lerp(0.62f, 1.18f, v) + (v > 0.72f && v < 0.86f ? 0.35f : 0f);
                tex.SetPixel(x, y, new Color(c.r * shade, c.g * shade, c.b * shade, 1f));
            }
            tex.Apply();
            gradients[key] = tex;
            return tex;
        }

        static int ColorKey(Color c) => ((int)(c.r * 255f) << 24) | ((int)(c.g * 255f) << 16) | ((int)(c.b * 255f) << 8) | (int)(c.a * 255f);

        // ------------------------------------------------------------------ primitives

        public static void DrawCentered(Texture tex, Vector2 center, float size, Color color)
        {
            var prev = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(center.x - size * 0.5f, center.y - size * 0.5f, size, size), tex);
            GUI.color = prev;
        }

        public static void Rect(Rect r, Color color) => Tex(r, Texture2D.whiteTexture, color);

        public static void Tex(Rect r, Texture tex, Color color)
        {
            var prev = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(r, tex);
            GUI.color = prev;
        }

        /// <summary>Simple framed bar (kept for small meters).</summary>
        public static void Bar(Rect r, float fill, Color color, Color back) =>
            FancyBar(r, fill, fill, color, Color.Lerp(color, Color.white, 0.35f), back.a, false);

        /// <summary>
        /// Framed gradient bar: soft outer glow, dark glass back, a pale chip trailing behind the fill (damage taken),
        /// the gradient fill, a highlight sweeping across it, a bright cap at its end and optional tick marks.
        /// </summary>
        public static void FancyBar(Rect r, float fill, float chip, Color from, Color to, float alpha = 1f, bool ticks = true, float glow = 0.35f)
        {
            float s = Scale;
            fill = Mathf.Clamp01(fill);
            chip = Mathf.Clamp01(Mathf.Max(chip, fill));
            float b = Mathf.Max(1f, 2f * s);
            var glowColor = Color.Lerp(from, to, 0.5f);
            Tex(new Rect(r.x - 18f * s, r.y - 14f * s, r.width + 36f * s, r.height + 28f * s), Soft, new Color(glowColor.r, glowColor.g, glowColor.b, glow * alpha * 0.6f));
            // Frame: thin light border over dark glass.
            Rect(new Rect(r.x - b, r.y - b, r.width + 2 * b, r.height + 2 * b), new Color(1f, 1f, 1f, 0.22f * alpha));
            Rect(r, new Color(0.04f, 0.02f, 0.07f, 0.82f * alpha));
            var inner = new Rect(r.x + b, r.y + b, r.width - 2 * b, r.height - 2 * b);
            Rect(new Rect(inner.x, inner.y, inner.width * chip, inner.height), new Color(1f, 0.92f, 1f, 0.55f * alpha));
            if (fill > 0f)
            {
                var fillRect = new Rect(inner.x, inner.y, inner.width * fill, inner.height);
                var prev = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, alpha);
                GUI.DrawTextureWithTexCoords(fillRect, Gradient(from, to), new Rect(0f, 0f, fill, 1f));
                GUI.color = prev;
                // Shine sweeping across the filled part.
                float sweep = Mathf.Repeat(Time.unscaledTime * 0.45f, 1.6f) - 0.3f;
                float sw = inner.height * 3f;
                float sx = inner.x + sweep * inner.width;
                if (sx > inner.x - sw && sx < fillRect.xMax)
                {
                    var sr = new Rect(Mathf.Max(inner.x, sx - sw * 0.5f), inner.y, 0f, inner.height);
                    sr.xMax = Mathf.Min(fillRect.xMax, sx + sw * 0.5f);
                    if (sr.width > 0f) Tex(sr, Stripe, new Color(1f, 1f, 1f, 0.35f * alpha));
                }
                // Bright cap.
                DrawCentered(Soft, new Vector2(fillRect.xMax, fillRect.center.y), inner.height * 2.2f, new Color(1f, 1f, 1f, 0.55f * alpha));
            }
            if (ticks)
                for (int i = 1; i < 10; i++)
                    Rect(new Rect(inner.x + inner.width * i / 10f - 0.5f * s, inner.y, Mathf.Max(1f, 1f * s), inner.height * 0.45f), new Color(0f, 0f, 0f, 0.35f * alpha));
        }

        /// <summary>Message banner: a dark band fading to both sides behind the text.</summary>
        public static void BannerBack(Rect r, Color tint) => Tex(r, Banner, tint);

        public static GUIStyle Style(int size, TextAnchor anchor, bool title = true)
        {
            var font = title ? TitleFont : (BodyFont != null ? BodyFont : TitleFont);
            return new GUIStyle(GUI.skin.label)
            {
                font = font,
                fontSize = Mathf.RoundToInt(size * Scale),
                alignment = anchor,
                fontStyle = font != null ? FontStyle.Normal : (title ? FontStyle.Bold : FontStyle.Normal),
                wordWrap = false,
                clipping = TextClipping.Overflow,
                richText = false,
            };
        }

        /// <summary>Text with a dark outline so it reads over bright effects.</summary>
        public static void Label(Rect r, string text, GUIStyle style, Color color, float alpha = 1f)
        {
            var c = style.normal.textColor;
            style.normal.textColor = new Color(0f, 0f, 0f, 0.75f * alpha);
            float o = Mathf.Max(1f, 2f * Scale);
            GUI.Label(new Rect(r.x - o, r.y, r.width, r.height), text, style);
            GUI.Label(new Rect(r.x + o, r.y, r.width, r.height), text, style);
            GUI.Label(new Rect(r.x, r.y - o, r.width, r.height), text, style);
            GUI.Label(new Rect(r.x, r.y + o, r.width, r.height), text, style);
            style.normal.textColor = new Color(color.r, color.g, color.b, color.a * alpha);
            GUI.Label(r, text, style);
            style.normal.textColor = c;
        }

        /// <summary>Text with a coloured glow behind it (call-outs).</summary>
        public static void GlowLabel(Rect r, string text, GUIStyle style, Color color, Color glow, float alpha = 1f)
        {
            var c = style.normal.textColor;
            float g = 4f * Scale;
            style.normal.textColor = new Color(glow.r, glow.g, glow.b, 0.18f * alpha);
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f;
                GUI.Label(new Rect(r.x + Mathf.Cos(a) * g, r.y + Mathf.Sin(a) * g, r.width, r.height), text, style);
            }
            style.normal.textColor = c;
            Label(r, text, style, color, alpha);
        }

        /// <summary>Screen-space crosshair at a viewport point (0..1, origin bottom-left).</summary>
        public static void Crosshair(Vector2 uv, Color color, float size)
        {
            float x = uv.x * Screen.width, y = (1f - uv.y) * Screen.height;
            float t = Mathf.Max(2f, 3f * Scale);
            DrawCentered(Soft, new Vector2(x, y), size * 1.2f, new Color(color.r, color.g, color.b, color.a * 0.35f));
            Rect(new Rect(x - size * 0.5f, y - t * 0.5f, size * 0.32f, t), color);
            Rect(new Rect(x + size * 0.18f, y - t * 0.5f, size * 0.32f, t), color);
            Rect(new Rect(x - t * 0.5f, y - size * 0.5f, t, size * 0.32f), color);
            Rect(new Rect(x - t * 0.5f, y + size * 0.18f, t, size * 0.32f), color);
            DrawCentered(Ring, new Vector2(x, y), size * 0.55f, color);
        }

        /// <summary>A ring of dots around <paramref name="center"/> from angle a0 to a1 (radians, 0 = up, clockwise), lit up to <paramref name="fill"/>.</summary>
        public static void DotArc(Vector2 center, float radius, float a0, float a1, int dots, float fill, Color on, Color off, float size)
        {
            for (int i = 0; i < dots; i++)
            {
                float u = (i + 0.5f) / dots;
                float a = Mathf.Lerp(a0, a1, u);
                var p = center + new Vector2(Mathf.Sin(a), -Mathf.Cos(a)) * radius;
                bool lit = u <= fill;
                DrawCentered(lit ? Soft : Disc, p, lit ? size * 2.2f : size, lit ? on : off);
                if (lit) DrawCentered(Disc, p, size * 0.9f, Color.Lerp(on, Color.white, 0.6f));
            }
        }
    }
}
