using AiHackathon.Game;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Waaang
{
    // The connection screen: shows the registration image and the pairing QR (RegistrationBoard), each player's
    // status, and starts the school day (PeriodFlow: 1교시 → 2교시 → 3교시) once every phone is in and calibrated.
    // Enter / Space starts at once with whoever is connected; missing players play with the mouse.
    public sealed class ConnectionFlow : MonoBehaviour
    {
        [Tooltip("Scene to open instead of the period flow (empty: the first period).")]
        public string gameSceneName = "";

        static readonly string[] Names = { "Amelia", "Nox" };
        static readonly Color[] Colors = { new Color(1f, 0.82f, 0.35f), new Color(0.45f, 0.75f, 1f) };

        bool started;

        void Awake()
        {
            var receiver = GetComponent<PhonePointer.PhonePointerReceiver>();
            var combined = GetComponent<PhonePointer.PhonePointerRegistrationView>();
            if (combined != null)
                combined.enabled = false;
            if (GetComponent<RegistrationBoard>() == null)
            {
                var board = gameObject.AddComponent<RegistrationBoard>();
                board.Receiver = receiver;
            }
        }

        void Update()
        {
            var link = PhoneLink.Instance;
            if (started || link == null || link.Receiver == null)
                return;
            var kb = Keyboard.current;
            bool force = kb != null && (kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame);
            if (!link.Receiver.GameStarted && !force)
                return;
            if (!link.Receiver.GameStarted)
                RegistrationBoard.StartedWith = link.Receiver.ConnectedCount;
            started = true;
            // The old setup pointed at the "Game" test scene; the school day starts at the first period.
            if (!string.IsNullOrEmpty(gameSceneName) && gameSceneName != "Game")
                PeriodFlow.Go(gameSceneName, "", "");
            else if (!PeriodFlow.Next())
                UnityEngine.SceneManagement.SceneManager.LoadScene("Game");
        }

        void OnGUI()
        {
            var link = PhoneLink.Instance;
            if (started || link == null || link.Receiver == null)
                return;
            // Behind the registration board (it draws the image and the QR on top).
            GUI.depth = 10;
            float s = GameGui.Scale;
            float w = Screen.width, h = Screen.height;
            GameGui.Rect(new Rect(0f, 0f, w, h), new Color(0.035f, 0.02f, 0.07f));
            float t = Time.unscaledTime;
            GameGui.DrawCentered(GameGui.Soft, new Vector2(w * 0.5f, h * 0.5f), h * 1.4f, new Color(0.35f, 0.18f, 0.6f, 0.35f));
            GameGui.DrawCentered(GameGui.Soft, new Vector2(w * (0.3f + 0.05f * Mathf.Sin(t * 0.3f)), h * 0.85f), h * 0.9f, new Color(0.55f, 0.3f, 0.9f, 0.18f));
            GameGui.DrawCentered(GameGui.Soft, new Vector2(w * (0.72f + 0.05f * Mathf.Cos(t * 0.27f)), h * 0.15f), h * 0.8f, new Color(1f, 0.7f, 0.35f, 0.1f));
            // Drifting motes.
            for (int i = 0; i < 40; i++)
            {
                float u = Mathf.Repeat(i * 0.6180339f + t * 0.01f * (1 + i % 3), 1f);
                float v = Mathf.Repeat(i * 0.3819660f - t * 0.02f * (1 + i % 2), 1f);
                float a = 0.25f + 0.25f * Mathf.Sin(t * 1.3f + i);
                GameGui.DrawCentered(GameGui.Soft, new Vector2(u * w, v * h), (6f + (i % 4) * 3f) * s, new Color(1f, 0.92f, 1f, a));
            }

            // Title band.
            GameGui.GlowLabel(new Rect(0f, 44f * s, w, 80f * s), "마법학교 입학", GameGui.Style(64, TextAnchor.MiddleCenter), new Color(1f, 0.95f, 0.88f), new Color(0.7f, 0.45f, 1f));
            GameGui.Label(new Rect(0f, 120f * s, w, 40f * s), "휴대폰을 지팡이로 연결하세요", GameGui.Style(26, TextAnchor.MiddleCenter, false), new Color(0.88f, 0.84f, 1f));

            var receiver = link.Receiver;
            if (!receiver.Listening)
            {
                GameGui.Label(new Rect(0f, h * 0.5f, w, 40f * s), "서버를 열지 못했습니다. " + receiver.Error, GameGui.Style(26, TextAnchor.MiddleCenter, false), new Color(1f, 0.6f, 0.65f));
                return;
            }

            // Player slots in the middle column, between the image and the QR.
            float cw = 300f * s, cx = (w - cw) * 0.5f, cy = h * 0.38f;
            for (int i = 0; i < 2; i++)
            {
                var session = CasterInput.Session(i);
                string state;
                float step;
                if (session == null) { state = "대기 중"; step = 0f; }
                else if (!session.Model.Registered) { state = "등록 이미지를 비춰 주세요"; step = 1f; }
                else if (!session.Model.Calibrated) { state = "화면 가운데를 가리키고 CENTER"; step = 2f; }
                else { state = "준비 완료"; step = 3f; }
                var c = Colors[i];
                float y = cy + i * 120f * s;
                bool ready = step >= 3f;
                GameGui.Tex(new Rect(cx - 20f * s, y - 16f * s, cw + 40f * s, 110f * s), GameGui.Soft, new Color(c.r, c.g, c.b, ready ? 0.22f : 0.08f));
                GameGui.GlowLabel(new Rect(cx, y, cw, 40f * s), (i + 1) + "P  " + Names[i], GameGui.Style(34, TextAnchor.MiddleCenter), Color.Lerp(c, Color.white, 0.2f), c, i < receiver.MaxPhones ? 1f : 0.4f);
                GameGui.Label(new Rect(cx - 40f * s, y + 40f * s, cw + 80f * s, 30f * s), state, GameGui.Style(20, TextAnchor.MiddleCenter, false),
                    ready ? new Color(1f, 0.95f, 0.75f) : new Color(1f, 1f, 1f, 0.55f + 0.35f * Mathf.Sin(t * 3f + i)));
                for (int k = 0; k < 3; k++)
                    GameGui.DrawCentered(GameGui.Diamond, new Vector2(w * 0.5f + (k - 1) * 22f * s, y + 80f * s), 11f * s, k < step ? c : new Color(1f, 1f, 1f, 0.2f));
            }

            // Footer: what to do, and the escape hatch.
            GameGui.Label(new Rect(0f, h - 92f * s, w, 34f * s), "같은 Wi-Fi에서 QR을 스캔하고, 등록 이미지를 휴대폰으로 비춰 주세요", GameGui.Style(22, TextAnchor.MiddleCenter, false), new Color(1f, 1f, 1f, 0.8f));
            GameGui.Label(new Rect(0f, h - 56f * s, w, 30f * s),
                "Enter: 지금 시작 (없는 플레이어는 마우스로)   ·   " + link.BoundAddress + "   ·   화면 " + link.displaySizeMeters.x.ToString("0.00") + "m x " + link.displaySizeMeters.y.ToString("0.00") + "m",
                GameGui.Style(18, TextAnchor.MiddleCenter, false), new Color(1f, 1f, 1f, 0.5f));
        }
    }
}
