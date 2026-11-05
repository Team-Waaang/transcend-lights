using System;
using UnityEditor;
using UnityEngine;
using static AiHackathon.Cutscene.Editor.CutsceneKit;

namespace AiHackathon.Cutscene.Editor
{
    /// <summary>
    /// Battle clips for Amelia and Nox in the boss defeat cutscene: a braced beam struggle (looping), the resonance
    /// beat (they glance at each other, nod and dig in), and the ultimate beam (wind-up, thrust, recoil, hold).
    /// Built like the period clips (code-keyed on the Tripo skeleton, staff in the lead hand), plus the boss's
    /// beam-push loop baked with a straining tremor.
    /// </summary>
    public static partial class PeriodPoseClips
    {
        public const float StruggleLength = 4f;
        public const float ResonanceLength = 2.2f;
        public const float UltimateLength = 3.6f;
        const float TwoPi = Mathf.PI * 2f;

        [MenuItem("Tools/Cutscene/Build Battle Pose Clips")]
        public static void BuildBattle()
        {
            EnsureFolder(AnimDir);
            Save(Make(AmeliaFbx, "Amelia_Struggle", StruggleLength, (rig, t) => Struggle(rig, t, R)));
            Save(Make(NoxFbx, "Nox_Struggle", StruggleLength, (rig, t) => Struggle(rig, t, L)));
            Save(Make(AmeliaFbx, "Amelia_Resonance", ResonanceLength, (rig, t) => Resonance(rig, t, R)));
            Save(Make(NoxFbx, "Nox_Resonance", ResonanceLength, (rig, t) => Resonance(rig, t, L)));
            Save(Make(AmeliaFbx, "Amelia_Ultimate", UltimateLength, (rig, t) => Ultimate(rig, t, R)));
            Save(Make(NoxFbx, "Nox_Ultimate", UltimateLength, (rig, t) => Ultimate(rig, t, L)));
            AssetDatabase.SaveAssets();
        }

        // Tremor that loops seamlessly over the struggle clip: whole numbers of cycles of a few fast sines.
        static float Tremor(float t, float length, float seed) =>
            (Mathf.Sin(TwoPi * 37f * t / length + seed) * 0.5f + Mathf.Sin(TwoPi * 53f * t / length + seed * 2.3f) * 0.3f
             + Mathf.Sin(TwoPi * 71f * t / length + seed * 4.1f) * 0.2f);

        /// <summary>
        /// Braced two-handed stance pushing a beam: knees bent, leaning in, staff thrust forward and aimed a little low.
        /// <paramref name="headYaw"/> turns the head (toward her partner), <paramref name="twist"/> the chest.
        /// </summary>
        static void Stance(PoseRig rig, string lead, float t, float crouch, float lean, float extend, float lower,
            float headYaw, float headPitch, float twist, float shake)
        {
            float sway = Mathf.Sin(TwoPi * t / 4f);
            float breath = Mathf.Sin(TwoPi * t / 2f);
            rig.Hips(new Vector3(0.003f * sway, -crouch, -0.3f * crouch), new Vector3(lean * 0.3f, -4f * Side(lead) + 1.2f * sway, -0.9f * sway));
            rig.Turn("Spine", X, lean * 0.3f + 0.4f * shake);
            rig.Turn("Chest", X, lean * 0.35f - 1.1f * breath + 0.8f * shake);
            rig.Turn("UpperChest", X, lean * 0.2f - 0.7f * breath);
            rig.Turn("Spine", Y, twist * 0.4f);
            rig.Turn("Chest", Y, twist * 0.6f + 0.6f * shake);
            rig.Turn("Left_Shoulder", Z, -0.9f * breath);
            rig.Turn("Right_Shoulder", Z, 0.9f * breath);
            rig.Turn("Neck", X, -lean * 0.35f);
            rig.Turn("Head", X, -lean * 0.45f + headPitch + 0.5f * shake);
            rig.Turn("Head", Y, headYaw - twist);
            BeamArms(rig, lead, extend, lower, lean, shake);
        }

