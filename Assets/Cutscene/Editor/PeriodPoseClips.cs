using System;
using UnityEditor;
using UnityEngine;
using static AiHackathon.Cutscene.Editor.CutsceneKit;
using Object = UnityEngine.Object;

namespace AiHackathon.Cutscene.Editor
{
    /// <summary>
    /// Hand-keyed clips for Amelia and Nox in the period-start cutscenes, built with <see cref="PoseRig"/>.
    /// Both share the Tripo skeleton, so a clip made on one plays on the other. Amelia holds her wand in the right
    /// hand, Nox (left-handed, as in the game) in the left.
    /// Positions are in model units (the bind pose stands ~1.1 tall; scenes scale the characters 1.5x like the game).
    /// </summary>
    public static class PeriodPoseClips
    {
        internal const string AnimDir = "Assets/Cutscene/Periods/Anim";
        internal const string AmeliaFbx = "Assets/TripoModels/amelia_polaris_3d_model/amelia_polaris_3d_model.fbx";
        internal const string NoxFbx = "Assets/TripoModels/nox/nox.fbx";
        internal const string AmeliaWandFbx = "Assets/TripoModels/amelia_weapon/amelia_weapon.fbx";
        internal const string NoxWandFbx = "Assets/TripoModels/nox_weapon/twisted_magic_wand_3d_model.fbx";
        // The game's wands: 1 m (Amelia) and 0.85 m (Nox) on a 1.5x character.
        internal const float AmeliaWandLength = 0.667f;
        internal const float NoxWandLength = 0.567f;
        internal const float GripFraction = 0.25f;
        const float Fps = 30f;

        const string R = "Right_", L = "Left_";
        static readonly Vector3 X = Vector3.right;    // character's right; positive turns pitch a bone forward
        static readonly Vector3 Y = Vector3.up;       // positive turns toward +X (her right)
        static readonly Vector3 Z = Vector3.forward;  // positive tilts the top toward -X (her left)

        [MenuItem("Tools/Cutscene/Build Period Pose Clips")]
        public static void Build()
        {
            EnsureFolder(AnimDir);
            Save(Make(AmeliaFbx, "Amelia_A1_Bow", 4.2f, AmeliaBow));
            Save(Make(AmeliaFbx, "Amelia_A2_Rune", 4.4f, AmeliaRune));
            Save(Make(AmeliaFbx, "Amelia_Ready", 3.0f, (rig, t) => Ready(rig, t, R)));
            Save(Make(NoxFbx, "Nox_N1_Taunt", 4.0f, NoxShoulder));
            Save(Make(NoxFbx, "Nox_N2_Flourish", 4.4f, NoxFlourish));
            Save(Make(NoxFbx, "Nox_Ready", 3.0f, (rig, t) => Ready(rig, t, L)));
            Save(Make(AmeliaFbx, "Caster_Idle", IdleLength, CasterIdle));
            Save(Make(AmeliaFbx, "Amelia_A3_Flinch", 4.0f, AmeliaFlinch));
            Save(Make(AmeliaFbx, "Amelia_A4_Steady", 3.6f, AmeliaSteady));
            Save(Make(NoxFbx, "Nox_N3_Torch", 4.4f, NoxTorch));
            AssetDatabase.SaveAssets();
        }

        internal static AnimationClip Load(string name) => AssetDatabase.LoadAssetAtPath<AnimationClip>(AnimDir + "/" + name + ".anim");

        static AnimationClip Make(string fbx, string name, float length, Action<PoseRig, float> pose)
        {
            using (var rig = new PoseRig(fbx))
            {
                int frames = Mathf.RoundToInt(length * Fps);
                for (int i = 0; i <= frames; i++)
                {
                    float t = i / Fps;
                    rig.Reset();
                    pose(rig, t);
                    rig.Key(t);
                }
                return rig.Bake(name);
            }
        }

        static void Save(AnimationClip clip)
        {
            string path = AnimDir + "/" + clip.name + ".anim";
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (existing != null)
            {
                var name = clip.name;
                EditorUtility.CopySerialized(clip, existing);
                existing.name = name;
                EditorUtility.SetDirty(existing);
            }
            else
            {
                AssetDatabase.CreateAsset(clip, path);
            }
        }

        // ---------------------------------------------------------------- building blocks

        static float Side(string side) => side == R ? 1f : -1f;
        static Vector3 M(Vector3 v, string side) => side == R ? v : new Vector3(-v.x, v.y, v.z);
        static float Ease(float t, float a, float b) => PoseRig.Ease(t, a, b);

