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
        [Tooltip("Seconds the clear screen stays before the next period loads (PeriodFlow). Negative: stay.")]
        public float advanceAfter = 4f;

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

        float clearedAt = -10f;

        IEnumerator Advance()
        {
            yield return new WaitForSecondsRealtime(advanceAfter);
            PeriodFlow.Next();
        }

        void CheckCleared()
        {
            foreach (var lane in lanes) if (!lane.done) return;
            Cleared = true;
            clearedAt = Time.unscaledTime;
            onCleared.Invoke();
            if (advanceAfter >= 0f) StartCoroutine(Advance());
        }

        void OnGUI()
        {
            if (!showHud || game == null || game.CurrentPhase != GameManager.Phase.Play) return;
            float s = GameGui.Scale;
            float now = Time.unscaledTime;
            for (int i = 0; i < lanes.Length && i < game.players.Length; i++)
                DrawLane(i, s, now);

            if (game.mouseTest && CasterInput.Session(0) == null)
                GameGui.Label(new Rect(0f, Screen.height - 44f * s, Screen.width, 36f * s),
                    "마우스 테스트: 좌클릭 = 왼쪽 플레이어, 우클릭 = 오른쪽 플레이어 · 돌의 룬을 따라 그리고 손을 떼면 판정",
                    GameGui.Style(19, TextAnchor.MiddleCenter, false), new Color(1f, 1f, 1f, 0.6f));

            if (Cleared)
            {
                float k = now - clearedAt;
                float a = Mathf.Clamp01(k / 0.4f);
                float punch = 1f + 0.3f * Mathf.Exp(-k * 7f);
                float y = Screen.height * 0.36f;
                GameGui.BannerBack(new Rect(0f, y - 30f * s, Screen.width, 210f * s), new Color(0.12f, 0.07f, 0.02f, 0.75f * a));
                GameGui.DrawCentered(GameGui.Soft, new Vector2(Screen.width * 0.5f, y + 55f * s), 900f * s, new Color(1f, 0.8f, 0.4f, 0.2f * a));
                GameGui.GlowLabel(new Rect(0f, y, Screen.width, 120f * s), "1교시 통과!", GameGui.Style(Mathf.RoundToInt(96 * punch), TextAnchor.MiddleCenter), new Color(1f, 0.93f, 0.7f), new Color(1f, 0.7f, 0.3f), a);
                GameGui.Label(new Rect(0f, y + 118f * s, Screen.width, 44f * s), "모든 룬을 깨웠다", GameGui.Style(30, TextAnchor.MiddleCenter, false), new Color(1f, 0.95f, 0.85f), a);
            }
        }

        void DrawLane(int i, float s, float now)
        {
            var lane = lanes[i];
            var profile = game.players[i].profile;
            var trace = lane.stone != null ? lane.stone.trace : null;
            bool left = i == 0;
            var color = profile != null ? profile.playerColor : Color.white;
            string name = profile != null ? profile.displayName : "P" + (i + 1);
            int total = Mathf.Max(1, lane.sequence.Length);
            float w = 470f * s, x = left ? 46f * s : Screen.width - w - 46f * s, y = 34f * s;
            var anchor = left ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight;

            // Panel: soft glow behind, name, rune pips.
            // Dark glass panel so the HUD reads over the sunlit classroom.
            GameGui.BannerBack(new Rect(x - 60f * s, y - 26f * s, w + 120f * s, 210f * s), new Color(0.03f, 0.015f, 0.06f, 0.88f));
            GameGui.Rect(new Rect(left ? x : x + w * 0.35f, y + 82f * s, w * 0.65f, Mathf.Max(1f, 1f * s)), new Color(color.r, color.g, color.b, 0.45f));
            GameGui.GlowLabel(new Rect(x, y, w, 44f * s), name, GameGui.Style(38, anchor), Color.Lerp(color, Color.white, 0.25f), color);
            for (int k = 0; k < total; k++)
            {
                bool done = k < lane.index || lane.done;
                bool current = k == lane.index && !lane.done;
                float px = left ? x + 12f * s + k * 30f * s : x + w - 12f * s - (total - 1 - k) * 30f * s;
                var c = done ? color : current ? Color.Lerp(color, Color.white, 0.5f + 0.5f * Mathf.Sin(now * 5f)) : new Color(1f, 1f, 1f, 0.25f);
                if (done || current) GameGui.DrawCentered(GameGui.Soft, new Vector2(px, y + 62f * s), 40f * s, new Color(color.r, color.g, color.b, 0.45f));
                GameGui.DrawCentered(GameGui.Diamond, new Vector2(px, y + 62f * s), (current ? 22f : 18f) * s, c);
            }
            GameGui.Label(new Rect(x, y + 2f * s, w, 40f * s), lane.done ? "완료" : $"룬  {Mathf.Min(lane.index + 1, total)} / {total}",
                GameGui.Style(26, left ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft), lane.done ? new Color(1f, 0.92f, 0.65f) : new Color(1f, 1f, 1f, 0.9f));

            // Trace meters: coverage and accuracy with their pass marks.
            if (trace != null && !lane.done)
            {
                float by = y + 92f * s;
                Meter(new Rect(x, by, w, 14f * s), "범위", trace.Coverage, trace.coverageToPass, color, left, s);
                Meter(new Rect(x, by + 44f * s, w, 14f * s), "정확도", trace.Accuracy, trace.accuracyToPass, color, left, s);
            }

            // Judgement call-out over the player's side of the screen.
            if (!string.IsNullOrEmpty(lane.message) && now - lane.messageTime < 1.6f)
            {
                float k = now - lane.messageTime;
                float a = Mathf.Clamp01(k / 0.1f) * Mathf.Clamp01((1.6f - k) / 0.4f);
                float punch = 1f + 0.4f * Mathf.Exp(-k * 10f);
                bool good = lane.message != "다시!";
                var glow = good ? color : new Color(1f, 0.35f, 0.45f);
                float cx = left ? Screen.width * 0.27f : Screen.width * 0.73f;
                GameGui.DrawCentered(GameGui.Soft, new Vector2(cx, Screen.height * 0.42f), 380f * s * punch, new Color(glow.r, glow.g, glow.b, 0.25f * a));
                GameGui.GlowLabel(new Rect(cx - 300f * s, Screen.height * 0.42f - 50f * s, 600f * s, 100f * s), lane.message,
                    GameGui.Style(Mathf.RoundToInt(72 * punch), TextAnchor.MiddleCenter), good ? Color.white : new Color(1f, 0.85f, 0.88f), glow, a);
            }
        }

        static void Meter(Rect r, string label, float value, float pass, Color color, bool left, float s)
        {
            bool ok = value >= pass;
            var from = new Color(color.r * 0.4f, color.g * 0.4f, color.b * 0.45f);
            var to = ok ? Color.Lerp(color, Color.white, 0.45f) : color;
            var bar = new Rect(r.x + (left ? 74f * s : 0f), r.y + 9f * s, r.width - 74f * s, r.height);
            GameGui.Label(new Rect(left ? r.x : r.xMax - 70f * s, r.y + 1f * s, 70f * s, 30f * s), label, GameGui.Style(22, left ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight), new Color(1f, 1f, 1f, 0.9f));
            GameGui.FancyBar(bar, value, value, from, to, 1f, false, ok ? 0.6f : 0.25f);
            float mx = bar.x + bar.width * Mathf.Clamp01(pass);
            GameGui.Rect(new Rect(mx - 1f * s, bar.y - 4f * s, 2f * s, bar.height + 8f * s), new Color(1f, 1f, 1f, 0.8f));
            GameGui.DrawCentered(GameGui.Diamond, new Vector2(mx, bar.y - 6f * s), 10f * s, new Color(1f, 0.95f, 0.85f, 0.9f));
            GameGui.Label(new Rect(bar.x, bar.yMax + 2f * s, bar.width, 24f * s), Mathf.RoundToInt(value * 100f) + "%" + (ok ? "  ✓" : ""),
                GameGui.Style(19, left ? TextAnchor.UpperRight : TextAnchor.UpperLeft), ok ? new Color(1f, 0.95f, 0.7f) : new Color(1f, 1f, 1f, 0.7f));
        }
    }
}
