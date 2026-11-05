using UnityEngine;
using UnityEngine.SceneManagement;

namespace Waaang
{
    // Shows the SDK pairing marker, then opens the game once the phone is registered.
    public sealed class ConnectionFlow : MonoBehaviour
    {
        public string gameSceneName = "Game";
        bool loading;

        void Update()
        {
            var link = PhoneLink.Instance;
            if (loading || link == null || link.Receiver == null)
                return;
            if (SceneManager.GetActiveScene().name == gameSceneName)
                return;
            if (!link.Receiver.GameStarted)
                return;
            loading = true;
            SceneManager.LoadScene(gameSceneName);
        }

        void OnGUI()
        {
            var link = PhoneLink.Instance;
            if (link == null || link.Receiver == null)
                return;
            if (SceneManager.GetActiveScene().name == gameSceneName)
                return;

            const float width = 760f;
            float y = Screen.height - 92f;
            if (!link.Receiver.Listening)
            {
                GUI.Label(new Rect(20f, y, width, 64f), "서버를 열지 못했습니다. " + link.Receiver.Error);
                return;
            }

            GUI.Label(new Rect(20f, y, width, 64f),
                "휴대폰과 같은 Wi-Fi에서 QR을 스캔하세요.  " + link.BoundAddress +
                "  /  화면 " + link.displaySizeMeters.x.ToString("0.00") + "m x " +
                link.displaySizeMeters.y.ToString("0.00") + "m");
        }
    }
}
