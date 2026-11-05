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
        Texture2D qr, surround;
        string pairingToken;
        int expectedPlayers;
        bool visible, showQr;

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
            if (visible)
            {
                bool started = Receiver.GameStarted || StartedWith >= 0;
                showQr = !started ? !Receiver.ExpectedPlayersConnected : AnyConnectionLost();
                visible = !started || showQr || AnyTrackingLost();
            }
            if (stage) stage.Present(visible, !showQr);
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
            if (stage && stage.View) GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), stage.View, ScaleMode.StretchToFill);
            else Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(.008f, .015f, .025f));
            if (!showQr || !qr) return;
            EnsureSurround();
            float side = Mathf.Min(Screen.height * .46f, Screen.width * .46f, 510f);
            var rect = new Rect((Screen.width - side) * .5f, (Screen.height - side) * .5f, side, side);
            // The soft light is outside the white plate; all QR modules and its quiet zone stay intact.
            float plateSide = side + Mathf.Clamp(Screen.height / 900f, .55f, 1.6f) * 24f;
            float glowSide = plateSide * (512f / 384f);
            GUI.DrawTexture(new Rect(rect.center.x - glowSide * .5f, rect.center.y - glowSide * .5f, glowSide, glowSide), surround);
            GUI.DrawTexture(rect, qr, ScaleMode.StretchToFill);
        }

        void Fill(Rect rect, Color color)
        {
            var previous = GUI.color; GUI.color = color; GUI.DrawTexture(rect, Texture2D.whiteTexture); GUI.color = previous;
        }
        void EnsureSurround()
        {
            if (surround) return;
            surround = new Texture2D(512, 512, TextureFormat.RGBA32, false)
                { name = "QR Soft Edge Glow", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[512 * 512];
            for (int y = 0; y < 512; y++) for (int x = 0; x < 512; x++)
            {
                var q = new Vector2(Mathf.Abs(x - 255.5f) - 178f, Mathf.Abs(y - 255.5f) - 178f);
                float distance = new Vector2(Mathf.Max(q.x, 0), Mathf.Max(q.y, 0)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0) - 14f;
                float plate = 1f - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-.75f, .75f, distance));
                float outside = Mathf.Max(distance, 0);
                float glow = .35f * Mathf.Exp(-outside * outside / (2f * 18f * 18f)) * (1f - plate);
                float alpha = plate + glow;
                Color rgb = alpha > 0 ? (Color.white * plate + new Color(.64f, .78f, 1f) * glow) / alpha : Color.clear;
                pixels[y * 512 + x] = new Color(rgb.r, rgb.g, rgb.b, alpha);
            }
            surround.SetPixels(pixels); surround.Apply();
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
        void OnDestroy() { if (qr) Destroy(qr); if (surround) Destroy(surround); }
    }
}