        /// <summary>Slow weight sway and breathing, so held poses never freeze.</summary>
        static void Alive(PoseRig rig, float t, float amount = 1f, Vector3 extraOffset = default, Vector3 extraEuler = default)
        {
            float sway = Mathf.Sin(t * Mathf.PI * 2f / 4.6f) * amount;
            float breath = Mathf.Sin(t * Mathf.PI * 2f / 3.3f) * amount;
            rig.Hips(new Vector3(0.004f * sway, -0.004f - 0.001f * breath, 0f) + extraOffset,
                new Vector3(0f, 1.2f * sway, -0.8f * sway) + extraEuler);
            rig.Turn("Chest", X, -0.5f * breath);
            rig.Turn("UpperChest", X, -0.6f * breath);
            rig.Turn("Left_Shoulder", Z, -0.6f * breath);
            rig.Turn("Right_Shoulder", Z, 0.6f * breath);
        }

        /// <summary>
        /// Rotation of a fist closed on a wand: the shaft runs along <paramref name="axis"/> (tip direction) through the
        /// palm, knuckles toward <paramref name="knuckles"/>.
        /// </summary>
        static Quaternion GripRotation(PoseRig rig, string side, Vector3 axis, Vector3 knuckles)
        {
            axis.Normalize();
            var f = Vector3.ProjectOnPlane(knuckles, axis).normalized;
            var p = side == R ? Vector3.Cross(f, axis) : Vector3.Cross(axis, f);
            return rig.HandRotation(side, f, p);
        }

        // Where the shaft passes through a closed fist, in hand space: across the palm just short of the knuckles,
        // one shaft radius plus the palm's thickness below the bones, so the fingers have room to close around it.
        static Vector3 GripLocal(PoseRig rig, string side) =>
            rig.FingerLocal(side) * (rig.KnuckleDistance(side) * 0.86f) + rig.PalmLocal(side) * 0.021f;
        static Vector3 AxisLocal(PoseRig rig, string side) =>
            Vector3.Cross(rig.FingerLocal(side), -rig.PalmLocal(side)).normalized * (side == R ? 1f : -1f);

        /// <summary>Closes the hand on the shaft at <paramref name="point"/> (world), reaching it with arm IK.</summary>
        static void Grip(PoseRig rig, string side, Vector3 point, Vector3 axis, Vector3 knuckles, Vector3 pole, float curl = 0.85f)
        {
            var rot = GripRotation(rig, side, axis, knuckles);
            rig.Arm(side, point - rot * GripLocal(rig, side), rig[side + "UpperArm"].position + pole, rot);
            // Fingers and thumb close around the shaft itself rather than into a fixed fist beside it. Wrap around
            // where the wand really is (it hangs off this hand), not the target: when the arm can't quite reach,
            // the hand and the wand stop short together.
            rig.Curl(side, 0.15f, 0.1f);
            ShaftLine(rig, side, out var held, out var heldAxis);
            rig.Wrap(side, held, heldAxis, ShaftContact, Mathf.Clamp01(curl / 0.85f));
        }

        /// <summary>The wand's line as the hand holding it (<paramref name="side"/>) carries it now.</summary>
        static void ShaftLine(PoseRig rig, string side, out Vector3 point, out Vector3 axis)
        {
            var hand = rig[side + "Hand"];
            point = hand.position + hand.rotation * GripLocal(rig, side);
            axis = hand.rotation * AxisLocal(rig, side);
        }

        // Shaft radius plus half a finger, in model units (the game's wands are about 1.2 cm in radius at 1x).
        const float ShaftContact = 0.017f;

        static void GripPose(string side, out Vector3 point, out Vector3 axis, out Vector3 knuckles, out Vector3 pole, float forward = 0f)
        {
            point = M(new Vector3(0.09f, 0.513f, 0.104f + forward), side);
            axis = Vector3.up;
            knuckles = M(new Vector3(0.1f, -0.04f, 1f), side);
            pole = M(new Vector3(0.3f, -0.12f, -0.35f), side);
        }

        /// <summary>Wand held upright at her side, knuckles forward.</summary>
        static void WandAtSide(PoseRig rig, string side, float forward = 0f)
        {
            GripPose(side, out var point, out var axis, out var knuckles, out var pole, forward);
            Grip(rig, side, point, axis, knuckles, pole);
        }

        /// <summary>Free arm hanging relaxed.</summary>
        static void ArmDown(PoseRig rig, string side, out Vector3 wrist, out Quaternion hand)
        {
            hand = rig.HandRotation(side, M(new Vector3(-0.05f, -1f, 0.18f), side).normalized, M(new Vector3(-1f, 0f, 0.05f), side).normalized);
            wrist = M(new Vector3(0.098f, 0.51f, 0.035f), side);
        }

        static void HandOnHip(PoseRig rig, string side, out Vector3 wrist, out Quaternion hand)
        {
            hand = rig.HandRotation(side, M(new Vector3(-0.15f, -0.5f, 1f), side).normalized, M(new Vector3(-1f, 0.1f, -0.1f), side).normalized);
            wrist = M(new Vector3(0.128f, 0.585f, -0.005f), side);
        }

