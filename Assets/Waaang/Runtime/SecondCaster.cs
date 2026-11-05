using UnityEngine;

namespace Waaang
{
    // Brings in the second caster (Nox, built into the Game scene by Waaang > Add Nox 2P) when a second phone
    // connects, and moves the camera between the solo over-the-shoulder shot and a wider shot behind both players.
    public sealed class SecondCaster : MonoBehaviour
    {
        public WandPointerRig primary;
        [Tooltip("씬에 미리 만들어 둔 2P 리그 (꺼진 상태). 비어 있으면 1P를 복제합니다.")]
        public WandPointerRig second;
        public float sideOffset = 2.4f;

        [Header("Camera")]
        [Tooltip("2인일 때 카메라 위치/회전. 에디터 메뉴가 두 캐릭터 기준으로 채웁니다.")]
        public Vector3 duoCameraPosition;
        public Vector3 duoCameraEuler;
        public float duoCameraFov = 56f;
        [Tooltip("카메라가 1인/2인 구도 사이를 옮겨가는 속도.")]
        public float cameraSharpness = 3f;

        [Header("Testing")]
        [Tooltip("휴대폰 없이 2P를 켜서 확인할 때 사용합니다.")]
        public bool forceSecondPlayer;

        Camera viewCamera;
        Vector3 soloPosition;
        Quaternion soloRotation;
        float soloFov;
        float duoBlend;

        void Start()
        {
            viewCamera = primary != null ? primary.viewCamera : null;
            if (viewCamera == null)
                viewCamera = Camera.main;
            if (viewCamera != null)
            {
                soloPosition = viewCamera.transform.position;
                soloRotation = viewCamera.transform.rotation;
                soloFov = viewCamera.fieldOfView;
            }
            if (second != null)
                SetSecondActive(false);
        }

        void LateUpdate()
        {
            bool wanted = forceSecondPlayer || SecondPhoneConnected();
            bool active = second != null && second.gameObject.activeSelf;
            if (wanted && !active && primary != null && primary.character != null)
            {
                if (second == null)
                    Clone();
                SetSecondActive(true);
            }
            else if (!wanted && active)
            {
                SetSecondActive(false);
            }
            MoveCamera(wanted && second != null);
        }

        void MoveCamera(bool duo)
        {
            if (viewCamera == null || duoCameraPosition == Vector3.zero)
                return;
            float k = 1f - Mathf.Exp(-cameraSharpness * Time.deltaTime);
            duoBlend = Mathf.Lerp(duoBlend, duo ? 1f : 0f, k);
            float t = Mathf.SmoothStep(0f, 1f, duoBlend);
            var cam = viewCamera.transform;
            cam.SetPositionAndRotation(
                Vector3.Lerp(soloPosition, duoCameraPosition, t),
                Quaternion.Slerp(soloRotation, Quaternion.Euler(duoCameraEuler), t));
            viewCamera.fieldOfView = Mathf.Lerp(soloFov, duoCameraFov, t);
        }

        void SetSecondActive(bool on)
        {
            if (second == null)
                return;
            if (second.character != null)
                second.character.gameObject.SetActive(on);
            if (second.aimLine != null)
                second.aimLine.transform.root.gameObject.SetActive(on);
            second.gameObject.SetActive(on);
        }

        static bool SecondPhoneConnected()
        {
            var link = PhoneLink.Instance;
            var receiver = link != null ? link.Receiver : null;
            if (receiver == null)
                return false;
            int connected = 0;
            var sessions = receiver.Sessions;
            for (int i = 0; i < sessions.Count; i++)
            {
                if (sessions[i].ConnectionStatus == PhonePointer.ConnectionStatus.Connected)
                    connected++;
            }
            return connected >= 2;
        }

        // Fallback for scenes without a prebuilt second caster: a copy of the first one, to the right.
        void Clone()
        {
            var character = Instantiate(primary.character.gameObject);
            character.name = primary.character.name + " 2";
            character.transform.position = primary.character.position + primary.character.right * sideOffset;
            character.transform.rotation = primary.character.rotation;
            ToonLook.Apply(character);

            var staff = character.transform.Find("Staff");
            var rigObject = new GameObject("Wand Pointer 2");
            rigObject.SetActive(false);
            var copy = rigObject.AddComponent<WandPointerRig>();
            copy.phoneSlot = 1;
            copy.mouseMode = false;
            copy.viewCamera = primary.viewCamera;
            copy.character = character.transform;
            copy.staff = staff;
            copy.rightGrip = staff != null ? staff.Find("Right Grip") : null;
            copy.leftGrip = staff != null ? staff.Find("Left Grip") : null;
            copy.hitMarker = CreateMarker(primary.hitMarker);
            copy.hitLight = CreateLight(primary.hitLight);
            copy.roomMask = primary.roomMask;
            copy.lightSurfaceOffset = primary.lightSurfaceOffset;
            copy.beamPrefab = primary.beamPrefab;
            CopySettings(primary, copy);
            second = copy;
        }

        public static void CopySettings(WandPointerRig from, WandPointerRig to)
        {
            to.holdOffset = from.holdOffset;
            to.holdReach = from.holdReach;
            to.staffUpright = from.staffUpright;
            to.bodyFollow = from.bodyFollow;
            to.headFollow = from.headFollow;
            to.maxYaw = from.maxYaw;
            to.maxPitch = from.maxPitch;
            to.aimSharpness = from.aimSharpness;
            to.useLeftHand = from.useLeftHand;
            to.fingerCurl = from.fingerCurl;
            to.gripAlongStaff = from.gripAlongStaff;
            to.holdDrop = from.holdDrop;
            to.holdSide = from.holdSide;
            to.beamLoopPeriod = from.beamLoopPeriod;
            to.beamThickness = from.beamThickness;
            to.impactSize = from.impactSize;
            to.beamStartGap = from.beamStartGap;
        }

        static Transform CreateMarker(Transform source)
        {
            var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = "Hit Marker 2";
            marker.transform.localScale = source != null ? source.localScale : Vector3.one * 0.08f;
            var collider = marker.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);
            var renderer = marker.GetComponent<Renderer>();
            if (source != null)
            {
                var sourceRenderer = source.GetComponent<Renderer>();
                if (sourceRenderer != null)
                    renderer.sharedMaterial = sourceRenderer.sharedMaterial;
            }
            marker.SetActive(false);
            return marker.transform;
        }

        static Light CreateLight(Light source)
        {
            var go = new GameObject("Hit Light 2");
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = source != null ? source.color : new Color(1f, 0.72f, 0.35f);
            light.intensity = source != null ? source.intensity : 6f;
            light.range = source != null ? source.range : 3.2f;
            light.shadows = LightShadows.None;
            light.enabled = false;
            return light;
        }
    }
}