        static void BeamArms(PoseRig rig, string lead, float extend, float lower, float lean, float shake)
        {
            string support = lead == R ? L : R;
            var axis = M(new Vector3(-0.05f + 0.01f * shake, 0.42f - 0.3f * lower, 1f), lead).normalized;
            var point = M(new Vector3(0.03f, 0.665f - 0.025f * lower - 0.0015f * lean + 0.002f * shake, 0.2f + 0.06f * extend + 0.0035f * lean), lead);
            Grip(rig, lead, point, axis, M(new Vector3(0.35f, 1f, 0f), lead), M(new Vector3(0.32f, -0.35f, -0.05f), lead));
            var gripRot = GripRotation(rig, support, axis, M(new Vector3(-0.2f, -1f, 0.3f), lead));
            var supportPoint = point - axis * 0.075f;
            rig.Arm(support, supportPoint - gripRot * GripLocal(rig, support), rig[support + "UpperArm"].position + M(new Vector3(-0.3f, -0.38f, 0f), lead), gripRot);
            rig.Curl(support, 0.85f, 0.7f);
        }

        /// <summary>Pushing against the boss's beam: a shove every second, heavy breathing, a straining tremor. Loops.</summary>
        static void Struggle(PoseRig rig, float t, string lead)
        {
            float push = 0.5f + 0.5f * Mathf.Sin(TwoPi * t / 1f - 1.2f);
            push = push * push * (3f - 2f * push);
            float shake = Tremor(t, StruggleLength, lead == R ? 0.3f : 1.7f);
            Stance(rig, lead, t, 0.016f + 0.006f * push, 7f + 3f * push, 0.55f + 0.4f * push, 0.6f, 0f, -1.5f * push, 0f, shake);
        }

        /// <summary>The resonance: she glances at her partner, they nod, then both dig in harder.</summary>
        static void Resonance(PoseRig rig, float t, string lead)
        {
            float look = Ease(t, 0.1f, 0.5f) * (1f - Ease(t, 1.35f, 1.75f));
            float nod = t > 0.75f && t < 1.25f ? Mathf.Sin((t - 0.75f) / 0.5f * Mathf.PI) : 0f;
            float dig = Ease(t, 1.45f, 2.0f);
            float shake = Tremor(t, StruggleLength, lead == R ? 0.3f : 1.7f) * (0.5f + 0.5f * dig);
            Stance(rig, lead, t, 0.018f + 0.012f * dig, 7f + 6f * dig, 0.6f + 0.45f * dig, 0.6f,
                42f * Side(lead) * look, 13f * nod - 2f * dig, 9f * Side(lead) * look, shake);
        }

        /// <summary>The ultimate beam: pull back, thrust everything forward, ride the recoil, hold it out.</summary>
        static void Ultimate(PoseRig rig, float t, string lead)
        {
            float wind = Ease(t, 0f, 0.22f) * (1f - Ease(t, 0.22f, 0.34f));
            float thrust = Ease(t, 0.22f, 0.36f);
            float since = Mathf.Max(0f, t - 0.36f);
            float recoil = t > 0.36f ? Mathf.Sin(since * 32f) * Mathf.Exp(-since * 3.5f) : 0f;
            float shake = Tremor(t, StruggleLength, lead == R ? 0.9f : 2.4f) * (0.4f + 0.6f * thrust);
            Stance(rig, lead, t, 0.02f + 0.018f * thrust - 0.006f * wind, 6f - 9f * wind + 12f * thrust + 2.5f * recoil,
                0.5f - 0.5f * wind + 0.85f * thrust + 0.1f * recoil, 0.6f, 0f, -4f * thrust + 3f * wind, 0f, shake);
        }

        /// <summary>
        /// Bakes <paramref name="source"/> (sampled on <paramref name="fbx"/>) with a straining tremor on the spine,
        /// head and wand arm, as a new looping clip the same length. Tremor frequencies are whole cycles over the clip.
        /// </summary>
        public static AnimationClip BakeStrain(string fbx, AnimationClip source, string name, float degrees) =>
            BakeStrain(fbx, source, name, degrees, source.length, t => t, 0f);