        /// <summary>Blends a free arm between two wrist/hand targets, swinging out through <paramref name="arc"/>.</summary>
        static void FreeArm(PoseRig rig, string side, Vector3 wristA, Quaternion handA, Vector3 wristB, Quaternion handB,
            float u, Vector3 arc, Vector3 poleA, Vector3 poleB, float curlA, float curlB)
        {
            var wrist = Vector3.Lerp(wristA, wristB, u) + arc * Mathf.Sin(u * Mathf.PI);
            var pole = rig[side + "UpperArm"].position + Vector3.Lerp(poleA, poleB, u);
            rig.Arm(side, wrist, pole, Quaternion.Slerp(handA, handB, Ease(u, 0.1f, 1f)));
            rig.Curl(side, Mathf.Lerp(curlA, curlB, u), Mathf.Lerp(0.25f, 0.2f, u));
        }

        /// <summary>
        /// Parents a wand to the lead hand where <see cref="Grip"/> closes the fist on it, held
        /// <see cref="GripFraction"/> of the way up from the handle.
        /// </summary>
        internal static Transform AttachWand(GameObject character, string characterFbx, string wandFbx, string side, float length)
        {
            Vector3 localPoint, localAxis;
            using (var rig = new PoseRig(characterFbx))
            {
                localPoint = GripLocal(rig, side);
                localAxis = AxisLocal(rig, side);
            }
            Transform hand = null;
            foreach (var t in character.GetComponentsInChildren<Transform>(true)) if (t.name == side + "Hand") hand = t;
            var wand = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(wandFbx));
            wand.name = "Wand";
            wand.transform.SetParent(hand, false);
            // The wand meshes lean in their own XY plane; find the shaft from the vertices like the game's AlignWand.
            var vertices = wand.GetComponentInChildren<MeshFilter>().sharedMesh.vertices;
            Vector3 handle = Vector3.zero, head = Vector3.zero;
            int nHandle = 0, nHead = 0;
            foreach (var v in vertices)
            {
                if (v.y < 0.25f) { handle += v; nHandle++; }
                else if (v.y > 0.7f) { head += v; nHead++; }
            }
            handle /= Mathf.Max(1, nHandle);
            head /= Mathf.Max(1, nHead);
            var meshAxis = (head - handle).normalized;
            float min = float.MaxValue, max = float.MinValue;
            foreach (var v in vertices)
            {
                float d = Vector3.Dot(v - handle, meshAxis);
                min = Mathf.Min(min, d);
                max = Mathf.Max(max, d);
            }
            float scale = length / (max - min);
            wand.transform.localRotation = Quaternion.FromToRotation(meshAxis, localAxis);
            // Hand space is the rig's model space, so lengths in model units scale with the character.
            wand.transform.localScale = Vector3.one * scale;
            var bottom = (handle + meshAxis * min) * scale;
            wand.transform.localPosition = localPoint - localAxis * (length * GripFraction) - wand.transform.localRotation * bottom;
            return wand.transform;
        }

        /// <summary>Where the shaft passes through the closed lead fist, and the shaft's direction, in hand space.</summary>
        internal static void HandGrip(string characterFbx, string side, out Vector3 point, out Vector3 axis)
        {
            using (var rig = new PoseRig(characterFbx))
            {
                point = GripLocal(rig, side);
                axis = AxisLocal(rig, side);
            }
        }

        /// <summary>The wand tip in the hand's space (for effects that follow the tip).</summary>
        internal static Vector3 WandTipLocal(string characterFbx, string side, float length)
        {
            using (var rig = new PoseRig(characterFbx))
                return GripLocal(rig, side) + AxisLocal(rig, side) * (length * (1f - GripFraction));
        }

        /// <summary>Centre of the palm in the hand's space (for effects drawn by the open hand).</summary>
        internal static Vector3 PalmLocal(string characterFbx, string side)
        {
            using (var rig = new PoseRig(characterFbx))
                return rig.FingerLocal(side) * 0.045f - rig.PalmLocal(side) * 0.012f;
        }

        // ---------------------------------------------------------------- A1: Amelia's bow

