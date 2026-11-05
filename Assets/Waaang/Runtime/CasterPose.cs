using UnityEngine;

namespace Waaang
{
    // Plays a Tripo idle clip (e.g. the cutscene mage's "Idle stance") on the caster so she is never in T-pose,
    // then layers a slow breath on the chest and shoulders like CutsceneKit.AddBreathing.
    // Only rotations are kept: the clip was authored on another Tripo body, so its bone positions and scales
    // would reshape this character. Arm/body IK from WandPointerRig runs after this, in LateUpdate.
    [DefaultExecutionOrder(-50)]
    public sealed class CasterPose : MonoBehaviour
    {
        public AnimationClip idle;
        public float speed = 1f;
        [Tooltip("숨 한 번의 길이(초).")]
        public float breathPeriod = 3.4f;
        [Tooltip("가슴이 들썩이는 각도. 0이면 꺼집니다.")]
        public float breathDegrees = 1.2f;

        Transform[] bones;
        Vector3[] restPositions;
        Vector3[] restScales;
        Transform spine, chest, upperChest, neck, head, leftShoulder, rightShoulder;
        float time;

        void Awake()
        {
            var armature = transform.Find("Armature");
            bones = (armature != null ? armature : transform).GetComponentsInChildren<Transform>(true);
            restPositions = new Vector3[bones.Length];
            restScales = new Vector3[bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                restPositions[i] = bones[i].localPosition;
                restScales[i] = bones[i].localScale;
                switch (bones[i].name)
                {
                    case "Spine": spine = bones[i]; break;
                    case "Chest": chest = bones[i]; break;
                    case "UpperChest": upperChest = bones[i]; break;
                    case "Neck": neck = bones[i]; break;
                    case "Head": head = bones[i]; break;
                    case "Left_Shoulder": leftShoulder = bones[i]; break;
                    case "Right_Shoulder": rightShoulder = bones[i]; break;
                }
            }
            // Different casters should not breathe in lockstep.
            time = Random.value * 10f;
        }

        void Update()
        {
            if (idle == null || bones == null)
                return;
            time += Time.deltaTime * speed;
            idle.SampleAnimation(gameObject, Mathf.Repeat(time, idle.length));
            for (int i = 0; i < bones.Length; i++)
            {
                bones[i].localPosition = restPositions[i];
                bones[i].localScale = restScales[i];
            }
            Breathe();
        }

        void Breathe()
        {
            if (breathDegrees <= 0f || breathPeriod <= 0f)
                return;
            float wave = Mathf.Sin(time / breathPeriod * Mathf.PI * 2f) * breathDegrees;
            Vector3 right = transform.right;
            Vector3 forward = transform.forward;
            Turn(spine, right, wave * 0.4f);
            Turn(chest, right, wave);
            Turn(upperChest, right, wave * 0.8f);
            Turn(neck, right, -wave * 0.5f);
            Turn(head, right, -wave * 0.4f);
            Turn(leftShoulder, forward, wave * 0.9f);
            Turn(rightShoulder, forward, -wave * 0.9f);
        }

        static void Turn(Transform bone, Vector3 axis, float degrees)
        {
            if (bone != null)
                bone.rotation = Quaternion.AngleAxis(degrees, axis) * bone.rotation;
        }
    }
}
