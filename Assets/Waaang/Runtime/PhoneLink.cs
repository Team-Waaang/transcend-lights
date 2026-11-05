using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using UnityEngine;

namespace Waaang
{
    // Keeps the phone session alive from the connection scene into the game scene.
    public sealed class PhoneLink : MonoBehaviour
    {
        public static PhoneLink Instance { get; private set; }

        public PhonePointer.PhonePointerReceiver Receiver;
        [Tooltip("Physical display size in meters, bezel excluded. The QR marker scale uses this.")]
        public Vector2 displaySizeMeters = new Vector2(0.53f, 0.30f);
        [Min(1)] public int requiredPhones = 1;

        public string BoundAddress { get; private set; }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            if (Receiver == null)
                Receiver = GetComponent<PhonePointer.PhonePointerReceiver>();
            if (Receiver != null)
                Receiver.MaxPhones = requiredPhones;
            if (Receiver != null && !Receiver.Listening)
            {
                BoundAddress = FindLanAddress();
                Receiver.StartServer(BoundAddress, displaySizeMeters, requiredPhones);
            }
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        static string FindLanAddress()
        {
            var candidates = new List<string>();
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up)
                    continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;
                foreach (var address in nic.GetIPProperties().UnicastAddresses)
                {
                    if (address.Address.AddressFamily != AddressFamily.InterNetwork)
                        continue;
                    string ip = address.Address.ToString();
                    if (ip.StartsWith("169.254."))
                        continue;
                    candidates.Add(ip);
                }
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                string ip = candidates[i];
                if (ip.StartsWith("192.168.") || ip.StartsWith("10."))
                    return ip;
            }

            return candidates.Count > 0 ? candidates[0] : "127.0.0.1";
        }
    }
}
