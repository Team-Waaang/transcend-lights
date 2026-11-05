using UnityEngine;

namespace Waaang
{
    // Connect every required phone, reveal the stone, then calibrate.
    public sealed class RegistrationBoard : MonoBehaviour
    {
        public PhonePointer.PhonePointerReceiver Receiver;

        /// <summary>
        /// Phones connected when the game was started by hand before everyone was in (ConnectionFlow, Enter): from
        /// then on the board treats the game as started and only asks those players again. -1: started normally.
        /// </summary>
        public static int StartedWith = -1;

        /// <summary>True while the QR is up (the connection screen draws its status around it).</summary>
        public bool ShowingQr => visible && showQr;
        public bool Visible => visible;
        RegistrationStage stage;
        Texture2D qr;
        string pairingToken;
        int expectedPlayers;
        bool visible, showQr, overGame;

        void Awake()
        {
            if (!Receiver) Receiver = GetComponent<PhonePointer.PhonePointerReceiver>();
            var combined = GetComponent<PhonePointer.PhonePointerRegistrationView>();
            if (combined) combined.enabled = false;
            var asset = Resources.Load<RegistrationStage>("RunicRegistrationStage");
            if (asset) stage = Instantiate(asset, transform, false);
            else Debug.LogError("RunicRegistrationStage prefab is missing.", this);
        }

        void Update()
        {
            visible = Receiver && Receiver.Listening && Receiver.Pairing != null;
            overGame = false;
            if (visible)
            {
                bool started = Receiver.GameStarted || StartedWith >= 0;
                showQr = !started ? !Receiver.ExpectedPlayersConnected : AnyConnectionLost();
                visible = !started || showQr || AnyTrackingLost();
                // Re-registering mid-game: the stone appears over the game screen instead of the dark chamber.
                overGame = started && !showQr;
            }
            if (stage) stage.Present(visible, !showQr, overGame);
        }

        void OnGUI()
        {
            if (!visible) return;
            if (pairingToken != Receiver.Pairing.token)
            {
                if (qr) Destroy(qr);
                qr = PhonePointer.QrUtility.Encode(JsonUtility.ToJson(Receiver.Pairing));
                pairingToken = Receiver.Pairing.token;
            }
            var screen = new Rect(0, 0, Screen.width, Screen.height);
            if (overGame && stage && stage.View)
            {
                // A soft dark halo just behind the stone keeps its outline crisp for the phone's image tracking.
                if (PhonePointer.ScreenMarkerLayout.TryGetRects(Screen.width, Screen.height, out _, out var reference))
                {
                    float halo = Mathf.Max(reference.width, reference.height) * 1.6f;
                    AiHackathon.Game.GameGui.DrawCentered(AiHackathon.Game.GameGui.Soft, reference.center, halo, new Color(0f, 0f, 0f, 0.75f));
                }
                GUI.DrawTexture(screen, stage.View, ScaleMode.StretchToFill, true);
                return;
            }
            if (stage && stage.View) GUI.DrawTexture(screen, stage.View, ScaleMode.StretchToFill);
            else Fill(screen, new Color(.008f, .015f, .025f));
            // A faint lift in the middle and darker corners, so the backdrop isn't one flat colour.
            AiHackathon.Game.GameGui.Backdrop(screen);
            if (!showQr || !qr) return;
            // Large and plain: the code carries its own white quiet zone, so nothing is drawn around it.
            float side = Mathf.Min(Screen.height * .58f, Screen.width * .46f);
            var rect = new Rect((Screen.width - side) * .5f, (Screen.height - side) * .5f + Screen.height * .01f, side, side);
            GUI.DrawTexture(rect, qr, ScaleMode.StretchToFill);
        }

        void Fill(Rect rect, Color color)
        {
            var previous = GUI.color; GUI.color = color; GUI.DrawTexture(rect, Texture2D.whiteTexture); GUI.color = previous;
        }
        bool AnyTrackingLost()
        {
            foreach (var s in Receiver.Sessions)
            {
                if (s.ConnectionStatus != PhonePointer.ConnectionStatus.Connected) continue;
                if (!s.Model.Registered || s.TrackingMode == PhonePointer.PhoneTrackingMode.AR && s.Status == PhonePointer.PointerStatus.TrackingLost) return true;
            }
            return false;
        }
        bool AnyConnectionLost()
        {
            int connected = 0;
            foreach (var s in Receiver.Sessions)
            {
                if (s.ConnectionStatus == PhonePointer.ConnectionStatus.Reconnecting) return true;
                if (s.ConnectionStatus == PhonePointer.ConnectionStatus.Connected) connected++;
            }
            int expected = StartedWith >= 0 ? StartedWith : Receiver.MaxPhones;
            expectedPlayers = Mathf.Max(expectedPlayers, Mathf.Max(expected, connected));
            return connected < expectedPlayers;
        }
        void OnDestroy() { if (qr) Destroy(qr); }
    }
}
