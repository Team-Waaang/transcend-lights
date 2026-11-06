using UnityEditor;
using UnityEngine;
using static AiHackathon.Cutscene.Editor.CutsceneKit;

namespace AiHackathon.Cutscene.Editor
{
    /// <summary>
    /// Clips for the fall into the boss arena (<see cref="BossFallCutsceneBuilder"/>): cheering after Period 2,
    /// startled as the floor rumbles, flailing through the dark, and the landing crouch that rises into the game's
    /// ready stance and looks warily round the court. Same code-keyed style as the period clips (staff in the lead
    /// hand); airborne poses free the legs with <see cref="PoseRig.HipsFree"/> and bend them by hand.
    /// </summary>
    public static partial class PeriodPoseClips
    {
        public const float CheerLength = 3.6f;
        public const float StartleLength = 1.8f;
        public const float FallLength = 3f;
        public const float LandLength = 4.6f;
        /// <summary>Amelia's hops in the cheer clip (seconds into it); the cutscene lifts her off the floor there.</summary>
        public static readonly float[] CheerHops = { 0.6f, 1.6f };
        public const float HopAir = 0.32f;

        [MenuItem("Tools/Cutscene/Build Fall Pose Clips")]
        public static void BuildFallMenu() => BuildFall();

        public static string BuildFall()
        {
            EnsureFolder(AnimDir);
            Save(Make(AmeliaFbx, "Amelia_Cheer", CheerLength, AmeliaCheer));
            Save(Make(NoxFbx, "Nox_Cheer", CheerLength, NoxCheer));
            Save(Make(AmeliaFbx, "Amelia_Startle", StartleLength, (rig, t) => Startle(rig, t, R)));
            Save(Make(NoxFbx, "Nox_Startle", StartleLength, (rig, t) => Startle(rig, t, L)));
            Save(Make(AmeliaFbx, "Amelia_Fall", FallLength, (rig, t) => Fall(rig, t, R), true));
            Save(Make(NoxFbx, "Nox_Fall", FallLength, (rig, t) => Fall(rig, t, L), true));
            Save(Make(AmeliaFbx, "Amelia_Land", LandLength, (rig, t) => Land(rig, t, R)));
            Save(Make(NoxFbx, "Nox_Land", LandLength, (rig, t) => Land(rig, t, L)));
            AssetDatabase.SaveAssets();
            return "baked";
        }

        /// <summary>0 → 1 → 0 over [a, b] (half a sine).</summary>
        static float Bump(float t, float a, float b) => t <= a || t >= b ? 0f : Mathf.Sin((t - a) / (b - a) * Mathf.PI);

        static void OpenArm(PoseRig rig, string side, Vector3 wrist, Vector3 finger, Vector3 palm, Vector3 pole, float curl = 0.12f)
        {
            rig.Arm(side, wrist, rig[side + "UpperArm"].position + pole, rig.HandRotation(side, finger.normalized, palm.normalized));
            rig.Curl(side, curl, 0.1f);
        }

        /// <summary>Bends a free (airborne) leg: thigh lifted forward, knee bent back, splayed out to the side.</summary>
        static void Leg(PoseRig rig, string side, float thigh, float knee, float splay)
        {
            rig.Turn(side + "UpperLeg", X, -thigh);
            rig.Turn(side + "UpperLeg", Z, splay * (side == R ? -1f : 1f));
            rig.Turn(side + "LowerLeg", X, knee);
        }

        // ---------------------------------------------------------------- cheering

        /// <summary>Amelia hops twice with both arms up, wand high, then bounces on her toes.</summary>
        static void AmeliaCheer(PoseRig rig, float t)
        {
            // Knees dip before each hop and take the landing after it.
            float dip = 0f;
            foreach (var hop in CheerHops) dip += Bump(t, hop - 0.18f, hop) + 0.8f * Bump(t, hop + HopAir, hop + HopAir + 0.2f);
            float bounce = 0.4f * Mathf.Max(0f, Mathf.Sin(t * Mathf.PI * 2f / 0.5f)) * Ease(t, 2.2f, 2.6f);
            float sway = Mathf.Sin(t * Mathf.PI * 2f / 0.9f);
            rig.Hips(new Vector3(0.006f * sway, -0.035f * dip - 0.012f * bounce, -0.012f * dip), new Vector3(-4f + 5f * dip, 3f * sway, -2f * sway));
            rig.Turn("Chest", X, -5f + 3f * dip);
            rig.Turn("UpperChest", X, -3f);
            rig.Turn("Neck", X, -4f);
            rig.Turn("Head", X, -9f + 4f * dip);
            rig.Turn("Head", Z, -7f * sway);

            // Wand thrust up on her right, the left arm up too, open hand waving.
            var point = new Vector3(0.17f + 0.03f * sway, 1.13f - 0.04f * dip, 0.1f);
            Grip(rig, R, point, new Vector3(0.28f + 0.1f * sway, 1f, 0.12f).normalized, new Vector3(1f, 0f, 0.2f), new Vector3(0.45f, -0.15f, -0.2f));
            OpenArm(rig, L, new Vector3(-0.19f - 0.04f * sway, 1.08f - 0.05f * dip, 0.08f), new Vector3(-0.2f - 0.25f * sway, 1f, 0.1f), new Vector3(0.1f, 0f, 1f),
                new Vector3(-0.45f, -0.15f, -0.2f));
        }

