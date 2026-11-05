using UnityEngine;

namespace Waaang
{
    // Draws the AR registration image and the pairing QR on opposite sides of the screen.
    public sealed class RegistrationBoard : MonoBehaviour
    {
        public PhonePointer.PhonePointerReceiver Receiver;
        Texture2D qr;
        string pairingToken;
        // Players in the running game: the phones it required, or more if more joined during play.
        int expectedPlayers;

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
            bool showMarker, showQr;
            if (initial)
            {
                // Before the game: both, until every expected phone is in and registered.
                showMarker = showQr = !Receiver.ExpectedPlayersRegistered;
            }
            else
            {
                // In the game: a phone that lost its AR tracking (or its anchor) needs to see the registration image
                // again; a phone whose connection dropped needs to rescan the QR.
                showMarker = AnyTrackingLost();
                showQr = AnyConnectionLost();
            }
            if (!showMarker && !showQr)
                return;

            var marker = Resources.Load<Texture2D>("ScreenMarker");
            float margin = Screen.width * 0.06f;
            float side = Mathf.Min(Screen.height * 0.62f, Screen.width * 0.36f);
            float y = (Screen.height - side) * 0.5f;
            // Same places as on the connection screen, so a phone finds the marker where it first registered it.
            var markerRect = new Rect(margin, y, side, side);
            var qrRect = new Rect(Screen.width - margin - side, y, side, side);

            var label = new GUIStyle(GUI.skin.label) { fontSize = 22, alignment = TextAnchor.UpperCenter };
            if (showMarker)
            {
                if (marker != null)
                    GUI.DrawTexture(markerRect, marker, ScaleMode.StretchToFill);
                else
                    GUI.Label(markerRect, "등록 이미지가 없습니다.");
                GUI.Label(new Rect(markerRect.x, markerRect.yMax + 8f, markerRect.width, 36f),
                    initial ? "등록 이미지" : "등록 이미지 - 휴대폰으로 다시 비춰 주세요", label);
            }
            if (showQr)
            {
                GUI.DrawTexture(qrRect, qr, ScaleMode.StretchToFill);
                GUI.Label(new Rect(qrRect.x, qrRect.yMax + 8f, qrRect.width, 36f),
                    initial ? "QR" : "QR - 연결이 끊겼습니다. 다시 스캔해 주세요", label);
            }
        }

        bool AnyTrackingLost()
        {
            var sessions = Receiver.Sessions;
            for (int i = 0; i < sessions.Count; i++)
            {
                var s = sessions[i];
                if (s.ConnectionStatus != PhonePointer.ConnectionStatus.Connected)
                    continue;
                if (!s.Model.Registered)
                    return true;
                // Air-mouse phones have no registration image to look at; they only need CENTER again.
                if (s.TrackingMode == PhonePointer.PhoneTrackingMode.AR && s.Status == PhonePointer.PointerStatus.TrackingLost)
                    return true;
            }
            return false;
        }

        bool AnyConnectionLost()
        {
            var sessions = Receiver.Sessions;
            int connected = 0;
            for (int i = 0; i < sessions.Count; i++)
            {
                if (sessions[i].ConnectionStatus == PhonePointer.ConnectionStatus.Reconnecting)
                    return true;
                if (sessions[i].ConnectionStatus == PhonePointer.ConnectionStatus.Connected)
                    connected++;
            }
            // A phone whose recovery window ran out is dropped from the list, so also count who is missing.
            expectedPlayers = Mathf.Max(expectedPlayers, Mathf.Max(Receiver.MaxPhones, connected));
            return connected < expectedPlayers;
        }

        void OnDestroy()
        {
            if (qr != null)
                Destroy(qr);
        }
    }
}
