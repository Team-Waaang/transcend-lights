using UnityEngine;
using UnityEngine.Rendering;
using Waaang;

namespace AiHackathon.Game
{
    /// <summary>
    /// A torch beam from a caster's wand (Period 2, the dark): while the player fires, a spot light leaves the wand
    /// tip along the staff, with a soft visible shaft and dust drifting in it. The rig still aims the staff at the
    /// pointer, it just draws no laser (<see cref="WandPointerRig.showBeam"/> off). Game logic asks
    /// <see cref="Lights"/> whether a point is in the beam.
    /// </summary>
    [DefaultExecutionOrder(50)]
    public class WandLight : MonoBehaviour
    {
        public WandPointerRig rig;
        public Color color = new Color(1f, 0.85f, 0.6f);
        [Tooltip("Spot light intensity when fully on.")]
        public float intensity = 45f;
        public float range = 14f;
        [Range(10f, 80f)] public float spotAngle = 34f;
        [Tooltip("Share of the spot's half-angle that counts as lit for the game (the bright middle of the pool).")]
        [Range(0.3f, 1f)] public float hitCone = 0.8f;
        public float onTime = 0.1f;
        public float offTime = 0.25f;
        [Tooltip("Light stays on without firing (for cutscenes and tests).")]
        public bool alwaysOn;
        [Tooltip("What blocks the beam (walls, pillars).")]
        public LayerMask blockMask = ~(1 << 2);

        [Header("Look")]
        public Material coneMaterial;
        public float coneIntensity = 0.42f;
        public Material dustMaterial;

        public float Level { get; private set; }
        public bool IsOn => Level > 0.35f;
        public Vector3 Origin { get; private set; }
        public Vector3 Direction { get; private set; } = Vector3.forward;
        /// <summary>How far the beam gets before it meets a wall.</summary>
        public float Reach { get; private set; }

        Light spot;
        Transform cone;
        Material coneInstance;
        ParticleSystem dust;
        static Mesh coneMesh;
        float flickerSeed;

        void Awake()
        {
            flickerSeed = Random.value * 50f;
            var lightGo = new GameObject("Wand Beam Light");
            lightGo.transform.SetParent(transform, false);
            spot = lightGo.AddComponent<Light>();
            spot.type = LightType.Spot;
            spot.color = color;
            spot.range = range;
            spot.spotAngle = spotAngle;
            spot.innerSpotAngle = spotAngle * 0.45f;
            spot.intensity = 0f;
            spot.shadows = LightShadows.Soft;
            spot.shadowStrength = 0.85f;
            spot.enabled = false;

            if (coneMaterial == null)
            {
                var shader = Shader.Find("AiHackathon/LightCone");
                if (shader != null) coneMaterial = new Material(shader);
            }
            if (coneMaterial != null)
            {
                var coneGo = new GameObject("Wand Beam Shaft");
                coneGo.transform.SetParent(lightGo.transform, false);
                coneGo.AddComponent<MeshFilter>().sharedMesh = ConeMesh();
                var mr = coneGo.AddComponent<MeshRenderer>();
                coneInstance = new Material(coneMaterial);
                mr.sharedMaterial = coneInstance;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                cone = coneGo.transform;
                coneGo.SetActive(false);
            }
            dust = BuildDust(lightGo.transform);
        }

        void OnDisable()
        {
            Level = 0f;
            if (spot != null) spot.enabled = false;
            if (cone != null) cone.gameObject.SetActive(false);
        }

