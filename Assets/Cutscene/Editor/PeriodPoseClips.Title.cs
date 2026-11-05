using UnityEngine;

namespace AiHackathon.Cutscene.Editor
{
    /// <summary>
    /// Looping poses for the main screen (<see cref="TitleSceneBuilder"/>). Every motion is a whole number of cycles
    /// over <see cref="TitleLength"/> (or a bump that is back at rest by the end), so the clips loop without a seam.
    /// </summary>
    public static partial class PeriodPoseClips
    {
        internal const float TitleLength = 8f;

        public static void BuildTitle()
        {
            Save(Make(AmeliaFbx, "Amelia_Title", TitleLength, AmeliaTitle));
            Save(Make(NoxFbx, "Nox_Title", TitleLength, NoxTitle));
        }

        /// <summary>Hold the light: wand upright at her right side, a small light cupped in her raised left palm, head bowed a touch toward it.</summary>
        static void AmeliaTitle(PoseRig rig, float t)
        {
            float w = t / TitleLength * Mathf.PI * 2f;
            float sway = Mathf.Sin(w);
            float breath = Mathf.Sin(2f * w);
            rig.Hips(new Vector3(0.004f * sway - 0.004f, -0.004f - 0.0012f * breath, 0f), new Vector3(0f, -2.5f + 1.2f * sway, 1.6f - 0.8f * sway));
            rig.Turn("Spine", X, 1f);
            rig.Turn("Spine", Z, -1f + 0.4f * sway);
            rig.Turn("Chest", X, 0.5f - 0.6f * breath);
            rig.Turn("UpperChest", X, -0.8f * breath);
            rig.Turn("UpperChest", Y, -4f);
            rig.Turn("Left_Shoulder", Z, -0.6f * breath);
            rig.Turn("Right_Shoulder", Z, 0.6f * breath);
            rig.Turn("Neck", X, 2.5f);
            rig.Turn("Head", X, 5f + 1.2f * Mathf.Sin(w + 0.5f));
            rig.Turn("Head", Y, -7f + 2.5f * Mathf.Sin(w + 1.1f));
            rig.Turn("Head", Z, -5f + 1f * Mathf.Sin(2f * w + 0.3f));

            WandAtSide(rig, R, 0.012f);

            // Palm up in front of her left side, the light resting on it; it rises and settles with her breath.
            var palm = TitlePalm + new Vector3(0.002f * sway, 0.007f * breath, 0f);
            var hand = rig.HandRotation(L, new Vector3(-0.42f, 0.12f, 1f).normalized, new Vector3(0.08f, 1f, 0.1f).normalized);
            var wrist = palm - (hand * rig.FingerLocal(L)) * 0.04f + (hand * rig.PalmLocal(L)) * 0.012f;
            ArmDown(rig, L, out var restWrist, out var restHand);
            FreeArm(rig, L, restWrist, restHand, wrist, hand, 1f, Vector3.zero,
                new Vector3(-0.35f, -0.3f, -0.08f), new Vector3(-0.32f, -0.4f, -0.12f), 0.3f, 0.28f);
        }

        /// <summary>Where Amelia's palm light sits (model space); the builder hangs the light on her hand there.</summary>
        internal static readonly Vector3 TitlePalm = new Vector3(-0.14f, 0.65f, 0.23f);

        /// <summary>The direction the palm faces, in the hand's space (for a light resting on an open hand).</summary>
        internal static Vector3 PalmNormalLocal(string characterFbx, string side)
        {
            using (var rig = new PoseRig(characterFbx))
                return rig.PalmLocal(side).normalized;
        }

        /// <summary>
        /// Cocky: wand raised beside her face with the tip leaning out, right hand on the hip, weight on one leg and the
        /// head tilted; once a lap the wand gives a quick flick out and back.
        /// </summary>
        static void NoxTitle(PoseRig rig, float t)
        {
            float w = t / TitleLength * Mathf.PI * 2f;
            float sway = Mathf.Sin(w);
            float breath = Mathf.Sin(2f * w);
            float flick = Ease(t, 5.0f, 5.22f) - Ease(t, 5.22f, 5.75f);

            rig.Hips(new Vector3(0.018f + 0.003f * sway, -0.004f - 0.0012f * breath, 0f), new Vector3(0f, -4f + 1.2f * sway, 3.5f - 0.6f * sway));
            rig.Turn("Spine", Z, -2f);
            rig.Turn("Chest", Z, -1.5f);
            rig.Turn("Chest", X, -2f - 0.6f * breath);
            rig.Turn("UpperChest", X, -0.7f * breath);
            rig.Turn("UpperChest", Y, 3f + 1.5f * flick);
            rig.Turn("Left_Shoulder", Z, -0.6f * breath);
            rig.Turn("Right_Shoulder", Z, 0.6f * breath);
            rig.Turn("Head", Z, 8f + 1.2f * Mathf.Sin(w + 0.9f) - 2f * flick);
            rig.Turn("Head", X, -3f + 0.8f * Mathf.Sin(2f * w + 1.4f));
            rig.Turn("Head", Y, 2f * Mathf.Sin(w + 2f));

            var point = new Vector3(-0.2f, 0.78f + 0.004f * breath + 0.012f * flick, 0.12f);
            var axis = Quaternion.AngleAxis(14f * flick, Vector3.forward) * new Vector3(-0.5f, 1f, 0.12f).normalized;
            Grip(rig, L, point, axis, new Vector3(-0.2f, 0f, 1f), new Vector3(-0.45f, -0.3f, -0.1f));

            ArmDown(rig, R, out var restWrist, out var restHand);
            HandOnHip(rig, R, out var hipWrist, out var hipHand);
            FreeArm(rig, R, restWrist, restHand, hipWrist, hipHand, 1f, Vector3.zero,
                new Vector3(0.35f, -0.3f, -0.08f), new Vector3(0.4f, -0.12f, -0.28f), 0.3f, 0.45f);
        }
    }
}
