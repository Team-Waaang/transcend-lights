using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

namespace Waaang
{
    // Screen pointer from PhonePointer, camera raycast, staff aim, and both-arm IK.
    public sealed class WandPointerRig : MonoBehaviour
    {
        public Camera viewCamera;
        public Transform character;
        public Transform staff;
        public Transform rightGrip;
        public Transform leftGrip;
        public Transform hitMarker;
        public LineRenderer aimLine;
        public LineRenderer glowLine;
        public Light hitLight;
        public LayerMask roomMask = ~0;
        [Tooltip("맞은 표면에서 광원을 얼마나 앞으로 띄울지. 면 안에 들어가면 그 면이 밝아지지 않습니다.")]
        public float lightSurfaceOffset = 0.28f;
        [Tooltip("이 리그가 따라갈 휴대폰 순서. 0이 왼쪽, 1이 오른쪽 플레이어입니다.")]
        public int phoneSlot;
        [FormerlySerializedAs("useMouseWhenPhoneNotReady")]
        [Tooltip("이 슬롯에 휴대폰이 없을 때 마우스로 조작합니다. 휴대폰이 붙으면 그 휴대폰이 우선합니다.")]
        public bool mouseMode = true;
        public enum MouseFireButton { Left, Right }
        [Tooltip("마우스로 쏠 버튼. 두 명을 마우스로 시험할 때 왼쪽 플레이어는 좌클릭, 오른쪽은 우클릭.")]
        public MouseFireButton mouseButton = MouseFireButton.Left;
        [Tooltip("켜면 마우스 버튼을 누르는 동안만 이 리그가 커서를 따라갑니다 (두 명이 커서 하나를 나눠 쓸 때).")]
        public bool mouseAimWhileFiring;
        [Tooltip("시작할 때 씬 전체를 lilToon으로 바꿉니다. 자체 재질을 쓰는 씬(컷씬 교실 등)에서는 끄세요.")]
        public bool toonScene = true;
        [Tooltip("지정하면 휴대폰/마우스 포인터 대신 이 대상을 겨눕니다 (테스트 씬용).")]
        public Transform aimTarget;

        [Header("Pose / IK")]
        [Tooltip("오른쪽 어깨 기준 지팡이를 쥐는 위치(m). x=오른쪽, y=위, z=앞.")]
        public Vector3 holdOffset = new Vector3(0.32f, 0f, 0.12f);
        [Tooltip("조준 방향으로 쥔 손이 얼마나 따라 나가는지(m).")]
        public float holdReach = 0.04f;
        [Tooltip("지팡이를 조준 방향 대신 위로 세우는 비율. 0이면 조준점을 정확히 가리킵니다.")]
        [Range(0f, 0.9f)] public float staffUpright;
        [Tooltip("몸통(척추~윗가슴)이 조준 방향으로 돌아가는 비율.")]
        [Range(0f, 1f)] public float bodyFollow = 0.55f;
        [Tooltip("머리가 나머지 조준 각도를 따라가는 비율.")]
        [Range(0f, 1f)] public float headFollow = 0.6f;
        public float maxYaw = 60f;
        public float maxPitch = 35f;
        [Tooltip("조준 흔들림을 줄이는 속도. 클수록 빠르게 따라갑니다.")]
        public float aimSharpness = 12f;
        [Tooltip("켜면 왼손도 지팡이를 잡습니다.")]
        public bool useLeftHand = true;
        public GameObject beamPrefab;
        [Tooltip("끄면 레이저(이펙트, 벽 흔적, 맞은 자리 빛)를 그리지 않고 조준만 합니다. 2교시처럼 지팡이 빛으로 노는 교시용.")]
        public bool showBeam = true;
        [Tooltip("누르고 있는 동안 레이저 이펙트를 다시 쏘는 주기(초).")]
        public float beamLoopPeriod = 0.45f;
        [Tooltip("레이저 굵기. 거리와 상관없이 일정합니다 (이펙트 원본 대비 배율).")]
        public float beamThickness = 0.12f;
        [Tooltip("맞은 자리 이펙트 크기 (이펙트 원본 대비 배율).")]
        public float impactSize = 0.06f;
        [Tooltip("레이저가 지팡이 끝에서 얼마나 떨어져서 시작할지(m).")]
        public float beamStartGap = 0.04f;
        [Tooltip("레이저 이펙트 색. 알파가 0이면 원본 색을 씁니다.")]
        public Color beamTint = new Color(0f, 0f, 0f, 0f);
        [Tooltip("벽에 남은 흔적이 이 시간(초)이 지나면 가늘어지며 사라집니다. 0이면 계속 남습니다.")]
        public float trailLifetime = 10f;
        [Tooltip("레이저가 닿은 곳에 흔적을 남깁니다. 움직이는 대상을 쏘는 전투에서는 끕니다.")]
        public bool drawTrails = true;
        [Tooltip("조준점 옆에 맞은 콜라이더 이름을 표시합니다 (디버그).")]
        public bool showHitLabel;
        [Tooltip("흔적이 사라지는 데 걸리는 시간(초).")]
        public float trailFade = 1.2f;
        [Tooltip("벽에 남는 흔적 색 (HDR).")]
        [ColorUsage(false, true)] public Color trailColor = new Color(1.5f, 0.78f, 0.28f, 1f);
        [Tooltip("오른손이 지팡이를 잡는 위치. 0=손잡이 끝, 1=지팡이 끝. 클수록 지팡이가 뒤로 들어갑니다.")]
        [Range(0.05f, 0.7f)] public float gripAlongStaff = 0.32f;
        [Tooltip("손을 어깨 높이에서 얼마나 내려 쥘지(m). 지팡이 뒤끝이 머리에 걸리지 않게 합니다.")]
        public float holdDrop = 0.16f;
        [Tooltip("손을 몸 바깥으로 얼마나 빼서 쥘지(m).")]
        public float holdSide = 0.06f;
        [Tooltip("켜면 왼손이 앞손이 되어 지팡이를 몸 왼쪽에서 잡습니다.")]
        public bool leftHanded;
        [Tooltip("오른손 손가락을 지팡이에 감는 정도.")]
        [Range(0f, 1.5f)] public float fingerCurl = 1f;