        /// <summary>
        /// Wand upright at her right side. The left hand rises to her chest, a small bow led by the chest with the
        /// head following, then she straightens with the chin a little high and holds, breathing.
        /// </summary>
        static void AmeliaBow(PoseRig rig, float t)
        {
            float raise = Ease(t, 0.35f, 1.2f);
            float bow = Ease(t, 1.05f, 1.85f) - Ease(t, 2.15f, 3.0f);
            float headBow = Ease(t, 1.15f, 2.0f) - Ease(t, 2.25f, 3.15f);
            float proud = Ease(t, 2.3f, 3.2f);
            float inhale = Mathf.Sin(Mathf.Clamp01((t - 0.3f) / 1.1f) * Mathf.PI);

            // The bow sits the hips back a touch and softens the knees.
            Alive(rig, t, Ease(t, 2.6f, 3.6f), new Vector3(0f, -0.008f * bow, -0.012f * bow), new Vector3(-2f * bow, 0f, 0f));
            rig.Turn("Spine", X, 5f * bow - 1f * proud);
            rig.Turn("Chest", X, 6f * bow - 1.5f * proud - 1.2f * inhale);
            rig.Turn("UpperChest", X, 6f * bow - 2f * proud - 1.2f * inhale);
            rig.Turn("Neck", X, 6f * headBow - 2f * proud);
            rig.Turn("Head", X, 12f * headBow - 6f * proud);

            WandAtSide(rig, R, 0.02f * bow);

            // Left hand: hanging relaxed, then swings forward and up onto the chest, riding the bow.
            var upperChest = rig["UpperChest"];
            ArmDown(rig, L, out var restWrist, out var restHand);
            var chestFinger = upperChest.rotation * ChestFinger;
            var chestFacing = upperChest.rotation * ChestFacing;
            var chestHand = rig.HandRotation(L, chestFinger, chestFacing);
            var chestPalm = upperChest.position + upperChest.rotation * ChestPalmOffset;
            var chestWrist = chestPalm - chestFinger * 0.036f - chestFacing * 0.012f;
            FreeArm(rig, L, restWrist, restHand, chestWrist, chestHand, raise, new Vector3(-0.02f, 0f, 0.045f),
                new Vector3(-0.35f, -0.3f, -0.08f), new Vector3(-0.3f, -0.28f, 0.05f), 0.3f, 0.12f);
        }

        // Palm on the sternum under the collar bow, fingers toward the right shoulder. In UpperChest's bind frame,
        // which for these Tripo rigs is world-aligned up to a fixed rotation baked into the offsets below.
        static Vector3 ChestPalmOffset, ChestFinger, ChestFacing;

        static void InitChest(string fbx)
        {
            using (var rig = new PoseRig(fbx))
            {
                var uc = rig["UpperChest"];
                float front = ChestFront(rig, -0.025f, 0.715f);
                var inv = Quaternion.Inverse(uc.rotation);
                ChestPalmOffset = inv * (new Vector3(-0.02f, 0.712f, front + 0.004f) - uc.position);
                ChestFinger = inv * new Vector3(0.78f, 0.55f, -0.12f).normalized;
                ChestFacing = inv * new Vector3(0.05f, 0f, -1f).normalized;
            }
        }

        /// <summary>Most forward (z) point of the body surface near (x, y), from the bind-pose mesh.</summary>
        static float ChestFront(PoseRig rig, float x, float y)
        {
            var smr = rig.Root.GetComponentInChildren<SkinnedMeshRenderer>();
            var mesh = new Mesh();
            smr.BakeMesh(mesh, true);
            var m = smr.transform.localToWorldMatrix;
            float front = 0.05f;
            foreach (var v in mesh.vertices)
            {
                var w = m.MultiplyPoint3x4(v);
                if (Mathf.Abs(w.x - x) < 0.025f && Mathf.Abs(w.y - y) < 0.02f) front = Mathf.Max(front, w.z);
            }
            Object.DestroyImmediate(mesh);
            return front;
        }

        // ---------------------------------------------------------------- A2: Amelia draws the rune

        /// <summary>
        /// Left palm comes up in front of her chest, fingers spread, and traces one slow circle in the air (the rune),
        /// then pushes gently forward and holds while the rune hangs there. Wand stays upright at her right side.
        /// </summary>
        static void AmeliaRune(PoseRig rig, float t)
        {
            float lift = Ease(t, 0.15f, 0.85f);
            float trace = Ease(t, 0.9f, 2.6f);
            float push = Ease(t, 2.6f, 3.0f) - 0.4f * Ease(t, 3.0f, 3.6f);
            float focus = Ease(t, 0.3f, 1.0f);

            Alive(rig, t, 0.6f + 0.4f * Ease(t, 3f, 4f), new Vector3(0f, 0f, 0.004f * push), new Vector3(0f, -2f * focus, 0f));
            rig.Turn("Chest", X, 1.5f * focus + 1.5f * push);
            rig.Turn("UpperChest", Y, -3f * focus);
            rig.Turn("Neck", X, 2f * focus);
            rig.Turn("Head", X, 3f * focus - 2f * push);
            rig.Turn("Head", Y, -5f * focus);

            WandAtSide(rig, R);

            // The palm faces forward and draws a clockwise circle (as she sees it) starting at the top.
            // Below the chin and a little out to her left, so the palm never hides her face.
            var center = new Vector3(-0.1f, 0.67f, 0.235f + 0.02f * push);
            float angle = Mathf.PI * 0.5f - trace * Mathf.PI * 2f;
            const float radius = 0.07f;
            var onCircle = center + new Vector3(Mathf.Cos(angle) * radius * -1f, Mathf.Sin(angle) * radius, 0f) * Ease(t, 0.75f, 0.95f);
            var drawHand = rig.HandRotation(L, new Vector3(-0.08f, 1f, 0.12f).normalized, new Vector3(0.05f, 0.08f, 1f).normalized);
            ArmDown(rig, L, out var restWrist, out var restHand);
            // Wrist sits below the palm centre.
            var drawWrist = onCircle - (drawHand * rig.FingerLocal(L)) * 0.04f;
            FreeArm(rig, L, restWrist, restHand, drawWrist, drawHand, lift, new Vector3(-0.03f, -0.02f, 0.05f),
                new Vector3(-0.35f, -0.3f, -0.08f), new Vector3(-0.3f, -0.35f, -0.05f), 0.3f, 0.04f);
        }

