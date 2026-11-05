using UnityEngine;

namespace Waaang
{
    public static class ArmIk
    {
        /// <summary>Optional limits that make a two-bone arm read as a human arm rather than a bare position solve.</summary>
        public struct Limits
        {
            /// <summary>Elbow hinge axis in upper-arm local space (flexion is a positive turn about it). Zero keeps the
            /// animation's upper-arm roll; set it so the elbow only ever bends the way the mesh was rigged to.</summary>
            public Vector3 hingeLocal;
            /// <summary>Fraction of full arm length where reach starts easing toward (never reaching) a locked elbow.
            /// 1 is the old hard clamp.</summary>
            public float softReach;
            /// <summary>Degrees the staff runs diagonally across the palm (off the knuckle line, toward the fingers), so
            /// the wrist does not have to cock fully when the forearm points along the staff.</summary>
            public float maxOblique;
            /// <summary>Share of the hand's twist about the forearm that the forearm carries (rigs without twist bones).</summary>
            public float forearmTwist;
            /// <summary>Hand local rotation (relative to the forearm) with a straight wrist, for <see cref="forearmTwist"/>.</summary>
            public Quaternion handBindLocal;
            public bool hasHandBind;
            /// <summary>Forearm local rotation (relative to the upper arm) with a straight, untwisted elbow. With it the
            /// forearm starts every solve from zero pronation instead of the animation's.</summary>
            public Quaternion lowerBindLocal;
            public bool hasLowerBind;

            public static Limits Default => new Limits
            {
                softReach = 1f, handBindLocal = Quaternion.identity, lowerBindLocal = Quaternion.identity,
            };
        }

        public static void Solve(Transform upper, Transform lower, Transform hand, Vector3 target, Vector3 pole)
        {
            Solve(upper, lower, hand, target, pole, Limits.Default);
        }

        public static void Solve(Transform upper, Transform lower, Transform hand, Vector3 target, Vector3 pole, in Limits limits)
        {
            if (upper == null || lower == null || hand == null)
                return;

            float upperLength = Vector3.Distance(upper.position, lower.position);
            float lowerLength = Vector3.Distance(lower.position, hand.position);
            if (upperLength < 0.0001f || lowerLength < 0.0001f)
                return;

            Vector3 toTarget = target - upper.position;
            float distance = toTarget.magnitude;
            float maxReach = upperLength + lowerLength - 0.001f;
            float minReach = Mathf.Abs(upperLength - lowerLength) + 0.001f;
            Vector3 direction = distance > 0.0001f ? toTarget / distance : upper.forward;
            distance = SoftClamp(distance, maxReach, limits.softReach);
            distance = Mathf.Clamp(distance, minReach, maxReach);
            Vector3 clampedTarget = upper.position + direction * distance;

            float along = (upperLength * upperLength + distance * distance - lowerLength * lowerLength) / (2f * distance);
            float height = Mathf.Sqrt(Mathf.Max(0f, upperLength * upperLength - along * along));
            Vector3 poleDirection = Vector3.ProjectOnPlane(pole - upper.position, direction);
            if (poleDirection.sqrMagnitude < 0.000001f)
                poleDirection = Vector3.Cross(direction, Vector3.up);
            poleDirection.Normalize();
            Vector3 elbow = upper.position + direction * along + poleDirection * height;

            AimChild(upper, lower, elbow);
            if (limits.hingeLocal.sqrMagnitude > 0.000001f)
            {
                // The bend plane is fixed by the pole even when the arm is straight: upper x lower = d * (pole x dir).
                Vector3 bendNormal = Vector3.Cross(poleDirection, direction);
                RollToward(upper, lower.position - upper.position, upper.rotation * limits.hingeLocal, bendNormal);
                // Straight, untwisted forearm first, so the aim below is a pure bend about the hinge.
                if (limits.hasLowerBind)
                    lower.rotation = upper.rotation * limits.lowerBindLocal;
            }
            AimChild(lower, hand, clampedTarget);
        }

        // Two-bone IK plus a hand that closes on a staff: the staff runs across the palm (gripAxisLocal, hand space)
        // through gripOffsetLocal (hand space, world metres). Solved twice because the wrist goal depends on the hand
        // rotation, which depends on where the forearm ended up.
        public static void SolveGrip(Transform upper, Transform lower, Transform hand, Vector3 grip, Vector3 staffAxis,
            Vector3 pole, Vector3 gripAxisLocal, Vector3 gripOffsetLocal)
        {
            if (upper == null || lower == null || hand == null)
                return;
            Vector3 wrist = grip;
            for (int pass = 0; pass < 2; pass++)
            {
                Solve(upper, lower, hand, wrist, pole);
                Vector3 current = hand.rotation * gripAxisLocal;
                hand.rotation = Quaternion.FromToRotation(current, staffAxis) * hand.rotation;
                wrist = grip - hand.rotation * gripOffsetLocal;
            }
        }