        [Header("Arm IK")]
        [Tooltip("어깨에서 지팡이 쥔 손까지의 거리 (팔 길이 비율). 1에 가까울수록 팔이 쭉 펴집니다.")]
        [Range(0.6f, 1f)] public float armExtension = 0.86f;
        [Tooltip("팔이 다 펴지기 전에 부드럽게 멈추기 시작하는 지점 (팔 길이 비율). 팔꿈치가 펴졌다 굽혔다 튀지 않게 합니다.")]
        [Range(0.8f, 1f)] public float softReach = 0.9f;
        [Tooltip("팔꿈치를 아래로 내리는 정도 (팔 길이 배율).")]
        public float elbowDrop = 0.7f;
        [Tooltip("팔꿈치를 몸 바깥으로 빼는 정도 (팔 길이 배율).")]
        public float elbowOut = 0.35f;
        [Tooltip("지팡이가 손바닥을 대각선으로 지나갈 수 있는 최대 각도. 클수록 손목이 덜 꺾입니다.")]
        [Range(0f, 75f)] public float gripOblique = 50f;
        [Tooltip("손 비틀림 중 아래팔이 나눠 갖는 비율. 트위스트 본이 없어 손목 피부가 꼬이는 것을 줄입니다.")]
        [Range(0f, 1f)] public float forearmTwist = 0.5f;
        [Tooltip("받치는 손에서 지팡이가 손바닥을 대각선으로 지나가는 각도.")]
        [Range(0f, 75f)] public float supportGripOblique = 15f;
        [Tooltip("받치는 손이 앞손에서 지팡이를 따라 얼마나 뒤에 있는지(m).")]
        public float supportGap = 0.16f;
        [Tooltip("받치는 손 손가락을 지팡이에 감는 정도.")]
        [Range(0f, 1.5f)] public float supportFingerCurl = 0.9f;

        static readonly string[] FingerNames = { "Index", "Middle", "Ring", "Pinky" };
        static readonly string[] JointNames = { "Proximal", "Intermediate", "Distal" };
        static readonly float[] JointCurl = { 68f, 90f, 60f };

        Transform rightUpper, rightLower, rightHand;
        Transform leftUpper, leftLower, leftHand;
        Transform spine, chest, upperChest, head;
        Quaternion[] restRotations;
        Transform[] twistBones;
        CasterPose pose;
        Transform boundCharacter;
        // Right-hand grip frame in hand space, from the finger bones: fingers, across the knuckles (pinky -> index,
        // where the staff runs), and the palm normal.
        Vector3 fingerLocal = Vector3.up, acrossLocal = Vector3.forward, palmLocal = Vector3.right;
        Transform[] fingerBones;
        Quaternion[] fingerRest;
        Transform thumb;
        Quaternion thumbRest;
        // The support hand's grip frame and finger chain, same layout as the lead hand's above.
        Vector3 supportFingerLocal = Vector3.up, supportAcrossLocal = Vector3.forward, supportPalmLocal = Vector3.left;
        Transform[] supportFingerBones;
        Quaternion[] supportFingerRest;
        Transform supportThumb;
        Quaternion supportThumbRest;
        float supportHandLength = 0.07f;
        // Elbow hinges and straight-wrist hand rotations taken from the skin's bind pose (see BindArm).
        ArmIk.Limits leadLimits = ArmIk.Limits.Default;
        ArmIk.Limits supportLimits = ArmIk.Limits.Default;
        float handLength = 0.07f;
        float armLength;
        Vector3 smoothedAim;
        bool hasSmoothedAim;
        Vector2 pointerUv = new Vector2(0.5f, 0.5f);
        bool hasPointerSample;
        bool pointerVisible;
        bool wasFiring;
        string hitName = "";
        Material trailMaterial;
        Transform trailRoot;
        LineRenderer trail;
        Vector3 lastTrailPoint;
        bool trailHasPoint;
        const int MaxTrails = 16;
        const int MaxTrailPoints = 512;
        GameObject beamInstance;
        Transform impactRoot;
        ParticleSystem[] strokeSystems = new ParticleSystem[0];
        Vector2[] strokeBaseWidth = new Vector2[0];
        const float AssetBeamLength = 20f;

        void Awake()
        {
            BindBones();
            if (viewCamera == null)
                viewCamera = Camera.main;
            if (hitLight == null && hitMarker != null)
                hitLight = hitMarker.GetComponent<Light>();
            if (hitLight != null && hitMarker != null && hitLight.transform == hitMarker)
            {
                var holder = new GameObject("Hit Light");
                holder.transform.SetParent(transform, false);
                var copy = holder.AddComponent<Light>();
                copy.type = hitLight.type;
                copy.color = hitLight.color;
                copy.intensity = hitLight.intensity;
                copy.range = hitLight.range;
                copy.shadows = LightShadows.None;
                hitLight.enabled = false;
                hitLight = copy;
            }
            trailRoot = new GameObject("Laser Trails").transform;
            trailRoot.SetParent(transform, false);
            SetBeamActive(false);
            EnsureAssetBeam();
            if (toonScene)
                ToonLook.ApplyScene();
        }

