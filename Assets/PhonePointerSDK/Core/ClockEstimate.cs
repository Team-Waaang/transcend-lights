using System;

namespace PhonePointer
{
    // Four-timestamp exchange. Offset is remote clock minus local clock; never compare
    // different machines' Stopwatch timestamps without this estimate.
    public sealed class ClockEstimate
    {
        readonly object gate = new object();
        double offset, uncertainty = double.PositiveInfinity, updated = double.NegativeInfinity;
        public double Rtt { get { lock (gate) return uncertainty * 2; } }
        public double Uncertainty { get { lock (gate) return uncertainty; } }
        public bool Observe(double t0, double t1, double t2, double t3)
        {
            if (!Finite(t0) || !Finite(t1) || !Finite(t2) || !Finite(t3) || t3 < t0 || t2 < t1) return false;
            double rtt = (t3 - t0) - (t2 - t1);
            if (rtt < 0 || rtt > 10) return false;
            lock (gate)
            {
                // Refresh from the best recent exchange rather than smoothing sensor motion.
                if (t3 - updated > 5 || rtt / 2 <= uncertainty)
                { offset = ((t1 - t0) + (t2 - t3)) / 2; uncertainty = rtt / 2; updated = t3; }
            }
            return true;
        }
        public bool EventFresh(double remoteTime, double receivedAt, double now, double lifetime, out string reason)
        {
            lock (gate)
            {
                double bound = uncertainty + .002 + Math.Max(0, now - updated) * .0001;
                if (!Finite(remoteTime) || now - updated > 10 || bound >= lifetime)
                { reason = "Clock estimate unavailable or uncertain"; return false; }
                double age = now - (remoteTime - offset);
                if (age < -bound || age + bound > lifetime || now - receivedAt > lifetime)
                { reason = "Expired or future button event"; return false; }
                reason = null; return true;
            }
        }
        static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
