using PhonePointer;
using UnityEngine;

namespace Waaang
{
    // Deliberately independent of ConnectionFlow: success never loads the game or hides the slab.
    [RequireComponent(typeof(PhoneLink), typeof(PhonePointerReceiver))]
    public sealed class RuneSlabRecognitionTest : MonoBehaviour
    {
        PhonePointerReceiver receiver;
        PhoneLink link;
        Texture2D qr, slab, reference;
        string token;
        GUIStyle title, text, small, toggle;
        bool compareReference, outline, observedSuccess;
        float started, firstSuccess = -1f;
        int selections;

        void Awake()
        {
            receiver = GetComponent<PhonePointerReceiver>();
            link = GetComponent<PhoneLink>();
            var combined = GetComponent<PhonePointerRegistrationView>();
            if (combined) combined.enabled = false;
            slab = Resources.Load<Texture2D>("ScreenMarker");
            reference = Resources.Load<Texture2D>("RuneSlabReference");
            receiver.SelectedBy += Selected;
            started = Time.unscaledTime;
        }
        void Selected(PhoneSession session, Vector2 uv) { selections++; }
        void Update()
        {
            if (!observedSuccess && receiver.RegisteredCount > 0)
            {
                observedSuccess = true; firstSuccess = Time.unscaledTime - started;
                Debug.Log("Rune slab registration succeeded after " + firstSuccess.ToString("F2") + " seconds.", this);
            }
        }
        void Styles()
        {
            if (title != null) return;
            var font = Resources.Load<Font>("RuneSlabFont");
            title = new GUIStyle(GUI.skin.label) { font = font, fontSize = 28, alignment = TextAnchor.UpperLeft };
            text = new GUIStyle(GUI.skin.label) { font = font, fontSize = 21, wordWrap = true };
            small = new GUIStyle(text) { fontSize = 17 };
            toggle = new GUIStyle(GUI.skin.toggle) { font = font, fontSize = 18 };
            title.normal.textColor = text.normal.textColor = small.normal.textColor = Color.black;
            toggle.normal.textColor = toggle.onNormal.textColor = Color.black;
        }
        void OnGUI()
        {
            Styles();
            Color previous = GUI.color;
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.Label(new Rect(24, 14, Screen.width - 48, 45), "룬 석판 · AR 인식 테스트", title);
            if (!string.IsNullOrEmpty(receiver.Error))
                GUI.Label(new Rect(24, 61, Screen.width - 48, 44), receiver.Error, small);
            if (!receiver.Listening)
            {
                GUI.Label(new Rect(24, 72, Screen.width - 48, 100), "서버 시작 실패: " + receiver.Error, text);
                GUI.color = previous; return;
            }
            if (!ScreenMarkerLayout.TryGetRects(Screen.width, Screen.height, out var slabRect, out var referenceRect))
            {
                GUI.Label(new Rect(24, 72, 900, 100), "RuneSlabLayout 리소스를 확인하세요.", text);
                GUI.color = previous; return;
            }
            if (compareReference && reference) GUI.DrawTexture(referenceRect, reference, ScaleMode.StretchToFill);
            else if (slab) GUI.DrawTexture(slabRect, slab, ScaleMode.StretchToFill);
            if (outline)
            {
                GUI.color = new Color(.95f, .5f, .05f);
                GUI.DrawTexture(new Rect(referenceRect.x, referenceRect.y, referenceRect.width, 2), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(referenceRect.x, referenceRect.yMax - 2, referenceRect.width, 2), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(referenceRect.x, referenceRect.y, 2, referenceRect.height), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(referenceRect.xMax - 2, referenceRect.y, 2, referenceRect.height), Texture2D.whiteTexture);
                GUI.color = Color.white;
            }
            if (token != receiver.Pairing.token)
            {
                if (qr) Destroy(qr);
                qr = QrUtility.Encode(JsonUtility.ToJson(receiver.Pairing)); token = receiver.Pairing.token;
            }
            var qrRect = ScreenMarkerLayout.QrRect(Screen.width, Screen.height);
            GUI.DrawTexture(qrRect, qr);
            GUI.Label(new Rect(qrRect.x, qrRect.yMax + 8, qrRect.width, 52), "1. QR 연결\n2. 석판을 비추세요", small);
            float bottom = Screen.height - 142;
            string result = receiver.RegisteredCount > 0 ? "인식·등록 성공" : observedSuccess ? "이전에 인식 성공 · 현재 재등록 대기" : "석판 인식 대기";
            GUI.Label(new Rect(24, bottom, Screen.width - 48, 35), result +
                "   연결 " + receiver.ConnectedCount + " / 등록 " + receiver.RegisteredCount + " / 클릭 " + selections +
                (firstSuccess >= 0 ? "   첫 성공 " + firstSuccess.ToString("F1") + "초" : ""), text);
            GUI.Label(new Rect(24, bottom + 35, Screen.width - 48, 30),
                link.BoundAddress + "   참조 이미지 폭 " + (receiver.MarkerWidth * 100).ToString("F1") +
                "cm   화면 실측 " + link.displaySizeMeters.x.ToString("F2") + " × " + link.displaySizeMeters.y.ToString("F2") + "m", small);
            compareReference = GUI.Toggle(new Rect(24, bottom + 73, 320, 32), compareReference, "비교: 인식용 원본만 표시", toggle);
            outline = GUI.Toggle(new Rect(365, bottom + 73, 280, 32), outline, "인식 영역 테두리", toggle);
            if (GUI.Button(new Rect(Screen.width - 194, bottom + 73, 164, 36), "REGISTER AGAIN"))
            { receiver.Reregister(); observedSuccess = false; firstSuccess = -1; started = Time.unscaledTime; }
            foreach (var session in receiver.Sessions)
                if (session.TryGetPointerNormalized(out var uv))
                {
                    GUI.color = Color.green;
                    GUI.DrawTexture(new Rect(uv.x * Screen.width - 5, (1 - uv.y) * Screen.height - 5, 10, 10), Texture2D.whiteTexture);
                }
            GUI.color = previous;
        }
        void OnDestroy()
        {
            if (receiver) receiver.SelectedBy -= Selected;
            if (qr) Destroy(qr);
        }
    }
}
