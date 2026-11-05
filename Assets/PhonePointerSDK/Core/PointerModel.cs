using System;
using UnityEngine;

namespace PhonePointer
{
    public enum PointerStatus { Disconnected, RegistrationRequired, CalibrationRequired, Ready, OutsideScreen, TrackingLost, DataDelayed }

    public interface IPhonePointer
    {
        PointerStatus Status { get; }
        bool TryGetPointerNormalized(out Vector2 position);
        bool TryGetPointerScreenPosition(out Vector2 position);
        event Action<Vector2> Selected;
    }

    [Serializable]
    public sealed class PoseSample
    {
        public string session;
        public int revision, generation;
        public long sequence;
        public double time, sendTime;
        public bool tracking;
        public Vector3 position;
        public Quaternion rotation = Quaternion.identity;
    }

    public static class PointerMath
    {
        public static bool Finite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);
        public static bool ValidPose(PoseSample p)
        {
            if (p == null || double.IsNaN(p.time) || double.IsInfinity(p.time) || p.time < 0) return false;
            var v = p.position; var q = p.rotation;
            float norm = q.x*q.x + q.y*q.y + q.z*q.z + q.w*q.w;
            return Finite(v.x) && Finite(v.y) && Finite(v.z) && v.sqrMagnitude < 10000 &&
                Finite(norm) && Mathf.Abs(norm - 1) < .02f;
        }

        // Tracked marker coordinates: +X right, +Y down in the observed image pose, +Z toward the player.
        // The public UV convention is bottom-left (0,0), so flip Y once here for both aiming and selection.
        public static bool Project(PoseSample pose, Vector3 localAim, Vector2 size, out Vector2 uv)
        => Project(pose, localAim, size, 1f, out uv);

        public const float MinSensitivity = .5f, MaxSensitivity = 2f;
        public static bool ValidSensitivity(float value) => Finite(value) && value >= MinSensitivity && value <= MaxSensitivity;

        // Gain scales the intersection around screen center; it does not modify AR poses.
        public static bool Project(PoseSample pose, Vector3 localAim, Vector2 size, float sensitivity, out Vector2 uv)
        {
            uv = default;
            if (!ValidSensitivity(sensitivity) || !ValidPose(pose) || !Finite(size.x) || !Finite(size.y) || size.x <= 0 || size.y <= 0 || pose.position.z <= 0) return false;
            var direction = pose.rotation * localAim;
            if (!Finite(direction.z) || direction.z >= -0.00001f) return false;
            float distance = -pose.position.z / direction.z;
            var hit = pose.position + distance * direction;
            uv = new Vector2(hit.x / size.x * sensitivity + .5f, .5f - hit.y / size.y * sensitivity);
            return Finite(uv.x) && Finite(uv.y) && uv.x >= 0 && uv.x <= 1 && uv.y >= 0 && uv.y <= 1;
        }

