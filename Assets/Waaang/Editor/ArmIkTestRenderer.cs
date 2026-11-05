using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Waaang.EditorTools
{
    // Checks ArmIKTest.unity without entering play mode: samples the idle, runs each WandPointerRig's pose/IK, then
    // logs joint angles (Measure) or writes one contact sheet per pose from every camera (RenderAsync).
    // The scene is reloaded afterwards so nothing these touch is saved.
    public static class ArmIkTestRenderer
    {
        const string ScenePath = "Assets/Scenes/Test/ArmIKTest.unity";
        const int CellWidth = 480;
        const int CellHeight = 360;
        const int Columns = 4;

        [MenuItem("Waaang/Arm IK Test/Render All Angles")]
        static void RenderFromMenu()
        {
            string dir = Path.Combine(Path.GetTempPath(), "ArmIkTest");
            Debug.Log(RenderAsync(dir, null));
        }

        [MenuItem("Waaang/Arm IK Test/Log Joint Angles")]
        static void MeasureFromMenu()
        {
            Debug.Log(Measure());
        }

        /// <summary>
        /// Per pose and arm: elbow bend, wrist bend/twist against the bind pose, forearm twist, plus the largest
        /// frame-to-frame hand rotation over a continuous sweep (a flip shows up as a big step).
        /// </summary>
        public static string Measure()
        {
            if (EditorApplication.isPlaying)
                return "Stop play mode first.";
            if (EditorSceneManager.GetActiveScene().path != ScenePath)
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var driver = Object.FindAnyObjectByType<ArmIkTestDriver>();
            const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            var rigType = typeof(WandPointerRig);
            var sb = new System.Text.StringBuilder();
            foreach (var rig in driver.rigs)
                Prepare(rig);

            string Arm(WandPointerRig rig, bool lead)
            {
                var upper = (Transform)rigType.GetField(lead ? "rightUpper" : "leftUpper", Any).GetValue(rig);
                var lower = (Transform)rigType.GetField(lead ? "rightLower" : "leftLower", Any).GetValue(rig);
                var hand = (Transform)rigType.GetField(lead ? "rightHand" : "leftHand", Any).GetValue(rig);
                var limits = (ArmIk.Limits)rigType.GetField(lead ? "leadLimits" : "supportLimits", Any).GetValue(rig);
                Vector3 a = lower.position - upper.position, b = hand.position - lower.position;
                Quaternion wrist = Quaternion.Inverse(limits.handBindLocal) * (Quaternion.Inverse(lower.rotation) * hand.rotation);
                Quaternion fore = Quaternion.Inverse(limits.lowerBindLocal) * (Quaternion.Inverse(upper.rotation) * lower.rotation);
                return $"{(lead ? "lead   " : "support")} elbow {Vector3.Angle(a, b),4:F0}  wristBend {Vector3.Angle(Vector3.up, wrist * Vector3.up),4:F0}" +
                    $"  wristTwist {ArmIk.TwistAngle(wrist, Vector3.up),4:F0}  forearmTwist {ArmIk.TwistAngle(fore, Vector3.up),4:F0}";
            }

            driver.autoSweep = false;
            for (int p = 0; p < driver.poses.Length; p++)
            {
                driver.poseIndex = p;
                driver.PlaceTargets();
                sb.AppendLine($"pose {p} yaw {driver.poses[p].x:F0} pitch {driver.poses[p].y:F0}");
                foreach (var rig in driver.rigs)
                {
                    for (int frame = 0; frame < 3; frame++)
                        Step(rig);
                    sb.AppendLine($"  {rig.character.name,-7} {Arm(rig, true)}");
                    sb.AppendLine($"  {rig.character.name,-7} {Arm(rig, false)}");
                }
            }

            driver.autoSweep = true;
            var timeField = typeof(ArmIkTestDriver).GetField("time", Any);
            foreach (var rig in driver.rigs)
            {
                var bones = new[]
                {
                    (Transform)rigType.GetField("rightHand", Any).GetValue(rig),
                    (Transform)rigType.GetField("leftHand", Any).GetValue(rig),
                };
                var previous = new Quaternion[bones.Length];
                var largest = new float[bones.Length];
                for (int i = 0; i <= 600; i++)
                {
                    timeField.SetValue(driver, i * 0.0125f);
                    driver.PlaceTargets();
                    Step(rig);
                    for (int k = 0; k < bones.Length; k++)
                    {
                        if (i > 0)
                            largest[k] = Mathf.Max(largest[k], Quaternion.Angle(previous[k], bones[k].rotation));
                        previous[k] = bones[k].rotation;
                    }
                }
                sb.AppendLine($"{rig.character.name} sweep: largest hand step lead {largest[0]:F1}°, support {largest[1]:F1}°");
            }
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            return sb.ToString();
        }

        public static void Prepare(WandPointerRig rig)
        {
            const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            var casterPose = rig.character.GetComponent<CasterPose>();
            if (casterPose != null)
                typeof(CasterPose).GetMethod("Awake", Any).Invoke(casterPose, null);
            typeof(WandPointerRig).GetMethod("BindBones", Any).Invoke(rig, null);
        }

        // One frame: idle sample (fixed time, so every pose starts from the same body) then the rig's pose/IK.
        public static void Step(WandPointerRig rig)
        {
            const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            var casterPose = rig.character.GetComponent<CasterPose>();
            if (casterPose != null)
            {
                typeof(CasterPose).GetField("time", Any).SetValue(casterPose, 0.4f);
                typeof(CasterPose).GetMethod("Update", Any).Invoke(casterPose, null);
            }
            typeof(WandPointerRig).GetMethod("PoseCaster", Any).Invoke(rig, new object[] { rig.aimTarget.position });
        }

        /// <summary>
        /// Writes pose_XX.png contact sheets (every camera of the driver) into outDir, one pose per editor tick pair:
        /// pose, let the editor tick so the GPU Resident Drawer and skinning pick up the new transforms, then render.
        /// poses null renders every pose. compareRigType (e.g. an older copy of WandPointerRig with the same private
        /// BindBones/PoseCaster) poses the casters with that component instead. Writes done.txt when finished.
        /// </summary>
        public static string RenderAsync(string outDir, int[] poses, string compareRigType = null)
        {
            if (EditorApplication.isPlaying)
                return "Stop play mode first.";
            if (running)
                return "A render is already running.";
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Directory.CreateDirectory(outDir);
            string done = Path.Combine(outDir, "done.txt");
            if (File.Exists(done))
                File.WriteAllText(done, "");
            var driver = Object.FindAnyObjectByType<ArmIkTestDriver>();
            if (driver == null)
                return "No ArmIkTestDriver in " + ScenePath;

            const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            System.Type rigType = typeof(WandPointerRig);
            var posers = new Component[driver.rigs.Length];
            if (!string.IsNullOrEmpty(compareRigType))
            {
                rigType = System.Type.GetType(compareRigType);
                if (rigType == null)
                    return "Unknown type " + compareRigType;
            }
            for (int i = 0; i < driver.rigs.Length; i++)
            {
                var rig = driver.rigs[i];
                var casterPose = rig.character.GetComponent<CasterPose>();
                if (casterPose != null)
                    typeof(CasterPose).GetMethod("Awake", Any).Invoke(casterPose, null);
                Component poser = rig;
                if (rigType != typeof(WandPointerRig))
                {
                    poser = rig.gameObject.AddComponent(rigType);
                    foreach (var field in rigType.GetFields(BindingFlags.Instance | BindingFlags.Public))
                    {
                        var source = typeof(WandPointerRig).GetField(field.Name);
                        if (source == null)
                            continue;
                        if (source.FieldType == field.FieldType)
                            field.SetValue(poser, source.GetValue(rig));
                        else if (field.FieldType.IsEnum)
                            field.SetValue(poser, System.Enum.ToObject(field.FieldType, System.Convert.ToInt32(source.GetValue(rig))));
                    }
                }
                rigType.GetMethod("BindBones", Any).Invoke(poser, null);
                posers[i] = poser;
                foreach (var skin in rig.character.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    skin.forceMatrixRecalculationPerRender = true;
            }

            var queue = new System.Collections.Generic.List<int>();
            for (int p = 0; p < driver.poses.Length; p++)
            {
                if (poses == null || System.Array.IndexOf(poses, p) >= 0)
                    queue.Add(p);
            }
            var poseCaster = rigType.GetMethod("PoseCaster", Any);
            int index = 0;
            int wait = -1;
            var log = new System.Text.StringBuilder();
            running = true;

            void Tick()
            {
                try
                {
                    if (index >= queue.Count)
                    {
                        Finish();
                        return;
                    }
                    int p = queue[index];
                    if (wait < 0)
                    {
                        driver.autoSweep = false;
                        driver.poseIndex = p;
                        driver.PlaceTargets();
                        for (int i = 0; i < driver.rigs.Length; i++)
                        {
                            var rig = driver.rigs[i];
                            var casterPose = rig.character.GetComponent<CasterPose>();
                            for (int frame = 0; frame < 3; frame++)
                            {
                                if (casterPose != null)
                                {
                                    typeof(CasterPose).GetField("time", Any).SetValue(casterPose, 0.4f);
                                    typeof(CasterPose).GetMethod("Update", Any).Invoke(casterPose, null);
                                }
                                poseCaster.Invoke(posers[i], new object[] { rig.aimTarget.position });
                            }
                        }
                        driver.FrameHands();
                        wait = 2;
                        EditorApplication.QueuePlayerLoopUpdate();
                        return;
                    }
                    if (wait > 0)
                    {
                        wait--;
                        EditorApplication.QueuePlayerLoopUpdate();
                        return;
                    }
                    Vector2 angles = driver.poses[p];
                    string file = Path.Combine(outDir, $"pose_{p:00}_yaw{angles.x:+0;-0;0}_pitch{angles.y:+0;-0;0}.png");
                    WriteSheet(driver.cameras, file);
                    log.AppendLine(file);
                    index++;
                    wait = -1;
                }
                catch (System.Exception e)
                {
                    log.AppendLine("ERROR " + e);
                    Finish();
                }
            }

            void Finish()
            {
                EditorApplication.update -= Tick;
                running = false;
                // Discard everything the sampling, IK and comparison components changed.
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                File.WriteAllText(done, log.ToString());
            }

            EditorApplication.update += Tick;
            EditorApplication.QueuePlayerLoopUpdate();
            return $"Rendering {queue.Count} poses into {outDir}; done.txt appears when finished.";
        }

        static bool running;

        static void WriteSheet(Camera[] cameras, string file)
        {
            int rows = (cameras.Length + Columns - 1) / Columns;
            var sheet = new Texture2D(CellWidth * Columns, CellHeight * rows, TextureFormat.RGB24, false);
            var rt = RenderTexture.GetTemporary(CellWidth, CellHeight, 24, RenderTextureFormat.ARGB32);
            var cell = new Texture2D(CellWidth, CellHeight, TextureFormat.RGB24, false);
            for (int c = 0; c < cameras.Length; c++)
            {
                var cam = cameras[c];
                bool oldEnabled = cam.enabled;
                cam.enabled = true;
                // URP: a render request draws synchronously into rt.
                var request = new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = rt };
                if (UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(cam, request))
                {
                    UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(cam, request);
                }
                else
                {
                    var oldTarget = cam.targetTexture;
                    cam.targetTexture = rt;
                    cam.Render();
                    cam.targetTexture = oldTarget;
                }
                cam.enabled = oldEnabled;
                RenderTexture.active = rt;
                cell.ReadPixels(new Rect(0, 0, CellWidth, CellHeight), 0, 0);
                cell.Apply();
                RenderTexture.active = null;
                sheet.SetPixels(c % Columns * CellWidth, (rows - 1 - c / Columns) * CellHeight, CellWidth, CellHeight, cell.GetPixels());
            }
            sheet.Apply();
            File.WriteAllBytes(file, sheet.EncodeToPNG());
            RenderTexture.ReleaseTemporary(rt);
            Object.DestroyImmediate(cell);
            Object.DestroyImmediate(sheet);
        }
    }
}