        /// <summary>
        /// Grip solve that also picks the hand's orientation: the hand starts from a straight wrist on the solved
        /// forearm (bind pose) and turns by the smallest rotation that lays its grip line on the staff, so it never
        /// keeps whatever twist the idle clip had. The grip line runs <see cref="Limits.maxOblique"/> off the knuckle
        /// line toward the fingers (fingerLocal, hand space), and part of the twist goes into the forearm.
        /// </summary>
        public static void SolveGrip(Transform upper, Transform lower, Transform hand, Vector3 grip, Vector3 staffAxis,
            Vector3 pole, Vector3 gripAxisLocal, Vector3 gripOffsetLocal, Vector3 fingerLocal, in Limits limits)
        {
            if (upper == null || lower == null || hand == null)
                return;
            if (staffAxis.sqrMagnitude < 0.000001f)
                return;
            staffAxis.Normalize();
            Vector3 acrossLocal = Vector3.ProjectOnPlane(gripAxisLocal, fingerLocal);
            if (acrossLocal.sqrMagnitude < 0.000001f || fingerLocal.sqrMagnitude < 0.000001f)
            {
                SolveGrip(upper, lower, hand, grip, staffAxis, pole, gripAxisLocal, gripOffsetLocal);
                return;
            }
            acrossLocal.Normalize();
            // A wand/staff does not cross the palm square to the fingers: it runs from the heel of the hand on the
            // pinky side out between thumb and index, tilted maxOblique degrees from the knuckle line toward the
            // fingers. That keeps the wrist near straight while the forearm points along the staff.
            float oblique = Mathf.Clamp(limits.maxOblique, 0f, 85f) * Mathf.Deg2Rad;
            Vector3 staffInHand = (acrossLocal * Mathf.Cos(oblique) + fingerLocal.normalized * Mathf.Sin(oblique)).normalized;

            Vector3 wrist = grip;
            Quaternion handRotation = hand.rotation;
            for (int pass = 0; pass < 3; pass++)
            {
                Solve(upper, lower, hand, wrist, pole, limits);
                // Start from a straight wrist on the solved forearm and turn the hand by the smallest rotation that
                // lays the grip line on the staff. Smallest rotation from neutral keeps the roll continuous; its only
                // singularity is a staff pointing back at the elbow, which aiming never produces. (Choosing the roll
                // from the forearm direction instead flipped the hand whenever the forearm ran along the staff.)
                Quaternion neutral = limits.hasHandBind ? lower.rotation * limits.handBindLocal : hand.rotation;
                handRotation = Quaternion.FromToRotation(neutral * staffInHand, staffAxis) * neutral;
                hand.rotation = handRotation;
                wrist = grip - handRotation * gripOffsetLocal;
            }

            if (limits.hasHandBind && limits.forearmTwist > 0f)
            {
                // Without twist bones all pronation would wring the wrist skin: let the forearm carry part of it.
                Vector3 axisWorld = hand.position - lower.position;
                if (axisWorld.sqrMagnitude > 0.000001f)
                {
                    Quaternion local = Quaternion.Inverse(lower.rotation) * handRotation;
                    Quaternion delta = local * Quaternion.Inverse(limits.handBindLocal);
                    Vector3 axisLocal = (Quaternion.Inverse(lower.rotation) * axisWorld).normalized;
                    float twist = TwistAngle(delta, axisLocal);
                    lower.rotation = Quaternion.AngleAxis(twist * limits.forearmTwist, axisWorld.normalized) * lower.rotation;
                    hand.rotation = handRotation;
                }
            }
        }

        /// <summary>Signed angle (degrees) of the twist part of q about axis (swing-twist decomposition).</summary>
        public static float TwistAngle(Quaternion q, Vector3 axis)
        {
            Vector3 v = new Vector3(q.x, q.y, q.z);
            float projected = Vector3.Dot(v, axis);
            float angle = 2f * Mathf.Atan2(projected, q.w) * Mathf.Rad2Deg;
            return Mathf.DeltaAngle(0f, angle);
        }

        // Eases distance toward maxReach from softReach * maxReach on, so an arm pulled past its length still keeps a
        // little bend and does not snap between bent and locked as the target crosses the limit.
        static float SoftClamp(float distance, float maxReach, float softReach)
        {
            if (softReach >= 0.999f)
                return distance;
            float start = maxReach * Mathf.Clamp(softReach, 0.5f, 0.999f);
            if (distance <= start)
                return distance;
            float room = maxReach - start;
            return start + room * (1f - Mathf.Exp(-(distance - start) / room));
        }

        // Turns bone about boneAxis so that axis (world) lines up with goal, both measured across boneAxis.
        static void RollToward(Transform bone, Vector3 boneAxis, Vector3 axis, Vector3 goal)
        {
            if (boneAxis.sqrMagnitude < 0.000001f)
                return;
            boneAxis.Normalize();
            Vector3 from = Vector3.ProjectOnPlane(axis, boneAxis);
            Vector3 to = Vector3.ProjectOnPlane(goal, boneAxis);
            if (from.sqrMagnitude < 0.000001f || to.sqrMagnitude < 0.000001f)
                return;
            float angle = Vector3.SignedAngle(from, to, boneAxis);
            bone.rotation = Quaternion.AngleAxis(angle, boneAxis) * bone.rotation;
        }

        static void AimChild(Transform bone, Transform child, Vector3 worldTarget)
        {
            Vector3 current = child.position - bone.position;
            Vector3 desired = worldTarget - bone.position;
            if (current.sqrMagnitude < 0.000001f || desired.sqrMagnitude < 0.000001f)
                return;
            bone.rotation = Quaternion.FromToRotation(current, desired) * bone.rotation;
        }
    }
}
