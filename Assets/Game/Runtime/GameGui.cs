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

        static Texture2D ring, thickRing, disc, soft, stripe, diamond, banner, vignette;
        static readonly Dictionary<long, Texture2D> gradients = new Dictionary<long, Texture2D>();

        public static float Scale => Screen.height / 1080f;

        public static Texture2D Ring => ring != null ? ring : ring = MakeRadial(128, (r, aa) => Mathf.Clamp01((0.98f - r) / aa) * Mathf.Clamp01((r - 0.86f) / aa));
        /// <summary>Bolder ring for the crosshair.</summary>
        public static Texture2D ThickRing => thickRing != null ? thickRing : thickRing = MakeRadial(128, (r, aa) => Mathf.Clamp01((0.98f - r) / aa) * Mathf.Clamp01((r - 0.68f) / aa));
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
                // A soft rounded pool of shade, not a box: alpha falls off smoothly from the middle along a
                // squircle, reaching zero well inside every edge, so no straight border ever shows.
                const int w = 256, h = 64;
                banner = New(w, h);
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = Mathf.Abs(x / (w - 1f) * 2f - 1f), v = Mathf.Abs(y / (h - 1f) * 2f - 1f);
                    float d = Mathf.Pow(Mathf.Pow(u, 3f) + Mathf.Pow(v, 2.2f), 1f / 2.6f);
                    float a = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.15f, 0.98f, d));
                    banner.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
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

        /// <summary>Message banner: a soft pool of shade behind the text, fading out on every side.</summary>
        public static void BannerBack(Rect r, Color tint) => Tex(r, Banner, tint);

        /// <summary>
        /// Full-screen backdrop treatment for menu screens: a faint cool lift in the middle and darker corners, so a
        /// flat clear colour reads as a dim room rather than a blank fill.
        /// </summary>
        public static void Backdrop(Rect r, float strength = 1f)
        {
            if (vignette == null)
            {
                const int n = 128;
                vignette = New(n, n);
                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = x / (n - 1f) * 2f - 1f, v = y / (n - 1f) * 2f - 1f;
                    float d = Mathf.Clamp01(Mathf.Sqrt(u * u * 0.8f + v * v) / 1.25f);
                    vignette.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.SmoothStep(0f, 1f, d)));
                }
                vignette.Apply();
            }
            Tex(new Rect(r.center.x - r.width * 0.45f, r.center.y - r.height * 0.5f, r.width * 0.9f, r.height), Soft, new Color(0.35f, 0.45f, 0.75f, 0.07f * strength));
            Tex(r, vignette, new Color(0f, 0f, 0.01f, 0.75f * strength));
        }

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
                hover = new GUIStyleState(),
                active = new GUIStyleState(),
                focused = new GUIStyleState(),
            };
        }

        /// <summary>
        /// Sets the text colour of every state. IMGUI draws a label in its hover / active colour when the mouse is
        /// over it, so changing only <c>normal</c> made the outline and glow passes turn bright under the cursor
        /// (the mouse-test aim) and the text looked doubled.
        /// </summary>
        static void SetColor(GUIStyle style, Color c)
        {
            style.normal.textColor = style.hover.textColor = style.active.textColor = style.focused.textColor = c;
            style.onNormal.textColor = style.onHover.textColor = style.onActive.textColor = style.onFocused.textColor = c;
        }

        /// <summary>Text with a soft dark shadow round it so it reads over bright effects.</summary>
        public static void Label(Rect r, string text, GUIStyle style, Color color, float alpha = 1f)
        {
            var c = style.normal.textColor;
            // Eight light passes (and a slight drop) instead of four hard ones: a shadow, not a cut-out outline.
            SetColor(style, new Color(0f, 0f, 0f, 0.32f * alpha));
            float o = Mathf.Max(1f, 1.6f * Scale);
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f;
                GUI.Label(new Rect(r.x + Mathf.Cos(a) * o, r.y + Mathf.Sin(a) * o + o * 0.6f, r.width, r.height), text, style);
            }
            SetColor(style, new Color(color.r, color.g, color.b, color.a * alpha));
            GUI.Label(r, text, style);
            SetColor(style, c);
        }

        /// <summary>Text with a coloured glow behind it (call-outs).</summary>
        public static void GlowLabel(Rect r, string text, GUIStyle style, Color color, Color glow, float alpha = 1f)
        {
            var c = style.normal.textColor;
            // Two faint rings of offset copies (near and far) build a soft falloff; one strong ring read as a
            // second, ghostly outline round the letters.
            for (int ring = 0; ring < 2; ring++)
            {
                float g = (ring == 0 ? 3f : 7f) * Scale;
                SetColor(style, new Color(glow.r, glow.g, glow.b, (ring == 0 ? 0.04f : 0.022f) * alpha));
                int n = ring == 0 ? 8 : 12;
                for (int i = 0; i < n; i++)
                {
                    float a = (i + ring * 0.5f) * Mathf.PI * 2f / n;
                    GUI.Label(new Rect(r.x + Mathf.Cos(a) * g, r.y + Mathf.Sin(a) * g, r.width, r.height), text, style);
                }
            }
            SetColor(style, c);
            Label(r, text, style, color, alpha);
        }

        static readonly Texture2D[] crosshairs = new Texture2D[2];
        static readonly string[] CrosshairNames = { "UI/Crosshair_Amelia", "UI/Crosshair_Nox" };

        /// <summary>The player's painted crosshair (0 Amelia gold, 1 Nox blue), or null if it is missing.</summary>
        public static Texture2D CrosshairTexture(int player)
        {
            if (player < 0 || player >= crosshairs.Length) return null;
            if (crosshairs[player] == null) crosshairs[player] = Resources.Load<Texture2D>(CrosshairNames[player]);
            return crosshairs[player];
        }

        /// <summary>
        /// The player's painted crosshair at a viewport point (0..1, origin bottom-left); <paramref name="color"/>'s
        /// alpha fades it. Falls back to the drawn crosshair if the texture is missing.
        /// </summary>
        static Texture2D crosshairGlow;

        /// <param name="glow">0..1: the ring lights up white (while firing).</param>
        public static void Crosshair(Vector2 uv, Color color, float size, int player, float glow = 0f)
        {
            var tex = CrosshairTexture(player);
            if (tex == null) { Crosshair(uv, color, size); return; }
            // The painted ring fills ~90% of its square and its strokes are fine, so it is drawn well past the old
            // crosshair's reach and twice over, which firms up the thin lines and glow against the dark arena.
            var at = new Vector2(uv.x * Screen.width, (1f - uv.y) * Screen.height);
            float side = size * 1.75f;
            var tint = new Color(1f, 1f, 1f, color.a);
            DrawCentered(tex, at, side, tint);
            DrawCentered(tex, at, side, tint);
            if (glow <= 0.001f) return;
            if (crosshairGlow == null) crosshairGlow = Resources.Load<Texture2D>("UI/Crosshair_Glow");
            // A white bloom behind it, then the ring itself in white over the player's colour.
            DrawCentered(Soft, at, side * 1.15f, new Color(1f, 1f, 1f, 0.22f * glow));
            if (crosshairGlow != null) DrawCentered(crosshairGlow, at, side, new Color(1f, 1f, 1f, glow));
        }

        /// <summary>Screen-space crosshair at a viewport point (0..1, origin bottom-left).</summary>
        public static void Crosshair(Vector2 uv, Color color, float size)
        {
            float x = uv.x * Screen.width, y = (1f - uv.y) * Screen.height;
            var c = new Vector2(x, y);
            float t = Mathf.Max(4f, 7f * Scale);   // arm thickness
            float o = Mathf.Max(1.5f, 2f * Scale); // dark outline so it reads on bright backgrounds
            var dark = new Color(0f, 0f, 0f, color.a * 0.6f);
            DrawCentered(Soft, c, size * 1.3f, new Color(color.r, color.g, color.b, color.a * 0.4f));
            // Outline pass, then the colored pass on top.
            for (int pass = 0; pass < 2; pass++)
            {
                float e = pass == 0 ? o : 0f;
                var col = pass == 0 ? dark : color;
                Rect(new Rect(x - size * 0.5f - e, y - t * 0.5f - e, size * 0.32f + 2f * e, t + 2f * e), col);
                Rect(new Rect(x + size * 0.18f - e, y - t * 0.5f - e, size * 0.32f + 2f * e, t + 2f * e), col);
                Rect(new Rect(x - t * 0.5f - e, y - size * 0.5f - e, t + 2f * e, size * 0.32f + 2f * e), col);
                Rect(new Rect(x - t * 0.5f - e, y + size * 0.18f - e, t + 2f * e, size * 0.32f + 2f * e), col);
            }
            DrawCentered(ThickRing, c, size * 0.6f + 2f * o, dark);
            DrawCentered(ThickRing, c, size * 0.6f, color);
            DrawCentered(Disc, c, t * 1.1f, color); // center dot
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
