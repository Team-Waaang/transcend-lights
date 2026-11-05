using UnityEngine;

namespace Waaang
{
    // Draws the AR registration image and the pairing QR on opposite sides of the screen.
    public sealed class RegistrationBoard : MonoBehaviour
    {
        public PhonePointer.PhonePointerReceiver Receiver;
        /// <summary>
        /// Phones connected when the game was started by hand before everyone was in (ConnectionFlow): from then on
        /// the board only asks for those players again. -1: started normally (every expected phone calibrated).
        /// </summary>
        public static int StartedWith = -1;

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

            bool initial = !Receiver.GameStarted && StartedWith < 0;
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

            var label = AiHackathon.Game.GameGui.Style(24, TextAnchor.UpperCenter);
            var gold = new Color(1f, 0.9f, 0.7f);
            if (showMarker)
            {
                Frame(markerRect);
                if (marker != null)
                    GUI.DrawTexture(markerRect, marker, ScaleMode.StretchToFill);
                else
                    GUI.Label(markerRect, "등록 이미지가 없습니다.");
                AiHackathon.Game.GameGui.Label(new Rect(markerRect.x - 40f, markerRect.yMax + 14f, markerRect.width + 80f, 36f),
                    initial ? "등록 이미지" : "등록 이미지 - 휴대폰으로 다시 비춰 주세요", label, gold);
            }
            if (showQr)
            {
                Frame(qrRect);
                GUI.DrawTexture(qrRect, qr, ScaleMode.StretchToFill);
                AiHackathon.Game.GameGui.Label(new Rect(qrRect.x - 40f, qrRect.yMax + 14f, qrRect.width + 80f, 36f),
                    initial ? "QR 연결" : "QR - 연결이 끊겼습니다. 다시 스캔해 주세요", label, gold);
            }
        }

        // A glowing gold frame just outside the image (never over it: the phones track the image itself).
        static void Frame(Rect r)
        {
            float pad = 10f, b = 3f;
            var outer = new Rect(r.x - pad, r.y - pad, r.width + pad * 2f, r.height + pad * 2f);
            AiHackathon.Game.GameGui.Tex(new Rect(outer.x - 40f, outer.y - 40f, outer.width + 80f, outer.height + 80f), AiHackathon.Game.GameGui.Soft, new Color(0.75f, 0.5f, 1f, 0.35f));
            var c = new Color(1f, 0.88f, 0.6f, 0.9f);
            AiHackathon.Game.GameGui.Rect(new Rect(outer.x, outer.y, outer.width, b), c);
            AiHackathon.Game.GameGui.Rect(new Rect(outer.x, outer.yMax - b, outer.width, b), c);
            AiHackathon.Game.GameGui.Rect(new Rect(outer.x, outer.y, b, outer.height), c);
            AiHackathon.Game.GameGui.Rect(new Rect(outer.xMax - b, outer.y, b, outer.height), c);
            foreach (var p in new[] { new Vector2(outer.x, outer.y), new Vector2(outer.xMax, outer.y), new Vector2(outer.x, outer.yMax), new Vector2(outer.xMax, outer.yMax) })
                AiHackathon.Game.GameGui.DrawCentered(AiHackathon.Game.GameGui.Diamond, p, 22f, c);
            // White quiet zone right round the image so the frame never touches it.
            AiHackathon.Game.GameGui.Rect(new Rect(r.x - 6f, r.y - 6f, r.width + 12f, r.height + 12f), Color.white);
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
            int expected = StartedWith >= 0 ? StartedWith : Receiver.MaxPhones;
            expectedPlayers = Mathf.Max(expectedPlayers, Mathf.Max(expected, connected));
            return connected < expectedPlayers;
        }

        void OnDestroy()
        {
            if (qr != null)
                Destroy(qr);
        }
    }
}
