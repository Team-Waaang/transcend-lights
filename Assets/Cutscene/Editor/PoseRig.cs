using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AiHackathon.Cutscene.Editor
{
    /// <summary>
    /// Keyframes a Tripo rig from code. Each frame: <see cref="Reset"/> to the bind pose, bend bones about
    /// character-space axes, place hands with two-bone IK, curl fingers, then <see cref="Key"/>. <see cref="Bake"/>
    /// turns every bone that moved into Generic local-rotation curves.
    /// The instance stands at the origin facing +Z with its right side on +X, which is how Tripo exports.
    /// </summary>
    internal sealed class PoseRig : IDisposable
    {
        public readonly GameObject Root;
        readonly Dictionary<string, Transform> bones = new Dictionary<string, Transform>();
        readonly List<Transform> order = new List<Transform>();
        readonly Dictionary<Transform, Quaternion> bind = new Dictionary<Transform, Quaternion>();
        readonly Dictionary<Transform, List<Keyframe>[]> keys = new Dictionary<Transform, List<Keyframe>[]>();
        readonly Dictionary<string, Vector3> fingerLocal = new Dictionary<string, Vector3>();
        readonly Dictionary<string, Vector3> palmLocal = new Dictionary<string, Vector3>();
        readonly Dictionary<string, float> knuckle = new Dictionary<string, float>();
        readonly Transform hips;
        readonly Vector3 hipsBindLocal, hipsBindWorld;
        readonly Quaternion hipsBindRotation;
        readonly Dictionary<string, (Vector3 position, Quaternion rotation, Vector3 knee)> feet = new Dictionary<string, (Vector3, Quaternion, Vector3)>();
        readonly List<Keyframe>[] hipsKeys = { new List<Keyframe>(), new List<Keyframe>(), new List<Keyframe>() };

        static readonly string[] Fingers = { "Index", "Middle", "Ring", "Pinky" };
        static readonly string[] Joints = { "Proximal", "Intermediate", "Distal" };
        static readonly float[] FingerBend = { 70f, 95f, 65f };
        static readonly float[] ThumbBend = { 25f, 35f, 40f };

        public PoseRig(string fbxPath)
        {
            Root = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath));
            Root.hideFlags = HideFlags.HideAndDontSave;
            Root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            foreach (var t in Root.GetComponentsInChildren<Transform>(true))
            {
                if (t == Root.transform || t.GetComponent<Renderer>() != null) continue;
                bones[t.name] = t;
                order.Add(t);
                bind[t] = t.localRotation;
            }
            foreach (var side in new[] { "Left_", "Right_" })
            {
                var hand = this[side + "Hand"];
                var toLocal = Quaternion.Inverse(hand.rotation);
                fingerLocal[side] = (toLocal * (this[side + "MiddleProximal"].position - hand.position)).normalized;
                knuckle[side] = (this[side + "MiddleProximal"].position - hand.position).magnitude;
                // Tripo binds in a T-pose with both palms facing the floor.
                palmLocal[side] = toLocal * Vector3.down;
                feet[side] = (this[side + "Foot"].position, this[side + "Foot"].rotation, this[side + "LowerLeg"].position);
            }
            hips = this["Hips"];
            hipsBindLocal = hips.localPosition;
            hipsBindWorld = hips.position;
            hipsBindRotation = hips.rotation;
        }

        public Vector3 FingerLocal(string side) => fingerLocal[side];
        public Vector3 PalmLocal(string side) => palmLocal[side];
        /// <summary>Wrist to the middle knuckle in the bind pose (model units).</summary>
        public float KnuckleDistance(string side) => knuckle[side];

        /// <summary>
        /// Moves and turns the hips (world offset from bind, world-space euler), then bends the legs with IK so both feet
        /// stay planted where the bind pose put them. Call before any spine <see cref="Turn"/>.
        /// </summary>
        public void Hips(Vector3 offset, Vector3 euler)
        {
            hips.position = hipsBindWorld + offset;
            hips.rotation = Quaternion.Euler(euler) * hipsBindRotation;
            foreach (var side in new[] { "Left_", "Right_" })
            {
                var foot = feet[side];
                Waaang.ArmIk.Solve(this[side + "UpperLeg"], this[side + "LowerLeg"], this[side + "Foot"], foot.position,
                    foot.knee + Vector3.forward * 0.4f);
                this[side + "Foot"].rotation = foot.rotation;
            }
        }

        public Transform this[string name] => bones[name];

        public void Reset()
        {
            foreach (var t in order) t.localRotation = bind[t];
            hips.localPosition = hipsBindLocal;
        }

        /// <summary>Rotates a bone (and everything under it) about a world/character-space axis.</summary>
        public void Turn(string bone, Vector3 axis, float degrees)
        {
            if (Mathf.Abs(degrees) < 0.0001f) return;
            var t = this[bone];
            t.rotation = Quaternion.AngleAxis(degrees, axis) * t.rotation;
        }

        /// <summary>World rotation that points the hand's fingers along <paramref name="finger"/> with the palm facing <paramref name="palm"/>.</summary>
        public Quaternion HandRotation(string side, Vector3 finger, Vector3 palm)
        {
            var from = Quaternion.LookRotation(fingerLocal[side], -palmLocal[side]);
            return Quaternion.LookRotation(finger, -palm) * Quaternion.Inverse(from);
        }

        /// <summary>Two-bone IK of the upper/lower arm so the wrist reaches <paramref name="wrist"/>, then sets the hand's world rotation.</summary>
        public void Arm(string side, Vector3 wrist, Vector3 pole, Quaternion hand)
        {
            Waaang.ArmIk.Solve(this[side + "UpperArm"], this[side + "LowerArm"], this[side + "Hand"], wrist, pole);
            this[side + "Hand"].rotation = hand;
        }

        public Vector3 Palm(string side) => this[side + "Hand"].rotation * palmLocal[side];
        public Vector3 FingerDir(string side) => this[side + "Hand"].rotation * fingerLocal[side];

        /// <summary>Curls the four fingers (0 = flat, 1 = fist) and the thumb toward the palm.</summary>
        public void Curl(string side, float fingers, float thumb)
        {
            foreach (var f in Fingers) CurlChain(side, side + f, fingers, FingerBend);
            CurlChain(side, side + "Thumb", thumb, ThumbBend);
        }

        void CurlChain(string side, string prefix, float amount, float[] bend)
        {
            // One hinge axis for the whole finger, taken before it bends: once the first joint curls the finger
            // points almost along the palm normal and a per-joint cross product would collapse.
            var first = this[prefix + Joints[0]];
            var next = this[prefix + Joints[1]];
            var axis = Vector3.Cross(next.position - first.position, Palm(side));
            if (axis.sqrMagnitude < 1e-10f) return;
            axis.Normalize();
            for (int j = 0; j < Joints.Length; j++)
            {
                var bone = this[prefix + Joints[j]];
                bone.rotation = Quaternion.AngleAxis(bend[j] * amount, axis) * bone.rotation;
            }
        }

        static readonly float[] FingerWrapMax = { 95f, 105f, 80f };
        static readonly float[] ThumbWrapMax = { 35f, 45f, 55f };

        /// <summary>
        /// Closes the hand around a shaft: each finger joint (and the thumb's) bends toward the palm until the next
        /// joint meets the cylinder of <paramref name="radius"/> about the line through <paramref name="point"/> along
        /// <paramref name="axis"/>, so the fingers wrap the shaft wherever it lies instead of folding to a fixed fist.
        /// <paramref name="amount"/> blends from the current curl (0) to the full wrap (1).
        /// </summary>
        public void Wrap(string side, Vector3 point, Vector3 axis, float radius, float amount = 1f, float thumb = 1f)
        {
            if (amount <= 0f) return;
            axis.Normalize();
            foreach (var f in Fingers) WrapChain(side, side + f, point, axis, radius, FingerWrapMax, amount);
            WrapChain(side, side + "Thumb", point, axis, radius, ThumbWrapMax, amount * thumb);
        }

        void WrapChain(string side, string prefix, Vector3 point, Vector3 axis, float radius, float[] max, float amount)
        {
            var first = this[prefix + Joints[0]];
            var next = this[prefix + Joints[1]];
            var hinge = Vector3.Cross(next.position - first.position, Palm(side));
            if (hinge.sqrMagnitude < 1e-10f) return;
            hinge.Normalize();
            for (int j = 0; j < Joints.Length; j++)
            {
                var bone = this[prefix + Joints[j]];
                var child = j + 1 < Joints.Length ? this[prefix + Joints[j + 1]]
                    : bones.TryGetValue(prefix + "DistalEnd", out var end) ? end : null;
                var start = bone.rotation;
                float best = max[j];
                if (child != null)
                {
                    // Already touching (or inside): leave this joint straight rather than fold through the shaft.
                    if (DistanceToLine(child.position, point, axis) <= radius) best = 0f;
                    else
                        for (float a = 2f; a <= max[j]; a += 2f)
                        {
                            bone.rotation = Quaternion.AngleAxis(a, hinge) * start;
                            if (DistanceToLine(child.position, point, axis) <= radius) { best = a; break; }
                        }
                }
                bone.rotation = Quaternion.AngleAxis(best * amount, hinge) * start;
            }
        }

        static float DistanceToLine(Vector3 p, Vector3 point, Vector3 axis)
        {
            var d = p - point;
            return (d - axis * Vector3.Dot(d, axis)).magnitude;
        }

        public void Key(float time)
        {
            var hp = hips.localPosition;
            hipsKeys[0].Add(new Keyframe(time, hp.x));
            hipsKeys[1].Add(new Keyframe(time, hp.y));
            hipsKeys[2].Add(new Keyframe(time, hp.z));
            foreach (var t in order)
            {
                if (!keys.TryGetValue(t, out var curves))
                {
                    curves = new[] { new List<Keyframe>(), new List<Keyframe>(), new List<Keyframe>(), new List<Keyframe>() };
                    keys[t] = curves;
                }
                var q = t.localRotation;
                if (curves[0].Count > 0)
                {
                    // Keep neighbouring keys on the same hemisphere so the per-component curves never flip.
                    var last = new Quaternion(curves[0][curves[0].Count - 1].value, curves[1][curves[1].Count - 1].value,
                        curves[2][curves[2].Count - 1].value, curves[3][curves[3].Count - 1].value);
                    if (Quaternion.Dot(last, q) < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                }
                curves[0].Add(new Keyframe(time, q.x));
                curves[1].Add(new Keyframe(time, q.y));
                curves[2].Add(new Keyframe(time, q.z));
                curves[3].Add(new Keyframe(time, q.w));
            }
        }

        /// <summary>Writes curves for every bone that moved away from its bind rotation at some key.</summary>
        public AnimationClip Bake(string name)
        {
            var clip = new AnimationClip { name = name, frameRate = 30f };
            string[] props = { "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w" };
            foreach (var pair in keys)
            {
                var b = bind[pair.Key];
                bool moved = false;
                var curves = pair.Value;
                for (int i = 0; i < curves[0].Count && !moved; i++)
                {
                    var q = new Quaternion(curves[0][i].value, curves[1][i].value, curves[2][i].value, curves[3][i].value);
                    moved = Quaternion.Angle(q, b) > 0.01f;
                }
                // Every bone gets a curve, still ones too (two keys): CasterPose layers its breathing on top of
                // each sampled frame, and a bone the clip leaves alone would keep the previous frame's turn and
                // drift (the idle's neck wobbled that way).
                string path = AnimationUtility.CalculateTransformPath(pair.Key, Root.transform);
                for (int c = 0; c < 4; c++)
                {
                    var curve = new AnimationCurve(Reduce(curves[c], 0.00015f));
                    AutoTangents(curve, curves[c]);
                    AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), props[c]), curve);
                }
            }
            bool hipsMoved = false;
            for (int i = 0; i < hipsKeys[0].Count && !hipsMoved; i++)
                hipsMoved = (new Vector3(hipsKeys[0][i].value, hipsKeys[1][i].value, hipsKeys[2][i].value) - hipsBindLocal).sqrMagnitude > 1e-8f;
            if (hipsMoved)
            {
                // The Hips' parent (Armature) is scaled 0.01-ish in some exports, so reduce in its own units.
                string path = AnimationUtility.CalculateTransformPath(hips, Root.transform);
                string[] axes = { "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z" };
                float scale = hips.parent.lossyScale.x;
                for (int c = 0; c < 3; c++)
                {
                    var curve = new AnimationCurve(Reduce(hipsKeys[c], 0.00015f / Mathf.Max(1e-6f, scale)));
                    AutoTangents(curve, hipsKeys[c]);
                    AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), axes[c]), curve);
                }
            }
            clip.EnsureQuaternionContinuity();
            return clip;
        }

        // Unity's clamped-auto tangents: smooth through every key without overshoot. (SmoothTangents on sparse keys
        // left flat spots, which made slow idle motion stop and start.)
        static void AutoTangents(AnimationCurve curve, List<Keyframe> source)
        {
            for (int k = 0; k < curve.length; k++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, k, AnimationUtility.TangentMode.ClampedAuto);
                AnimationUtility.SetKeyRightTangentMode(curve, k, AnimationUtility.TangentMode.ClampedAuto);
            }
            // Clamped ends would stop the motion at the first and last key; give them the slope of the full-rate
            // samples instead, so a looping clip (the idle) keeps moving through its seam.
            int n = source.Count;
            if (curve.length < 2 || n < 2) return;
            float start = (source[1].value - source[0].value) / Mathf.Max(1e-5f, source[1].time - source[0].time);
            float end = (source[n - 1].value - source[n - 2].value) / Mathf.Max(1e-5f, source[n - 1].time - source[n - 2].time);
            AnimationUtility.SetKeyRightTangentMode(curve, 0, AnimationUtility.TangentMode.Free);
            AnimationUtility.SetKeyLeftTangentMode(curve, curve.length - 1, AnimationUtility.TangentMode.Free);
            var first = curve[0]; first.outTangent = start; curve.MoveKey(0, first);
            var last = curve[curve.length - 1]; last.inTangent = end; curve.MoveKey(curve.length - 1, last);
        }

        /// <summary>Drops keys that a straight line between their kept neighbours already reproduces.</summary>
        static Keyframe[] Reduce(List<Keyframe> src, float tolerance = 0.0006f)
        {
            if (src.Count <= 2) return src.ToArray();
            var kept = new List<Keyframe> { src[0] };
            for (int i = 1; i < src.Count - 1; i++)
            {
                var a = kept[kept.Count - 1];
                var b = src[i + 1];
                // Every skipped key between a and b must stay on the line a-b.
                bool fits = true;
                for (int j = src.IndexOf(a) + 1; j <= i && fits; j++)
                {
                    float u = (src[j].time - a.time) / (b.time - a.time);
                    fits = Mathf.Abs(Mathf.Lerp(a.value, b.value, u) - src[j].value) < tolerance;
                }
                if (!fits) kept.Add(src[i]);
            }
            kept.Add(src[src.Count - 1]);
            return kept.ToArray();
        }

        public void Dispose()
        {
            if (Root != null) UnityEngine.Object.DestroyImmediate(Root);
        }

        /// <summary>Smootherstep from 0 at <paramref name="a"/> to 1 at <paramref name="b"/>.</summary>
        public static float Ease(float t, float a, float b)
        {
            float x = Mathf.Clamp01((t - a) / (b - a));
            return x * x * x * (x * (x * 6f - 15f) + 10f);
        }
    }
}
