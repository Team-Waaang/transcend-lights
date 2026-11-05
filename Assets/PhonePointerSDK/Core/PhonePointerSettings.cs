using System;
using UnityEngine;

namespace PhonePointer
{
    public enum ConnectionStatus { Disconnected, Connecting, Connected, Reconnecting }
    public enum DisconnectReason { None, Timeout, SocketError, QueueOverflow, AppPaused, TrackingLost, AnchorLost, DisplayChanged, RecoveryExpired, ResumeRejected, VersionMismatch, ServerStopped }

    [Serializable]
    public sealed class ConnectionOptions
    {
        public double poseStaleSeconds = .25, heartbeatSeconds = .5, silenceSeconds = 2,
            recoverySeconds = 10, eventLifetimeSeconds = .5;
        public float smoothingSeconds = .03f;
        public int poseRate = 60, tcpPort = 47777, udpPort = 47778;
        public bool automaticPorts = true;
        public ConnectionOptions Copy() => (ConnectionOptions)MemberwiseClone();
        public bool Validate(out string error)
        {
            error = null;
            foreach (double value in new[] { poseStaleSeconds, heartbeatSeconds, silenceSeconds, recoverySeconds, eventLifetimeSeconds })
                if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0 || value > 3600)
                { error = "Timing values must be finite and between 0 and 3600 seconds."; return false; }
            if (heartbeatSeconds >= silenceSeconds) error = "Heartbeat must be shorter than the silence timeout.";
            else if (!PointerMath.Finite(smoothingSeconds) || smoothingSeconds < 0 || smoothingSeconds > 10) error = "Smoothing must be between 0 and 10 seconds.";
            else if (poseRate < 1 || poseRate > 60) error = "Pose rate must be between 1 and 60 Hz.";
            else if (tcpPort < 1 || tcpPort > 65535 || udpPort < 1 || udpPort > 65535) error = "Ports must be between 1 and 65535.";
            return error == null;
        }
    }

    [CreateAssetMenu(menuName = "Phone Pointer/Settings", fileName = "PhonePointerSettings")]
    public sealed class PhonePointerSettings : ScriptableObject
    {
        public ConnectionOptions options = new ConnectionOptions();
    }

    public sealed class ConnectionDiagnostics
    {
        public double LastTcpReceived, LastUdpReceived = double.NegativeInfinity;
        public double RttSeconds, ClockUncertaintySeconds, PoseProcessingSeconds, ReceiveToDisplaySeconds,
            CaptureToSendSeconds, FrameSeconds, PhoneFrameSeconds, ActualPoseRate;
        public float SmoothingSeconds;
        public string ArState, TrackingReason;
        public DisconnectReason LastReason;
        public long RejectedEvents, ReplacedPoses;
    }
}