        // ---------- Read by game logic (e.g. the rune period) ----------

        /// <summary>True while this caster is firing.</summary>
        public bool Firing { get; private set; }
        /// <summary>True when the laser currently hits something; see <see cref="HitPoint"/> / <see cref="HitCollider"/>.</summary>
        public bool HasHit { get; private set; }
        public Vector3 HitPoint { get; private set; }
        public Collider HitCollider { get; private set; }
        /// <summary>Where the beam leaves the wand (just past its tip) and the way it points (along the staff).</summary>
        public Vector3 BeamOrigin { get; private set; }
        public Vector3 BeamDirection { get; private set; } = Vector3.forward;

        /// <summary>Points the caster at a screen position (0..1) until the phone or mouse moves it.</summary>
        public void SetPointer(Vector2 uv)
        {
            pointerUv = uv;
            hasPointerSample = true;
        }

        /// <summary>Removes every stroke this caster has drawn.</summary>
        public void ClearTrails()
        {
            trail = null;
            trailHasPoint = false;
            if (trailRoot == null)
                return;
            for (int i = trailRoot.childCount - 1; i >= 0; i--)
                Destroy(trailRoot.GetChild(i).gameObject);
        }

        // Recolour one layer to tint, keeping each layer's own alpha and fades (CutsceneKit.Tint).
        static void Tint(ParticleSystem system, Color tint)
        {
            var main = system.main;
            var start = main.startColor;
            Color c = start.mode == ParticleSystemGradientMode.Color ? start.color : start.colorMax;
            main.startColor = new Color(tint.r, tint.g, tint.b, c.a);
            var overLife = system.colorOverLifetime;
            if (overLife.enabled && overLife.color.mode == ParticleSystemGradientMode.Gradient && overLife.color.gradient != null)
            {
                var gradient = overLife.color.gradient;
                var colorKeys = gradient.colorKeys;
                for (int i = 0; i < colorKeys.Length; i++)
                    colorKeys[i].color = Color.white;
                var recoloured = new Gradient();
                recoloured.SetKeys(colorKeys, gradient.alphaKeys);
                overLife.color = recoloured;
            }
        }

        // Slide the staff back through the hands: the right grip sits gripAlongStaff of the way from the butt to
        // the tip, so more of the shaft is behind the hands and less sticks out in front.
        void PlaceGrip()
        {
            if (staff == null || rightGrip == null)
                return;
            var tip = staff.Find("Staff Tip");
            if (tip != null)
                rightGrip.localPosition = tip.localPosition * gripAlongStaff;
        }

        // A second caster is switched off when its phone leaves: hide everything it drew outside its own object.
        void OnDisable()
        {
            trail = null;
            if (beamInstance != null)
                beamInstance.SetActive(false);
            if (impactRoot != null)
                impactRoot.gameObject.SetActive(false);
            if (hitMarker != null)
                hitMarker.gameObject.SetActive(false);
            if (hitLight != null)
                hitLight.enabled = false;
            SetBeamActive(false);
        }

        void OnDestroy()
        {
            // The asset beam lives at the scene root, so a despawned second caster would leave it behind.
            if (beamInstance != null)
                Destroy(beamInstance);
            if (impactRoot != null)
                Destroy(impactRoot.gameObject);
        }

        void EnsureAssetBeam()
        {
            if (beamInstance != null)
                return;
            if (beamPrefab == null)
                beamPrefab = Resources.Load<GameObject>("HCFX_Beam_03");
            if (beamPrefab == null)
                return;
            beamInstance = Instantiate(beamPrefab);
            beamInstance.name = "Asset Laser";
            LoopBeam(beamInstance);
            beamInstance.SetActive(false);
        }

        // Same treatment as the cutscene beams (CutsceneKit.KlausBeam + Looping): the HCFX beam is a one-shot,
        // so re-fire it every beamLoopPeriod while held, and drop the smoke and dark stroke layers that pile
        // up into mud when it re-fires.
        //
        // The layers are also split by job. The stretched strokes (the core on the root, "laser_outer") stay on the
        // beam and are scaled to its length; the hit layers move to their own object so the impact keeps one size
        // and lies on the surface that was hit.
        void LoopBeam(GameObject beam)
        {
            impactRoot = new GameObject("Asset Laser Impact").transform;
            impactRoot.gameObject.SetActive(false);
            var strokes = new System.Collections.Generic.List<ParticleSystem>();
            var systems = beam.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < systems.Length; i++)
            {
                var system = systems[i];
                string layer = system.name;
                if (layer == "smoke_up" || layer == "hit_smoke" || layer == "laser_black")
                {
                    system.gameObject.SetActive(false);
                    continue;
                }
                system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = system.main;
                if (beamTint.a > 0f)
                    Tint(system, beamTint);
                main.loop = true;
                main.duration = beamLoopPeriod;
                main.playOnAwake = true;

                var renderer = system.GetComponent<ParticleSystemRenderer>();
                if (renderer != null && renderer.renderMode == ParticleSystemRenderMode.Stretch)
                {
                    // The core is 22 units long on a 20-unit beam, so it poked 10% of the beam length back past
                    // the tip into the wand. Trim it to the beam.
                    // Its -0.05 pivot also pushed it ~1 unit toward the caster; -0.004 (measured with BakeMesh)
                    // starts it with laser_outer, at the beam start just past the wand tip.
                    if (main.startSize3D && main.startSizeYMultiplier > AssetBeamLength)
                    {
                        main.startSizeYMultiplier = AssetBeamLength;
                        renderer.pivot = new Vector3(renderer.pivot.x, -0.004f, renderer.pivot.z);
                    }
                    strokes.Add(system);
                }
                else if (system.gameObject != beam)
                {
                    system.transform.SetParent(impactRoot, false);
                }
            }
            strokeSystems = strokes.ToArray();
            strokeBaseWidth = new Vector2[strokeSystems.Length];
            for (int i = 0; i < strokeSystems.Length; i++)
            {
                var main = strokeSystems[i].main;
                strokeBaseWidth[i] = new Vector2(main.startSizeXMultiplier, main.startSizeZMultiplier);
            }
        }

