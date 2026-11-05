using UnityEngine;

namespace Waaang
{
    // Spawns a second caster on the right when a second phone is connected.
    public sealed class SecondCaster : MonoBehaviour
    {
        public WandPointerRig primary;
        public float sideOffset = 2.4f;
        WandPointerRig copy;

        void LateUpdate()
        {
            bool wanted = SecondPhoneConnected();
            if (wanted && copy == null && primary != null && primary.character != null)
                Spawn();
            else if (!wanted && copy != null)
                Despawn();
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

        void Spawn()
        {
            var character = Instantiate(primary.character.gameObject);
            character.name = primary.character.name + " 2";
            character.transform.position = primary.character.position + primary.character.right * sideOffset;
            character.transform.rotation = primary.character.rotation;
            ToonLook.Apply(character);

            var staff = character.transform.Find("Staff");
            var lines = CreateBeam(primary.aimLine, primary.glowLine);
            var marker = CreateMarker(primary.hitMarker);
            var light = CreateLight(primary.hitLight);

            var rigObject = new GameObject("Wand Pointer 2");
            copy = rigObject.AddComponent<WandPointerRig>();
            copy.phoneSlot = 1;
            copy.mouseMode = false;
            copy.viewCamera = primary.viewCamera;
            copy.character = character.transform;
            copy.staff = staff;
            copy.rightGrip = staff != null ? staff.Find("Right Grip") : null;
            copy.leftGrip = staff != null ? staff.Find("Left Grip") : null;
            copy.hitMarker = marker;
            copy.hitLight = light;
            copy.aimLine = lines[0];
            copy.glowLine = lines[1];
            copy.roomMask = primary.roomMask;
            copy.lightSurfaceOffset = primary.lightSurfaceOffset;
            copy.holdOffset = primary.holdOffset;
            copy.holdReach = primary.holdReach;
            copy.staffUpright = primary.staffUpright;
            copy.bodyFollow = primary.bodyFollow;
            copy.headFollow = primary.headFollow;
            copy.maxYaw = primary.maxYaw;
            copy.maxPitch = primary.maxPitch;
            copy.aimSharpness = primary.aimSharpness;
            copy.useLeftHand = primary.useLeftHand;
            copy.fingerCurl = primary.fingerCurl;
        }

        void Despawn()
        {
            if (copy.character != null)
                Destroy(copy.character.gameObject);
            if (copy.aimLine != null)
                Destroy(copy.aimLine.transform.root.gameObject);
            if (copy.hitMarker != null)
                Destroy(copy.hitMarker.gameObject);
            if (copy.hitLight != null)
                Destroy(copy.hitLight.gameObject);
            Destroy(copy.gameObject);
            copy = null;
        }

        static LineRenderer[] CreateBeam(LineRenderer sourceCore, LineRenderer sourceGlow)
        {
            var root = new GameObject("Laser 2");
            var core = root.AddComponent<LineRenderer>();
            var glowObject = new GameObject("Laser Glow");
            glowObject.transform.SetParent(root.transform, false);
            var glow = glowObject.AddComponent<LineRenderer>();
            CopyLine(sourceCore, core, 0.018f);
            CopyLine(sourceGlow, glow, 0.09f);
            core.enabled = false;
            glow.enabled = false;
            return new[] { core, glow };
        }

        static void CopyLine(LineRenderer source, LineRenderer target, float width)
        {
            target.useWorldSpace = true;
            target.positionCount = 2;
            target.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            target.receiveShadows = false;
            target.numCapVertices = 4;
            target.startWidth = width;
            target.endWidth = width * 0.6f;
            if (source != null && source.sharedMaterial != null)
                target.sharedMaterial = source.sharedMaterial;
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