        /// <summary>Nox pumps her wand overhead twice, other hand on her hip, leaning back with a grin.</summary>
        static void NoxCheer(PoseRig rig, float t)
        {
            float pump = 0.5f + 0.5f * Mathf.Sin(t * Mathf.PI * 2f / 0.75f - Mathf.PI * 0.5f);
            pump *= 1f - 0.6f * Ease(t, 2.0f, 2.6f);
            float sway = Mathf.Sin(t * Mathf.PI * 2f / 1.8f);
            rig.Hips(new Vector3(0.016f, -0.006f - 0.01f * pump, 0f), new Vector3(-3f, -5f + 2f * sway, 3f));
            rig.Turn("Spine", Z, -2f);
            rig.Turn("Chest", X, -4f - 2f * pump);
            rig.Turn("UpperChest", Y, 5f);
            rig.Turn("Head", X, -6f - 3f * pump);
            rig.Turn("Head", Z, 9f);
            rig.Turn("Head", Y, 8f * sway);

            var low = new Vector3(-0.13f, 0.9f, 0.13f);
            var high = new Vector3(-0.1f, 1.2f, 0.08f);
            Grip(rig, L, Vector3.Lerp(low, high, pump), new Vector3(-0.2f, 1f, 0.15f).normalized, new Vector3(-0.9f, 0.1f, 0.4f),
                new Vector3(-0.4f, -0.3f, -0.15f));
            HandOnHip(rig, R, out var hipWrist, out var hipHand);
            rig.Arm(R, hipWrist, rig["Right_UpperArm"].position + new Vector3(0.4f, -0.12f, -0.28f), hipHand);
            rig.Curl(R, 0.45f, 0.2f);
        }

        // ---------------------------------------------------------------- the rumble

        /// <summary>The floor shakes: she flinches into a crouch, wand pulled in, free arm out for balance, and looks down.</summary>
        static void Startle(PoseRig rig, float t, string lead)
        {
            string support = lead == R ? L : R;
            float s = Ease(t, 0f, 0.25f);
            float look = Ease(t, 0.25f, 0.7f);
            float shake = Mathf.Sin(t * 41f) * 0.6f + Mathf.Sin(t * 67f + 1f) * 0.4f;
            rig.Hips(new Vector3(0f, -0.032f * s, -0.01f * s), new Vector3(5f * s, 0f, 1.2f * shake * s));
            rig.Turn("Spine", X, 5f * s);
            rig.Turn("Chest", X, 4f * s + 0.6f * shake);
            rig.Turn("Left_Shoulder", Z, -5f * s);
            rig.Turn("Right_Shoulder", Z, 5f * s);
            rig.Turn("Neck", X, 9f * look);
            rig.Turn("Head", X, 20f * look);
            rig.Turn("Head", Y, 10f * Side(lead) * look);

            Grip(rig, lead, M(new Vector3(0.07f, 0.72f, 0.15f), lead), M(new Vector3(0.15f, 1f, 0.12f), lead).normalized,
                M(new Vector3(0.2f, 0f, 1f), lead), M(new Vector3(0.3f, -0.35f, -0.2f), lead));
            OpenArm(rig, support, M(new Vector3(0.3f, 0.62f + 0.01f * shake, 0.12f), support), M(new Vector3(1f, -0.25f, 0.3f), support),
                new Vector3(0f, -1f, 0f), M(new Vector3(0.4f, -0.1f, -0.25f), support), 0.3f);
        }

        // ---------------------------------------------------------------- falling