        // Also called later when character is assigned after AddComponent (SecondCaster).
        //
        // "right*" bones and rightGrip are the lead arm (the hand nearer the tip, fingers curled round the shaft) and
        // "left*" the support arm. For a left-handed caster those are the left and right arms respectively.
        void BindBones()
        {
            boundCharacter = character;
            PlaceGrip();
            string lead = leftHanded ? "Left_" : "Right_";
            string support = leftHanded ? "Right_" : "Left_";
            rightUpper = FindBone(lead + "UpperArm");
            rightLower = FindBone(lead + "LowerArm");
            rightHand = FindBone(lead + "Hand");
            leftUpper = FindBone(support + "UpperArm");
            leftLower = FindBone(support + "LowerArm");
            leftHand = FindBone(support + "Hand");
            spine = FindBone("Spine");
            chest = FindBone("Chest");
            upperChest = FindBone("UpperChest");
            head = FindBone("Head");
            var middle = FindBone(lead + "MiddleProximal");
            var index = FindBone(lead + "IndexProximal");
            var pinky = FindBone(lead + "PinkyProximal");
            if (rightHand != null && middle != null)
                handLength = Vector3.Distance(rightHand.position, middle.position);
            if (rightUpper != null && rightLower != null && rightHand != null)
                armLength = Vector3.Distance(rightUpper.position, rightLower.position)
                    + Vector3.Distance(rightLower.position, rightHand.position);
            if (rightHand != null && middle != null && index != null && pinky != null)
            {
                Quaternion toLocal = Quaternion.Inverse(rightHand.rotation);
                fingerLocal = (toLocal * (middle.position - rightHand.position)).normalized;
                acrossLocal = Vector3.ProjectOnPlane(toLocal * (index.position - pinky.position), fingerLocal).normalized;
                // A left hand is a mirror image, so the same cross product points out of its back instead.
                palmLocal = Vector3.Cross(fingerLocal, acrossLocal) * (leftHanded ? -1f : 1f);
            }
            fingerBones = new Transform[FingerNames.Length * JointNames.Length];
            for (int f = 0; f < FingerNames.Length; f++)
                for (int j = 0; j < JointNames.Length; j++)
                    fingerBones[f * JointNames.Length + j] = FindBone(lead + FingerNames[f] + JointNames[j]);
            fingerRest = new Quaternion[fingerBones.Length];
            for (int i = 0; i < fingerBones.Length; i++)
                fingerRest[i] = fingerBones[i] != null ? fingerBones[i].localRotation : Quaternion.identity;
            thumb = FindBone(lead + "ThumbProximal");
            thumbRest = thumb != null ? thumb.localRotation : Quaternion.identity;

            // Support hand: same grip frame from its own finger bones. It is the other side, so its palm sign flips.
            var supportMiddle = FindBone(support + "MiddleProximal");
            var supportIndex = FindBone(support + "IndexProximal");
            var supportPinky = FindBone(support + "PinkyProximal");
            if (leftHand != null && supportMiddle != null)
                supportHandLength = Vector3.Distance(leftHand.position, supportMiddle.position);
            if (leftHand != null && supportMiddle != null && supportIndex != null && supportPinky != null)
            {
                Quaternion toLocal = Quaternion.Inverse(leftHand.rotation);
                supportFingerLocal = (toLocal * (supportMiddle.position - leftHand.position)).normalized;
                supportAcrossLocal = Vector3.ProjectOnPlane(toLocal * (supportIndex.position - supportPinky.position), supportFingerLocal).normalized;
                supportPalmLocal = Vector3.Cross(supportFingerLocal, supportAcrossLocal) * (leftHanded ? 1f : -1f);
            }
            supportFingerBones = new Transform[FingerNames.Length * JointNames.Length];
            for (int f = 0; f < FingerNames.Length; f++)
                for (int j = 0; j < JointNames.Length; j++)
                    supportFingerBones[f * JointNames.Length + j] = FindBone(support + FingerNames[f] + JointNames[j]);
            supportFingerRest = new Quaternion[supportFingerBones.Length];
            for (int i = 0; i < supportFingerBones.Length; i++)
                supportFingerRest[i] = supportFingerBones[i] != null ? supportFingerBones[i].localRotation : Quaternion.identity;
            supportThumb = FindBone(support + "ThumbProximal");
            supportThumbRest = supportThumb != null ? supportThumb.localRotation : Quaternion.identity;

            leadLimits = BindArm(rightUpper, rightLower, rightHand);
            supportLimits = BindArm(leftUpper, leftLower, leftHand);
            twistBones = new[] { spine, chest, upperChest, head, rightUpper, rightLower, rightHand, leftUpper, leftLower, leftHand };
            restRotations = new Quaternion[twistBones.Length];
            for (int i = 0; i < twistBones.Length; i++)
                restRotations[i] = twistBones[i] != null ? twistBones[i].localRotation : Quaternion.identity;
            pose = character != null ? character.GetComponent<CasterPose>() : null;
        }

