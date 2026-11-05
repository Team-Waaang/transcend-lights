using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace AiHackathon.Game
{
    /// <summary>
    /// Period 2 ("암흑") play: the room is dark and rock creatures roam it. Each player holds fire to shine her wand
    /// light where she points (<see cref="WandLight"/>); a creature caught in a light startles and scurries off, and
    /// one kept lit for about three seconds turns to stone (<see cref="StoneCreature"/>). Both lights on the same
    /// creature harden it faster. The period is cleared when every creature is stone.
    /// </summary>
    public class LightHuntPeriod : MonoBehaviour
    {
        public GameManager game;
        [Tooltip("Light i belongs to GameManager player i.")]
        public WandLight[] lights = new WandLight[0];
        public StoneCreature[] creatures = new StoneCreature[0];
        [Tooltip("How much a second light on the same creature adds.")]
        [Range(0f, 1f)] public float secondLight = 0.6f;
        [Tooltip("Creatures keep at least this far apart.")]
        public float spacing = 0.9f;
        public bool showHud = true;
        public string clearedText = "통과!";
        [Tooltip("Line under the clear banner.")]
        public string clearedSubtitle = "어둠 속의 돌정령을 모두 잠재웠다";
        [Tooltip("Seconds the clear screen stays before the next period loads (PeriodFlow). Negative: stay.")]
        public float advanceAfter = 4f;

        public UnityEvent onCleared = new UnityEvent();
        public bool Cleared { get; private set; }

        int[] scores = new int[2];
        int left;
        bool started;
        string message;
        float messageTime = -10f;

        void Start()
        {
            left = creatures.Length;
            foreach (var c in creatures)
                if (c != null) c.onPetrified.AddListener(OnPetrified);
            scores = new int[Mathf.Max(2, lights.Length)];
        }

        void Update()
        {
            if (game == null || game.CurrentPhase != GameManager.Phase.Play) return;
            if (!started)
            {
                started = true;
                foreach (var c in creatures) if (c != null) c.Wake();
                Say("빛을 비춰 찾아요!");
            }

            foreach (var c in creatures)
            {
                if (c == null || c.Petrified) continue;
                float strength = 0f, best = 0f;
                int who = -1;
                Vector3 origin = Vector3.zero, dir = Vector3.forward;
                for (int i = 0; i < lights.Length; i++)
                {
                    var l = lights[i];
                    if (l == null) continue;
                    float k = l.Lights(c.Center, c.radius);
                    if (k <= 0f) continue;
                    // Anywhere in the pool counts nearly fully; the second light adds a share.
                    float add = Mathf.Lerp(0.75f, 1f, k);
                    strength += strength > 0f ? add * secondLight : add;
                    if (k > best) { best = k; who = i; origin = l.Origin; dir = l.Direction; }
                }
                if (who >= 0) c.Illuminate(strength, who, origin, dir);
            }

            // Keep them from walking into each other.
            for (int a = 0; a < creatures.Length; a++)
            for (int b = a + 1; b < creatures.Length; b++)
            {
                var ca = creatures[a];
                var cb = creatures[b];
                if (ca == null || cb == null) continue;
                Vector3 d = cb.transform.position - ca.transform.position; d.y = 0f;
                float m = d.magnitude;
                if (m >= spacing || m < 1e-4f) continue;
                Vector3 push = d / m * (spacing - m) * 0.5f;
                if (!ca.Petrified) ca.transform.position -= cb.Petrified ? push * 2f : push;
                if (!cb.Petrified) cb.transform.position += ca.Petrified ? push * 2f : push;
            }
        }

        void OnPetrified(StoneCreature c)
        {
            left--;
            if (c.LastLighter >= 0 && c.LastLighter < scores.Length)
            {
                scores[c.LastLighter]++;
                if (c.LastLighter < scoredAt.Length) scoredAt[c.LastLighter] = Time.unscaledTime;
            }
            if (left <= 0)
            {
                Cleared = true;
                clearedAt = Time.unscaledTime;
                onCleared.Invoke();
                if (advanceAfter >= 0f) StartCoroutine(Advance());
            }
            else Say(left == 1 ? "마지막 하나!" : "석화! 남은 " + left);
        }

        float clearedAt = -10f;
        readonly float[] scoredAt = { -10f, -10f, -10f, -10f };

        IEnumerator Advance()
        {
            yield return new WaitForSecondsRealtime(advanceAfter);
            PeriodFlow.Next();
        }

        void Say(string text)
        {
            message = text;
            messageTime = Time.time;
        }

        void OnGUI()
        {
            if (!showHud || game == null || game.CurrentPhase != GameManager.Phase.Play) return;
            float s = GameGui.Scale;
            float now = Time.unscaledTime;
            var moon = new Color(0.62f, 0.75f, 1f);

            // Remaining creatures across the top: a crystal per creature, lit while it still roams, grey once stone.
            int total = creatures.Length;
            float cy = 50f * s;
            GameGui.BannerBack(new Rect(Screen.width * 0.3f, cy - 30f * s, Screen.width * 0.4f, 120f * s), new Color(0.01f, 0.02f, 0.05f, 0.85f));
            GameGui.Label(new Rect(0f, cy - 22f * s, Screen.width, 40f * s), "남은 돌정령", GameGui.Style(26, TextAnchor.MiddleCenter), new Color(0.85f, 0.9f, 1f));
            for (int k = 0; k < total; k++)
            {
                bool stone = creatures[k] == null || creatures[k].Petrified;
                var p = new Vector2(Screen.width * 0.5f + (k - (total - 1) * 0.5f) * 40f * s, cy + 34f * s);
                if (!stone) GameGui.DrawCentered(GameGui.Soft, p, 46f * s, new Color(moon.r, moon.g, moon.b, 0.35f + 0.2f * Mathf.Sin(now * 3f + k)));
                GameGui.DrawCentered(GameGui.Diamond, p, (stone ? 18f : 22f) * s, stone ? new Color(0.55f, 0.55f, 0.6f, 0.7f) : Color.Lerp(moon, Color.white, 0.35f));
            }

            // Player panels: name and how many each has turned to stone.
            for (int i = 0; i < game.players.Length && i < scores.Length; i++)
            {
                var profile = game.players[i].profile;
                bool leftSide = i == 0;
                var color = profile != null ? profile.playerColor : Color.white;
                string name = profile != null ? profile.displayName : "P" + (i + 1);
                float w = 360f * s, x = leftSide ? 46f * s : Screen.width - w - 46f * s, y = 30f * s;
                var anchor = leftSide ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight;
                GameGui.BannerBack(new Rect(x - 50f * s, y - 20f * s, w + 100f * s, 120f * s), new Color(0.01f, 0.02f, 0.05f, 0.85f));
                GameGui.GlowLabel(new Rect(x, y, w, 44f * s), name, GameGui.Style(36, anchor), Color.Lerp(color, Color.white, 0.25f), color);
                float pop = Mathf.Clamp01(1f - (now - scoredAt[Mathf.Min(i, scoredAt.Length - 1)]) / 0.6f);
                var style = GameGui.Style(Mathf.RoundToInt(26 * (1f + 0.35f * pop)), anchor);
                GameGui.Label(new Rect(x, y + 44f * s, w, 36f * s), "석화  " + scores[i], style, Color.Lerp(new Color(1f, 1f, 1f, 0.9f), color, pop));
            }

            if (!Cleared && !string.IsNullOrEmpty(message) && now - messageTime < 2f)
            {
                float k = now - messageTime;
                float a = Mathf.Clamp01(k / 0.15f) * Mathf.Clamp01((2f - k) / 0.5f);
                float my = Screen.height * 0.2f;
                GameGui.BannerBack(new Rect(Screen.width * 0.2f, my - 8f * s, Screen.width * 0.6f, 72f * s), new Color(0.01f, 0.02f, 0.05f, 0.8f * a));
                GameGui.GlowLabel(new Rect(0f, my, Screen.width, 56f * s), message, GameGui.Style(40, TextAnchor.MiddleCenter), new Color(1f, 0.95f, 0.8f), new Color(1f, 0.8f, 0.4f), a);
            }

            if (game.mouseTest && CasterInput.Session(0) == null)
                GameGui.Label(new Rect(0f, Screen.height - 44f * s, Screen.width, 36f * s),
                    "마우스 테스트: 좌클릭 = 왼쪽 플레이어 빛, 우클릭 = 오른쪽 플레이어 빛 · 움직이는 돌정령을 3초 동안 계속 비추면 석화",
                    GameGui.Style(19, TextAnchor.MiddleCenter, false), new Color(1f, 1f, 1f, 0.6f));

            if (Cleared)
            {
                float k = now - clearedAt;
                float a = Mathf.Clamp01(k / 0.4f);
                float punch = 1f + 0.3f * Mathf.Exp(-k * 7f);
                float y = Screen.height * 0.36f;
                GameGui.BannerBack(new Rect(0f, y - 30f * s, Screen.width, 210f * s), new Color(0.01f, 0.02f, 0.06f, 0.8f * a));
                GameGui.DrawCentered(GameGui.Soft, new Vector2(Screen.width * 0.5f, y + 55f * s), 900f * s, new Color(moon.r, moon.g, moon.b, 0.2f * a));
                GameGui.GlowLabel(new Rect(0f, y, Screen.width, 120f * s), clearedText, GameGui.Style(Mathf.RoundToInt(96 * punch), TextAnchor.MiddleCenter), new Color(0.95f, 0.97f, 1f), moon, a);
                GameGui.Label(new Rect(0f, y + 118f * s, Screen.width, 44f * s), clearedSubtitle, GameGui.Style(30, TextAnchor.MiddleCenter, false), new Color(0.9f, 0.93f, 1f), a);
            }
        }
    }
}