        /// <summary>Through the dark: tipped back, legs kicking, arms thrown up and flailing. Loops.</summary>
        static void Fall(PoseRig rig, float t, string lead)
        {
            string support = lead == R ? L : R;
            float w = t / FallLength * Mathf.PI * 2f;
            // Whole numbers of cycles over the clip so it loops.
            float f1 = Mathf.Sin(w * 5f), f2 = Mathf.Sin(w * 7f + 1f), f3 = Mathf.Sin(w * 4f + 2f);
            float phase = lead == R ? 0f : 1.3f;
            rig.HipsFree(Vector3.zero, new Vector3(-16f + 4f * f2, 3f * f3, 3f * f1));
            rig.Turn("Spine", X, -6f);
            rig.Turn("Chest", X, -6f + 2f * f1);
            rig.Turn("Neck", X, -6f);
            rig.Turn("Head", X, -12f + 4f * f2);
            rig.Turn("Head", Y, 8f * f3);
            Leg(rig, L, 38f + 16f * Mathf.Sin(w * 5f + phase), 62f + 22f * Mathf.Sin(w * 5f + phase + 1f), 10f);
            Leg(rig, R, 30f + 16f * Mathf.Sin(w * 5f + phase + 2.4f), 70f + 22f * Mathf.Sin(w * 5f + phase + 3.4f), 10f);

            Grip(rig, lead, M(new Vector3(0.22f + 0.04f * f1, 1.12f + 0.05f * f2, 0.04f), lead), M(new Vector3(0.55f + 0.15f * f3, 0.8f, -0.15f), lead).normalized,
                M(new Vector3(0.3f, 0f, 1f), lead), M(new Vector3(0.45f, -0.1f, -0.2f), lead));
            OpenArm(rig, support, M(new Vector3(0.3f + 0.04f * f2, 1.05f + 0.06f * f1, 0.06f), support), M(new Vector3(0.7f, 1f + 0.3f * f3, 0f), support),
                new Vector3(0f, 0f, 1f), M(new Vector3(0.45f, -0.1f, -0.2f), support), 0.05f);
        }

        // ---------------------------------------------------------------- landing

        /// <summary>
        /// Lands in a deep crouch (free hand on the flagstones, wand out for balance), holds, rises into the game's
        /// ready stance, and looks round the court: left, right, then ahead.
        /// </summary>
        static void Land(PoseRig rig, float t, string lead)
        {
            string support = lead == R ? L : R;
            float rise = Ease(t, 0.95f, 1.75f);
            float k = 1f - rise;
            float crouch = 0.19f * k * (0.6f + 0.4f * Ease(t, 0f, 0.14f));
            float breath = Mathf.Sin(t * Mathf.PI * 2f / 2.2f);
            float scanA = Bump(t, 1.9f, 2.9f), scanB = Bump(t, 2.8f, 3.8f);
            float yaw = (-28f * scanA + 28f * scanB) * (lead == R ? 1f : -1f);
            rig.Hips(new Vector3(0f, -crouch - 0.006f * rise, -0.3f * crouch), new Vector3(26f * k + 2f * rise, -2f * Side(lead) * rise + yaw * 0.15f, 0f));
            rig.Turn("Spine", X, 8f * k + 2f * rise);
            rig.Turn("Chest", X, 6f * k + 1.5f * rise - 0.8f * breath);
            rig.Turn("UpperChest", Y, yaw * 0.3f);
            rig.Turn("Neck", X, -10f * k);
            rig.Turn("Head", X, -16f * k - 1.5f * rise);
            rig.Turn("Head", Y, yaw * 0.6f);

            // Lead: wand out to the side and back for balance, lifting into the ready grip.
            var p0 = M(new Vector3(0.3f, 0.42f - 0.1f * crouch, -0.02f), lead);
            var a0 = M(new Vector3(0.6f, 0.6f, -0.3f), lead).normalized;
            var p1 = M(new Vector3(0.03f, 0.665f, 0.2f), lead);
            var a1 = M(new Vector3(-0.06f, 0.42f, 1f), lead).normalized;
            var point = Vector3.Lerp(p0, p1, rise) + M(new Vector3(0.04f, 0.05f, 0.05f), lead) * Mathf.Sin(rise * Mathf.PI);
            var axis = Vector3.Slerp(a0, a1, rise);
            Grip(rig, lead, point, axis, Vector3.Slerp(M(new Vector3(0f, 1f, 0f), lead), M(new Vector3(0.35f, 1f, 0f), lead), rise),
                Vector3.Lerp(M(new Vector3(0.4f, 0f, -0.3f), lead), M(new Vector3(0.32f, -0.35f, -0.05f), lead), rise));

            // Support: palm on the floor ahead, then up to the shaft below the lead fist.
            var floorWrist = M(new Vector3(0.13f, 0.07f - 0.02f * (1f - Ease(t, 0f, 0.14f)), 0.24f), support);
            var floorHand = rig.HandRotation(support, M(new Vector3(0.15f, -0.25f, 1f), support).normalized, new Vector3(0f, -1f, 0f));
            var gripRot = EasyGrip(rig, support, point - axis * 0.075f, axis, M(new Vector3(-0.2f, -1f, 0.3f), lead),
                M(new Vector3(-0.3f, -0.38f, 0f), lead), SupportOblique, out var supportWrist);
            FreeArm(rig, support, floorWrist, floorHand, supportWrist, gripRot, rise, M(new Vector3(0.05f, 0.05f, 0.05f), support),
                M(new Vector3(0.35f, -0.1f, 0.2f), support), M(new Vector3(-0.3f, -0.38f, 0f), lead), 0.15f, 0.2f);
            ShaftLine(rig, lead, out var held, out var heldAxis);
            rig.Wrap(support, held, heldAxis, ShaftContact, rise);
        }
    }
}
