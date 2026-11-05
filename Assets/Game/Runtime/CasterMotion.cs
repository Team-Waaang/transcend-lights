using UnityEngine;
using Waaang;

namespace AiHackathon.Game
{
    /// <summary>
    /// Body life for a caster in play, between <see cref="CasterPose"/> (idle, Update) and the
    /// <see cref="WandPointerRig"/> arm IK (LateUpdate), so the arms still land on the aim:
    /// a weight shift and breathing bounce at rest; while firing she braces (knees bend, leans in, the chest strains);
    /// <see cref="Flinch"/> knocks her back when the curse hits. The legs are re-planted with two-bone IK.
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public class CasterMotion : MonoBehaviour
    {
        public WandPointerRig rig;
        [Tooltip("Overall amount of idle motion.")]
        public float life = 1f;
        [Tooltip("Metres the hips drop while firing.")]
        public float braceDrop = 0.05f;

        Transform hips, spine, chest, upperChest, head;
        Transform lUpper, lLower, lFoot, rUpper, rLower, rFoot;
        Vector3 lFootPos, rFootPos;
        Quaternion lFootRot, rFootRot;
        float fire, flinch, phase;

        void Awake()
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
                switch (t.name)
                {
                    case "Hips": hips = t; break;
                    case "Spine": spine = t; break;
                    case "Chest": chest = t; break;
                    case "UpperChest": upperChest = t; break;
                    case "Head": head = t; break;
                    case "Left_UpperLeg": lUpper = t; break;
                    case "Left_LowerLeg": lLower = t; break;
                    case "Left_Foot": lFoot = t; break;
                    case "Right_UpperLeg": rUpper = t; break;
                    case "Right_LowerLeg": rLower = t; break;
                    case "Right_Foot": rFoot = t; break;
                }
            if (lFoot != null) { lFootPos = lFoot.position; lFootRot = lFoot.rotation; }
            if (rFoot != null) { rFootPos = rFoot.position; rFootRot = rFoot.rotation; }
            phase = Random.value * 10f;
        }

        /// <summary>Knock her back (0..1).</summary>
        public void Flinch(float strength) => flinch = Mathf.Clamp01(Mathf.Max(flinch, strength));

        void Update()
        {
            if (hips == null) return;
            float dt = Time.deltaTime;
            phase += dt;
            bool firing = rig != null && rig.isActiveAndEnabled && rig.Firing;
            fire = Mathf.Lerp(fire, firing ? 1f : 0f, 1f - Mathf.Exp(-(firing ? 9f : 4f) * dt));
            flinch = Mathf.Max(0f, flinch - dt * 1.8f);
            // Flinch: a fast kick back that eases out.
            float kick = flinch * flinch * (3f - 2f * flinch);

            var fwd = transform.forward;
            var right = transform.right;
            var up = Vector3.up;
            float sway = Mathf.Sin(phase * Mathf.PI * 2f / 4.2f) * life;
            float bounce = Mathf.Sin(phase * Mathf.PI * 2f / 2.1f) * life;
            float scale = transform.lossyScale.y;

            hips.position += -up * (braceDrop * fire + 0.03f * kick + 0.004f * (1f + bounce) * scale)
                             + right * (0.012f * sway * (1f - 0.6f * fire) * scale)
                             + fwd * (-0.02f * fire - 0.05f * kick) * scale;
            hips.rotation = Quaternion.AngleAxis(2.2f * sway * (1f - 0.5f * fire), fwd)
                            * Quaternion.AngleAxis(4f * fire - 6f * kick, right) * hips.rotation;

            // Lean in and strain while the beam is held; shake a little with the effort.
            float strain = (Mathf.PerlinNoise(phase * 13f, 0.3f) - 0.5f) * 2f * fire;
            Turn(spine, right, 3f * fire - 5f * kick);
            Turn(chest, right, 4f * fire - 9f * kick + 0.8f * bounce + 1.4f * strain);
            Turn(chest, up, 1.2f * strain);
            Turn(upperChest, right, 2f * fire - 6f * kick);
            Turn(head, right, -6f * fire + 7f * kick - 0.5f * bounce);

            Plant(lUpper, lLower, lFoot, lFootPos, lFootRot, fwd);
            Plant(rUpper, rLower, rFoot, rFootPos, rFootRot, fwd);
        }

        static void Turn(Transform bone, Vector3 axis, float degrees)
        {
            if (bone != null && Mathf.Abs(degrees) > 1e-4f) bone.rotation = Quaternion.AngleAxis(degrees, axis) * bone.rotation;
        }

        static void Plant(Transform upper, Transform lower, Transform foot, Vector3 at, Quaternion rot, Vector3 fwd)
        {
            if (upper == null || lower == null || foot == null) return;
            ArmIk.Solve(upper, lower, foot, at, lower.position + fwd * 0.5f);
            foot.rotation = rot;
        }
    }
}
