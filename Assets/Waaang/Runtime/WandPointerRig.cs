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
        [FormerlySerializedAs("useMouseWhenPhoneNotReady")]
        [Tooltip("켜면 휴대폰 연결 없이 마우스 위치로 포인터와 지팡이를 조작합니다.")]
        public bool mouseMode = true;

        Transform rightUpper, rightLower, rightHand;
        Transform leftUpper, leftLower, leftHand;
        Vector2 pointerUv = new Vector2(0.5f, 0.5f);
        bool hasPointerSample;
        bool pointerVisible;
        bool wasFiring;
        string hitName = "";
        Material scorchMaterial;
        Transform scorchRoot;
        const int MaxScorches = 48;

        void Awake()
        {
            rightUpper = FindBone("Right_UpperArm");
            rightLower = FindBone("Right_LowerArm");
            rightHand = FindBone("Right_Hand");
            leftUpper = FindBone("Left_UpperArm");
            leftLower = FindBone("Left_LowerArm");
            leftHand = FindBone("Left_Hand");
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
            scorchRoot = new GameObject("Scorch Marks").transform;
            scorchRoot.SetParent(transform, false);
            SetBeamActive(false);
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
            UpdatePointer();
            if (viewCamera == null || staff == null)
                return;

            Ray ray = viewCamera.ScreenPointToRay(new Vector3(
                pointerUv.x * Screen.width,
                pointerUv.y * Screen.height,
                0f));
            Vector3 aimPoint;
            bool hit = Physics.Raycast(ray, out RaycastHit info, 40f, roomMask, QueryTriggerInteraction.Ignore);
            if (hit)
            {
                aimPoint = info.point;
                hitName = info.collider.name;
            }
            else
            {
                aimPoint = ray.origin + ray.direction * 8f;
                hitName = "";
            }

            bool firing = IsFiring();
            if (firing && !wasFiring && hit)
                LeaveScorch(info);
            wasFiring = firing;

            Vector3 aimDirection = aimPoint - staff.position;
            if (aimDirection.sqrMagnitude > 0.0001f)
            {
                Vector3 side = Vector3.Cross(character != null ? character.right : Vector3.right, aimDirection);
                if (side.sqrMagnitude < 0.0001f)
                    side = Vector3.Cross(Vector3.up, aimDirection);
                staff.rotation = Quaternion.LookRotation(side, aimDirection);
            }

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

            var tip = staff.Find("Staff Tip");
            Vector3 tipPosition = tip != null ? tip.position : staff.TransformPoint(Vector3.up);
            SetBeamActive(firing);
            SetBeam(aimLine, tipPosition, aimPoint);
            SetBeam(glowLine, tipPosition, aimPoint);

            if (character != null)
            {
                Vector3 rightPole = rightUpper != null
                    ? rightUpper.position + character.right * 0.45f - character.forward * 0.2f
                    : staff.position;
                Vector3 leftPole = leftUpper != null
                    ? leftUpper.position - character.right * 0.45f - character.forward * 0.2f
                    : staff.position;
                if (rightGrip != null)
                    ArmIk.Solve(rightUpper, rightLower, rightHand, rightGrip.position, rightPole);
                if (leftGrip != null)
                    ArmIk.Solve(leftUpper, leftLower, leftHand, leftGrip.position, leftPole);
            }
        }

        bool IsFiring()
        {
            if (mouseMode)
                return Mouse.current != null && Mouse.current.leftButton.isPressed;
            var link = PhoneLink.Instance;
            return link != null && link.Receiver != null && link.Receiver.Model.Pressed;
        }

        void SetBeamActive(bool active)
        {
            if (aimLine != null)
                aimLine.enabled = active;
            if (glowLine != null)
                glowLine.enabled = active;
        }

        void LeaveScorch(RaycastHit info)
        {
            if (scorchMaterial == null)
                scorchMaterial = CreateScorchMaterial();
            var mark = GameObject.CreatePrimitive(PrimitiveType.Quad);
            mark.name = "Scorch";
            var collider = mark.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);
            mark.transform.SetParent(scorchRoot, true);
            mark.transform.position = info.point + info.normal * 0.012f;
            mark.transform.rotation = Quaternion.LookRotation(-info.normal);
            mark.transform.localScale = Vector3.one * 0.16f;
            var renderer = mark.GetComponent<Renderer>();
            renderer.sharedMaterial = scorchMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (scorchRoot.childCount > MaxScorches)
                Destroy(scorchRoot.GetChild(0).gameObject);
        }

        static Material CreateScorchMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            var material = new Material(shader);
            var color = new Color(1.4f, 0.72f, 0.25f, 0.9f);
            material.color = color;
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 1f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            material.SetInt("_ZWrite", 0);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
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

        void UpdatePointer()
        {
            if (mouseMode)
            {
                ApplyMouse();
                return;
            }

            var link = PhoneLink.Instance;
            var receiver = link != null ? link.Receiver : null;
            Vector2 phoneUv;
            if (receiver != null && receiver.TryGetPointerNormalized(out phoneUv))
            {
                pointerUv = phoneUv;
                hasPointerSample = true;
                pointerVisible = true;
                return;
            }

            // The phone ray left the screen. Keep the last point that was still on it.
            if (receiver != null && hasPointerSample &&
                receiver.Status == PhonePointer.PointerStatus.OutsideScreen)
            {
                pointerVisible = true;
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
            GUI.color = new Color(1f, 0.82f, 0.2f, 1f);
            GUI.DrawTexture(new Rect(x - 14f, y - 1.5f, 28f, 3f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(x - 1.5f, y - 14f, 3f, 28f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(x - 3f, y - 3f, 6f, 6f), Texture2D.whiteTexture);
            GUI.color = previous;
            if (!string.IsNullOrEmpty(hitName))
                GUI.Label(new Rect(x + 16f, y - 10f, 240f, 24f), hitName);
        }
    }
}