        public static bool CenterAim(PoseSample pose, out Vector3 localAim)
        {
            localAim = Vector3.up;
            if (!ValidPose(pose) || pose.position.z < .05f) return false;
            localAim = Quaternion.Inverse(pose.rotation) * (-pose.position.normalized);
            return true;
        }
    }

    // Pure state machine: time is injected so timeout/packet behavior can be tested.
    public sealed class PointerModel
    {
        public const double StaleSeconds = .25, DisconnectSeconds = 2;
        public PointerStatus Status { get; private set; } = PointerStatus.Disconnected;
        public string Session { get; private set; }
        public int Revision { get; private set; }
        public int Generation { get; private set; }
        public bool InputEnabled { get; private set; } = true;
        public double PoseStaleSeconds = StaleSeconds;
        public Vector2 Size { get; private set; }
        public bool Registered { get; private set; }
        public bool Calibrated { get; private set; }
        public PoseSample Latest { get; private set; }
        public double LastPoseReceived { get; private set; } = double.NegativeInfinity;
        public double LastCommunication { get; private set; }
        public float SmoothingSeconds = .03f;
        public float Sensitivity { get; private set; } = 1f;
        public bool SetSensitivity(float value)
        {
            if (!PointerMath.ValidSensitivity(value)) return false;
            if (Mathf.Approximately(Sensitivity, value)) return true;
            Sensitivity = value; filterValid = false; Release();
            // Do not expose a coordinate filtered using the old gain before the next Tick.
            if (Status == PointerStatus.Ready) Status = PointerStatus.DataDelayed;
            return true;
        }
        public event Action<Vector2> Selected;
        public Vector3 LocalAim => aim;
        Vector3 aim = Vector3.up;
        Vector2 filtered;
        bool filterValid;
        long lastSequence = -1, lastEvent = -1;

        public void Connect(string session, Vector2 size, double now)
        {
            Session = session; Size = size; Revision = Generation = 0; InputEnabled = true; Sensitivity = 1f;
            LastCommunication = now; lastEvent = -1;
            BeginRegistration();
        }

        public void Disconnect()
        {
            Session = null; Latest = null; Registered = Calibrated = filterValid = Pressed = false;
            Status = PointerStatus.Disconnected;
        }

        public int BeginRegistration()
        {
            Revision++; Registered = Calibrated = filterValid = Pressed = false; aim=Vector3.up;
            Latest = null; lastSequence = lastEvent = -1; LastPoseReceived = double.NegativeInfinity;
            Status = Session == null ? PointerStatus.Disconnected : PointerStatus.RegistrationRequired;
            return Revision;
        }

        public void SuspendInput() { InputEnabled = false; Pressed = filterValid = false; Status = PointerStatus.DataDelayed; }
        public void ResumeConnection(int generation, double now)
        {
            Generation = generation; InputEnabled = true; Latest = null; LastPoseReceived = double.NegativeInfinity;
            lastSequence = lastEvent = -1; Pressed = filterValid = false; LastCommunication = now;
        }
        // Calibrated means the aim axis is initialized (default top edge or explicit CENTER).
        // Never reuse a CENTER axis from a previous registration.
        public void MarkRegistered()
        {
            Registered=Calibrated=true; aim=Vector3.up; filterValid=Pressed=false;
            Status=PointerStatus.DataDelayed; // Wait for a fresh valid tracking pose.
        }
        public void LoseTracking() { Calibrated=filterValid=Pressed=false; Status=PointerStatus.TrackingLost; }
        public void Heartbeat(double now) { if (Session != null) LastCommunication = now; }
        bool Matches(PoseSample p) => PointerMath.ValidPose(p) && p.session == Session && Session != null && p.revision == Revision && p.generation == Generation;

        public bool AcceptPose(PoseSample p, double received)
        {
            if (!InputEnabled || !Registered || !Matches(p) || p.sequence <= lastSequence || (Latest != null && p.time < Latest.time)) return false;
            Latest = p; lastSequence = p.sequence; LastPoseReceived = received; LastCommunication = received;
            if (!p.tracking) { Calibrated = filterValid = Pressed = false; Status = PointerStatus.TrackingLost; }
            return true;
        }

        // SELECT button state, driven by explicit press/release edges over the control channel rather
        // than a per-frame UDP flag. Drawing mode lays down a continuous stroke while this stays true.
        public bool Pressed { get; private set; }
        public bool Holding => Pressed && Status == PointerStatus.Ready;
        public event Action Pressing; // Raised on a fresh press edge (used for the drawing stroke start / UI cues).

        // Called on a "press" control message. Returns true if this opened a new press.
        public bool Press()
        {
            if (!InputEnabled || !Registered) return false;
            bool edge = !Pressed;
            Pressed = true;
            if (edge) Pressing?.Invoke();
            return edge;
        }

        // Called on a "release" control message, or automatically when tracking/session is lost.
        public void Release() { Pressed = false; }

        public bool AcceptEvent(long id)
        {
            if (id <= lastEvent) return false;
            lastEvent = id; return true;
        }

        bool UsableEvent(PoseSample p, double now) => InputEnabled && Registered && Matches(p) && p.tracking;
        public bool Press(PoseSample pose) => Calibrated && UsableEvent(pose, 0) && Press();

        public bool Calibrate(PoseSample p, double now)
        {
            if (!UsableEvent(p, now) || !PointerMath.CenterAim(p, out var direction)) return false;
            aim = direction; Calibrated = true; filterValid = false;
            return true;
        }

        public bool Select(PoseSample p, double now)
        {
            if (!Calibrated || !UsableEvent(p, now) || !PointerMath.Project(p, aim, Size, Sensitivity, out var uv)) return false;
            Selected?.Invoke(uv); return true;
        }

        public void Tick(double now, float deltaTime)
        {
            if (Session == null) { Disconnect(); return; }
            if (!InputEnabled) { Status = PointerStatus.DataDelayed; filterValid = false; return; }
            if (!Registered) { Status = PointerStatus.RegistrationRequired; return; }
            if (now - LastPoseReceived > PoseStaleSeconds) { Status = PointerStatus.DataDelayed; filterValid = Pressed = false; return; }
            if (Latest == null || !Latest.tracking) { Status = PointerStatus.TrackingLost; filterValid = false; return; }
            if (!Calibrated) { Status = PointerStatus.CalibrationRequired; return; }
            if (!PointerMath.Project(Latest, aim, Size, Sensitivity, out var uv)) { Status = PointerStatus.OutsideScreen; filterValid = false; return; }
            float alpha = SmoothingSeconds <= 0 ? 1 : 1 - Mathf.Exp(-Mathf.Max(0, deltaTime) / SmoothingSeconds);
            filtered = filterValid ? Vector2.Lerp(filtered, uv, alpha) : uv;
            filterValid = true; Status = PointerStatus.Ready;
        }

        public bool TryGetPointer(out Vector2 uv) { uv = filtered; return Status == PointerStatus.Ready; }
    }
}