        // ---------------------------------------------------------------- ready stance (both)

        /// <summary>
        /// From the wand at her side to the game's two-handed stance: lead fist on the shaft, support fist just below,
        /// wand pointed forward and up. Held with breathing so the cut into gameplay matches.
        /// </summary>
        static void Ready(PoseRig rig, float t, string lead)
        {
            float u = Ease(t, 0.1f, 0.85f);
            float set = Ease(t, 0.55f, 1.0f);
            Alive(rig, t, 0.5f, new Vector3(0f, -0.006f * u, 0f), new Vector3(0f, -2f * Side(lead) * u, 0f));
            rig.Turn("Spine", X, 2f * u);
            rig.Turn("Chest", X, 1.5f * u);
            rig.Turn("Head", X, -1.5f * u);
            ReadyArms(rig, lead, u, set);
        }

        static void ReadyArms(PoseRig rig, string lead, float u, float set)
        {
            string support = lead == R ? L : R;
            GripPose(lead, out var p0, out var a0, out var k0, out var pole0);
            var a1 = M(new Vector3(-0.06f, 0.42f, 1f), lead).normalized;
            var p1 = M(new Vector3(0.03f, 0.665f, 0.2f), lead);
            var k1 = M(new Vector3(0.35f, 1f, 0f), lead);
            var pole1 = M(new Vector3(0.32f, -0.35f, -0.05f), lead);
            // Lift on an arc so the wand doesn't sweep through her legs.
            var point = Vector3.Lerp(p0, p1, u) + M(new Vector3(0.03f, 0f, 0.06f), lead) * Mathf.Sin(u * Mathf.PI);
            var axis = Vector3.Slerp(a0, a1, u);
            Grip(rig, lead, point, axis, Vector3.Slerp(k0, k1, u), Vector3.Lerp(pole0, pole1, u));

            // Support hand joins the shaft once it is up.
            ArmDown(rig, support, out var restWrist, out var restHand);
            var gripRot = GripRotation(rig, support, axis, M(new Vector3(-0.2f, -1f, 0.3f), lead));
            var supportPoint = point - axis * 0.075f;
            var supportWrist = supportPoint - gripRot * GripLocal(rig, support);
            FreeArm(rig, support, restWrist, restHand, supportWrist, gripRot, set, M(new Vector3(0.02f, -0.02f, 0.05f), lead),
                M(new Vector3(-0.35f, -0.3f, -0.08f), lead), M(new Vector3(-0.3f, -0.38f, 0.0f), lead), 0.3f, 0.2f);
            ShaftLine(rig, lead, out var held, out var heldAxis);
            rig.Wrap(support, held, heldAxis, ShaftContact, set);
        }

        // ---------------------------------------------------------------- in-game idle

        internal const float IdleLength = 6.4f;

        /// <summary>
        /// The game's idle (played by CasterPose under the aiming IK): the two-handed stance with a slow weight sway
        /// and breathing. Every motion is a whole number of cycles over the clip, so it loops without a seam.
        /// Hips only rotate (CasterPose keeps rest positions); the legs are re-planted by IK.
        /// </summary>
        static void CasterIdle(PoseRig rig, float t)
        {
            float w = t / IdleLength * Mathf.PI * 2f;
            float sway = Mathf.Sin(w);
            float breath = Mathf.Sin(2f * w);
            rig.Hips(Vector3.zero, new Vector3(0.4f * Mathf.Sin(w + 1.3f), -2f + 1.0f * sway, -0.9f * sway));
            rig.Turn("Spine", X, 2f);
            rig.Turn("Spine", Z, 0.6f * sway);
            rig.Turn("Chest", X, 1.5f - 0.7f * breath);
            rig.Turn("UpperChest", X, -0.8f * breath);
            rig.Turn("Left_Shoulder", Z, -0.7f * breath);
            rig.Turn("Right_Shoulder", Z, 0.7f * breath);
            rig.Turn("Head", X, -1.5f + 0.6f * Mathf.Sin(2f * w + 0.4f));
            rig.Turn("Head", Y, 1.2f * Mathf.Sin(w + 0.7f));
            ReadyArms(rig, R, 1f, 1f);
        }

