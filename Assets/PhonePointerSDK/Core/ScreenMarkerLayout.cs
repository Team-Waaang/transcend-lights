using System;
using UnityEngine;

namespace PhonePointer
{
    // The tracked reference is a crop of the decorative slab, not the whole slab image.
    // Place that crop's centre at screen centre, with the same pixel scale in X and Y.
    // This keeps the existing SDK anchor and protocol accurate without a new wire format.
    public static class ScreenMarkerLayout
    {
        [Serializable]
        sealed class Profile
        {
            public float displayWidth, displayHeight, referenceX, referenceY;
            public float referenceWidth, referenceHeight;
        }
        static Profile profile;
        static bool loaded;
        public static bool HasProfile { get { Load(); return profile != null; } }

        static void Load()
        {
            if (loaded) return;
            loaded = true;
            var json = Resources.Load<TextAsset>("RuneSlabLayout");
            if (!json) return;
            var value = JsonUtility.FromJson<Profile>(json.text);
            if (value == null || value.displayWidth <= 0 || value.displayHeight <= 0 ||
                value.referenceWidth <= 0 || value.referenceHeight <= 0 ||
                value.referenceX < 0 || value.referenceY < 0 ||
                value.referenceX + value.referenceWidth > value.displayWidth ||
                value.referenceY + value.referenceHeight > value.displayHeight) return;
            profile = value;
        }

        public static Rect QrRect(float width, float height)
        {
            float side = Mathf.Clamp(Mathf.Min(height * .36f, width * .27f), 144f, 390f);
            return new Rect(width - side - 28f, (height - side) * .5f, side, side);
        }

        public static bool TryGetRects(float width, float height, out Rect slab, out Rect reference)
        {
            Load();
            slab = reference = default;
            if (profile == null || width <= 0 || height <= 0) return false;
            float cx = width * .5f, cy = height * .5f;
            float rx = profile.referenceX + profile.referenceWidth * .5f;
            float ry = profile.referenceY + profile.referenceHeight * .5f;
            float top = Mathf.Min(120f, height * .12f), bottom = Mathf.Min(170f, height * .35f);
            float right = QrRect(width, height).xMin - 24f;
            float scale = Mathf.Min((cx - 24f) / rx, (right - cx) / (profile.displayWidth - rx));
            scale = Mathf.Min(scale, (cy - top) / ry, (height - bottom - cy) / (profile.displayHeight - ry));
            if (scale <= 0) return false;
            slab = new Rect(cx - rx * scale, cy - ry * scale, profile.displayWidth * scale, profile.displayHeight * scale);
            reference = new Rect(cx - profile.referenceWidth * scale * .5f, cy - profile.referenceHeight * scale * .5f,
                profile.referenceWidth * scale, profile.referenceHeight * scale);
            return true;
        }

        public static float ReferenceWidthMeters(Vector2 displaySizeMeters, int pixelWidth, int pixelHeight)
        {
            if (TryGetRects(pixelWidth, pixelHeight, out _, out var reference))
                return reference.width / Mathf.Max(1, pixelWidth) * displaySizeMeters.x;
            return Mathf.Min(displaySizeMeters.x, displaySizeMeters.y) * .72f;
        }
    }
}
