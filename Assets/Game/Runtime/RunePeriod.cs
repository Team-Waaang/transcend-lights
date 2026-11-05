using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace AiHackathon.Game
{
    /// <summary>
    /// Period 1 ("룬") play: each player traces the rune on her own stone with her laser. Hits on her stone feed its
    /// <see cref="RuneTrace"/>; strokes can be lifted and resumed, and once she stops firing for
    /// <see cref="finishDelay"/> the trace is judged. Success moves her stone to the next rune of her lane;
    /// a failure clears the strokes for another try. The period is cleared when every lane is done.
    /// </summary>
    public class RunePeriod : MonoBehaviour
    {
        [Serializable]
        public class Lane
        {
            public RuneStoneFx stone;
            public RuneDefinition[] sequence = new RuneDefinition[0];
            [NonSerialized] public int index;
            [NonSerialized] public bool busy, done;
            [NonSerialized] public float lastFire = -10f;
            [NonSerialized] public string message;
            [NonSerialized] public float messageTime = -10f;
        }

        public GameManager game;
        [Tooltip("Lane i belongs to GameManager player i.")]
        public Lane[] lanes = new Lane[0];
        [Tooltip("Seconds without firing before a trace is judged.")]
        public float finishDelay = 0.9f;
        [Tooltip("Fewer trace points than this are dropped as a stray shot.")]
        public int minPoints = 10;
        public bool showHud = true;

        public UnityEvent onCleared = new UnityEvent();
        public bool Cleared { get; private set; }

        void Start()
        {
            foreach (var lane in lanes)
                if (lane.stone != null && lane.sequence.Length > 0 && lane.stone.rune != lane.sequence[0])
                    lane.stone.SetRune(lane.sequence[0]);
        }

        void Update()
        {
            if (game == null || game.CurrentPhase != GameManager.Phase.Play) return;
            for (int i = 0; i < lanes.Length && i < game.players.Length; i++)
            {
                var lane = lanes[i];
                var rig = game.players[i].rig;
                if (lane.stone == null || lane.stone.trace == null || rig == null || lane.busy || lane.done) continue;
                var trace = lane.stone.trace;
                if (rig.Firing)
                {
                    lane.lastFire = Time.time;
                    if (rig.HasHit && rig.HitCollider != null && rig.HitCollider.GetComponentInParent<RuneStoneFx>() == lane.stone)
                        trace.AddPoint(rig.HitPoint);
                }
                else if (trace.PointCount > 0 && Time.time - lane.lastFire > finishDelay)
                {
                    if (trace.PointCount < minPoints) trace.Clear();
                    else StartCoroutine(Judge(i));
                }
            }
        }

        IEnumerator Judge(int i)
        {
            var lane = lanes[i];
            var rig = game.players[i].rig;
            lane.busy = true;
            var trace = lane.stone.trace;
            bool ok = trace.Finish();
            lane.message = ok ? "성공!" : "다시!";
            lane.messageTime = Time.time;
            yield return new WaitForSeconds(0.35f);
            rig.ClearTrails();
            trace.Clear();
            while (lane.stone.IsPlaying) yield return null;
            if (ok)
            {
                lane.index++;
                if (lane.index < lane.sequence.Length)
                {
                    lane.stone.ChangeRune(lane.sequence[lane.index]);
                    while (lane.stone.IsPlaying) yield return null;
                }
                else
                {
                    lane.done = true;
                    lane.message = "완료!";
                    lane.messageTime = Time.time;
                    CheckCleared();
                }
            }
            lane.busy = false;
        }

        void CheckCleared()
        {
            foreach (var lane in lanes) if (!lane.done) return;
            Cleared = true;
            onCleared.Invoke();
        }

        void OnGUI()
        {
            if (!showHud || game == null || game.CurrentPhase != GameManager.Phase.Play) return;
            float scale = Screen.height / 1080f;
            var style = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(26 * scale), fontStyle = FontStyle.Bold };
            var small = new GUIStyle(style) { fontSize = Mathf.RoundToInt(20 * scale), fontStyle = FontStyle.Normal };
            for (int i = 0; i < lanes.Length && i < game.players.Length; i++)
            {
                var lane = lanes[i];
                var profile = game.players[i].profile;
                var trace = lane.stone != null ? lane.stone.trace : null;
                bool left = i == 0;
                float w = 520f * scale, x = left ? 30f * scale : Screen.width - w - 30f * scale, y = 24f * scale;
                style.alignment = small.alignment = left ? TextAnchor.UpperLeft : TextAnchor.UpperRight;
                style.normal.textColor = profile != null ? profile.playerColor : Color.white;
                string name = profile != null ? profile.displayName : "P" + (i + 1);
                int total = Mathf.Max(1, lane.sequence.Length);
                GUI.Label(new Rect(x, y, w, 40f * scale), $"{name}   룬 {Mathf.Min(lane.index + 1, total)}/{total}", style);
                small.normal.textColor = Color.white;
                if (trace != null && !lane.done)
                    GUI.Label(new Rect(x, y + 38f * scale, w, 30f * scale), $"범위 {trace.Coverage * 100f:0}%  ·  정확도 {trace.Accuracy * 100f:0}%  (기준 {trace.coverageToPass * 100f:0}%)", small);
                if (!string.IsNullOrEmpty(lane.message) && Time.time - lane.messageTime < 1.6f)
                    GUI.Label(new Rect(x, y + 70f * scale, w, 40f * scale), lane.message, style);
            }
            if (game.mouseTest)
            {
                small.alignment = TextAnchor.LowerCenter;
                small.normal.textColor = new Color(1f, 1f, 1f, 0.75f);
                GUI.Label(new Rect(0f, Screen.height - 50f * scale, Screen.width, 40f * scale), "마우스 테스트: 좌클릭 = 왼쪽 플레이어, 우클릭 = 오른쪽 플레이어 · 돌의 룬을 따라 그리고 손을 떼면 판정", small);
            }
            if (Cleared)
            {
                var big = new GUIStyle(style) { fontSize = Mathf.RoundToInt(64 * scale), alignment = TextAnchor.MiddleCenter };
                big.normal.textColor = new Color(1f, 0.85f, 0.45f);
                GUI.Label(new Rect(0f, Screen.height * 0.38f, Screen.width, 90f * scale), "1교시 통과!", big);
            }
        }
    }
}