        // ---------------------------------------------------------------- N1: Nox, wand on the shoulder

        /// <summary>
        /// Raises the wand beside her face, right hand to the hip, weight onto one leg and a cocky head tilt;
        /// a little flick of the wand, then holds.
        /// </summary>
        static void NoxShoulder(PoseRig rig, float t)
        {
            float up = Ease(t, 0.2f, 0.85f);
            float hip = Ease(t, 0.4f, 1.1f);
            float weight = Ease(t, 0.5f, 1.4f);
            float tilt = Ease(t, 0.95f, 1.65f) + 0.35f * (Ease(t, 2.3f, 2.6f) - Ease(t, 2.6f, 3.1f));
            float bounce = Ease(t, 1.9f, 2.05f) - Ease(t, 2.05f, 2.35f);

            Alive(rig, t, Ease(t, 1.6f, 2.6f), new Vector3(0.018f * weight, -0.004f * weight, 0f), new Vector3(0f, -4f * weight, 3.5f * weight));
            rig.Turn("Spine", Z, -2f * weight);
            rig.Turn("Chest", Z, -1.5f * weight);
            rig.Turn("Chest", X, -2f * weight);
            rig.Turn("UpperChest", Y, 3f * weight);
            rig.Turn("Head", Z, 8f * tilt);
            rig.Turn("Head", X, -3f * tilt);

            // Wand raised beside her face like the concept's taunt, tip leaning out; it flicks out and back once.
            GripPose(L, out var p0, out var a0, out var k0, out var pole0);
            var p1 = new Vector3(-0.15f, 0.705f + 0.01f * bounce, 0.1f);
            var a1 = Quaternion.AngleAxis(9f * bounce, Vector3.forward) * new Vector3(-0.34f, 1f, 0.06f).normalized;
            var k1 = new Vector3(-0.15f, 0f, 1f);
            var pole1 = new Vector3(-0.3f, -0.4f, -0.12f);
            var point = Vector3.Lerp(p0, p1, up) + new Vector3(-0.04f, 0f, 0.05f) * Mathf.Sin(up * Mathf.PI);
            Grip(rig, L, point, Vector3.Slerp(a0, a1, up), Vector3.Slerp(k0, k1, up), Vector3.Lerp(pole0, pole1, up));

            ArmDown(rig, R, out var restWrist, out var restHand);
            HandOnHip(rig, R, out var hipWrist, out var hipHand);
            FreeArm(rig, R, restWrist, restHand, hipWrist, hipHand, hip, new Vector3(0.04f, 0.01f, -0.02f),
                new Vector3(0.35f, -0.3f, -0.08f), new Vector3(0.4f, -0.12f, -0.28f), 0.3f, 0.45f);
        }

        // ---------------------------------------------------------------- N2: Nox's flourish

        /// <summary>
        /// Wand up beside her face, a quick low sweep out to the side and across, ending pointed straight ahead;
        /// right hand stays on the hip. Chest follows the sweep, head stays on the target.
        /// </summary>
        static void NoxFlourish(PoseRig rig, float t)
        {
            float raise = Ease(t, 0.15f, 0.65f);
            float sweep = Ease(t, 0.8f, 1.45f);
            float settle = Ease(t, 1.45f, 1.9f);

            Alive(rig, t, 0.5f + 0.5f * Ease(t, 2f, 3f), new Vector3(0.012f, -0.006f * sweep, 0.006f * sweep), new Vector3(0f, -3f, 2f));
            float twist = Mathf.Sin(sweep * Mathf.PI * 2f) * (1f - settle);
            rig.Turn("Spine", Y, -5f * twist);
            rig.Turn("Chest", Y, -6f * twist);
            rig.Turn("Chest", X, 2f * sweep);
            rig.Turn("Head", Y, 8f * twist);
            rig.Turn("Head", Z, 4f * (1f - sweep) * raise);
            rig.Turn("Head", X, 2f * sweep);

            GripPose(L, out var p0, out var a0, out var k0, out var pole0);
            // Catmull-Rom through: by the face, low and out, across the front, pointing ahead.
            var pts = new[]
            {
                new Vector3(-0.09f, 0.79f, 0.1f), new Vector3(-0.2f, 0.63f, 0.17f),
                new Vector3(-0.05f, 0.67f, 0.25f), new Vector3(-0.1f, 0.75f, 0.255f),
            };
            var axes = new[]
            {
                new Vector3(-0.1f, 1f, -0.1f).normalized, new Vector3(-0.9f, 0.2f, 0.35f).normalized,
                new Vector3(0.35f, 0.3f, 1f).normalized, new Vector3(-0.05f, 0.33f, 1f).normalized,
            };
            var knucks = new[] { new Vector3(-0.2f, 0f, 1f), new Vector3(0f, 1f, 0.2f), new Vector3(-0.2f, 1f, 0f), new Vector3(-0.35f, 1f, 0f) };
            float s = sweep * 3f;
            int seg = Mathf.Min(2, Mathf.FloorToInt(s));
            float f = s - seg;
            var spline = CatmullRom(pts[Mathf.Max(0, seg - 1)], pts[seg], pts[seg + 1], pts[Mathf.Min(3, seg + 2)], f);
            var axisS = Vector3.Slerp(axes[seg], axes[seg + 1], f);
            var knuckS = Vector3.Slerp(knucks[seg], knucks[seg + 1], f);
            var point = Vector3.Lerp(p0, spline, raise) + new Vector3(-0.03f, 0f, 0.05f) * Mathf.Sin(raise * Mathf.PI);
            var axis = Vector3.Slerp(a0, axisS, raise);
            var pole = Vector3.Lerp(pole0, new Vector3(-0.38f, -0.25f, -0.05f), raise);
            Grip(rig, L, point, axis, Vector3.Slerp(k0, knuckS, raise), pole);

            HandOnHip(rig, R, out var hipWrist, out var hipHand);
            rig.Arm(R, hipWrist, rig["Right_UpperArm"].position + new Vector3(0.4f, -0.12f, -0.28f), hipHand);
            rig.Curl(R, 0.45f, 0.2f);
        }

