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
            if (c.LastLighter >= 0 && c.LastLighter < scores.Length) scores[c.LastLighter]++;
            if (left <= 0)
            {
                Cleared = true;
                onCleared.Invoke();
            }
            else Say(left == 1 ? "마지막 하나!" : "석화! 남은 " + left);
        }

        void Say(string text)
        {
            message = text;
            messageTime = Time.time;
        }

        void OnGUI()
        {
            if (!showHud || game == null || game.CurrentPhase != GameManager.Phase.Play) return;
            float scale = Screen.height / 1080f;
            var style = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(26 * scale), fontStyle = FontStyle.Bold };
            var small = new GUIStyle(style) { fontSize = Mathf.RoundToInt(20 * scale), fontStyle = FontStyle.Normal };
            for (int i = 0; i < game.players.Length && i < scores.Length; i++)
            {
                var profile = game.players[i].profile;
                bool leftSide = i == 0;
                float w = 520f * scale, x = leftSide ? 30f * scale : Screen.width - w - 30f * scale, y = 24f * scale;
                style.alignment = leftSide ? TextAnchor.UpperLeft : TextAnchor.UpperRight;
                style.normal.textColor = profile != null ? profile.playerColor : Color.white;
                string name = profile != null ? profile.displayName : "P" + (i + 1);
                GUI.Label(new Rect(x, y, w, 40f * scale), $"{name}   석화 {scores[i]}", style);
            }

            var center = new GUIStyle(style) { alignment = TextAnchor.UpperCenter };
            center.normal.textColor = new Color(0.85f, 0.9f, 1f);
            GUI.Label(new Rect(0f, 24f * scale, Screen.width, 40f * scale), $"남은 돌정령 {Mathf.Max(0, left)}/{creatures.Length}", center);
            if (!Cleared && !string.IsNullOrEmpty(message) && Time.time - messageTime < 2f)
            {
                center.normal.textColor = new Color(1f, 0.85f, 0.45f);
                GUI.Label(new Rect(0f, 64f * scale, Screen.width, 40f * scale), message, center);
            }

            if (game.mouseTest)
            {
                small.alignment = TextAnchor.LowerCenter;
                small.normal.textColor = new Color(1f, 1f, 1f, 0.75f);
                GUI.Label(new Rect(0f, Screen.height - 50f * scale, Screen.width, 40f * scale),
                    "마우스 테스트: 좌클릭 = 왼쪽 플레이어 빛, 우클릭 = 오른쪽 플레이어 빛 · 움직이는 돌정령을 3초 동안 계속 비추면 석화", small);
            }
            if (Cleared)
            {
                var big = new GUIStyle(style) { fontSize = Mathf.RoundToInt(64 * scale), alignment = TextAnchor.MiddleCenter };
                big.normal.textColor = new Color(1f, 0.85f, 0.45f);
                GUI.Label(new Rect(0f, Screen.height * 0.38f, Screen.width, 90f * scale), clearedText, big);
            }
        }
    }
}