        // Elbow hinge (upper-arm space) and straight-wrist hand rotation (forearm space) from the skin's bind pose,
        // so they do not depend on whatever pose the idle clip or the scene left the arm in. A straight bind arm
        // bends its forearm toward the character's front, like an elbow from a T-pose.
        ArmIk.Limits BindArm(Transform upper, Transform lower, Transform hand)
        {
            var limits = ArmIk.Limits.Default;
            if (upper == null || lower == null || hand == null)
                return limits;
            if (!BindPose(upper, out Vector3 upperPos, out Quaternion upperRot)
                || !BindPose(lower, out Vector3 lowerPos, out Quaternion lowerRot)
                || !BindPose(hand, out Vector3 handPos, out Quaternion handRot))
                return limits;
            Vector3 upperDir = lowerPos - upperPos;
            Vector3 lowerDir = handPos - lowerPos;
            Vector3 hinge = Vector3.Angle(upperDir, lowerDir) > 10f
                ? Vector3.Cross(upperDir, lowerDir)
                : Vector3.Cross(upperDir, Vector3.forward);
            if (hinge.sqrMagnitude > 0.000001f)
                limits.hingeLocal = Quaternion.Inverse(upperRot) * hinge.normalized;
            limits.handBindLocal = Quaternion.Inverse(lowerRot) * handRot;
            limits.hasHandBind = true;
            limits.lowerBindLocal = Quaternion.Inverse(upperRot) * lowerRot;
            limits.hasLowerBind = true;
            return limits;
        }

        // Bone position/rotation at bind time in character space, from the first skin that uses it.
        bool BindPose(Transform bone, out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            if (character == null)
                return false;
            foreach (var skin in character.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (skin.sharedMesh == null)
                    continue;
                int index = System.Array.IndexOf(skin.bones, bone);
                var bindposes = skin.sharedMesh.bindposes;
                if (index < 0 || index >= bindposes.Length)
                    continue;
                Matrix4x4 m = character.worldToLocalMatrix * skin.transform.localToWorldMatrix * bindposes[index].inverse;
                position = m.GetColumn(3);
                rotation = Quaternion.LookRotation(m.GetColumn(2), m.GetColumn(1));
                return true;
            }
            return false;
        }