        // ---------------------------------------------------------------- Period 2: the lights go out

        // Wand clutched upright in front of the chest, both hands on it (end of A3, start of A4).
        static readonly Vector3 ClutchPoint = new Vector3(0.085f, 0.64f, 0.15f);
        static readonly Vector3 ClutchAxis = new Vector3(0.2f, 1f, 0.1f).normalized;
        static readonly Vector3 ClutchKnuckles = new Vector3(-0.3f, 0.1f, 1f);
        static readonly Vector3 ClutchPole = new Vector3(0.3f, -0.3f, -0.2f);

        static void ClutchSupport(PoseRig rig, Vector3 point, Vector3 axis, float u, float release = 0f)
        {
            ArmDown(rig, L, out var restWrist, out var restHand);
            var gripRot = GripRotation(rig, L, axis, new Vector3(0.3f, 0.1f, 1f));
            var gripWrist = point - axis * 0.07f - gripRot * GripLocal(rig, L);
            float k = u * (1f - release);
            FreeArm(rig, L, restWrist, restHand, gripWrist, gripRot, k, new Vector3(-0.02f, 0f, 0.04f),
                new Vector3(-0.35f, -0.3f, -0.08f), new Vector3(-0.3f, -0.35f, -0.15f), 0.3f, 0.2f);
            ShaftLine(rig, R, out var held, out var heldAxis);
            rig.Wrap(L, held, heldAxis, ShaftContact, k);
        }

        /// <summary>
        /// The lights cut: she flinches, shoulders up, and pulls the wand to her chest with both hands, then looks
        /// left and right into the dark.
        /// </summary>
        static void AmeliaFlinch(PoseRig rig, float t)
        {
            float flinch = Ease(t, 0.05f, 0.3f);
            float settle = Ease(t, 0.3f, 0.9f);
            float clutch = Ease(t, 0.1f, 0.55f);
            float lookL = Ease(t, 0.9f, 1.5f) - Ease(t, 1.8f, 2.4f);
            float lookR = Ease(t, 1.9f, 2.5f) - Ease(t, 2.9f, 3.5f);
            float tense = flinch * (1f - 0.5f * settle);
            Alive(rig, t, 0.4f, new Vector3(0f, -0.012f * tense - 0.004f * clutch, -0.008f * tense), new Vector3(0f, -5f * lookL + 5f * lookR, 0f));
            rig.Turn("Spine", X, -3f * tense + 2f * clutch);
            rig.Turn("Chest", X, -3f * tense);
            rig.Turn("UpperChest", Y, -8f * lookL + 8f * lookR);
            rig.Turn("Left_Shoulder", Z, -6f * tense);
            rig.Turn("Right_Shoulder", Z, 6f * tense);
            rig.Turn("Neck", X, -3f * tense);
            rig.Turn("Head", X, -4f * tense);
            rig.Turn("Head", Y, -22f * lookL + 22f * lookR);

            GripPose(R, out var p0, out var a0, out var k0, out var pole0);
            var point = Vector3.Lerp(p0, ClutchPoint, clutch) + new Vector3(-0.02f, 0f, 0.04f) * Mathf.Sin(clutch * Mathf.PI);
            var axis = Vector3.Slerp(a0, ClutchAxis, clutch);
            Grip(rig, R, point, axis, Vector3.Slerp(k0, ClutchKnuckles, clutch), Vector3.Lerp(pole0, ClutchPole, clutch));
            ClutchSupport(rig, point, axis, Ease(t, 0.15f, 0.6f));
        }