        // After the rig has posed the staff (LateUpdate), so the beam leaves the wand where it is this frame.
        void LateUpdate()
        {
            if (rig == null) return;
            bool want = alwaysOn || (rig.isActiveAndEnabled && rig.Firing);
            float rate = want ? 1f / Mathf.Max(0.01f, onTime) : -1f / Mathf.Max(0.01f, offTime);
            Level = Mathf.Clamp01(Level + rate * Time.deltaTime);

            Origin = rig.BeamOrigin;
            Direction = rig.BeamDirection.sqrMagnitude > 0.5f ? rig.BeamDirection.normalized : Vector3.forward;
            Reach = Physics.Raycast(Origin, Direction, out var hit, range, blockMask, QueryTriggerInteraction.Ignore) ? hit.distance : range;

            var t = spot.transform;
            t.SetPositionAndRotation(Origin, Quaternion.LookRotation(Direction));
            // A soft flicker, like a flame held in the wand.
            float flicker = 1f + (Mathf.PerlinNoise(Time.time * 7f, flickerSeed) - 0.5f) * 0.12f;
            float k = Level * Level * (3f - 2f * Level);
            spot.intensity = intensity * k * flicker;
            spot.color = color;
            spot.spotAngle = spotAngle;
            spot.enabled = k > 0.001f;

            if (cone != null)
            {
                cone.gameObject.SetActive(k > 0.001f);
                float length = Mathf.Max(0.3f, Mathf.Min(Reach + 0.4f, range * 0.85f));
                float radius = Mathf.Tan(spotAngle * 0.5f * 0.72f * Mathf.Deg2Rad) * length;
                cone.localScale = new Vector3(radius, radius, length);
                coneInstance.SetColor("_Color", color);
                coneInstance.SetFloat("_Intensity", coneIntensity * k * flicker);
            }
            if (dust != null)
            {
                var emission = dust.emission;
                emission.enabled = k > 0.5f;
                var shape = dust.shape;
                shape.angle = spotAngle * 0.3f;
                shape.length = Mathf.Min(Reach, 7f);
                if (k > 0.5f && !dust.isPlaying) dust.Play();
            }
        }

        /// <summary>
        /// How strongly the beam falls on a sphere at <paramref name="point"/> (0 = not at all, 1 = dead centre).
        /// Walls and pillars in the way block it.
        /// </summary>
        public float Lights(Vector3 point, float radius)
        {
            if (Level < 0.35f) return 0f;
            Vector3 to = point - Origin;
            float dist = to.magnitude;
            if (dist < 0.01f || dist > range || dist > Reach + radius) return 0f;
            float half = spotAngle * 0.5f * hitCone + Mathf.Atan2(radius, dist) * Mathf.Rad2Deg;
            float angle = Vector3.Angle(Direction, to);
            if (angle > half) return 0f;
            if (Physics.Raycast(Origin, to / dist, Mathf.Max(0f, dist - radius), blockMask, QueryTriggerInteraction.Ignore)) return 0f;
            return Mathf.Clamp01(1.25f - angle / half) * Level;
        }

        ParticleSystem BuildDust(Transform parent)
        {
            var go = new GameObject("Wand Beam Dust");
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.duration = 2f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.008f, 0.022f);
            main.startColor = new Color(color.r * 2f, color.g * 2f, color.b * 2f, 0.55f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 160;
            var emission = ps.emission;
            emission.rateOverTime = 45f;
            emission.enabled = false;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.ConeVolume;
            shape.angle = spotAngle * 0.3f;
            shape.radius = 0.02f;
            shape.length = 5f;
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.08f;
            noise.frequency = 0.6f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.3f), new GradientAlphaKey(0f, 1f) });
            col.color = fade;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            if (dustMaterial != null) renderer.sharedMaterial = dustMaterial;
            else
            {
                var urp = GraphicsSettings.currentRenderPipeline;
                if (urp != null && urp.defaultParticleMaterial != null) renderer.sharedMaterial = urp.defaultParticleMaterial;
            }
            return ps;
        }

        // Unit cone along +Z: a small ring at the wand, radius 1 at z = 1. uv.y runs 0 → 1 along it.
        static Mesh ConeMesh()
        {
            if (coneMesh != null) return coneMesh;
            const int seg = 48;
            const float apex = 0.03f;
            var verts = new Vector3[(seg + 1) * 2];
            var normals = new Vector3[verts.Length];
            var uvs = new Vector2[verts.Length];
            var tris = new int[seg * 6];
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                var ring = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                verts[i * 2] = ring * apex;
                verts[i * 2 + 1] = ring + Vector3.forward;
                var n = new Vector3(ring.x, ring.y, -(1f - apex)).normalized;
                normals[i * 2] = normals[i * 2 + 1] = n;
                uvs[i * 2] = new Vector2(i / (float)seg, 0f);
                uvs[i * 2 + 1] = new Vector2(i / (float)seg, 1f);
            }
            for (int i = 0; i < seg; i++)
            {
                int a = i * 2, b = a + 1, c = a + 2, d = a + 3;
                int o = i * 6;
                tris[o] = a; tris[o + 1] = c; tris[o + 2] = b;
                tris[o + 3] = b; tris[o + 4] = c; tris[o + 5] = d;
            }
            coneMesh = new Mesh { name = "WandBeamCone", vertices = verts, normals = normals, uv = uvs, triangles = tris };
            coneMesh.bounds = new Bounds(new Vector3(0f, 0f, 0.5f), new Vector3(2f, 2f, 1f));
            return coneMesh;
        }
    }
}
