using System.Collections.Generic;
using UnityEngine;

namespace AiHackathon.Game
{
    /// <summary>
    /// Scores a trace of the rune on its <see cref="RuneStoneFx"/>. The rune is a grid on the stone's face (local XY
    /// of this transform, face toward -Z) copied from the <see cref="RuneDefinition"/>. Feed world points with
    /// <see cref="AddPoint"/> (the laser's hit point), then <see cref="Finish"/>. <see cref="Coverage"/> is the share
    /// of rune cells that came within <see cref="brushRadius"/> of the trace, <see cref="Accuracy"/> the share of
    /// trace points that were on the rune; success needs both at their thresholds.
    /// </summary>
    public class RuneTrace : MonoBehaviour
    {
        public RuneStoneFx stone;

        [Header("Scoring")]
        [Tooltip("Distance (local units) within which a trace point counts a rune cell as drawn.")]
        public float brushRadius = 0.07f;
        [Range(0f, 1f)] public float coverageToPass = 0.7f;
        [Range(0f, 1f)] public float accuracyToPass = 0.5f;

        public float Coverage { get; private set; }
        public float Accuracy { get; private set; }
        public int PointCount => points.Count;
        public bool Passed => Coverage >= coverageToPass && Accuracy >= accuracyToPass;

        int gridWidth, gridHeight;
        Vector2 gridMin, gridMax;
        float faceZ;
        bool[] cells;
        bool[] drawn;
        int runeCells, onRune;
        float audioUntil = -10f;
        readonly List<Vector2> points = new List<Vector2>();

        void Awake()
        {
            if (cells == null && stone != null && stone.rune != null) SetRune(stone.rune);
        }

        public void SetRune(RuneDefinition rune)
        {
            if (rune == null) return;
            gridWidth = rune.gridWidth;
            gridHeight = rune.gridHeight;
            gridMin = rune.gridMin;
            gridMax = rune.gridMax;
            faceZ = rune.faceZ;
            cells = rune.cells;
            runeCells = 0;
            if (cells != null) foreach (var c in cells) if (c) runeCells++;
            Clear();
        }

        public void Clear()
        {
            audioUntil = -10f;
            points.Clear();
            onRune = 0;
            drawn = cells != null ? new bool[cells.Length] : null;
            Coverage = Accuracy = 0f;
        }

        Vector2 CellSize => new Vector2((gridMax.x - gridMin.x) / Mathf.Max(1, gridWidth), (gridMax.y - gridMin.y) / Mathf.Max(1, gridHeight));

        /// <summary>Adds a world-space point; it is flattened onto the face plane.</summary>
        public void AddPoint(Vector3 world)
        {
            if (cells == null || cells.Length == 0 || drawn == null) return;
            audioUntil = Time.unscaledTime + 0.12f;
            var local = transform.InverseTransformPoint(world);
            var p = new Vector2(local.x, local.y);
            points.Add(p);
            var cell = CellSize;
            int rx = Mathf.CeilToInt(brushRadius / cell.x), ry = Mathf.CeilToInt(brushRadius / cell.y);
            int cx = Mathf.FloorToInt((p.x - gridMin.x) / cell.x), cy = Mathf.FloorToInt((p.y - gridMin.y) / cell.y);
            bool hit = false;
            int got = 0;
            for (int y = cy - ry; y <= cy + ry; y++)
            for (int x = cx - rx; x <= cx + rx; x++)
            {
                if (x < 0 || y < 0 || x >= gridWidth || y >= gridHeight) continue;
                int i = y * gridWidth + x;
                if (!cells[i]) continue;
                var center = gridMin + new Vector2((x + 0.5f) * cell.x, (y + 0.5f) * cell.y);
                if ((center - p).sqrMagnitude > brushRadius * brushRadius) continue;
                drawn[i] = true;
                hit = true;
            }
            if (hit) onRune++;
            for (int i = 0; i < drawn.Length; i++) if (drawn[i]) got++;
            Coverage = runeCells > 0 ? (float)got / runeCells : 0f;
            Accuracy = points.Count > 0 ? (float)onRune / points.Count : 0f;
        }

        /// <summary>Plays the stone's success or failure for the trace so far. Returns whether it passed.</summary>
        public bool Finish()
        {
            audioUntil = -10f;
            bool ok = Passed;
            if (stone != null)
            {
                if (ok) stone.PlaySuccess(); else stone.PlayFail();
            }
            return ok;
        }

        void Update() => GameAudio.Loop("rune_trace_loop", transform, Time.unscaledTime < audioUntil ? 1f : 0f);

        void OnDrawGizmosSelected()
        {
            var rune = stone != null ? stone.rune : null;
            if (rune == null || rune.cells == null) return;
            var cell = new Vector2((rune.gridMax.x - rune.gridMin.x) / Mathf.Max(1, rune.gridWidth), (rune.gridMax.y - rune.gridMin.y) / Mathf.Max(1, rune.gridHeight));
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.6f);
            for (int y = 0; y < rune.gridHeight; y++)
            for (int x = 0; x < rune.gridWidth; x++)
                if (rune.cells[y * rune.gridWidth + x])
                    Gizmos.DrawCube(new Vector3(rune.gridMin.x + (x + 0.5f) * cell.x, rune.gridMin.y + (y + 0.5f) * cell.y, rune.faceZ - 0.01f), new Vector3(cell.x, cell.y, 0.002f));
        }
    }
}