        Transform FindBone(string boneName)
        {
            if (character == null)
                return null;
            var bones = character.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i].name == boneName)
                    return bones[i];
            }
            return null;
        }

        void LateUpdate()
        {
            if (boundCharacter != character)
                BindBones();
            UpdatePointer();
            if (viewCamera == null || staff == null)
                return;

            Ray ray = viewCamera.ScreenPointToRay(new Vector3(
                pointerUv.x * Screen.width,
                pointerUv.y * Screen.height,
                0f));
            // The pointer only steers the caster. The laser itself leaves the staff tip along the staff, so it
            // sweeps with the arms (and their damping) instead of snapping to the pointer.
            Vector3 aimPoint;
            if (aimTarget != null)
            {
                aimPoint = aimTarget.position;
                hitName = aimTarget.name;
            }
            else if (Physics.Raycast(ray, out RaycastHit pointerHit, 40f, roomMask, QueryTriggerInteraction.Ignore))
            {
                aimPoint = pointerHit.point;
                hitName = pointerHit.collider.name;
            }
            else
            {
                aimPoint = ray.origin + ray.direction * 8f;
                hitName = "";
            }

            bool firing = IsFiring();

            // The body and staff follow a damped aim so phone jitter does not shake the whole caster.
            if (!hasSmoothedAim)
            {
                smoothedAim = aimPoint;
                hasSmoothedAim = true;
            }
            else
            {
                smoothedAim = Vector3.Lerp(smoothedAim, aimPoint, 1f - Mathf.Exp(-aimSharpness * Time.deltaTime));
            }

            if (character != null)
                PoseCaster(smoothedAim);
            else
                AimStaff(smoothedAim);

            var tip = staff.Find("Staff Tip");
            Vector3 tipPosition = tip != null ? tip.position : staff.TransformPoint(Vector3.up);
            Vector3 staffAxis = staff.up;
            // Start a little past the wand head so the glow never sits on the ornament.
            Vector3 beamStart = tipPosition + staffAxis * beamStartGap;
            bool hit = Physics.Raycast(beamStart, staffAxis, out RaycastHit info, 40f, roomMask, QueryTriggerInteraction.Ignore);
            Vector3 beamEnd = hit ? info.point : beamStart + staffAxis * 40f;

            FadeTrails();
            Firing = firing;
            HasHit = hit;
            HitPoint = hit ? info.point : beamEnd;
            HitCollider = hit ? info.collider : null;
            BeamOrigin = beamStart;
            BeamDirection = staffAxis;
            // Without the laser the rig only aims: nothing is drawn, scorched or lit here.
            firing &= showBeam;
            if (firing && hit && drawTrails)
                ExtendTrail(info);
            else
                trail = null;
            wasFiring = firing;

            if (hitMarker != null)
            {
                hitMarker.gameObject.SetActive(firing && hit);
                if (hit)
                    hitMarker.position = info.point + info.normal * 0.02f;
            }
            if (hitLight != null)
            {
                hitLight.enabled = firing && hit;
                if (hit)
                    hitLight.transform.position = info.point + info.normal * lightSurfaceOffset;
            }

            // SecondCaster assigns beamPrefab after AddComponent, i.e. after Awake already ran.
            if (beamInstance == null && beamPrefab != null)
                EnsureAssetBeam();
            if (beamInstance != null)
                UpdateAssetBeam(firing, beamStart, beamEnd, hit, hit ? info.normal : -staffAxis);
            else
            {
                SetBeamActive(firing);
                SetBeam(aimLine, beamStart, beamEnd);
                SetBeam(glowLine, beamStart, beamEnd);
            }
        }

        void PoseCaster(Vector3 aim)
        {
            // Without an animation driving the bones every frame, start from the rest pose so twists do not pile up.
            if (pose == null || !pose.isActiveAndEnabled || pose.idle == null)
            {
                for (int i = 0; i < twistBones.Length; i++)
                {
                    if (twistBones[i] != null)
                        twistBones[i].localRotation = restRotations[i];
                }
            }

            Vector3 up = Vector3.up;
            Vector3 from = chest != null ? chest.position : character.position + up;
            Vector3 toAim = aim - from;
            Vector3 flat = Vector3.ProjectOnPlane(toAim, up);
            if (flat.sqrMagnitude < 0.0001f)
                flat = character.forward;
            float yaw = Mathf.Clamp(Vector3.SignedAngle(character.forward, flat, up), -maxYaw, maxYaw);
            float pitch = Mathf.Clamp(Mathf.Atan2(toAim.y, flat.magnitude) * Mathf.Rad2Deg, -maxPitch, maxPitch);

            // Spread the turn up the spine like the cutscene breathing rig: the chest carries most of it.
            float bodyYaw = yaw * bodyFollow;
            float bodyPitch = pitch * bodyFollow * 0.5f;
            Twist(spine, 0.25f, bodyYaw, bodyPitch);
            Twist(chest, 0.35f, bodyYaw, bodyPitch);
            Twist(upperChest, 0.4f, bodyYaw, bodyPitch);
            Twist(head, 1f, (yaw - bodyYaw) * headFollow, (pitch - bodyPitch) * headFollow);

            // Both arms reach straight toward the pointer. The right hand is nearer the tip, the left hand behind it on the shaft.
            Vector3 bodyForward = Quaternion.AngleAxis(bodyYaw, up) * character.forward;
            // "Outward" from the lead shoulder: right for a right-handed caster, left for a left-handed one.
            Vector3 bodyRight = Vector3.Cross(up, bodyForward) * (leftHanded ? -1f : 1f);
            Vector3 shoulder = rightUpper != null ? rightUpper.position : from;
            Vector3 aimDir = aim - shoulder;
            if (aimDir.sqrMagnitude < 0.0001f)
                aimDir = bodyForward;
            aimDir.Normalize();
            // Keep the elbow comfortably bent: a grip at full reach locked the arm straight and left the forearm
            // running along the staff, which folded the wrist back.
            float reach = armLength > 0.01f ? armLength * armExtension : 0.4f;
            Vector3 hold = shoulder + aimDir * reach;
            // Hold it a little under the shoulder line: with the staff slid back through the hands, a grip at eye
            // level ran the butt through the head when aiming across the body.
            hold += -up * holdDrop + bodyRight * holdSide;
            AimStaff(aim, hold);

            Vector3 axis = staff.up;
            float poleScale = Mathf.Max(armLength, 0.2f);
            // Elbows hang down and a little out and back, far enough from the shoulder that the hint never sits on
            // the shoulder-wrist line (which is what made the elbow flip when aiming down or across).
            Vector3 rightPole = shoulder + (-up * elbowDrop + bodyRight * elbowOut - bodyForward * 0.15f) * poleScale;
            leadLimits.softReach = softReach;
            leadLimits.maxOblique = gripOblique;
            leadLimits.forearmTwist = forearmTwist;
            if (rightGrip != null)
            {
                ArmIk.SolveGrip(rightUpper, rightLower, rightHand, rightGrip.position, axis, rightPole, acrossLocal,
                    FistOffset(), fingerLocal, leadLimits);
                CurlFingers();
            }

            if (useLeftHand && leftGrip != null && leftUpper != null && rightGrip != null)
            {
                leftGrip.position = rightGrip.position - axis * supportGap;
                Vector3 leftPole = leftUpper.position
                    + (-up * elbowDrop - bodyRight * elbowOut * 0.6f - bodyForward * 0.15f) * poleScale;
                supportLimits.softReach = softReach;
                supportLimits.maxOblique = supportGripOblique;
                supportLimits.forearmTwist = forearmTwist;
                ArmIk.SolveGrip(leftUpper, leftLower, leftHand, leftGrip.position, axis, leftPole, supportAcrossLocal,
                    SupportFistOffset(), supportFingerLocal, supportLimits);
                CurlHand(leftHand, supportFingerLocal, supportPalmLocal, supportFingerBones, supportFingerRest,
                    supportThumb, supportThumbRest, supportFingerCurl, !leftHanded);
            }
        }

        Vector3 SupportFistOffset()
        {
            return (supportFingerLocal * 0.8f + supportPalmLocal * 0.5f) * supportHandLength;
        }

        // Where the staff passes through the closed fist, in hand space: past the knuckles, off the palm.
        Vector3 FistOffset()
        {
            return (fingerLocal * 0.8f + palmLocal * 0.5f) * handLength;
        }

        // Close the right hand around the staff. Starts from the rest pose every frame so it never accumulates.
        void CurlFingers()
        {
            CurlHand(rightHand, fingerLocal, palmLocal, fingerBones, fingerRest, thumb, thumbRest, fingerCurl, leftHanded);
        }

        static void CurlHand(Transform hand, Vector3 fingerLocal, Vector3 palmLocal, Transform[] bones, Quaternion[] rest,
            Transform thumb, Quaternion thumbRest, float curl, bool leftSide)
        {
            if (hand == null || bones == null || rest == null)
                return;
            // A positive turn about finger x palm swings the fingertips toward the palm, for either hand.
            Vector3 curlAxis = hand.rotation * Vector3.Cross(fingerLocal, palmLocal);
            for (int i = 0; i < bones.Length; i++)
            {
                var bone = bones[i];
                if (bone == null)
                    continue;
                bone.localRotation = rest[i];
                float angle = JointCurl[i % JointNames.Length] * curl;
                bone.rotation = Quaternion.AngleAxis(angle, curlAxis) * bone.rotation;
            }
            if (thumb != null)
            {
                thumb.localRotation = thumbRest;
                float thumbTurn = 35f * curl * (leftSide ? -1f : 1f);
                thumb.rotation = Quaternion.AngleAxis(thumbTurn, hand.rotation * fingerLocal) * thumb.rotation;
            }
        }

        void Twist(Transform bone, float share, float yaw, float pitch)
        {
            if (bone == null)
                return;
            Vector3 right = Quaternion.AngleAxis(yaw, Vector3.up) * character.right;
            bone.rotation = Quaternion.AngleAxis(yaw * share, Vector3.up)
                * Quaternion.AngleAxis(-pitch * share, right)
                * bone.rotation;
        }

        void AimStaff(Vector3 aim)
        {
            AimStaff(aim, staff.position, false);
        }

        void AimStaff(Vector3 aim, Vector3 hold)
        {
            AimStaff(aim, hold, true);
        }

        // Points the staff at aim and, when place is set, slides it so the right grip sits on hold.
        void AimStaff(Vector3 aim, Vector3 hold, bool place)
        {
            Vector3 direction = aim - hold;
            if (place && direction.sqrMagnitude > 0.0001f && staffUpright > 0f)
                direction = Vector3.Slerp(direction.normalized, Vector3.up, staffUpright);
            if (direction.sqrMagnitude > 0.0001f)
            {
                Vector3 side = Vector3.Cross(character != null ? character.right : Vector3.right, direction);
                if (side.sqrMagnitude < 0.0001f)
                    side = Vector3.Cross(Vector3.up, direction);
                staff.rotation = Quaternion.LookRotation(side, direction);
            }
            if (place && rightGrip != null)
                staff.position += hold - rightGrip.position;
        }

        bool IsFiring()
        {
            var session = FindSession(phoneSlot);
            if (session != null)
                return session.Model.Pressed;
            if (mouseMode)
                return MousePressed();
            return false;
        }

        bool MousePressed()
        {
            var mouse = Mouse.current;
            if (mouse == null)
                return false;
            return mouseButton == MouseFireButton.Right ? mouse.rightButton.isPressed : mouse.leftButton.isPressed;
        }

        void UpdateAssetBeam(bool firing, Vector3 start, Vector3 end, bool hit, Vector3 normal)
        {
            if (aimLine != null)
                aimLine.enabled = false;
            if (glowLine != null)
                glowLine.enabled = false;
            if (impactRoot != null && impactRoot.gameObject.activeSelf != (firing && hit))
                impactRoot.gameObject.SetActive(firing && hit);
            if (!firing)
            {
                beamInstance.SetActive(false);
                return;
            }
            Vector3 along = start - end;
            float length = along.magnitude;
            if (length < 0.05f)
                return;

            // Impact at the pivot, strokes running AssetBeamLength up local +Y to the staff. The strokes are
            // stretched billboards: Unity sizes them from the uniform scale only, so scale uniformly to the length
            // and hand the width back through the start size, keeping the beam equally thick at any distance.
            float scale = length / AssetBeamLength;
            beamInstance.transform.SetPositionAndRotation(end, Quaternion.FromToRotation(Vector3.up, along / length));
            beamInstance.transform.localScale = Vector3.one * scale;
            float widthFix = Mathf.Clamp(beamThickness / scale, 0.1f, 4f);
            for (int i = 0; i < strokeSystems.Length; i++)
            {
                var main = strokeSystems[i].main;
                main.startSizeXMultiplier = strokeBaseWidth[i].x * widthFix;
                main.startSizeZMultiplier = strokeBaseWidth[i].y * widthFix;
            }

            if (impactRoot != null && hit)
            {
                impactRoot.SetPositionAndRotation(end, Quaternion.FromToRotation(Vector3.up, normal));
                impactRoot.localScale = Vector3.one * impactSize;
            }
            // Activating restarts the looping systems (playOnAwake), so each press starts a fresh beam.
            if (!beamInstance.activeSelf)
                beamInstance.SetActive(true);
        }

        void SetBeamActive(bool active)
        {
            if (aimLine != null)
                aimLine.enabled = active;
            if (glowLine != null)
                glowLine.enabled = active;
        }

        void ExtendTrail(RaycastHit info)
        {
            Vector3 point = info.point + info.normal * 0.015f;
            if (trail == null || !trailHasPoint)
            {
                BeginTrail(point);
                return;
            }
            if ((point - lastTrailPoint).sqrMagnitude < 0.0006f)
                return;
            // A very long hold would grow one LineRenderer forever; continue it as a new stroke instead.
            if (trail.positionCount >= MaxTrailPoints)
            {
                Vector3 joint = lastTrailPoint;
                BeginTrail(joint);
            }
            trail.positionCount++;
            trail.SetPosition(trail.positionCount - 1, point);
            lastTrailPoint = point;
        }

        // Strokes and the time each was last drawn on, so old ones can thin out and go.
        readonly System.Collections.Generic.List<(LineRenderer line, float time)> strokes = new System.Collections.Generic.List<(LineRenderer, float)>();

        void FadeTrails()
        {
            if (trailLifetime <= 0f)
                return;
            for (int i = strokes.Count - 1; i >= 0; i--)
            {
                var (line, time) = strokes[i];
                if (line == null)
                {
                    strokes.RemoveAt(i);
                    continue;
                }
                if (line == trail)
                {
                    strokes[i] = (line, Time.time);
                    continue;
                }
                float age = Time.time - time - trailLifetime;
                if (age <= 0f)
                    continue;
                float k = Mathf.Clamp01(age / Mathf.Max(0.01f, trailFade));
                line.widthMultiplier = 0.035f * (1f - k * k);
                if (k >= 1f)
                {
                    strokes.RemoveAt(i);
                    Destroy(line.gameObject);
                }
            }
        }

        void BeginTrail(Vector3 point)
        {
            if (trailMaterial == null)
                trailMaterial = CreateTrailMaterial(trailColor);
            var lineObject = new GameObject("Scorch Line");
            lineObject.transform.SetParent(trailRoot, false);
            trail = lineObject.AddComponent<LineRenderer>();
            // sharedMaterial: .material would clone a new material for every stroke.
            trail.sharedMaterial = trailMaterial;
            trail.useWorldSpace = true;
            trail.widthMultiplier = 0.035f;
            trail.numCornerVertices = 4;
            trail.numCapVertices = 4;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.positionCount = 2;
            trail.SetPosition(0, point);
            trail.SetPosition(1, point);
            strokes.Add((trail, Time.time));
            lastTrailPoint = point;
            trailHasPoint = true;
            // Destroy() only happens at the end of the frame, so childCount does not drop inside this loop.
            // Detach the old stroke first, otherwise the loop never ends and the game freezes on the 17th stroke.
            while (trailRoot.childCount > MaxTrails)
            {
                var oldest = trailRoot.GetChild(0);
                oldest.SetParent(null, false);
                Destroy(oldest.gameObject);
            }
        }

        static Material CreateTrailMaterial(Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            var material = new Material(shader);
            material.color = color;
            material.SetColor("_BaseColor", color);
            return material;
        }

        static void SetBeam(LineRenderer line, Vector3 from, Vector3 to)
        {
            if (line == null)
                return;
            line.positionCount = 2;
            line.SetPosition(0, from);
            line.SetPosition(1, to);
        }

        PhonePointer.PhoneSession FindSession(int slot)
        {
            var link = PhoneLink.Instance;
            var receiver = link != null ? link.Receiver : null;
            if (receiver == null)
                return null;
            var sessions = receiver.Sessions;
            for (int i = 0; i < sessions.Count; i++)
            {
                var session = sessions[i];
                if (session.ConnectionStatus != PhonePointer.ConnectionStatus.Connected)
                    continue;
                if (session.PlayerSlot == slot)
                    return session;
            }
            return null;
        }

        void UpdatePointer()
        {
            var session = FindSession(phoneSlot);
            Vector2 phoneUv;
            if (session != null && session.TryGetPointerNormalized(out phoneUv))
            {
                pointerUv = phoneUv;
                hasPointerSample = true;
                pointerVisible = true;
                return;
            }

            if (session != null && hasPointerSample &&
                session.Status == PhonePointer.PointerStatus.OutsideScreen)
            {
                pointerVisible = true;
                return;
            }

            if (mouseMode && session == null)
            {
                if (!mouseAimWhileFiring || MousePressed())
                    ApplyMouse();
                else
                    pointerVisible = hasPointerSample;
                return;
            }

            pointerVisible = hasPointerSample;
        }

        void ApplyMouse()
        {
            if (Mouse.current == null)
            {
                pointerVisible = false;
                return;
            }

            Vector2 mouse = Mouse.current.position.ReadValue();
            pointerUv = new Vector2(
                Mathf.Clamp01(mouse.x / Mathf.Max(1f, Screen.width)),
                Mathf.Clamp01(mouse.y / Mathf.Max(1f, Screen.height)));
            pointerVisible = true;
        }

        void OnGUI()
        {
            if (!pointerVisible)
                return;

            float x = pointerUv.x * Screen.width;
            float y = (1f - pointerUv.y) * Screen.height;
            var color = phoneSlot == 0 ? new Color(1f, 0.82f, 0.3f, 1f) : new Color(0.45f, 0.75f, 1f, 1f);
            // Tightens and brightens while firing.
            float size = (Firing ? 34f : 42f) * AiHackathon.Game.GameGui.Scale;
            if (!Firing) color.a = 0.8f;
            AiHackathon.Game.GameGui.Crosshair(pointerUv, color, size);
            if (showHitLabel && !string.IsNullOrEmpty(hitName))
                GUI.Label(new Rect(x + 16f, y - 10f, 240f, 24f), hitName);
        }
    }
}
