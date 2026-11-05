using UnityEngine;

namespace Waaang
{
    // Draws the AR registration image and the pairing QR on opposite sides of the screen.
    public sealed class RegistrationBoard : MonoBehaviour
    {
        public PhonePointer.PhonePointerReceiver Receiver;
        Texture2D qr;
        string pairingToken;

        void Awake()
        {
            if (Receiver == null)
                Receiver = GetComponent<PhonePointer.PhonePointerReceiver>();
            var combined = GetComponent<PhonePointer.PhonePointerRegistrationView>();
            if (combined != null)
                combined.enabled = false;
        }

        void OnGUI()
        {
            if (Receiver == null || !Receiver.Listening || Receiver.Pairing == null)
                return;
            if (pairingToken != Receiver.Pairing.token)
            {
                if (qr != null)
                    Destroy(qr);
                qr = PhonePointer.QrUtility.Encode(JsonUtility.ToJson(Receiver.Pairing));
                pairingToken = Receiver.Pairing.token;
            }

            bool initial = !Receiver.GameStarted;
            bool needsRegistration = false;
            var sessions = Receiver.Sessions;
            for (int i = 0; i < sessions.Count; i++)
                needsRegistration |= sessions[i].ConnectionStatus == PhonePointer.ConnectionStatus.Connected
                    && !sessions[i].Model.Registered;
            if (!needsRegistration && !(initial && !Receiver.ExpectedPlayersRegistered))
                return;

            var marker = Resources.Load<Texture2D>("ScreenMarker");
            float margin = Screen.width * 0.06f;
            float side = Mathf.Min(Screen.height * 0.62f, Screen.width * 0.36f);
            float y = (Screen.height - side) * 0.5f;
            var markerRect = new Rect(margin, y, side, side);
            var qrRect = new Rect(Screen.width - margin - side, y, side, side);

            if (marker != null)
                GUI.DrawTexture(markerRect, marker, ScaleMode.StretchToFill);
            else
                GUI.Label(markerRect, "등록 이미지가 없습니다.");
            GUI.DrawTexture(qrRect, qr, ScaleMode.StretchToFill);

            var label = new GUIStyle(GUI.skin.label) { fontSize = 22, alignment = TextAnchor.UpperCenter };
            GUI.Label(new Rect(markerRect.x, markerRect.yMax + 8f, markerRect.width, 36f), "등록 이미지", label);
            GUI.Label(new Rect(qrRect.x, qrRect.yMax + 8f, qrRect.width, 36f), "QR", label);
        }

        void OnDestroy()
        {
            if (qr != null)
                Destroy(qr);
        }
    }
}
