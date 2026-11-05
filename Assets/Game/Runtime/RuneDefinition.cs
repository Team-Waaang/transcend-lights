using UnityEngine;

namespace AiHackathon.Game
{
    /// <summary>
    /// One rune stone, baked from its Tripo model by the Period 1 builder: how the model sits on a
    /// <see cref="RuneStoneFx"/> (standing on the stone's origin, rune facing -Z), the glow material whose emission
    /// map is the rune alone, the rune's own colour, and the rune rasterised onto the face for <see cref="RuneTrace"/>.
    /// Positions are in the stone root's local space.
    /// </summary>
    public class RuneDefinition : ScriptableObject
    {
        public string displayName;
        public GameObject model;
        public Material material;
        [ColorUsage(false, true)] public Color glowColor = new Color(1f, 0.72f, 0.28f) * 1.8f;

        [Header("Placement")]
        public Vector3 modelPosition;
        public Quaternion modelRotation = Quaternion.identity;
        public float modelScale = 1f;
        public Vector3 runeCenter;
        public Bounds bounds;

        [Header("Trace grid (local XY of the face)")]
        public int gridWidth;
        public int gridHeight;
        public Vector2 gridMin, gridMax;
        public float faceZ;
        public bool[] cells;
    }
}
