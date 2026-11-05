using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AiHackathon.Parchment.Editor
{
    /// <summary>
    /// Bakes the parchment textures, creates the Gowun Batang TMP font assets and builds the period title card
    /// (prefab + Parchment scene demo). Safe to run again: everything it made before is replaced.
    /// </summary>
    internal static class ParchmentTitleCardBuilder
    {
        const string Root = "Assets/Parchment";
        const string TexDir = Root + "/Textures";
        const string FontDir = Root + "/Fonts";
        const string PrefabPath = Root + "/ParchmentTitleCard.prefab";
        const string ScenePath = "Assets/Scenes/Parchment.unity";
        const string CanvasName = "ParchmentTitleCanvas";
        const string DemoName = "ParchmentDemo";

        // Card size in canvas units (1920 x 1080 reference).
        const float W = 1120f, H = 260f, Margin = 16f, RollD = 68f;
        const float S = 2f;   // texture pixels per canvas unit

        static readonly Color Ink = Hex("3a2410"), InkSoft = Hex("6b4a26"), PlateInk = Hex("3b2108");
        static readonly Color GoldHi = Hex("fbe6a2"), Gold = Hex("d9a646"), GoldLo = Hex("8a5a1c"), GoldDeep = Hex("b98330");
        static readonly Color Night = Hex("0d0b1a"), Night2 = Hex("1d1736");

        [MenuItem("Tools/Parchment/Build Title Card")]
        internal static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[Parchment] Exit Play mode before building the title card.");
                return;
            }
            EnsureFolder(TexDir);
            var tex = BakeTextures();
            var bold = FontAsset("GowunBatang-Bold");
            var regular = FontAsset("GowunBatang-Regular");

            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != ScenePath) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            foreach (var go in scene.GetRootGameObjects())
                if (go.name == CanvasName || go.name == DemoName) Object.DestroyImmediate(go);

            var canvas = BuildCard(tex, bold, regular, out var card);
            PrefabUtility.SaveAsPrefabAssetAndConnect(canvas, PrefabPath, InteractionMode.AutomatedAction);
            BuildDemo(tex, regular, card);

            var cam = Camera.main;
            if (cam)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Night;
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[Parchment] Built {PrefabPath} and {ScenePath}");
        }

        // ── Hierarchy ───────────────────────────────────────────────

        static GameObject BuildCard(Textures t, TMP_FontAsset bold, TMP_FontAsset regular, out ParchmentTitleCard card)
        {
            var canvasGo = new GameObject(CanvasName, typeof(RectTransform));
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var root = Rect("TitleCard", canvasGo.transform, new Vector2(W, H));
            root.anchoredPosition = new Vector2(0f, 60f);
            card = root.gameObject.AddComponent<ParchmentTitleCard>();
            card.group = root.gameObject.AddComponent<CanvasGroup>();
            card.group.blocksRaycasts = card.group.interactable = false;
            card.rollDiameter = RollD;

            // Soft shadow on the floor under the scroll.
            var shadow = Rect("Shadow", root, new Vector2(W, 34f));
            Anchor(shadow, new Vector2(0.5f, 0f));
            shadow.anchoredPosition = new Vector2(0f, -18f);
            Img(shadow, t.Shadow, new Color(1f, 1f, 1f, 1f));
            card.shadow = shadow;

            // Paper between the rolls: only this mask moves, the sheet itself stays put.
            var clip = Rect("SheetClip", root, new Vector2(W, H + 2f * Margin + 8f));
            clip.gameObject.AddComponent<RectMask2D>();
            card.sheetClip = clip;

            var sheet = Rect("Sheet", clip, new Vector2(W, H + 2f * Margin));
            Img(sheet, t.Sheet, Color.white);

            var glintMask = Rect("GlintMask", clip, new Vector2(W, H - 12f));
            glintMask.gameObject.AddComponent<RectMask2D>();
            var glint = Rect("Glint", glintMask, new Vector2(120f, H * 1.25f));
            glint.localRotation = Quaternion.Euler(0f, 0f, -15f);
            Img(glint, t.Glint, new Color(1f, 0.93f, 0.67f, 0.55f));
            card.glint = glint;

            var content = Rect("Content", clip, new Vector2(W, H));
            card.content = content.gameObject.AddComponent<CanvasGroup>();
            card.content.blocksRaycasts = card.content.interactable = false;

            var title = Rect("Title", content, new Vector2(W - 2f * RollD - 80f, 120f));
            title.anchoredPosition = new Vector2(0f, 24f);
            var titleText = Text(title, bold, 92f, Ink, 14f);
            card.titleReveal = title.gameObject.AddComponent<ParchmentTextReveal>();
            card.titleReveal.fadeTime = 0.32f;
            card.titleReveal.rise = 10f;
            titleText.text = "룬";

            var ornament = Rect("Ornament", content, new Vector2(460f, 26f));
            ornament.anchoredPosition = new Vector2(0f, -32f);
            var line = Rect("Line", ornament, new Vector2(460f, 6f));
            Img(line, t.OrnamentLine, Color.white);
            var diamond = Rect("Diamond", ornament, new Vector2(26f, 26f));
            Img(diamond, t.Diamond, Color.white);
            card.ornamentLine = line;
            card.ornamentDiamond = diamond;

            var subtitle = Rect("Subtitle", content, new Vector2(W - 2f * RollD - 120f, 50f));
            subtitle.anchoredPosition = new Vector2(0f, -72f);
            var subText = Text(subtitle, regular, 30f, InkSoft, 1f);
            card.subtitleReveal = subtitle.gameObject.AddComponent<ParchmentTextReveal>();
            subText.text = "손끝이 떨리면, 룬도 떨린다.";

            card.rollL = Roll("RollL", root, t, out card.capL, out card.rollTexL, out card.rollMaskL);
            card.rollR = Roll("RollR", root, t, out card.capR, out card.rollTexR, out card.rollMaskR);
            card.rollTile = new Vector2(t.RollTex.width / S, t.RollTex.height / S);

            // Gold ribbon with the period name, sized to its text.
            var plate = Rect("Nameplate", root, new Vector2(220f, 54f));
            Anchor(plate, new Vector2(0.5f, 1f));
            plate.anchoredPosition = new Vector2(0f, card.nameplateY);
            var plateImg = Img(plate, t.Nameplate, Color.white);
            plateImg.type = Image.Type.Sliced;
            var drop = plate.gameObject.AddComponent<Shadow>();
            drop.effectColor = new Color(0f, 0f, 0f, 0.35f);
            drop.effectDistance = new Vector2(0f, -3f);
            card.nameplateGroup = plate.gameObject.AddComponent<CanvasGroup>();
            var layout = plate.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(40, 40, 4, 6);
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = true;
            var fitter = plate.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            var label = Rect("Label", plate, new Vector2(140f, 44f));
            card.labelText = Text(label, bold, 28f, PlateInk, 8f);
            card.labelText.text = "제1교시";
            card.nameplate = plate;

            card.PreviewOpen(new TitleCardInfo("제1교시", "룬", "손끝이 떨리면, 룬도 떨린다."));
            return canvasGo;
        }

        static RectTransform Roll(string name, RectTransform parent, Textures t, out RectTransform cap, out RawImage tex, out Image mask)
        {
            var roll = Rect(name, parent, new Vector2(RollD, H));
            Anchor(roll, new Vector2(0.5f, 1f));
            roll.pivot = new Vector2(0.5f, 1f);

            var shadow = Rect("RollShadow", roll, new Vector2(0f, 22f));
            shadow.anchorMin = new Vector2(0f, 0f);
            shadow.anchorMax = new Vector2(1f, 0f);
            shadow.sizeDelta = new Vector2(16f, 22f);
            shadow.anchoredPosition = new Vector2(0f, -5f);
            Img(shadow, t.Shadow, new Color(1f, 1f, 1f, 0.85f));

            var body = Stretch("Body", roll);
            mask = Img(body, t.RollMask, Color.white);
            mask.type = Image.Type.Sliced;
            body.gameObject.AddComponent<Mask>().showMaskGraphic = false;

            var texRt = Stretch("Surface", body);
            tex = texRt.gameObject.AddComponent<RawImage>();
            tex.texture = t.RollTex;
            tex.raycastTarget = false;

            var shade = Stretch("Shade", body);
            Img(shade, t.RollShade, Color.white);

            cap = Rect("Cap", roll, new Vector2(0f, 20f));
            cap.anchorMin = new Vector2(0f, 1f);
            cap.anchorMax = new Vector2(1f, 1f);
            cap.anchoredPosition = Vector2.zero;
            Img(cap, t.RollCap, Color.white);
            return roll;
        }

        static void BuildDemo(Textures t, TMP_FontAsset regular, ParchmentTitleCard card)
        {
            var go = new GameObject(DemoName, typeof(RectTransform));
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = -10;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var bg = Stretch("Backdrop", (RectTransform)go.transform);
            var raw = bg.gameObject.AddComponent<RawImage>();
            raw.texture = t.Backdrop;
            raw.raycastTarget = false;

            var hint = Rect("Hint", (RectTransform)go.transform, new Vector2(900f, 40f));
            Anchor(hint, new Vector2(0.5f, 0f));
            hint.anchoredPosition = new Vector2(0f, 48f);
            var hintText = Text(hint, regular, 24f, new Color(0.96f, 0.91f, 0.78f, 0.55f), 2f);
            hintText.text = "Space · Enter · 클릭 — 넘기기";

            var demo = go.AddComponent<ParchmentTitleCardDemo>();
            demo.card = card;
        }

        // ── UI helpers ──────────────────────────────────────────────

        static RectTransform Rect(string name, Transform parent, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = LayerMask.NameToLayer("UI");
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            return rt;
        }

        static RectTransform Stretch(string name, Transform parent)
        {
            var rt = Rect(name, parent, Vector2.zero);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        static void Anchor(RectTransform rt, Vector2 anchor) => rt.anchorMin = rt.anchorMax = anchor;

        static Image Img(RectTransform rt, Sprite sprite, Color color)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        static TextMeshProUGUI Text(RectTransform rt, TMP_FontAsset font, float size, Color color, float spacing)
        {
            var text = rt.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.fontSize = size;
            text.color = color;
            text.characterSpacing = spacing;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            return text;
        }

        // ── Fonts ───────────────────────────────────────────────────

        static TMP_FontAsset FontAsset(string name)
        {
            var path = $"{FontDir}/{name} SDF.asset";
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (existing) return existing;

            var font = AssetDatabase.LoadAssetAtPath<Font>($"{FontDir}/{name}.ttf");
            // Dynamic: Hangul glyphs are added to the atlas the first time they are drawn.
            var asset = TMP_FontAsset.CreateFontAsset(font, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024,
                AtlasPopulationMode.Dynamic, true);
            asset.name = $"{name} SDF";
            AssetDatabase.CreateAsset(asset, path);
            asset.atlasTextures[0].name = $"{name} Atlas";
            AssetDatabase.AddObjectToAsset(asset.atlasTextures[0], asset);
            asset.material.name = $"{name} Material";
            AssetDatabase.AddObjectToAsset(asset.material, asset);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            return asset;
        }

        // ── Textures ────────────────────────────────────────────────

        sealed class Textures
        {
            public Sprite Sheet, RollMask, RollShade, RollCap, Glint, Shadow, Nameplate, OrnamentLine, Diamond;
            public Texture2D RollTex, Backdrop;
        }

        static Textures BakeTextures()
        {
            var t = new Textures();
            t.Sheet = SaveSprite("parchment_sheet", (int)(W * S), (int)((H + 2f * Margin) * S), SheetPixel, 200f);
            t.RollTex = SaveTexture("parchment_roll", 92, 256, RollPixel, TextureWrapMode.Repeat);
            t.RollShade = SaveSprite("parchment_roll_shade", 256, 4, RollShadePixel, 100f);
            t.RollMask = SaveSprite("parchment_roll_mask", 64, 96, RollMaskPixel, 100f, new Vector4(0f, 32f, 0f, 0f));
            t.RollCap = SaveSprite("parchment_roll_cap", 128, 128, RollCapPixel, 100f);
            t.Glint = SaveSprite("parchment_glint", 64, 256, GlintPixel, 100f);
            t.Shadow = SaveSprite("parchment_shadow", 128, 64, ShadowPixel, 100f);
            t.Nameplate = SaveSprite("parchment_nameplate", 160, 108, NameplatePixel, 200f, new Vector4(44f, 0f, 44f, 0f));
            t.OrnamentLine = SaveSprite("parchment_ornament_line", 512, 8, OrnamentLinePixel, 200f);
            t.Diamond = SaveSprite("parchment_diamond", 64, 64, DiamondPixel, 200f);
            t.Backdrop = SaveTexture("parchment_demo_backdrop", 960, 540, BackdropPixel, TextureWrapMode.Clamp);
            return t;
        }

        // Sheet: blotchy parchment, darker top/bottom bands, inner shadow, gold double rule, hand-torn edges.
        static Color SheetPixel(int x, int y, int w, int h)
        {
            float u = (x + 0.5f) / S, v = (y + 0.5f) / S;

            // Low-frequency wobble over everything (the HTML version's feDisplacementMap).
            float su = u + (Fbm(u * 0.045f, v * 0.06f, 3, 1) - 0.5f) * 8f;
            float sv = v + (Fbm(u * 0.045f, v * 0.06f, 3, 2) - 0.5f) * 8f;

            float top = Margin + H + (Fbm(u * 0.35f, 3.3f, 2, 3) - 0.5f) * 4f;
            float bottom = Margin + (Fbm(u * 0.35f, 7.7f, 2, 4) - 0.5f) * 4f;
            float edge = Mathf.Min(sv - bottom, top - sv);
            float alpha = Mathf.Clamp01(edge * S + 0.5f);

            var c = Parchment(su, sv, Hex("eedbb0"));

            float fy = (sv - Margin) / H;   // 0 at the bottom rule … 1 at the top
            c = Over(c, new Color(120 / 255f, 72 / 255f, 22 / 255f), 0.28f * Mathf.Clamp01(1f - (1f - fy) / 0.16f));
            c = Over(c, new Color(120 / 255f, 72 / 255f, 22 / 255f), 0.32f * Mathf.Clamp01(1f - fy / 0.16f));

            float side = Mathf.Min(su, W - su);
            float inner = Mathf.Min(Mathf.Max(edge, 0f), side);
            float sh = 1f - Smooth(0f, 38f, inner);
            c = Over(c, new Color(110 / 255f, 62 / 255f, 18 / 255f), 0.38f * sh * sh);
            c = Over(c, new Color(90 / 255f, 50 / 255f, 15 / 255f), 0.35f * (1f - Smooth(0f, 5f, edge)));

            var rule = new Color(160 / 255f, 108 / 255f, 30 / 255f);
            float dTop = Margin + H - sv, dBottom = sv - Margin;
            foreach (float d in new[] { dTop, dBottom })
            {
                c = Over(c, rule, 0.75f * Band(d, 15f, 16.5f));
                c = Over(c, rule, 0.45f * Band(d, 19.5f, 20.5f));
            }
            c.a = alpha;
            return c;
        }

        static Color Parchment(float u, float v, Color baseColor)
        {
            var c = baseColor;
            float blot = Mathf.Clamp01(1.7f * Fbm(u * 0.012f, v * 0.022f, 4, 7) - 0.62f);
            c = Over(c, new Color(0.45f, 0.27f, 0.08f), blot * 0.45f);
            float fiber = Mathf.Clamp01(1.3f * Fbm(u * 0.9f, v * 0.4f, 2, 3) - 0.5f);
            c = Over(c, new Color(0.38f, 0.25f, 0.1f), fiber * 0.5f);
            return c;
        }

        // Roll surface: one horizontally and vertically tileable patch of paper with the wound-layer lines.
        static Color RollPixel(int x, int y, int w, int h)
        {
            float pu = w / S, pv = h / S;
            float u = (x + 0.5f) / S, v = (y + 0.5f) / S;
            float a = u / pu, b = v / pv;
            Color Sample(float uu, float vv) => Parchment(uu, vv, Hex("e3c995"));
            var c = Color.Lerp(
                Color.Lerp(Sample(u, v), Sample(u - pu, v), a),
                Color.Lerp(Sample(u, v - pv), Sample(u - pu, v - pv), a), b);
            float m = u % 23f;
            c = Over(c, new Color(100 / 255f, 62 / 255f, 20 / 255f), 0.22f * Band(m, 7f, 8f));
            c = Over(c, new Color(1f, 244 / 255f, 214 / 255f), 0.25f * Band(m, 13f, 14f));
            c.a = 1f;
            return c;
        }

        // Cylinder shading across the roll, premultiplied so the dark→light stops blend like CSS gradients.
        static Color RollShadePixel(int x, int y, int w, int h)
        {
            var stops = new (float at, Color c)[]
            {
                (0f, new Color(58 / 255f, 32 / 255f, 8 / 255f, 0.62f)),
                (0.16f, new Color(58 / 255f, 32 / 255f, 8 / 255f, 0.2f)),
                (0.36f, new Color(1f, 246 / 255f, 220 / 255f, 0.42f)),
                (0.54f, new Color(1f, 246 / 255f, 220 / 255f, 0f)),
                (0.76f, new Color(58 / 255f, 32 / 255f, 8 / 255f, 0.22f)),
                (1f, new Color(44 / 255f, 24 / 255f, 6 / 255f, 0.7f)),
            };
            float k = (x + 0.5f) / w;
            for (int i = 1; i < stops.Length; i++)
            {
                if (k > stops[i].at) continue;
                float f = Mathf.InverseLerp(stops[i - 1].at, stops[i].at, k);
                Color p0 = Premul(stops[i - 1].c), p1 = Premul(stops[i].c);
                var p = Color.Lerp(p0, p1, f);
                return p.a > 1e-4f ? new Color(p.r / p.a, p.g / p.a, p.b / p.a, p.a) : new Color(1f, 1f, 1f, 0f);
            }
            return stops[^1].c;
        }

        // White mask with a half-ellipse bottom (sliced: the bottom 32 px keep their shape).
        static Color RollMaskPixel(int x, int y, int w, int h)
        {
            if (y >= 32) return Color.white;
            float nx = (x + 0.5f - 32f) / 32f, ny = (32f - (y + 0.5f)) / 32f;
            float r = Mathf.Sqrt(nx * nx + ny * ny);
            return new Color(1f, 1f, 1f, Mathf.Clamp01((1f - r) * 32f + 0.5f));
        }

        // The roll's end seen from slightly above: wound rings around a dark core.
        static Color RollCapPixel(int x, int y, int w, int h)
        {
            float nx = (x + 0.5f) / (w * 0.5f) - 1f, ny = (y + 0.5f) / (h * 0.5f) - 1f;
            float r = Mathf.Sqrt(nx * nx + ny * ny);
            float alpha = Mathf.Clamp01((1f - r) * w * 0.5f + 0.5f);
            if (alpha <= 0f) return new Color(Hex("f2e1b9").r, Hex("f2e1b9").g, Hex("f2e1b9").b, 0f);

            float f = r * 7f % 1f;
            float ring = Smooth(0.42f, 0.5f, f) * (1f - Smooth(0.68f, 0.76f, f));
            var c = Color.Lerp(Hex("f2e1b9"), Hex("b28750"), ring);
            c = Color.Lerp(Hex("5a3a16"), c, Smooth(0.14f, 0.17f, r));
            c = Over(c, new Color(110 / 255f, 70 / 255f, 26 / 255f), 0.6f * Smooth(0.9f, 0.97f, r));
            c = Over(c, new Color(70 / 255f, 40 / 255f, 10 / 255f), 0.45f * Smooth(0.55f, 1f, r) * Mathf.Clamp01(-ny * 1.6f));
            c.a = alpha;
            return c;
        }

        static Color GlintPixel(int x, int y, int w, int h)
        {
            float nx = Mathf.Abs((x + 0.5f) / w * 2f - 1f);
            float a = Mathf.Pow(1f - nx, 1.6f);
            return new Color(1f, 1f, 1f, a);
        }

        static Color ShadowPixel(int x, int y, int w, int h)
        {
            float nx = (x + 0.5f) / w * 2f - 1f, ny = (y + 0.5f) / h * 2f - 1f;
            float r = Mathf.Sqrt(nx * nx + ny * ny);
            float a = 1f - Smooth(0f, 1f, r);
            return new Color(0f, 0f, 0f, 0.55f * a * a);
        }

        // Gold ribbon with pointed ends; the 44 px borders are the ends, the middle stretches.
        static Color NameplatePixel(int x, int y, int w, int h)
        {
            const float point = 32f;
            bool Inside(float px, float py, float inset)
            {
                float hh = h * 0.5f - inset;
                float dy = Mathf.Abs(py - h * 0.5f);
                if (dy > hh) return false;
                float lx = px - inset * 1.4f, rx = w - inset * 1.4f - px;
                float reach = Mathf.Min(lx, rx);
                return reach >= 0f && dy <= hh * Mathf.Min(1f, reach / point);
            }
            float cov = Coverage((px, py) => Inside(px, py, 0f), x, y);
            if (cov <= 0f) return new Color(Gold.r, Gold.g, Gold.b, 0f);
            float inner = Coverage((px, py) => Inside(px, py, 2.5f), x, y);

            float tTop = 1f - (y + 0.5f) / h;
            var c = tTop < 0.55f ? Color.Lerp(GoldHi, Gold, tTop / 0.55f) : Color.Lerp(Gold, GoldDeep, (tTop - 0.55f) / 0.45f);
            c = Over(c, new Color(1f, 0.98f, 0.88f), 0.55f * Band(h - (y + 0.5f), 4f, 6f));
            c = Over(c, new Color(120 / 255f, 80 / 255f, 20 / 255f), 0.35f * Band(y + 0.5f, 4f, 6f));
            c = Over(c, new Color(130 / 255f, 84 / 255f, 22 / 255f), 0.8f * (cov - inner));
            c.a = cov;
            return c;
        }

        static Color OrnamentLinePixel(int x, int y, int w, int h)
        {
            float k = (x + 0.5f) / w;
            float fade = Smooth(0f, 0.45f, Mathf.Min(k, 1f - k));
            float gap = Smooth(0.025f, 0.045f, Mathf.Abs(k - 0.5f));
            float line = Mathf.Clamp01(Mathf.Min(y + 0.5f - 1.5f, 6.5f - (y + 0.5f)) + 0.5f);
            return new Color(128 / 255f, 82 / 255f, 22 / 255f, fade * gap * line);
        }

        static Color DiamondPixel(int x, int y, int w, int h)
        {
            float nx = (x + 0.5f) / (w * 0.5f) - 1f, ny = (y + 0.5f) / (h * 0.5f) - 1f;
            float cov = Coverage((px, py) =>
            {
                float ax = px / (w * 0.5f) - 1f, ay = py / (h * 0.5f) - 1f;
                return Mathf.Abs(ax) + Mathf.Abs(ay) <= 0.62f;
            }, x, y);
            float d = Mathf.Abs(nx) + Mathf.Abs(ny);
            float glow = 0.6f * (1f - Smooth(0.6f, 1f, d));
            float g = Mathf.Clamp01((nx - ny + 1.24f) / 2.48f);   // top-left → bottom-right
            var c = g < 0.5f ? Color.Lerp(GoldHi, Gold, g * 2f) : Color.Lerp(Gold, GoldLo, g * 2f - 1f);
            var glowColor = new Color(Gold.r, Gold.g, Gold.b, glow);
            var outC = Color.Lerp(glowColor, new Color(c.r, c.g, c.b, 1f), cov);
            outC.a = Mathf.Max(cov, glow * (1f - cov));
            return outC;
        }

        // Demo only: night sky with a crystal glow and a vignette, as in the HTML stage.
        static Color BackdropPixel(int x, int y, int w, int h)
        {
            float u = (x + 0.5f) / w, v = 1f - (y + 0.5f) / h;   // v = 0 at the top
            float r1 = Mathf.Sqrt(Sq((u - 0.5f) / 1.2f) + Sq(v / 0.8f));
            var c = Color.Lerp(Night2, Night, Mathf.Clamp01(r1 / 0.7f));
            float r2 = Mathf.Sqrt(Sq((u - 0.5f) / 0.6f) + Sq((v - 0.32f) / 0.45f));
            c = Over(c, new Color(143 / 255f, 216 / 255f, 1f), 0.18f * (1f - Mathf.Clamp01(r2 / 0.7f)));
            float r3 = Mathf.Sqrt(Sq((u - 0.5f) / 1f) + Sq((v - 0.45f) / 0.75f));
            c = Over(c, Color.black, 0.55f * Mathf.Clamp01((r3 - 0.55f) / 0.45f));
            // Dither so the dark gradient does not band.
            float n = (Hash(x, y) - 0.5f) / 255f;
            c.r += n; c.g += n; c.b += n;
            c.a = 1f;
            return c;
        }

        // ── Texture IO ──────────────────────────────────────────────

        static Sprite SaveSprite(string name, int w, int h, Func<int, int, int, int, Color> pixel, float ppu, Vector4 border = default)
        {
            var path = Write(name, w, h, pixel);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.spritePixelsPerUnit = ppu;
            imp.spriteBorder = border;
            imp.mipmapEnabled = false;
            imp.alphaIsTransparency = true;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            var settings = new TextureImporterSettings();
            imp.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            imp.SetTextureSettings(settings);
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        static Texture2D SaveTexture(string name, int w, int h, Func<int, int, int, int, Color> pixel, TextureWrapMode wrap)
        {
            var path = Write(name, w, h, pixel);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = TextureImporterType.Default;
            imp.mipmapEnabled = false;
            imp.wrapMode = wrap;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static string Write(string name, int w, int h, Func<int, int, int, int, Color> pixel)
        {
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                px[y * w + x] = pixel(x, y, w, h);
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels32(px);
            var bytes = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);
            var path = $"{TexDir}/{name}.png";
            File.WriteAllBytes(path, bytes);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            return path;
        }

        // ── Math ────────────────────────────────────────────────────

        static float Fbm(float x, float y, int octaves, int seed)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            float ox = seed * 17.13f, oy = seed * 31.71f;
            for (int i = 0; i < octaves; i++)
            {
                sum += amp * Mathf.PerlinNoise(x + ox, y + oy);
                norm += amp;
                amp *= 0.5f;
                x *= 2f;
                y *= 2f;
            }
            return sum / norm;
        }

        // Coverage of the band [a, b] (canvas units) by a pixel centered at d.
        static float Band(float d, float a, float b)
        {
            float hw = 0.5f / S;
            return Mathf.Clamp01((Mathf.Min(b, d + hw) - Mathf.Max(a, d - hw)) / (2f * hw));
        }

        static float Coverage(Func<float, float, bool> inside, int x, int y)
        {
            int hit = 0;
            for (int j = 0; j < 4; j++)
            for (int i = 0; i < 4; i++)
                if (inside(x + (i + 0.5f) / 4f, y + (j + 0.5f) / 4f)) hit++;
            return hit / 16f;
        }

        static Color Over(Color under, Color over, float a)
        {
            a = Mathf.Clamp01(a);
            return new Color(
                Mathf.Lerp(under.r, over.r, a),
                Mathf.Lerp(under.g, over.g, a),
                Mathf.Lerp(under.b, over.b, a),
                under.a);
        }

        // GLSL-style smoothstep (Mathf.SmoothStep is an interpolator, not this).
        static float Smooth(float e0, float e1, float x)
        {
            float t = Mathf.Clamp01((x - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }

        static Color Premul(Color c) => new(c.r * c.a, c.g * c.a, c.b * c.a, c.a);
        static float Sq(float v) => v * v;

        static float Hash(int x, int y)
        {
            uint h = (uint)(x * 374761393 + y * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;
            return (h ^ (h >> 16)) / (float)uint.MaxValue;
        }

        static Color Hex(string hex) => ColorUtility.TryParseHtmlString("#" + hex, out var c) ? c : Color.magenta;

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
