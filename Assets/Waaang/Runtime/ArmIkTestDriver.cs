using UnityEngine;
using UnityEngine.InputSystem;

namespace Waaang
{
    // ArmIKTest scene only: sweeps each caster's aim target through a range of directions and switches between
    // camera presets so the wand-holding arms can be checked from every side.
    // Keys: 1-9 camera, Space auto sweep on/off, Left/Right previous/next fixed pose.
    // Runs after the rigs' LateUpdate so the close-ups frame the hands where the IK just put them.
    [DefaultExecutionOrder(100)]
    public sealed class ArmIkTestDriver : MonoBehaviour
    {
        public WandPointerRig[] rigs;
        [Tooltip("휴대폰/마우스 대신 각 리그가 겨눌 대상. rigs와 같은 순서.")]
        public Transform[] targets;
        public Camera[] cameras;
        [Tooltip("손을 따라가는 클로즈업 카메라. rigs와 같은 순서.")]
        public Camera[] handCameras;
        [Tooltip("클로즈업 카메라 위치: 캐릭터 기준 x=앞손 바깥쪽, y=위, z=앞 (m).")]
        public Vector3 handCameraOffset = new Vector3(0.45f, 0.2f, 0.6f);
        [Tooltip("켜면 조준점이 계속 움직입니다. 끄면 poses 중 하나에 고정됩니다.")]
        public bool autoSweep = true;
        public float sweepSpeed = 0.35f;
        public float targetDistance = 4f;
        public float yawRange = 65f;
        public float pitchRange = 40f;
        [Tooltip("고정 자세 목록: x=좌우(도, +가 오른쪽), y=상하(도, +가 위).")]
        public Vector2[] poses =
        {
            new Vector2(0f, 0f), new Vector2(-45f, 0f), new Vector2(45f, 0f), new Vector2(0f, 30f),
            new Vector2(0f, -30f), new Vector2(-55f, 25f), new Vector2(55f, -25f), new Vector2(65f, 35f),
            new Vector2(-65f, -35f),
        };
        public int poseIndex;
        public int cameraIndex;

        float time;

        void Start()
        {
            for (int i = 0; i < rigs.Length && i < targets.Length; i++)
            {
                if (rigs[i] != null)
                    rigs[i].aimTarget = targets[i];
            }
            SelectCamera(cameraIndex);
            PlaceTargets();
        }

        void Update()
        {
            ReadKeys();
            if (autoSweep)
                time += Time.deltaTime * sweepSpeed;
            PlaceTargets();
        }

        void LateUpdate()
        {
            FrameHands();
        }

        /// <summary>Points each hand close-up at the middle of its caster's two grips.</summary>
        public void FrameHands()
        {
            if (handCameras == null)
                return;
            for (int i = 0; i < rigs.Length && i < handCameras.Length; i++)
            {
                var rig = rigs[i];
                var cam = handCameras[i];
                if (rig == null || cam == null || rig.character == null || rig.rightGrip == null)
                    continue;
                Vector3 focus = rig.leftGrip != null && rig.useLeftHand
                    ? (rig.rightGrip.position + rig.leftGrip.position) * 0.5f
                    : rig.rightGrip.position;
                Transform body = rig.character;
                Vector3 offset = handCameraOffset;
                if (rig.leftHanded)
                    offset.x = -offset.x;
                cam.transform.position = focus + body.TransformDirection(offset);
                cam.transform.rotation = Quaternion.LookRotation(focus - cam.transform.position, Vector3.up);
            }
        }

        void ReadKeys()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
                return;
            for (int i = 0; i < 9 && i < cameras.Length; i++)
            {
                if (keyboard[Key.Digit1 + i].wasPressedThisFrame)
                    SelectCamera(i);
            }
            if (keyboard.spaceKey.wasPressedThisFrame)
                autoSweep = !autoSweep;
            if (keyboard.rightArrowKey.wasPressedThisFrame)
                StepPose(1);
            if (keyboard.leftArrowKey.wasPressedThisFrame)
                StepPose(-1);
        }

        void StepPose(int step)
        {
            autoSweep = false;
            poseIndex = (poseIndex + step + poses.Length) % poses.Length;
        }

        public void SelectCamera(int index)
        {
            if (cameras == null || cameras.Length == 0)
                return;
            cameraIndex = Mathf.Clamp(index, 0, cameras.Length - 1);
            for (int i = 0; i < cameras.Length; i++)
            {
                if (cameras[i] != null)
                    cameras[i].enabled = i == cameraIndex;
            }
        }

        /// <summary>Current aim angles (yaw right +, pitch up +) in degrees.</summary>
        public Vector2 CurrentAngles()
        {
            if (!autoSweep && poses.Length > 0)
                return poses[Mathf.Clamp(poseIndex, 0, poses.Length - 1)];
            // A slow Lissajous figure covers the whole aim range without repeating too quickly.
            return new Vector2(Mathf.Sin(time) * yawRange, Mathf.Sin(time * 1.7f + 0.6f) * pitchRange);
        }

        public void PlaceTargets()
        {
            Vector2 angles = CurrentAngles();
            for (int i = 0; i < rigs.Length && i < targets.Length; i++)
            {
                var rig = rigs[i];
                if (rig == null || targets[i] == null || rig.character == null)
                    continue;
                Transform body = rig.character;
                Vector3 origin = body.position + Vector3.up * 1.2f * body.lossyScale.y / 1.5f;
                Quaternion turn = Quaternion.AngleAxis(angles.x, Vector3.up)
                    * Quaternion.AngleAxis(-angles.y, body.right);
                targets[i].position = origin + turn * body.forward * targetDistance;
            }
        }

        void OnGUI()
        {
            Vector2 angles = CurrentAngles();
            string cam = cameras != null && cameraIndex < cameras.Length && cameras[cameraIndex] != null
                ? cameras[cameraIndex].name : "-";
            GUI.Label(new Rect(12, 12, 900, 22),
                $"Camera [{cameraIndex + 1}] {cam}   |   1-{Mathf.Min(9, cameras != null ? cameras.Length : 0)}: camera   Space: sweep {(autoSweep ? "ON" : "OFF")}   ←/→: pose");
            GUI.Label(new Rect(12, 32, 900, 22), $"yaw {angles.x:F0}°  pitch {angles.y:F0}°" + (autoSweep ? "" : $"   (pose {poseIndex + 1}/{poses.Length})"));
        }
    }
}
