using UnityEngine;

namespace Waaang
{
    public static class ArmIk
    {
        public static void Solve(Transform upper, Transform lower, Transform hand, Vector3 target, Vector3 pole)
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
            distance = Mathf.Clamp(distance, minReach, maxReach);
            Vector3 clampedTarget = upper.position + direction * distance;

            float along = (upperLength * upperLength + distance * distance - lowerLength * lowerLength) / (2f * distance);
            float height = Mathf.Sqrt(Mathf.Max(0f, upperLength * upperLength - along * along));
            Vector3 poleDirection = Vector3.ProjectOnPlane(pole - upper.position, direction);
            if (poleDirection.sqrMagnitude < 0.000001f)
                poleDirection = Vector3.Cross(direction, Vector3.up);
            Vector3 elbow = upper.position + direction * along + poleDirection.normalized * height;

            AimChild(upper, lower, elbow);
            AimChild(lower, hand, clampedTarget);
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
