using AiHackathon.Game;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Waaang
{
    // The connection screen: RegistrationBoard shows the pairing QR, then the runic stone to register, and the
    // school day (PeriodFlow: 1교시 → 2교시 → 3교시) starts once every phone is ready.
    // Enter / Space starts at once with whoever is connected; missing players play with the mouse.
    public sealed class ConnectionFlow : MonoBehaviour
    {
        [Tooltip("Scene to open instead of the period flow (empty or the first period: PeriodFlow).")]
        public string gameSceneName = "";

        static readonly string[] Names = { "Amelia", "Nox" };
        static readonly Color[] Colors = { new Color(1f, 0.82f, 0.35f), new Color(0.45f, 0.75f, 1f) };

        bool started;
        RegistrationBoard board;

        void Awake()
        {
            var receiver = GetComponent<PhonePointer.PhonePointerReceiver>();
            var combined = GetComponent<PhonePointer.PhonePointerRegistrationView>();
            if (combined != null)
                combined.enabled = false;
            board = GetComponent<RegistrationBoard>();
            if (board == null)
            {
                board = gameObject.AddComponent<RegistrationBoard>();
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
            // The flow starts at the first period; an explicit other scene (a test scene) still works.
            bool explicitScene = !string.IsNullOrEmpty(gameSceneName) && gameSceneName != "Period1Cutscene" && gameSceneName != "Game";
            if (explicitScene)
                SceneManager.LoadScene(gameSceneName);
            else if (!PeriodFlow.Next())
                SceneManager.LoadScene(string.IsNullOrEmpty(gameSceneName) ? "Game" : gameSceneName);
        }

        void OnGUI()
        {
            var link = PhoneLink.Instance;
            if (started || link == null || link.Receiver == null)
                return;
            // On top of the registration board's full-screen view.
            GUI.depth = -10;
            float s = GameGui.Scale, w = Screen.width, h = Screen.height;
            var receiver = link.Receiver;
            if (!receiver.Listening)
            {
                GameGui.Rect(new Rect(0f, 0f, w, h), new Color(0.03f, 0.02f, 0.06f));
                GameGui.Label(new Rect(0f, h * 0.5f, w, 40f * s), "서버를 열지 못했습니다. " + receiver.Error, GameGui.Style(26, TextAnchor.MiddleCenter, false), new Color(1f, 0.6f, 0.65f));
                return;
            }
            // While the phones look at the runic stone, keep the screen clean: only the QR step gets the overlay,
            // and only along the top and bottom edges.
            if (board != null && board.Visible && !board.ShowingQr)
            {
                Footer(link, s, w, h, false);
                return;
            }
            GameGui.GlowLabel(new Rect(0f, 26f * s, w, 70f * s), "마법학교 입학", GameGui.Style(54, TextAnchor.MiddleCenter), new Color(1f, 0.95f, 0.88f), new Color(0.55f, 0.65f, 1f));
            GameGui.Label(new Rect(0f, 92f * s, w, 34f * s), "휴대폰으로 QR을 스캔해 지팡이를 연결하세요", GameGui.Style(24, TextAnchor.MiddleCenter, false), new Color(0.88f, 0.9f, 1f));
            Footer(link, s, w, h, true);
        }

        // Player slots and the start hint along the bottom edge.
        void Footer(PhoneLink link, float s, float w, float h, bool full)
        {
            var receiver = link.Receiver;
            float t = Time.unscaledTime;
            float slotW = 300f * s, y = h - (full ? 150f : 92f) * s;
            for (int i = 0; i < 2; i++)
            {
                var session = CasterInput.Session(i);
                string state;
                int step;
                if (session == null) { state = "대기 중"; step = 0; }
                else if (!session.Model.Registered) { state = "룬석을 비춰 주세요"; step = 1; }
                else if (!session.Model.Calibrated) { state = "가운데를 가리키고 CENTER"; step = 2; }
                else { state = "준비 완료"; step = 3; }
                var c = Colors[i];
                float x = i == 0 ? w * 0.5f - slotW - 40f * s : w * 0.5f + 40f * s;
                GameGui.BannerBack(new Rect(x - 30f * s, y - 10f * s, slotW + 60f * s, 82f * s), new Color(0.02f, 0.02f, 0.06f, 0.85f));
                GameGui.GlowLabel(new Rect(x, y, slotW, 34f * s), (i + 1) + "P  " + Names[i], GameGui.Style(28, TextAnchor.MiddleCenter), Color.Lerp(c, Color.white, 0.2f), c, i < receiver.MaxPhones ? 1f : 0.4f);
                GameGui.Label(new Rect(x - 30f * s, y + 32f * s, slotW + 60f * s, 26f * s), state, GameGui.Style(18, TextAnchor.MiddleCenter, false),
                    step >= 3 ? new Color(1f, 0.95f, 0.75f) : new Color(1f, 1f, 1f, 0.55f + 0.35f * Mathf.Sin(t * 3f + i)));
                for (int k = 0; k < 3; k++)
                    GameGui.DrawCentered(GameGui.Diamond, new Vector2(x + slotW * 0.5f + (k - 1) * 20f * s, y + 66f * s), 10f * s, k < step ? c : new Color(1f, 1f, 1f, 0.2f));
            }
            if (full)
                GameGui.Label(new Rect(0f, h - 40f * s, w, 30f * s),
                    "Enter: 지금 시작 (없는 플레이어는 마우스로)   ·   " + link.BoundAddress,
                    GameGui.Style(17, TextAnchor.MiddleCenter, false), new Color(1f, 1f, 1f, 0.5f));
        }
    }
}