        /// <summary>
        /// Same, but the source is sampled at <paramref name="sampleAt"/>(t) over <paramref name="length"/> seconds (e.g. a
        /// held frame), and the body shoves forward once a second by <paramref name="shove"/> degrees.
        /// </summary>
        public static AnimationClip BakeStrain(string fbx, AnimationClip source, string name, float degrees, float length, Func<float, float> sampleAt, float shove)
        {
            var go = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(fbx));
            go.hideFlags = HideFlags.HideAndDontSave;
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            try
            {
                var bones = go.GetComponentsInChildren<Transform>(true);
                (string bone, Vector3 axis, float gain)[] rig =
                {
                    ("Spine", Vector3.right, 0.4f), ("Chest", Vector3.right, 0.6f), ("Chest", Vector3.up, 0.4f), ("UpperChest", Vector3.right, 0.6f),
                    ("Neck", Vector3.right, 0.5f), ("Head", Vector3.right, 0.7f), ("Head", Vector3.up, 0.5f),
                    ("Right_Shoulder", Vector3.forward, 0.7f), ("Right_UpperArm", Vector3.right, 1f), ("Right_UpperArm", Vector3.up, 0.8f),
                    ("Right_LowerArm", Vector3.right, 0.8f), ("Left_UpperArm", Vector3.right, 0.5f),
                };
                const float fps = 30f;
                int frames = Mathf.RoundToInt(length * fps);
                var curves = new System.Collections.Generic.Dictionary<Transform, AnimationCurve[]>();
                foreach (var b in bones)
                    if (b != go.transform && b.GetComponent<Renderer>() == null)
                        curves[b] = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
                for (int f = 0; f <= frames; f++)
                {
                    float t = f / fps;
                    source.SampleAnimation(go, Mathf.Min(sampleAt(t), source.length - 1e-3f));
                    // A shove into the beam once a second (whole cycles over the clip, so it loops).
                    float push = 0.5f + 0.5f * Mathf.Sin(TwoPi * Mathf.Round(length) * t / length);
                    push = push * push * (3f - 2f * push) * shove;
                    foreach (var (boneName, gain) in new[] { ("Spine", 0.35f), ("Chest", 0.45f), ("Head", -0.3f), ("Right_UpperArm", -0.25f) })
                    {
                        var b = Array.Find(bones, x => x.name == boneName);
                        if (b != null) b.rotation = Quaternion.AngleAxis(push * gain, Vector3.right) * b.rotation;
                    }
                    for (int i = 0; i < rig.Length; i++)
                    {
                        var bone = Array.Find(bones, x => x.name == rig[i].bone);
                        if (bone == null) continue;
                        float a = rig[i].gain * degrees * Tremor(t, length, i * 1.37f);
                        bone.rotation = Quaternion.AngleAxis(a, rig[i].axis) * bone.rotation;
                    }
                    foreach (var pair in curves)
                    {
                        var q = pair.Key.localRotation;
                        var c = pair.Value;
                        if (c[0].length > 0)
                        {
                            var last = new Quaternion(c[0][c[0].length - 1].value, c[1][c[1].length - 1].value, c[2][c[2].length - 1].value, c[3][c[3].length - 1].value);
                            if (Quaternion.Dot(last, q) < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                        }
                        c[0].AddKey(t, q.x); c[1].AddKey(t, q.y); c[2].AddKey(t, q.z); c[3].AddKey(t, q.w);
                    }
                }
                var clip = new AnimationClip { name = name, frameRate = fps };
                string[] props = { "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w" };
                foreach (var pair in curves)
                {
                    var path = AnimationUtility.CalculateTransformPath(pair.Key, go.transform);
                    for (int c = 0; c < 4; c++) AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), props[c]), pair.Value[c]);
                }
                var settings = AnimationUtility.GetAnimationClipSettings(clip);
                settings.loopTime = true;
                AnimationUtility.SetAnimationClipSettings(clip, settings);
                clip.EnsureQuaternionContinuity();
                var assetPath = AnimDir + "/" + name + ".anim";
                AssetDatabase.DeleteAsset(assetPath);
                AssetDatabase.CreateAsset(clip, assetPath);
                return clip;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
