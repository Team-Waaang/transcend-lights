using UnityEngine;

namespace PhonePointer
{
    // Deterministic per-phone colors. Index 0..N map to distinct, high-contrast hues so that
    // several phones drawing or pointing at once stay visually separable. Pure and testable.
    public static class SessionPalette
    {
        static readonly Color[] Colors =
        {
            new Color(.22f, .89f, .82f), // cyan
            new Color(.98f, .55f, .25f), // orange
            new Color(.55f, .80f, .35f), // green
            new Color(.95f, .40f, .62f), // pink
            new Color(.60f, .55f, .98f), // violet
            new Color(.98f, .84f, .30f), // yellow
            new Color(.40f, .70f, .98f), // blue
            new Color(.90f, .35f, .35f), // red
        };

        // Count describes the legacy presets, not a participant limit.
        public static int Count => Colors.Length;

        // Index is assigned once per session. Never divide by the current phone count:
        // joining/leaving phones must not recolor existing pointers or drawing strokes.
        public static Color Get(int index)
        {
            if(index<0) throw new System.ArgumentOutOfRangeException(nameof(index));
            if(index<Colors.Length) return Colors[index];
            double step=(double)index-Colors.Length+1;
            double hue=step*0.6180339887498948482;
            hue-=System.Math.Floor(hue);
            float saturation=.62f+.10f*(float)((index-Colors.Length)%3);
            float brightness=.88f+.05f*(float)(((index-Colors.Length)/3)%3);
            return Color.HSVToRGB((float)hue,saturation,brightness);
        }
    }
}