        /// <summary>
        /// Seeing Nox's light, she breathes in, lets her shoulders down, and lifts the wand in front of her face like a
        /// torch, chin up; the left hand lets go.
        /// </summary>
        static void AmeliaSteady(PoseRig rig, float t)
        {
            float breathIn = Ease(t, 0.1f, 0.7f) - Ease(t, 0.8f, 1.3f);
            float relax = Ease(t, 0.2f, 1.0f);
            float tense = 0.5f * (1f - relax);
            float raise = Ease(t, 0.9f, 1.7f);
            float resolve = Ease(t, 1.4f, 2.1f);
            Alive(rig, t, 0.4f + 0.6f * Ease(t, 2f, 3f), new Vector3(0f, -0.012f * tense - 0.004f, -0.008f * tense), Vector3.zero);
            rig.Turn("Spine", X, -3f * tense + 2f * (1f - raise) - 1f * resolve);
            rig.Turn("Chest", X, -3f * tense - 3f * breathIn - 1f * resolve);
            rig.Turn("Left_Shoulder", Z, -6f * tense - 3f * breathIn);
            rig.Turn("Right_Shoulder", Z, 6f * tense + 3f * breathIn);
            rig.Turn("Neck", X, -3f * tense);
            rig.Turn("Head", X, -4f * tense - 4f * resolve);

            // Held out to the right of her face, tip forward and up, so the light sits in front of her.
            var torch = new Vector3(0.12f, 0.8f, 0.2f);
            var torchAxis = new Vector3(0.55f, 0.3f, 0.75f).normalized;
            var point = Vector3.Lerp(ClutchPoint, torch, raise) + new Vector3(0.02f, 0f, 0.05f) * Mathf.Sin(raise * Mathf.PI);
            var axis = Vector3.Slerp(ClutchAxis, torchAxis, raise);
            // Knuckles must stay across the shaft: with the wand pointed forward they face up and out.
            Grip(rig, R, point, axis, Vector3.Slerp(ClutchKnuckles, new Vector3(0.3f, 1f, -0.3f), raise),
                Vector3.Lerp(ClutchPole, new Vector3(0.3f, -0.35f, -0.15f), raise));
            ClutchSupport(rig, point, axis, 1f, Ease(t, 0.75f, 1.4f));
        }

        /// <summary>
        /// Unbothered by the dark: she lifts the wand above her shoulder like a torch, sweeps the room left and right
        /// with it, and shrugs. Right hand stays on the hip.
        /// </summary>
        static void NoxTorch(PoseRig rig, float t)
        {
            float up = Ease(t, 0.2f, 0.9f);
            float scanL = Ease(t, 1.1f, 1.8f) - Ease(t, 2.0f, 2.8f);
            float scanR = Ease(t, 2.1f, 2.9f) - Ease(t, 3.1f, 3.7f);
            float shrug = Ease(t, 3.7f, 3.9f) - Ease(t, 3.95f, 4.25f);
            Alive(rig, t, Ease(t, 1f, 2f), new Vector3(0.012f, 0f, 0f), new Vector3(0f, -4f * scanL + 4f * scanR, 2f));
            rig.Turn("UpperChest", Y, -7f * scanL + 7f * scanR);
            rig.Turn("Head", Y, -20f * scanL + 20f * scanR);
            rig.Turn("Head", X, -3f * up + 2f * shrug);
            rig.Turn("Head", Z, 5f * shrug);
            rig.Turn("Left_Shoulder", Z, -7f * shrug);
            rig.Turn("Right_Shoulder", Z, 7f * shrug);

            GripPose(L, out var p0, out var a0, out var k0, out var pole0);
            // Raised to shoulder height, tip out ahead and up: the light hangs in front of her, not over her head.
            var p1 = new Vector3(-0.15f, 0.86f, 0.12f) + new Vector3(-0.05f * scanL + 0.05f * scanR, 0f, 0f);
            var a1 = (new Vector3(-0.55f, 0.3f, 0.75f) + new Vector3(-0.3f * scanL + 0.3f * scanR, 0f, 0f)).normalized;
            var point = Vector3.Lerp(p0, p1, up) + new Vector3(-0.04f, 0f, 0.05f) * Mathf.Sin(up * Mathf.PI);
            Grip(rig, L, point, Vector3.Slerp(a0, a1, up), Vector3.Slerp(k0, new Vector3(-0.3f, 1f, -0.3f), up),
                Vector3.Lerp(pole0, new Vector3(-0.35f, -0.25f, -0.15f), up));

            HandOnHip(rig, R, out var hipWrist, out var hipHand);
            rig.Arm(R, hipWrist, rig["Right_UpperArm"].position + new Vector3(0.4f, -0.12f, -0.28f), hipHand);
            rig.Curl(R, 0.45f, 0.2f);
        }

        static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        static PeriodPoseClips() => InitChest(AmeliaFbx);
    }
}
