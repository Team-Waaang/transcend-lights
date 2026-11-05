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
        [Tooltip("누르고 있는 동안 레이저 이펙트를 다시 쏘는 주기(초).")]
        public float beamLoopPeriod = 0.45f;
        [Tooltip("레이저 굵기. 거리와 상관없이 일정합니다 (이펙트 원본 대비 배율).")]
        public float beamThickness = 0.12f;
        [Tooltip("맞은 자리 이펙트 크기 (이펙트 원본 대비 배율).")]
        public float impactSize = 0.06f;
        [Tooltip("레이저가 지팡이 끝에서 얼마나 떨어져서 시작할지(m).")]
        public float beamStartGap = 0.04f;
        [Tooltip("오른손 손가락을 지팡이에 감는 정도.")]
        [Range(0f, 1.5f)] public float fingerCurl = 1f;

        static readonly string[] FingerNames = { "Index", "Middle", "Ring", "Pinky" };
        static readonly string[] JointNames = { "Proximal", "Intermediate", "Distal" };
        static readonly float[] JointCurl = { 55f, 80f, 55f };

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
            ToonLook.ApplyScene();
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
        void BindBones()
        {
            boundCharacter = character;
            rightUpper = FindBone("Right_UpperArm");
            rightLower = FindBone("Right_LowerArm");
            rightHand = FindBone("Right_Hand");
            leftUpper = FindBone("Left_UpperArm");
            leftLower = FindBone("Left_LowerArm");
            leftHand = FindBone("Left_Hand");
            spine = FindBone("Spine");
            chest = FindBone("Chest");
            upperChest = FindBone("UpperChest");
            head = FindBone("Head");
            var middle = FindBone("Right_MiddleProximal");
            var index = FindBone("Right_IndexProximal");
            var pinky = FindBone("Right_PinkyProximal");
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
                palmLocal = Vector3.Cross(fingerLocal, acrossLocal);
            }
            fingerBones = new Transform[FingerNames.Length * JointNames.Length];
            for (int f = 0; f < FingerNames.Length; f++)
                for (int j = 0; j < JointNames.Length; j++)
                    fingerBones[f * JointNames.Length + j] = FindBone("Right_" + FingerNames[f] + JointNames[j]);
            fingerRest = new Quaternion[fingerBones.Length];
            for (int i = 0; i < fingerBones.Length; i++)
                fingerRest[i] = fingerBones[i] != null ? fingerBones[i].localRotation : Quaternion.identity;
            thumb = FindBone("Right_ThumbProximal");
            thumbRest = thumb != null ? thumb.localRotation : Quaternion.identity;
            twistBones = new[] { spine, chest, upperChest, head, rightUpper, rightLower, rightHand, leftUpper, leftLower, leftHand };
            restRotations = new Quaternion[twistBones.Length];
            for (int i = 0; i < twistBones.Length; i++)
                restRotations[i] = twistBones[i] != null ? twistBones[i].localRotation : Quaternion.identity;
            pose = character != null ? character.GetComponent<CasterPose>() : null;
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
            if (Physics.Raycast(ray, out RaycastHit pointerHit, 40f, roomMask, QueryTriggerInteraction.Ignore))
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

            if (firing && hit)
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
            Vector3 bodyRight = Vector3.Cross(up, bodyForward);
            Vector3 shoulder = rightUpper != null ? rightUpper.position : from;
            Vector3 aimDir = aim - shoulder;
            if (aimDir.sqrMagnitude < 0.0001f)
                aimDir = bodyForward;
            aimDir.Normalize();
            float reach = armLength * 0.98f - FistOffset().magnitude;
            if (reach < 0.05f)
                reach = Mathf.Max(0.2f, armLength * 0.9f);
            Vector3 hold = shoulder + aimDir * reach;
            AimStaff(aim, hold);

            Vector3 axis = staff.up;
            Vector3 rightPole = shoulder + bodyRight * 0.12f - up * 0.08f;
            if (rightGrip != null)
            {
                ArmIk.SolveGrip(rightUpper, rightLower, rightHand, rightGrip.position, axis, rightPole, acrossLocal, FistOffset());
                CurlFingers();
            }

            if (useLeftHand && leftGrip != null && leftUpper != null && rightGrip != null)
            {
                leftGrip.position = rightGrip.position - axis * 0.16f;
                Vector3 leftPole = leftUpper.position - bodyRight * 0.12f - up * 0.08f;
                ArmIk.Solve(leftUpper, leftLower, leftHand, leftGrip.position, leftPole);
            }
        }

        // Where the staff passes through the closed fist, in hand space: past the knuckles, off the palm.
        Vector3 FistOffset()
        {
            return (fingerLocal * 0.85f + palmLocal * 0.35f) * handLength;
        }

        // Close the right hand around the staff. Starts from the rest pose every frame so it never accumulates.
        void CurlFingers()
        {
            if (rightHand == null || fingerBones == null)
                return;
            // A positive turn about index->pinky swings the fingertips toward the palm.
            Vector3 curlAxis = rightHand.rotation * -acrossLocal;
            for (int i = 0; i < fingerBones.Length; i++)
            {
                var bone = fingerBones[i];
                if (bone == null)
                    continue;
                bone.localRotation = fingerRest[i];
                float angle = JointCurl[i % JointNames.Length] * fingerCurl;
                bone.rotation = Quaternion.AngleAxis(angle, curlAxis) * bone.rotation;
            }
            if (thumb != null)
            {
                thumb.localRotation = thumbRest;
                thumb.rotation = Quaternion.AngleAxis(35f * fingerCurl, rightHand.rotation * fingerLocal) * thumb.rotation;
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
            if (phoneSlot == 0 && mouseMode)
                return Mouse.current != null && Mouse.current.leftButton.isPressed;
            return false;
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

        void BeginTrail(Vector3 point)
        {
            if (trailMaterial == null)
                trailMaterial = CreateTrailMaterial();
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

        static Material CreateTrailMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            var material = new Material(shader);
            var color = new Color(1.5f, 0.78f, 0.28f, 1f);
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
            int index = 0;
            var sessions = receiver.Sessions;
            for (int i = 0; i < sessions.Count; i++)
            {
                var session = sessions[i];
                if (session.ConnectionStatus != PhonePointer.ConnectionStatus.Connected)
                    continue;
                if (index == slot)
                    return session;
                index++;
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

            if (phoneSlot == 0 && mouseMode && session == null)
            {
                ApplyMouse();
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
            Color previous = GUI.color;
            GUI.color = phoneSlot == 0
                ? new Color(1f, 0.82f, 0.2f, 1f)
                : new Color(0.45f, 0.75f, 1f, 1f);
            GUI.DrawTexture(new Rect(x - 14f, y - 1.5f, 28f, 3f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(x - 1.5f, y - 14f, 3f, 28f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(x - 3f, y - 3f, 6f, 6f), Texture2D.whiteTexture);
            GUI.color = previous;
            if (!string.IsNullOrEmpty(hitName))
                GUI.Label(new Rect(x + 16f, y - 10f, 240f, 24f), hitName);
        }
    }
}
