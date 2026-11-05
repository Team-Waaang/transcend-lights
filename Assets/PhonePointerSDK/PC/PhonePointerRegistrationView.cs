using UnityEngine;

namespace PhonePointer
{
    // Optional AR-free PC overlay. Games may replace it with their own Canvas/UI.
    // Call ShowPairing/ShowRegistration during recovery without pausing game simulation.
    public sealed class PhonePointerRegistrationView : MonoBehaviour
    {
        public PhonePointerReceiver Receiver;
        public bool ShowPairing, ShowRegistration;
        Texture2D qr;
        string pairingToken;
        void OnGUI()
        {
            if(Receiver==null || !Receiver.Listening) return;
            if(pairingToken!=Receiver.Pairing.token)
            {
                if(qr!=null) Destroy(qr);
                qr=QrUtility.Encode(JsonUtility.ToJson(Receiver.Pairing)); pairingToken=Receiver.Pairing.token;
            }
            bool initial=!Receiver.GameStarted;
            bool needsRegistration=false;
            foreach(var phone in Receiver.Sessions)
                needsRegistration|=phone.ConnectionStatus==ConnectionStatus.Connected && !phone.Model.Registered;
            if(ShowPairing || ShowRegistration || needsRegistration || (initial && !Receiver.ExpectedPlayersRegistered))
            {
                var marker=Resources.Load<Texture2D>("ScreenMarker");
                if(marker==null) { GUI.Label(new Rect(20,20,700,40),"ScreenMarker resource missing. Restore the SDK registration image."); return; }
                float w=Receiver.MarkerWidth/Receiver.Size.x*Screen.width, h=Receiver.MarkerWidth/Receiver.Size.y*Screen.height;
                var area=new Rect((Screen.width-w)/2,(Screen.height-h)/2,w,h);
                GUI.DrawTexture(area,marker,ScaleMode.StretchToFill);
                GUI.DrawTexture(QrUtility.CombinedQrRect(area),qr,ScaleMode.StretchToFill);
                GUI.Label(new Rect(20,20,700,40),$"Scan this square and keep it in view / TCP {Receiver.Pairing.tcpPort} / UDP {Receiver.Pairing.udpPort}");
            }
            else if(initial && Receiver.ExpectedPlayersRegistered && !Receiver.ExpectedPlayersCalibrated)
            {
                GUI.DrawTexture(new Rect(Screen.width/2-18,Screen.height/2-2,36,4),Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(Screen.width/2-2,Screen.height/2-18,4,36),Texture2D.whiteTexture);
            }
        }
        void OnDestroy() { if(qr!=null) Destroy(qr); }
    }
}
