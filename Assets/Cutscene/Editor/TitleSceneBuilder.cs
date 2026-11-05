using System.Collections.Generic;
using System.IO;
using AiHackathon.Cutscene;
using AiHackathon.Game;
using TMPro;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using static AiHackathon.Cutscene.Editor.CutsceneKit;
using Object = UnityEngine.Object;

namespace AiHackathon.Cutscene.Editor
{
    /// <summary>
    /// The main screen ("Transcend Lights"): Amelia and Nox on the academy's night terrace above a sea of clouds,
    /// under a starry sky and a big moon, the school's spires lit in the distance. A gold and a blue ribbon of light
    /// twine round the pair, a magic circle turns under their feet and a halo behind them, sky lanterns drift up from
    /// the valley, shooting stars cross now and then, and the camera drifts slowly: everything loops, so the screen
    /// stays alive while it waits. <see cref="TitleScreen"/> runs the logo and hands over to the connection screen.
    /// Every texture is painted here; rebuilding rewrites them and the materials in place.
    /// </summary>
    public static class TitleSceneBuilder
    {
        public const string Root = "Assets/Game/Title";
        public const string ScenePath = Root + "/Title.unity";
        const string TexDir = Root + "/Textures";
        const string TitleFontPath = "Assets/Parchment/Fonts/GowunBatang-Bold SDF.asset";
        const string BodyFontPath = "Assets/Parchment/Fonts/GowunBatang-Regular SDF.asset";
        public const string LogoText = "Transcend Lights";

        static readonly Color Gold = new Color(1f, 0.76f, 0.36f);
        static readonly Color Blue = new Color(0.42f, 0.72f, 1f);
        // Sky (sRGB, as painted). The fog is the horizon colour, so the far scene melts into the sky.
        static readonly Color Zenith = new Color(0.015f, 0.03f, 0.08f);
        static readonly Color High = new Color(0.05f, 0.1f, 0.2f);
        static readonly Color Horizon = new Color(0.26f, 0.34f, 0.46f);
        static readonly Color Below = new Color(0.15f, 0.2f, 0.29f);

        static readonly Vector3 CamPos = new Vector3(0f, 0.8f, 4.15f);
        static readonly Vector3 CamLook = new Vector3(0f, 1.02f, 0f);
        const float CamFov = 33f;
        static readonly Vector3 MoonPos = new Vector3(-17f, 11f, -41f);
        const float MoonRadius = 1.9f;
        // Facing the camera (+Z), turned a little toward each other; Amelia is on the left of the screen as in play.
        static readonly Vector3 AmeliaPos = new Vector3(0.66f, 0f, 0f);
        static readonly Vector3 NoxPos = new Vector3(-0.66f, 0f, -0.06f);
        const float AmeliaYaw = -14f, NoxYaw = 14f;

        [MenuItem("Tools/Periods/Build Title Scene")]
        static void BuildFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build();
        }

        public static string Build()
        {
            PeriodPoseClips.BuildTitle();
            PeriodSceneKit.EnsureProfiles();
            AssetDatabase.SaveAssets();
            AssetRoot = Root;
            PrepareFolders();
            EnsureFolder(TexDir);
            PaintTextures();

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // A single-mode NewScene unloads unreferenced assets: load everything by path from here on.
            var profiles = new[]
            {
                AssetDatabase.LoadAssetAtPath<CasterProfile>(PeriodSceneKit.ProfileDir + "/Amelia.asset"),
                AssetDatabase.LoadAssetAtPath<CasterProfile>(PeriodSceneKit.ProfileDir + "/Nox.asset"),
            };

            Atmosphere();
            var cam = TitleCameraRig();
            var world = new GameObject("World").transform;
            Terrace(world);
            Academy(world);
            Clouds(world);
            Moon(world);
            SkyLife(world);
            var circles = MagicCircles(world);

            var amelia = Character(profiles[0], PeriodPoseClips.AmeliaFbx, PeriodPoseClips.AmeliaWandFbx, "Right_", PeriodPoseClips.AmeliaWandLength,
                AmeliaPos, AmeliaYaw, "Amelia_Title", "Amelia");
            var nox = Character(profiles[1], PeriodPoseClips.NoxFbx, PeriodPoseClips.NoxWandFbx, "Left_", PeriodPoseClips.NoxWandLength,
                NoxPos, NoxYaw, "Nox_Title", "Nox");
            CharacterLights(amelia, nox);
            Ribbons(world);
            Lights(world);
            Look(world);

            var ui = BuildUi(cam);
            var screen = new GameObject("Title").AddComponent<TitleScreen>();
            screen.logo = ui.logo;
            screen.ornament = ui.ornament;
            screen.prompt = ui.prompt;
            screen.credit = ui.credit;
            screen.black = ui.black;
            screen.flash = ui.flash;
            screen.cameraRig = cam.GetComponent<TitleCamera>();

            AssetDatabase.SaveAssets();
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("[Title] Built " + ScenePath);
            return "built " + ScenePath + " (" + circles + " circles)";
        }

        // ------------------------------------------------------------------ sky, fog, camera

        static void Atmosphere()
        {
            var sky = Mat("Sky", "Skybox/Panoramic");
            sky.SetTexture("_MainTex", T("SkyPanorama"));
            sky.SetFloat("_Mapping", 1f);
            sky.SetFloat("_ImageType", 0f);
            sky.SetFloat("_Exposure", 1f);
            sky.SetFloat("_Rotation", 0f);
            sky.EnableKeyword("_MAPPING_LATITUDE_LONGITUDE_LAYOUT");
            sky.DisableKeyword("_MAPPING_6_FRAMES_LAYOUT");
            EditorUtility.SetDirty(sky);
            RenderSettings.skybox = sky;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = 0.011f;
            RenderSettings.fogColor = Horizon.linear;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.2f, 0.27f, 0.4f);
            RenderSettings.ambientEquatorColor = new Color(0.12f, 0.15f, 0.22f);
            RenderSettings.ambientGroundColor = new Color(0.05f, 0.05f, 0.07f);
            RenderSettings.reflectionIntensity = 0.6f;
        }

        static Camera TitleCameraRig()
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            go.transform.position = CamPos;
            go.transform.LookAt(CamLook);
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.fieldOfView = CamFov;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 800f;
            go.AddComponent<AudioListener>();
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;
            var rig = go.AddComponent<TitleCamera>();
            rig.lookAt = CamLook;
            return cam;
        }

        static void Look(Transform world)
        {
            var volumes = new GameObject("Volumes").transform;
            volumes.SetParent(world);
            Volume(volumes, "Title Look", 0, 1f, Profile("TitleLook", p =>
            {
                p.Add<Tonemapping>().mode.Override(TonemappingMode.ACES);
                var b = p.Add<Bloom>();
                b.threshold.Override(0.95f);
                b.intensity.Override(0.9f);
                b.scatter.Override(0.72f);
                b.highQualityFiltering.Override(true);
                var v = p.Add<Vignette>();
                v.intensity.Override(0.34f);
                v.smoothness.Override(0.45f);
                var ca = p.Add<ColorAdjustments>();
                ca.postExposure.Override(0.25f);
                ca.contrast.Override(12f);
                ca.saturation.Override(14f);
                var st = p.Add<SplitToning>();
                st.shadows.Override(new Color(0.36f, 0.5f, 0.66f));
                st.highlights.Override(new Color(1f, 0.84f, 0.64f));
                st.balance.Override(-15f);
                p.Add<WhiteBalance>().temperature.Override(-4f);
                p.Add<ChromaticAberration>().intensity.Override(0.06f);
            }));
        }

        // ------------------------------------------------------------------ terrace

        static void Terrace(Transform world)
        {
            var root = new GameObject("Terrace").transform;
            root.SetParent(world);
            var floor = Lit("TerraceStone", new Color(0.42f, 0.44f, 0.5f), 0.25f, T("Flagstone"), new Vector2(3f, 3f));
            var stone = Lit("BalustradeStone", new Color(0.55f, 0.56f, 0.62f), 0.2f, T("Flagstone"), new Vector2(0.4f, 0.4f));
            var dark = Lit("DarkStone", new Color(0.18f, 0.19f, 0.24f), 0.15f);
            Block(root, "Floor", PrimitiveType.Cylinder, new Vector3(0f, -0.05f, 0.4f), new Vector3(7.2f, 0.05f, 7.2f), floor);
            Block(root, "Floor Edge", PrimitiveType.Cylinder, new Vector3(0f, -0.4f, 0.4f), new Vector3(7.4f, 0.32f, 7.4f), dark);
            // The terrace's underside tapers into the rock it stands on, down into the clouds.
            Block(root, "Rock", PrimitiveType.Cylinder, new Vector3(0f, -4.5f, 0.4f), new Vector3(5.6f, 4f, 5.6f), dark);

            // Balustrade round the back: plinth, balusters and rail on an arc.
            const float radius = 2.55f;
            const int steps = 26;
            Vector3 P(float deg) => new Vector3(Mathf.Sin(deg * Mathf.Deg2Rad) * radius, 0f, 0.35f - Mathf.Cos(deg * Mathf.Deg2Rad) * radius);
            for (int i = 0; i <= steps; i++)
            {
                float a = Mathf.Lerp(-60f, 60f, i / (float)steps);
                var p = P(a);
                Block(root, "Baluster", PrimitiveType.Cylinder, p + Vector3.up * 0.43f, new Vector3(0.075f, 0.3f, 0.075f), stone);
                Block(root, "Baluster Bulb", PrimitiveType.Sphere, p + Vector3.up * 0.34f, new Vector3(0.11f, 0.13f, 0.11f), stone);
                if (i == steps) break;
                var q = P(Mathf.Lerp(-60f, 60f, (i + 1) / (float)steps));
                var mid = (p + q) * 0.5f;
                var rot = Quaternion.LookRotation(q - p);
                float len = Vector3.Distance(p, q) + 0.02f;
                Block(root, "Plinth", PrimitiveType.Cube, mid + Vector3.up * 0.07f, new Vector3(0.24f, 0.14f, len), stone, rot);
                Block(root, "Rail", PrimitiveType.Cube, mid + Vector3.up * 0.79f, new Vector3(0.2f, 0.08f, len), stone, rot);
            }
            // A pillar with a lantern at each end of the balustrade.
            foreach (float a in new[] { -64f, 64f })
            {
                var p = P(a);
                Block(root, "Pillar", PrimitiveType.Cube, p + Vector3.up * 1.15f, new Vector3(0.34f, 2.3f, 0.34f), stone, Quaternion.Euler(0f, a, 0f));
                Block(root, "Pillar Cap", PrimitiveType.Cube, p + Vector3.up * 2.34f, new Vector3(0.44f, 0.1f, 0.44f), stone, Quaternion.Euler(0f, a, 0f));
                Block(root, "Pillar Base", PrimitiveType.Cube, p + Vector3.up * 0.1f, new Vector3(0.46f, 0.2f, 0.46f), stone, Quaternion.Euler(0f, a, 0f));
                var lantern = new GameObject("Lantern").transform;
                lantern.SetParent(root);
                lantern.position = p + Vector3.up * 2.58f;
                Block(lantern, "Frame Top", PrimitiveType.Cube, lantern.position + Vector3.up * 0.17f, new Vector3(0.26f, 0.04f, 0.26f), dark);
                Block(lantern, "Frame Bottom", PrimitiveType.Cube, lantern.position - Vector3.up * 0.15f, new Vector3(0.22f, 0.04f, 0.22f), dark);
                foreach (var c in new[] { new Vector3(1f, 0f, 1f), new Vector3(-1f, 0f, 1f), new Vector3(1f, 0f, -1f), new Vector3(-1f, 0f, -1f) })
                    Block(lantern, "Frame Post", PrimitiveType.Cube, lantern.position + c * 0.1f, new Vector3(0.025f, 0.32f, 0.025f), dark);
                var glow = Billboard(lantern, "Flame Glow", lantern.position, 0.55f, Glow("LanternGlow", T("Glow"), new Color(2.2f, 1.25f, 0.55f, 0.9f)));
                Billboard(lantern, "Flame Core", lantern.position, 0.13f, Glow("LanternCore", T("Glow"), new Color(4f, 2.6f, 1.4f, 1f)));
                var light = Light(lantern, "Light", LightType.Point, new Color(1f, 0.68f, 0.38f), 2.2f, 4.5f);
                light.transform.position = lantern.position;
                light.shadows = LightShadows.None;
                var flicker = light.gameObject.AddComponent<LightFlicker>();
                flicker.glow = glow.transform;
            }
            NoColliders(root);
        }

        // ------------------------------------------------------------------ academy, mountains, clouds, moon

        static void Academy(Transform world)
        {
            var root = new GameObject("Academy").transform;
            root.SetParent(world);
            var wall = Lit("AcademyWall", new Color(0.3f, 0.34f, 0.45f), 0.1f);
            var roof = Lit("AcademyRoof", new Color(0.16f, 0.21f, 0.34f), 0.35f);
            var rock = Lit("CliffRock", new Color(0.12f, 0.13f, 0.18f), 0.05f);
            var window = Unlit("AcademyWindow", new Color(3.2f, 1.9f, 0.85f));
            var cone = ConeMesh(Root + "/Meshes/Cone.asset");
            var rng = new System.Random(11);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

            // The school on its own crag to the right of the moon, a smaller wing far left.
            (float x, float z, float r, float h, float roofH)[] towers =
            {
                (-11f, -52f, 1.1f, 8.5f, 4.2f), (-16f, -60f, 1.7f, 12f, 5.6f), (-12.5f, -68f, 1.35f, 17f, 7f),
                (-22f, -66f, 2.1f, 10f, 5f), (-6.5f, -74f, 1.5f, 11.5f, 5.2f), (-27f, -58f, 1.2f, 7.5f, 3.8f),
                (24f, -72f, 1.3f, 9.5f, 4.6f), (30f, -82f, 1.7f, 12.5f, 5.8f), (19f, -86f, 1.1f, 7f, 3.6f),
            };
            foreach (var t in towers)
            {
                var basePos = new Vector3(t.x, -1.5f, t.z);
                Block(root, "Tower", PrimitiveType.Cylinder, basePos + Vector3.up * (t.h * 0.5f), new Vector3(t.r * 2f, t.h * 0.5f, t.r * 2f), wall);
                Block(root, "Tower Ledge", PrimitiveType.Cylinder, basePos + Vector3.up * t.h, new Vector3(t.r * 2.3f, 0.12f, t.r * 2.3f), wall);
                var spire = new GameObject("Spire");
                spire.transform.SetParent(root);
                spire.transform.position = basePos + Vector3.up * (t.h + 0.1f);
                spire.transform.localScale = new Vector3(t.r * 2.35f, t.roofH, t.r * 2.35f);
                spire.AddComponent<MeshFilter>().sharedMesh = cone;
                spire.AddComponent<MeshRenderer>().sharedMaterial = roof;
                // Lit windows on the side that faces the terrace.
                var toward = (new Vector3(0f, 0f, 0f) - basePos);
                toward.y = 0f;
                toward.Normalize();
                int n = 1 + rng.Next(3);
                for (int k = 0; k < n; k++)
                {
                    float y = R(0.25f, 0.85f) * t.h;
                    float side = R(-0.5f, 0.5f);
                    var dir = Quaternion.Euler(0f, side * 50f, 0f) * toward;
                    var p = basePos + Vector3.up * y + dir * (t.r + 0.02f);
                    Block(root, "Window", PrimitiveType.Quad, p, new Vector3(0.2f, 0.36f, 1f), window, Quaternion.LookRotation(-dir));
                }
            }
            // The main hall between the towers, with a long gabled roof and a row of lit windows.
            Block(root, "Hall", PrimitiveType.Cube, new Vector3(-17f, 2.2f, -63f), new Vector3(9f, 7.4f, 5f), wall, Quaternion.Euler(0f, 18f, 0f));
            Block(root, "Hall Roof", PrimitiveType.Cube, new Vector3(-17f, 6.1f, -63f), new Vector3(9.2f, 2.4f, 2.4f), roof, Quaternion.Euler(0f, 18f, 0f) * Quaternion.Euler(45f, 0f, 0f));
            for (int k = 0; k < 6; k++)
            {
                var local = new Vector3(-3.4f + k * 1.36f, 3.6f, 2.52f);
                var p = new Vector3(-17f, 0f, -63f) + Quaternion.Euler(0f, 18f, 0f) * local;
                Block(root, "Hall Window", PrimitiveType.Quad, p, new Vector3(0.32f, 0.9f, 1f), window, Quaternion.Euler(0f, 18f + 180f, 0f));
            }
            // Crags the school stands on, falling away into the clouds.
            for (int k = 0; k < 9; k++)
            {
                var p = new Vector3(R(-32f, -6f), R(-12f, -5f), R(-78f, -50f));
                Block(root, "Crag", PrimitiveType.Cube, p, new Vector3(R(5f, 10f), R(9f, 16f), R(5f, 10f)), rock, Quaternion.Euler(R(-8f, 8f), R(0f, 90f), R(-8f, 8f)));
            }
            for (int k = 0; k < 5; k++)
            {
                var p = new Vector3(R(16f, 34f), R(-12f, -6f), R(-90f, -70f));
                Block(root, "Crag", PrimitiveType.Cube, p, new Vector3(R(4f, 8f), R(9f, 14f), R(4f, 8f)), rock, Quaternion.Euler(R(-8f, 8f), R(0f, 90f), R(-8f, 8f)));
            }
            // Far ridges, two layers lost in the haze.
            Ridge(root, "Ridge Near", -95f, -7f, 9f, 3, Lit("RidgeNear", new Color(0.1f, 0.12f, 0.18f), 0f));
            Ridge(root, "Ridge Far", -160f, -4f, 16f, 7, Lit("RidgeFar", new Color(0.12f, 0.15f, 0.22f), 0f));
            NoColliders(root);
        }

        static void Ridge(Transform parent, string name, float z, float baseY, float height, int seed, Material material)
        {
            var path = Root + "/Meshes/" + name.Replace(" ", "") + ".asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null)
            {
                mesh = new Mesh();
                AssetDatabase.CreateAsset(mesh, path);
            }
            mesh.Clear();
            const int n = 160;
            var verts = new List<Vector3>();
            var tris = new List<int>();
            float width = Mathf.Abs(z) * 2.6f;
            for (int i = 0; i <= n; i++)
            {
                float u = i / (float)n;
                float x = (u - 0.5f) * width;
                float h = Fbm(u * 6f + seed * 3.1f, seed * 1.7f, 4);
                // Fewer, broader peaks: square the noise.
                float top = baseY + height * h * h * 2.2f;
                verts.Add(new Vector3(x, top, z));
                verts.Add(new Vector3(x, baseY - 30f, z));
                if (i == n) break;
                int a = i * 2;
                tris.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 });
            }
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            var go = new GameObject(name);
            go.transform.SetParent(parent);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = ShadowCastingMode.Off;
        }

        static void Clouds(Transform world)
        {
            var root = new GameObject("Clouds").transform;
            root.SetParent(world);
            // The sea of clouds below the terrace: one big tiled sheet drifting by a tile at a time, seamlessly.
            var sea = Glow("CloudSea", T("CloudSea"), new Color(0.62f, 0.7f, 0.86f, 0.95f), false);
            sea.SetTextureScale("_BaseMap", new Vector2(10f, 10f));
            var sheet = Block(root, "Cloud Sea", PrimitiveType.Quad, new Vector3(0f, -6.5f, -60f), new Vector3(500f, 500f, 1f), sea, Quaternion.Euler(90f, 0f, 0f));
            sheet.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            var seaDrift = sheet.AddComponent<Drift>();
            seaDrift.velocity = new Vector3(0.35f, 0f, 0f);
            seaDrift.span = 50f;
            var sea2 = Glow("CloudSeaHigh", T("CloudSea"), new Color(0.7f, 0.78f, 0.92f, 0.45f), false);
            sea2.SetTextureScale("_BaseMap", new Vector2(6f, 6f));
            sea2.SetTextureOffset("_BaseMap", new Vector2(0.37f, 0.21f));
            var sheet2 = Block(root, "Cloud Sea High", PrimitiveType.Quad, new Vector3(0f, -4.6f, -60f), new Vector3(500f, 500f, 1f), sea2, Quaternion.Euler(90f, 0f, 0f));
            sheet2.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            var seaDrift2 = sheet2.AddComponent<Drift>();
            seaDrift2.velocity = new Vector3(0.6f, 0f, 0f);
            seaDrift2.span = 500f / 6f;

            // Puffs and wisps standing up out of the sea, and two thin strands across the moon.
            var rng = new System.Random(5);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var puff = Glow("CloudPuff", T("CloudPuff"), new Color(0.66f, 0.74f, 0.9f, 0.55f), false);
            var wisp = Glow("CloudWisp", T("CloudWisp"), new Color(0.72f, 0.8f, 0.95f, 0.4f), false);
            for (int i = 0; i < 12; i++)
            {
                float z = R(-75f, -18f);
                var p = new Vector3(R(-45f, 45f), R(-6f, -2.5f), z);
                float s = R(10f, 22f);
                var q = Block(root, "Cloud Puff", PrimitiveType.Quad, p, new Vector3(s, s * 0.5f, 1f), puff, Quaternion.LookRotation(p - CamPos));
                q.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                var d = q.AddComponent<Drift>();
                d.velocity = new Vector3(R(0.15f, 0.4f), 0f, 0f);
                d.span = 110f;
            }
            foreach (var (p, s) in new[] { (MoonPos + new Vector3(-2f, -1.4f, 3f), new Vector2(18f, 2.6f)), (MoonPos + new Vector3(4f, 1.6f, 4f), new Vector2(14f, 1.8f)), (new Vector3(-20f, 9f, -50f), new Vector2(22f, 3f)) })
            {
                var q = Block(root, "Cloud Wisp", PrimitiveType.Quad, p, new Vector3(s.x, s.y, 1f), wisp, Quaternion.LookRotation(p - CamPos));
                q.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                var d = q.AddComponent<Drift>();
                d.velocity = new Vector3(0.12f, 0f, 0f);
                d.span = 60f;
            }
            NoColliders(root);
        }

        static void Moon(Transform world)
        {
            var root = new GameObject("Moon").transform;
            root.SetParent(world);
            root.position = MoonPos;
            root.rotation = Quaternion.LookRotation(MoonPos - CamPos);
            var disc = Glow("Moon", T("Moon"), new Color(2.1f, 2.05f, 1.92f, 1f), false);
            Block(root, "Disc", PrimitiveType.Quad, MoonPos, Vector3.one * (MoonRadius * 2f), disc, root.rotation);
            var halo = Glow("MoonHalo", T("Glow"), new Color(0.55f, 0.68f, 0.95f, 0.5f));
            Block(root, "Halo", PrimitiveType.Quad, MoonPos + (CamPos - MoonPos).normalized * 0.5f, Vector3.one * (MoonRadius * 9f), halo, root.rotation);
            var corona = Glow("MoonCorona", T("Glow"), new Color(1f, 0.96f, 0.88f, 0.6f));
            Block(root, "Corona", PrimitiveType.Quad, MoonPos + (CamPos - MoonPos).normalized * 0.4f, Vector3.one * (MoonRadius * 3.2f), corona, root.rotation);
            NoColliders(root);
        }

        // ------------------------------------------------------------------ lanterns, stars, motes

        static void SkyLife(Transform world)
        {
            var root = new GameObject("Sky Life").transform;
            root.SetParent(world);

            // Sky lanterns drifting up from the valley.
            var lanterns = Particles(root, "Sky Lanterns", new Vector3(0f, -6f, -48f), Glow("SkyLantern", T("Lantern"), new Color(2.6f, 1.5f, 0.7f, 1f), true, true));
            {
                var main = lanterns.main;
                main.duration = 10f;
                main.prewarm = true;
                main.startLifetime = new ParticleSystem.MinMaxCurve(60f, 80f);
                main.startSpeed = 0f;
                main.startSize = new ParticleSystem.MinMaxCurve(0.24f, 0.42f);
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.62f, 0.3f), new Color(1f, 0.78f, 0.45f));
                main.maxParticles = 160;
                var emission = lanterns.emission;
                emission.rateOverTime = 1.1f;
                var shape = lanterns.shape;
                shape.shapeType = ParticleSystemShapeType.Box;
                shape.scale = new Vector3(100f, 3f, 50f);
                var vel = lanterns.velocityOverLifetime;
                vel.enabled = true;
                vel.space = ParticleSystemSimulationSpace.World;
                vel.x = new ParticleSystem.MinMaxCurve(0.02f, 0.12f);
                vel.y = new ParticleSystem.MinMaxCurve(0.16f, 0.3f);
                vel.z = new ParticleSystem.MinMaxCurve(-0.03f, 0.03f);
                var noise = lanterns.noise;
                noise.enabled = true;
                noise.strength = 0.08f;
                noise.frequency = 0.15f;
                var col = lanterns.colorOverLifetime;
                col.enabled = true;
                col.color = FadeInOut(0.05f, 0.85f);
                // A slow flicker, like a flame behind paper.
                var size = lanterns.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, Flicker01(24));
            }

            // Stars that twinkle in front of the painted ones.
            var twinkle = Particles(root, "Twinkle Stars", new Vector3(0f, 17f, -40f), Glow("TwinkleStar", T("Sparkle"), new Color(1.6f, 1.7f, 2f, 1f), true, true));
            {
                var main = twinkle.main;
                main.duration = 5f;
                main.prewarm = true;
                main.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 5f);
                main.startSpeed = 0f;
                main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.13f);
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, 0.3f);
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.85f, 0.92f, 1f), new Color(1f, 0.95f, 0.85f));
                main.maxParticles = 80;
                var emission = twinkle.emission;
                emission.rateOverTime = 4f;
                var shape = twinkle.shape;
                shape.shapeType = ParticleSystemShapeType.Box;
                shape.scale = new Vector3(80f, 16f, 4f);
                var col = twinkle.colorOverLifetime;
                col.enabled = true;
                col.color = FadeInOut(0.35f, 0.65f);
            }

            // Gold and blue motes round the pair.
            var dust = Particles(root, "Motes", new Vector3(0f, 0.95f, 0.2f), Glow("Mote", T("Glow"), new Color(1.8f, 1.8f, 1.8f, 1f), true, true));
            {
                var main = dust.main;
                main.duration = 5f;
                main.prewarm = true;
                main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 9f);
                main.startSpeed = 0f;
                main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.035f);
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.8f, 0.45f), new Color(0.55f, 0.8f, 1f));
                main.maxParticles = 300;
                var emission = dust.emission;
                emission.rateOverTime = 26f;
                var shape = dust.shape;
                shape.shapeType = ParticleSystemShapeType.Box;
                shape.scale = new Vector3(6f, 2.6f, 4f);
                var vel = dust.velocityOverLifetime;
                vel.enabled = true;
                vel.space = ParticleSystemSimulationSpace.World;
                vel.x = new ParticleSystem.MinMaxCurve(-0.03f, 0.03f);
                vel.y = new ParticleSystem.MinMaxCurve(0.02f, 0.07f);
                vel.z = new ParticleSystem.MinMaxCurve(-0.03f, 0.03f);
                var noise = dust.noise;
                noise.enabled = true;
                noise.strength = 0.12f;
                noise.frequency = 0.35f;
                var col = dust.colorOverLifetime;
                col.enabled = true;
                col.color = FadeInOut(0.2f, 0.75f);
            }

            // Shooting stars.
            var shooter = new GameObject("Shooting Star");
            shooter.transform.SetParent(root);
            var trail = shooter.AddComponent<TrailRenderer>();
            trail.time = 0.4f;
            trail.minVertexDistance = 0.1f;
            trail.widthMultiplier = 0.16f;
            trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
            trail.colorGradient = MakeGradient(new[] { (Color.white, 0f), (new Color(0.7f, 0.85f, 1f), 0.3f), (new Color(0.5f, 0.7f, 1f), 1f) },
                new[] { (1f, 0f), (0.6f, 0.4f), (0f, 1f) });
            trail.sharedMaterial = Glow("ShootingStar", T("Ribbon"), new Color(3f, 3.2f, 3.6f, 1f), true, true);
            trail.shadowCastingMode = ShadowCastingMode.Off;
            shooter.AddComponent<ShootingStar>();
        }

        // ------------------------------------------------------------------ magic circles

        static int MagicCircles(Transform world)
        {
            var root = new GameObject("Magic Circles").transform;
            root.SetParent(world);
            var gold = Glow("CircleGold", T("MagicCircle"), new Color(Gold.r * 1.5f, Gold.g * 1.5f, Gold.b * 1.5f, 0.55f));
            var blue = Glow("CircleBlue", T("MagicCircle"), new Color(Blue.r * 1.5f, Blue.g * 1.5f, Blue.b * 1.5f, 0.5f));
            var haloGold = Glow("HaloGold", T("MagicCircle"), new Color(Gold.r * 1.3f, Gold.g * 1.3f, Gold.b * 1.3f, 0.075f));
            var haloBlue = Glow("HaloBlue", T("MagicCircle"), new Color(Blue.r * 1.2f, Blue.g * 1.2f, Blue.b * 1.2f, 0.065f));
            Ring(root, "Floor Circle", new Vector3(0f, 0.012f, 0.05f), 3.8f, Quaternion.Euler(90f, 0f, 0f), gold, 4f);
            Ring(root, "Floor Circle Inner", new Vector3(0f, 0.016f, 0.05f), 2.4f, Quaternion.Euler(90f, 0f, 0f), blue, -7f);
            Ring(root, "Halo", new Vector3(0f, 0.95f, -1.45f), 2.25f, Quaternion.identity, haloGold, 2.5f);
            Ring(root, "Halo Inner", new Vector3(0f, 0.95f, -1.4f), 1.45f, Quaternion.identity, haloBlue, -4f);
            return 4;
        }

        static void Ring(Transform parent, string name, Vector3 position, float size, Quaternion rotation, Material material, float speed)
        {
            var q = Block(parent, name, PrimitiveType.Quad, position, new Vector3(size, size, 1f), material, rotation);
            q.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            Object.DestroyImmediate(q.GetComponent<Collider>());
            var spin = q.AddComponent<Spin>();
            spin.axis = Vector3.forward;
            spin.speed = speed;
        }

        // ------------------------------------------------------------------ characters

        static Animator Character(CasterProfile profile, string fbx, string wandFbx, string side, float wandLength, Vector3 position, float yaw, string clipName, string name)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(fbx));
            go.name = name;
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            go.transform.localScale = Vector3.one * 1.5f;
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (profile != null && profile.bodyMaterial != null) smr.sharedMaterial = profile.bodyMaterial;
                smr.updateWhenOffscreen = true;
            }
            var wand = PeriodPoseClips.AttachWand(go, fbx, wandFbx, side, wandLength);
            if (profile != null && profile.wandMaterial != null)
                foreach (var r in wand.GetComponentsInChildren<Renderer>()) r.sharedMaterial = profile.wandMaterial;
            if (!go.TryGetComponent<Animator>(out var animator)) animator = go.AddComponent<Animator>();
            var clip = PeriodPoseClips.Load(clipName);
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            animator.runtimeAnimatorController = Controller(clipName, clip);
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            // Bind the pose clip's first frame in the saved scene so edit-mode previews match play.
            clip.SampleAnimation(go, 0f);
            return animator;
        }

        static RuntimeAnimatorController Controller(string name, AnimationClip clip)
        {
            var path = Root + "/Anim/" + name + ".controller";
            var c = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (c == null) return AnimatorController.CreateAnimatorControllerAtPathWithClip(path, clip);
            var sm = c.layers[0].stateMachine;
            var state = sm.defaultState != null ? sm.defaultState : sm.AddState("Loop");
            state.motion = clip;
            sm.defaultState = state;
            EditorUtility.SetDirty(c);
            return c;
        }

        /// <summary>The light in Amelia's palm and on both wand tips.</summary>
        static void CharacterLights(Animator amelia, Animator nox)
        {
            // Amelia: a small sun resting on her open left hand.
            var hand = FindBone(amelia.transform, "Left_Hand");
            var palmLocal = PeriodPoseClips.PalmLocal(PeriodPoseClips.AmeliaFbx, "Left_") + PeriodPoseClips.PalmNormalLocal(PeriodPoseClips.AmeliaFbx, "Left_") * 0.055f;
            var orb = Holder(hand, "Palm Light", palmLocal);
            var orbGlow = Billboard(orb, "Glow", orb.position, 0.34f, Glow("PalmGlow", T("Glow"), new Color(2.4f, 1.6f, 0.7f, 0.85f))).AddComponent<GlowSprite>();
            Billboard(orb, "Core", orb.position, 0.07f, Glow("PalmCore", T("Glow"), new Color(5f, 3.8f, 2.2f, 1f))).AddComponent<GlowSprite>().pulse = 0.15f;
            var orbLight = Light(orb, "Light", LightType.Point, new Color(1f, 0.78f, 0.45f), 1.6f, 2.4f);
            orbLight.transform.localPosition = Vector3.zero;
            orbLight.shadows = LightShadows.None;
            orbGlow.lit = orbLight;
            Sparkles(orb, "Sparkles", Gold, 0.06f, 16f);

            // Wand tips: a soft glow, gold and blue.
            Tip(amelia, PeriodPoseClips.AmeliaFbx, "Right_", PeriodPoseClips.AmeliaWandLength, Gold, "AmeliaTip", 0.2f, 0.5f);
            Tip(nox, PeriodPoseClips.NoxFbx, "Left_", PeriodPoseClips.NoxWandLength, Blue, "NoxTip", 0.3f, 1.2f);
        }

        static void Tip(Animator character, string fbx, string side, float length, Color color, string name, float size, float light)
        {
            var hand = FindBone(character.transform, side + "Hand");
            var tip = Holder(hand, "Wand Tip Light", PeriodPoseClips.WandTipLocal(fbx, side, length));
            Billboard(tip, "Glow", tip.position, size, Glow(name + "Glow", T("Glow"), new Color(color.r * 2.2f, color.g * 2.2f, color.b * 2.2f, 0.8f))).AddComponent<GlowSprite>();
            Billboard(tip, "Core", tip.position, size * 0.22f, Glow(name + "Core", T("Glow"), new Color(color.r * 4f + 1f, color.g * 4f + 1f, color.b * 4f + 1f, 1f))).AddComponent<GlowSprite>().pulse = 0.2f;
            if (light > 0f)
            {
                var l = Light(tip, "Light", LightType.Point, color, light, 1.8f);
                l.transform.localPosition = Vector3.zero;
                l.shadows = LightShadows.None;
            }
            Sparkles(tip, "Sparkles", color, 0.04f, 10f);
        }

        /// <summary>An empty under <paramref name="bone"/> at <paramref name="local"/>, scaled back to world size 1.</summary>
        static Transform Holder(Transform bone, string name, Vector3 local)
        {
            var go = new GameObject(name).transform;
            go.SetParent(bone, false);
            go.localPosition = local;
            go.localRotation = Quaternion.identity;
            var s = bone.lossyScale;
            go.localScale = new Vector3(1f / s.x, 1f / s.y, 1f / s.z);
            return go;
        }

        static void Sparkles(Transform parent, string name, Color color, float radius, float rate)
        {
            var ps = Particles(parent, name, parent.position, Glow("Sparkle" + ColorUtility.ToHtmlStringRGB(color), T("Sparkle"), new Color(color.r * 2.5f + 0.5f, color.g * 2.5f + 0.5f, color.b * 2.5f + 0.5f, 1f), true, true));
            ps.transform.localPosition = Vector3.zero;
            var main = ps.main;
            main.duration = 2f;
            main.prewarm = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.08f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.04f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI);
            main.startColor = new ParticleSystem.MinMaxGradient(Color.white, Color.Lerp(color, Color.white, 0.4f));
            main.maxParticles = 80;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = ps.emission;
            emission.rateOverTime = rate;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = radius;
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.y = new ParticleSystem.MinMaxCurve(0.03f, 0.1f);
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = FadeInOut(0.1f, 0.5f);
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.2f, 1f), new Keyframe(1f, 0f)));
        }

        // ------------------------------------------------------------------ light ribbons

        static void Ribbons(Transform world)
        {
            var root = new GameObject("Light Ribbons").transform;
            root.SetParent(world);
            for (int i = 0; i < 2; i++)
            {
                var color = i == 0 ? Gold : Blue;
                var go = new GameObject(i == 0 ? "Ribbon Gold" : "Ribbon Blue");
                go.transform.SetParent(root);
                var ribbon = go.AddComponent<LightRibbon>();
                ribbon.center = new Vector3(0f, 0.85f, -0.03f);
                ribbon.radius = new Vector2(1.35f, 0.82f);
                ribbon.height = 0.42f;
                ribbon.lap = 7.5f;
                ribbon.phase = i * 0.5f;
                ribbon.heightPhase = i * Mathf.PI;
                go.transform.position = ribbon.center + new Vector3(ribbon.radius.x, 0f, 0f);
                var trail = go.AddComponent<TrailRenderer>();
                trail.time = 1.7f;
                trail.minVertexDistance = 0.02f;
                trail.widthMultiplier = 0.085f;
                trail.widthCurve = new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.08f, 1f), new Keyframe(1f, 0f));
                trail.colorGradient = MakeGradient(new[] { (Color.white, 0f), (color, 0.18f), (color, 1f) }, new[] { (1f, 0f), (0.75f, 0.35f), (0f, 1f) });
                trail.sharedMaterial = Glow("Ribbon" + (i == 0 ? "Gold" : "Blue"), T("Ribbon"), new Color(2.4f, 2.4f, 2.4f, 1f), true, true);
                trail.textureMode = LineTextureMode.Stretch;
                trail.shadowCastingMode = ShadowCastingMode.Off;
                trail.numCapVertices = 2;
                Billboard(go.transform, "Head", go.transform.position, 0.22f, Glow("RibbonHead" + i, T("Glow"), new Color(color.r * 3f + 0.6f, color.g * 3f + 0.6f, color.b * 3f + 0.6f, 0.9f))).AddComponent<GlowSprite>().pulse = 0.1f;
                var l = Light(go.transform, "Light", LightType.Point, color, 0.9f, 1.6f);
                l.transform.localPosition = Vector3.zero;
                l.shadows = LightShadows.None;
                var ps = Particles(go.transform, "Sparkles", go.transform.position, Glow("RibbonSparkle" + i, T("Sparkle"), new Color(color.r * 2.5f + 0.6f, color.g * 2.5f + 0.6f, color.b * 2.5f + 0.6f, 1f), true, true));
                ps.transform.localPosition = Vector3.zero;
                var main = ps.main;
                main.duration = 2f;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.5f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0f, 0.05f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.05f);
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI);
                main.maxParticles = 200;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.gravityModifier = -0.01f;
                var emission = ps.emission;
                emission.rateOverTime = 0f;
                emission.rateOverDistance = 14f;
                var shape = ps.shape;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.04f;
                var col = ps.colorOverLifetime;
                col.enabled = true;
                col.color = FadeInOut(0.05f, 0.4f);
            }
        }

        // ------------------------------------------------------------------ lights

        static void Lights(Transform world)
        {
            var root = new GameObject("Lights").transform;
            root.SetParent(world);
            // Moonlight from behind and above: a cool rim on both girls and long shadows toward the camera.
            var moon = Light(root, "Moonlight", LightType.Directional, new Color(0.62f, 0.74f, 1f), 1.2f, 0f);
            moon.transform.rotation = Quaternion.LookRotation(CamLook - MoonPos);
            moon.shadows = LightShadows.Soft;
            moon.shadowStrength = 0.75f;
            // Warm key from the front left, cool fill from the front right.
            var key = Light(root, "Key", LightType.Spot, new Color(1f, 0.84f, 0.66f), 7f, 9f);
            key.transform.position = new Vector3(2.4f, 2.5f, 3.1f);
            key.transform.LookAt(new Vector3(0f, 0.95f, 0f));
            key.spotAngle = 42f;
            key.innerSpotAngle = 20f;
            key.shadows = LightShadows.Soft;
            var fill = Light(root, "Fill", LightType.Point, new Color(0.5f, 0.66f, 1f), 2.4f, 6f);
            fill.transform.position = new Vector3(-2.5f, 1.3f, 2.3f);
            fill.shadows = LightShadows.None;
            // The floor circle's glow lifting into their faces from below.
            var under = Light(root, "Circle Glow Gold", LightType.Point, Gold, 0.7f, 2.4f);
            under.transform.position = new Vector3(0.45f, 0.12f, 0.35f);
            under.shadows = LightShadows.None;
            var underBlue = Light(root, "Circle Glow Blue", LightType.Point, Blue, 0.7f, 2.4f);
            underBlue.transform.position = new Vector3(-0.45f, 0.12f, 0.35f);
            underBlue.shadows = LightShadows.None;
        }

        // ------------------------------------------------------------------ UI

        struct Ui
        {
            public TitleLogo logo;
            public CanvasGroup ornament, prompt, credit;
            public Image black, flash;
        }

        static Ui BuildUi(Camera cam)
        {
            var titleFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(TitleFontPath);
            var bodyFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(BodyFontPath);
            // Make sure the glyphs are in the dynamic atlases before the material copies them.
            titleFont.TryAddCharacters(LogoText);
            bodyFont.TryAddCharacters("PRESS ENTER Enter · 클릭으로 시작 Team Waaang");

            var go = new GameObject("Title UI");
            var canvas = go.AddComponent<Canvas>();
            // Overlay, after post-processing: the tonemapper and split toning turned the logo's gold olive.
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            var ui = new Ui();

            // Logo, top centre.
            var logo = UiText(go.transform, "Logo", titleFont, LogoMaterial(titleFont), LogoText, 128f, new Vector2(0.5f, 1f), new Vector2(0f, -170f), new Vector2(1700f, 220f));
            logo.characterSpacing = 6f;
            logo.color = Color.white;
            ui.logo = logo.gameObject.AddComponent<TitleLogo>();

            // Ornament under the logo: a fine line from gold to blue with a diamond in the middle.
            var ornament = UiRect(go.transform, "Ornament", new Vector2(0.5f, 1f), new Vector2(0f, -276f), new Vector2(900f, 40f));
            ui.ornament = ornament.gameObject.AddComponent<CanvasGroup>();
            var line = UiRect(ornament, "Line", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(820f, 14f)).gameObject.AddComponent<Image>();
            line.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(TexDir + "/Ornament.png");
            line.raycastTarget = false;
            var diamond = UiRect(ornament, "Diamond", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(34f, 34f)).gameObject.AddComponent<Image>();
            diamond.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(TexDir + "/Diamond.png");
            diamond.raycastTarget = false;
            foreach (float x in new[] { -430f, 430f })
            {
                var dot = UiRect(ornament, "Dot", new Vector2(0.5f, 0.5f), new Vector2(x, 0f), new Vector2(14f, 14f)).gameObject.AddComponent<Image>();
                dot.sprite = diamond.sprite;
                dot.color = x < 0f ? new Color(1f, 0.85f, 0.55f, 0.8f) : new Color(0.65f, 0.85f, 1f, 0.8f);
                dot.raycastTarget = false;
            }

            // Prompt, bottom centre.
            var prompt = UiRect(go.transform, "Prompt", new Vector2(0.5f, 0f), new Vector2(0f, 262f), new Vector2(900f, 110f));
            var shade = UiRect(prompt, "Shade", new Vector2(0.5f, 0.5f), new Vector2(0f, 0f), new Vector2(620f, 150f)).gameObject.AddComponent<Image>();
            shade.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(TexDir + "/Shade.png");
            shade.color = new Color(0f, 0.01f, 0.03f, 0.55f);
            shade.raycastTarget = false;
            ui.prompt = prompt.gameObject.AddComponent<CanvasGroup>();
            var press = UiText(prompt, "Press", bodyFont, PromptMaterial(bodyFont), "PRESS ENTER", 34f, new Vector2(0.5f, 0.5f), new Vector2(0f, 18f), new Vector2(900f, 50f));
            press.characterSpacing = 30f;
            press.color = new Color(1f, 0.95f, 0.86f);
            var hint = UiText(prompt, "Hint", bodyFont, PromptMaterial(bodyFont), "Enter · 클릭으로 시작", 22f, new Vector2(0.5f, 0.5f), new Vector2(0f, -28f), new Vector2(900f, 34f));
            hint.characterSpacing = 4f;
            hint.color = new Color(0.86f, 0.9f, 1f, 0.75f);

            // Credit, bottom right.
            var credit = UiRect(go.transform, "Credit", new Vector2(1f, 0f), new Vector2(-150f, 40f), new Vector2(260f, 30f));
            ui.credit = credit.gameObject.AddComponent<CanvasGroup>();
            var team = UiText(credit, "Team", bodyFont, PromptMaterial(bodyFont), "Team Waaang", 20f, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(260f, 30f));
            team.characterSpacing = 8f;
            team.alignment = TextAlignmentOptions.Right;
            team.color = new Color(1f, 1f, 1f, 0.9f);

            ui.flash = FullScreen(go.transform, "Flash", new Color(1f, 0.96f, 0.88f, 0f));
            ui.black = FullScreen(go.transform, "Black", Color.black);
            return ui;
        }

        static Material LogoMaterial(TMP_FontAsset font)
        {
            var m = Mat("TitleLogo", "TextMeshPro/Distance Field");
            m.CopyPropertiesFromMaterial(font.material);
            m.shaderKeywords = new string[0];
            m.EnableKeyword("GLOW_ON");
            m.EnableKeyword("UNDERLAY_ON");
            m.SetColor("_FaceColor", new Color(1.25f, 1.22f, 1.18f, 1f));
            m.SetFloat("_FaceDilate", 0.06f);
            m.SetColor("_OutlineColor", new Color(0.32f, 0.22f, 0.12f, 1f));
            m.SetFloat("_OutlineWidth", 0.08f);
            m.SetFloat("_OutlineSoftness", 0.05f);
            m.SetColor("_GlowColor", new Color(1f, 0.86f, 0.62f, 0.55f));
            m.SetFloat("_GlowOffset", 0.05f);
            m.SetFloat("_GlowInner", 0.05f);
            m.SetFloat("_GlowOuter", 0.55f);
            m.SetFloat("_GlowPower", 0.85f);
            m.SetColor("_UnderlayColor", new Color(0f, 0.01f, 0.04f, 0.6f));
            m.SetFloat("_UnderlayOffsetX", 0f);
            m.SetFloat("_UnderlayOffsetY", -0.35f);
            m.SetFloat("_UnderlayDilate", 0.25f);
            m.SetFloat("_UnderlaySoftness", 0.6f);
            ShaderUtilities.UpdateShaderRatios(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material PromptMaterial(TMP_FontAsset font)
        {
            var m = Mat("TitlePrompt", "TextMeshPro/Distance Field");
            m.CopyPropertiesFromMaterial(font.material);
            m.shaderKeywords = new string[0];
            m.SetColor("_FaceColor", Color.white);
            m.SetFloat("_OutlineWidth", 0f);
            m.SetFloat("_FaceDilate", 0.08f);
            ShaderUtilities.UpdateShaderRatios(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        static TextMeshProUGUI UiText(Transform parent, string name, TMP_FontAsset font, Material material, string text, float size, Vector2 anchor, Vector2 position, Vector2 box)
        {
            var rt = UiRect(parent, name, anchor, position, box);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = font;
            t.fontSharedMaterial = material;
            t.text = text;
            t.fontSize = size;
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            t.raycastTarget = false;
            return t;
        }

        static RectTransform UiRect(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
            return rt;
        }

        static Image FullScreen(Transform parent, string name, Color color)
        {
            var rt = UiRect(parent, name, Vector2.zero, Vector2.zero, Vector2.zero);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        // ------------------------------------------------------------------ small helpers

        static Texture2D T(string name) => AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "/" + name + ".png");

        static GameObject Block(Transform parent, string name, PrimitiveType type, Vector3 position, Vector3 scale, Material material, Quaternion? rotation = null)
            => PeriodSceneKit.Block(parent, name, type, position, scale, material, rotation);

        static GameObject Billboard(Transform parent, string name, Vector3 position, float size, Material material)
        {
            var q = Block(parent, name, PrimitiveType.Quad, position, Vector3.one, material);
            Object.DestroyImmediate(q.GetComponent<Collider>());
            var r = q.GetComponent<Renderer>();
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            // World size regardless of what it hangs under.
            var s = parent.lossyScale;
            q.transform.localScale = new Vector3(size / s.x, size / s.y, size / s.z);
            q.transform.rotation = Quaternion.LookRotation(q.transform.position - CamPos);
            return q;
        }

        static ParticleSystem Particles(Transform parent, string name, Vector3 position, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.scalingMode = ParticleSystemScalingMode.Shape;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        static void NoColliders(Transform root)
        {
            foreach (var c in root.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
        }

        static ParticleSystem.MinMaxGradient FadeInOut(float inAt, float outAt) =>
            new ParticleSystem.MinMaxGradient(MakeGradient(new[] { (Color.white, 0f), (Color.white, 1f) }, new[] { (0f, 0f), (1f, inAt), (1f, outAt), (0f, 1f) }));

        static Gradient MakeGradient((Color c, float t)[] colors, (float a, float t)[] alphas)
        {
            var g = new Gradient();
            var ck = new GradientColorKey[colors.Length];
            for (int i = 0; i < colors.Length; i++) ck[i] = new GradientColorKey(colors[i].c, colors[i].t);
            var ak = new GradientAlphaKey[alphas.Length];
            for (int i = 0; i < alphas.Length; i++) ak[i] = new GradientAlphaKey(alphas[i].a, alphas[i].t);
            g.SetKeys(ck, ak);
            return g;
        }

        static AnimationCurve Flicker01(int keys)
        {
            var rng = new System.Random(3);
            var k = new Keyframe[keys + 1];
            for (int i = 0; i <= keys; i++) k[i] = new Keyframe(i / (float)keys, i == 0 || i == keys ? 0.9f : 0.82f + 0.18f * (float)rng.NextDouble());
            return new AnimationCurve(k);
        }

        // ------------------------------------------------------------------ materials

        static Material Mat(string name, string shader)
        {
            var path = $"{Root}/Materials/{name}.mat";
            var sh = Shader.Find(shader);
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(sh);
                AssetDatabase.CreateAsset(m, path);
            }
            else if (m.shader != sh) m.shader = sh;
            return m;
        }

        static Material Lit(string name, Color color, float smoothness, Texture texture = null, Vector2? tiling = null)
        {
            var m = Mat(name, "Universal Render Pipeline/Lit");
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smoothness);
            m.SetTexture("_BaseMap", texture);
            m.SetTextureScale("_BaseMap", tiling ?? Vector2.one);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material Unlit(string name, Color color)
        {
            var m = Mat(name, "Universal Render Pipeline/Unlit");
            m.SetColor("_BaseColor", color);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Transparent unlit (additive, or alpha-blended when <paramref name="additive"/> is false); particles get the particle shader.</summary>
        static Material Glow(string name, Texture texture, Color color, bool additive = true, bool particles = false)
        {
            var m = Mat(name, particles ? "Universal Render Pipeline/Particles/Unlit" : "Universal Render Pipeline/Unlit");
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", additive ? 2f : 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetTexture("_BaseMap", texture);
            m.SetColor("_BaseColor", color);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Mesh ConeMesh(string path)
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null)
            {
                mesh = new Mesh();
                AssetDatabase.CreateAsset(mesh, path);
            }
            mesh.Clear();
            const int n = 16;
            var verts = new List<Vector3>();
            var tris = new List<int>();
            // Flat-shaded sides (each face its own vertices) read as slate panels.
            for (int i = 0; i < n; i++)
            {
                float a0 = i * Mathf.PI * 2f / n, a1 = (i + 1) * Mathf.PI * 2f / n;
                int b = verts.Count;
                verts.Add(new Vector3(Mathf.Cos(a0) * 0.5f, 0f, Mathf.Sin(a0) * 0.5f));
                verts.Add(new Vector3(0f, 1f, 0f));
                verts.Add(new Vector3(Mathf.Cos(a1) * 0.5f, 0f, Mathf.Sin(a1) * 0.5f));
                tris.AddRange(new[] { b, b + 1, b + 2 });
            }
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        // ------------------------------------------------------------------ painted textures

        static void PaintTextures()
        {
            Save(SkyPanorama(), "SkyPanorama", wrapU: TextureWrapMode.Repeat, mips: false, hq: true, maxSize: 4096);
            Save(MoonTexture(), "Moon", mips: true);
            Save(GlowTexture(), "Glow");
            Save(SparkleTexture(), "Sparkle");
            Save(LanternTexture(), "Lantern");
            Save(RibbonTexture(), "Ribbon");
            Save(MagicCircleTexture(), "MagicCircle", hq: true, maxSize: 2048);
            Save(CloudTexture(512, 256, 3, false), "CloudPuff");
            Save(CloudTexture(512, 128, 9, true), "CloudWisp");
            Save(CloudSeaTexture(), "CloudSea", wrapU: TextureWrapMode.Repeat, wrapV: TextureWrapMode.Repeat, hq: true);
            Save(FlagstoneTexture(), "Flagstone", wrapU: TextureWrapMode.Repeat, wrapV: TextureWrapMode.Repeat);
            Save(OrnamentTexture(), "Ornament", sprite: true);
            Save(DiamondTexture(), "Diamond", sprite: true);
            Save(GlowTexture(), "Shade", sprite: true);
        }

        static void Save(Texture2D tex, string name, TextureWrapMode wrapU = TextureWrapMode.Clamp, TextureWrapMode wrapV = TextureWrapMode.Clamp,
            bool mips = true, bool hq = false, int maxSize = 2048, bool sprite = false)
        {
            var path = TexDir + "/" + name + ".png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = sprite ? TextureImporterType.Sprite : TextureImporterType.Default;
            if (sprite) imp.spriteImportMode = SpriteImportMode.Single;
            imp.sRGBTexture = true;
            imp.alphaIsTransparency = true;
            imp.wrapModeU = wrapU;
            imp.wrapModeV = wrapV;
            imp.mipmapEnabled = mips;
            imp.maxTextureSize = maxSize;
            imp.textureCompression = hq ? TextureImporterCompression.CompressedHQ : TextureImporterCompression.Compressed;
            imp.filterMode = FilterMode.Bilinear;
            imp.anisoLevel = 4;
            imp.SaveAndReimport();
        }

        static float Fbm(float x, float y, int octaves)
        {
            float sum = 0f, amp = 0.5f, freq = 1f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += amp * Mathf.PerlinNoise(x * freq + i * 17.31f, y * freq + i * 9.17f);
                norm += amp;
                amp *= 0.5f;
                freq *= 2.03f;
            }
            return sum / norm;
        }

        /// <summary>Fbm that tiles over [0,1)² at <paramref name="scale"/> cells (blend of four shifted copies).</summary>
        static float TileFbm(float u, float v, float scale, int octaves)
        {
            float x = u * scale, y = v * scale;
            float a = Fbm(x, y, octaves), b = Fbm(x - scale, y, octaves), c = Fbm(x, y - scale, octaves), d = Fbm(x - scale, y - scale, octaves);
            return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
        }

        static float Smooth(float a, float b, float x) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, x));

        /// <summary>
        /// Lat-long night sky for Skybox/Panoramic: gradient from the horizon haze up to a deep zenith, a glow round
        /// the moon, a faint warm band on the horizon below it, a teal milky way, and thousands of stars.
        /// </summary>
        static Texture2D SkyPanorama()
        {
            const int W = 4096, H = 2048;
            var px = new Color[W * H];
            var moonDir = (MoonPos - CamPos).normalized;
            var moonFlat = new Vector3(moonDir.x, 0f, moonDir.z).normalized;
            // Milky way: a great circle tilted across the sky behind the school.
            var band = new Vector3(0.55f, 0.35f, 0.2f).normalized;
            var rng = new System.Random(21);
            for (int y = 0; y < H; y++)
            {
                float v = (y + 0.5f) / H;
                float lat = (1f - v) * Mathf.PI;
                float elev = 90f - lat * Mathf.Rad2Deg;
                Color baseColor;
                if (elev < 0f) baseColor = Color.Lerp(Horizon, Below, Smooth(0f, -10f, elev));
                else if (elev < 16f) baseColor = Color.Lerp(Horizon, High, Smooth(0f, 16f, elev));
                else baseColor = Color.Lerp(High, Zenith, Smooth(16f, 75f, elev));
                float sinLat = Mathf.Sin(lat), cosLat = Mathf.Cos(lat);
                for (int x = 0; x < W; x++)
                {
                    float u = (x + 0.5f) / W;
                    float lon = (0.5f - u) * Mathf.PI * 2f;
                    var dir = new Vector3(sinLat * Mathf.Cos(lon), cosLat, sinLat * Mathf.Sin(lon));
                    var c = baseColor;
                    float ang = Mathf.Acos(Mathf.Clamp(Vector3.Dot(dir, moonDir), -1f, 1f)) * Mathf.Rad2Deg;
                    float glow = 0.3f * Mathf.Exp(-ang / 7f) + 0.16f * Mathf.Exp(-ang / 26f);
                    c += new Color(0.7f, 0.78f, 0.9f) * glow;
                    var flat = new Vector3(dir.x, 0f, dir.z).normalized;
                    float toward = 0.5f + 0.5f * Vector3.Dot(flat, moonFlat);
                    c += new Color(0.5f, 0.4f, 0.3f) * (0.1f * Mathf.Exp(-Mathf.Abs(elev) / 4f) * toward * toward);
                    if (elev > -2f)
                    {
                        float d = Vector3.Dot(dir, band);
                        float mw = Mathf.Exp(-d * d / 0.03f);
                        if (mw > 0.02f)
                        {
                            float n = Fbm(dir.x * 3f + 11f, dir.y * 3f + dir.z * 2.3f + 5f, 4);
                            float dust = Fbm(dir.x * 9f + 3f, dir.y * 9f - dir.z * 7f, 3);
                            mw *= Smooth(0.35f, 0.75f, n) * (1f - 0.6f * Smooth(0.55f, 0.75f, dust)) * Smooth(-2f, 12f, elev);
                            c += new Color(0.22f, 0.32f, 0.42f) * (0.22f * mw);
                        }
                    }
                    // Dither against banding in the long gradients.
                    float dither = ((float)rng.NextDouble() - 0.5f) / 255f;
                    c.r += dither; c.g += dither; c.b += dither;
                    c.a = 1f;
                    px[y * W + x] = c;
                }
            }
            // Stars: more of them along the milky way and higher up, none in the haze.
            void Star(Vector3 dir, float bright, Color tint)
            {
                float lat = Mathf.Acos(Mathf.Clamp(dir.y, -1f, 1f));
                float lon = Mathf.Atan2(dir.z, dir.x);
                float fx = (0.5f - lon / (Mathf.PI * 2f)) * W, fy = (1f - lat / Mathf.PI) * H;
                float sigma = 0.42f + 0.45f * bright;
                float sx = sigma / Mathf.Max(0.2f, Mathf.Sin(lat));
                int r = Mathf.CeilToInt(sigma * 3f), rx = Mathf.CeilToInt(sx * 3f);
                float elev = 90f - lat * Mathf.Rad2Deg;
                float fade = Smooth(1f, 10f, elev) * (1f - 0.7f * Mathf.Exp(-Mathf.Acos(Mathf.Clamp(Vector3.Dot(dir, moonDir), -1f, 1f)) * Mathf.Rad2Deg / 12f));
                for (int yy = -r; yy <= r; yy++)
                for (int xx = -rx; xx <= rx; xx++)
                {
                    int px0 = ((int)fx + xx + W) % W, py0 = (int)fy + yy;
                    if (py0 < 0 || py0 >= H) continue;
                    float dx = ((int)fx + xx + 0.5f - fx) / sx, dy = (py0 + 0.5f - fy) / sigma;
                    float g = Mathf.Exp(-(dx * dx + dy * dy) * 0.5f) * bright * fade;
                    px[py0 * W + px0] += tint * g;
                }
            }
            Vector3 RandomDir()
            {
                float z = (float)rng.NextDouble() * 2f - 1f, t = (float)rng.NextDouble() * Mathf.PI * 2f, s = Mathf.Sqrt(1f - z * z);
                return new Vector3(s * Mathf.Cos(t), z, s * Mathf.Sin(t));
            }
            Color Tint() => Color.Lerp(new Color(0.75f, 0.85f, 1f), new Color(1f, 0.92f, 0.8f), (float)rng.NextDouble());
            for (int i = 0; i < 9000; i++)
            {
                var d = RandomDir();
                if (d.y < 0f) d.y = -d.y * 0.4f;
                float b = Mathf.Pow((float)rng.NextDouble(), 9f) * 0.9f + 0.05f;
                Star(d.normalized, b, Tint());
            }
            for (int i = 0; i < 5000; i++)
            {
                var d = RandomDir();
                d -= band * (Vector3.Dot(d, band) * 0.85f);
                if (d.y < 0f) d.y = -d.y;
                float b = Mathf.Pow((float)rng.NextDouble(), 10f) * 0.6f + 0.035f;
                Star(d.normalized, b, Tint());
            }
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        static Texture2D MoonTexture()
        {
            const int N = 512;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
            var rng = new System.Random(4);
            var craters = new List<Vector3>();
            for (int i = 0; i < 34; i++)
                craters.Add(new Vector3((float)rng.NextDouble() * 1.7f - 0.85f, (float)rng.NextDouble() * 1.7f - 0.85f, 0.03f + 0.12f * Mathf.Pow((float)rng.NextDouble(), 2f)));
            float aa = 2f / N * 1.5f;
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float fx = x / (N - 1f) * 2f - 1f, fy = y / (N - 1f) * 2f - 1f;
                float r = Mathf.Sqrt(fx * fx + fy * fy);
                float a = Mathf.Clamp01((0.98f - r) / aa);
                if (a <= 0f) { tex.SetPixel(x, y, new Color(1f, 1f, 1f, 0f)); continue; }
                float limb = Mathf.Sqrt(Mathf.Max(0f, 1f - r * r));
                float shade = 0.78f + 0.22f * Mathf.Sqrt(limb);
                float maria = Fbm(fx * 1.5f + 3f, fy * 1.5f + 5f, 4);
                float dark = Smooth(0.48f, 0.66f, maria) * 0.26f;
                float crater = 0f;
                foreach (var c in craters)
                {
                    float d = Mathf.Sqrt((fx - c.x) * (fx - c.x) + (fy - c.y) * (fy - c.y)) / c.z;
                    if (d < 1f) crater -= 0.09f * (1f - d * d);
                    else if (d < 1.2f) crater += 0.06f * (1f - (d - 1f) / 0.2f);
                }
                float detail = (Fbm(fx * 9f, fy * 9f, 3) - 0.5f) * 0.1f;
                float v = Mathf.Clamp01(shade - dark + crater + detail);
                tex.SetPixel(x, y, new Color(v, v * 0.98f, v * 0.94f, a));
            }
            tex.Apply();
            return tex;
        }

        static Texture2D GlowTexture()
        {
            const int N = 128;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float fx = (x + 0.5f) / N * 2f - 1f, fy = (y + 0.5f) / N * 2f - 1f;
                float r = Mathf.Sqrt(fx * fx + fy * fy);
                float a = Mathf.Pow(Mathf.Clamp01(1f - r), 2.4f) * 0.75f + Mathf.Exp(-r * r * 40f) * 0.25f;
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(a)));
            }
            tex.Apply();
            return tex;
        }

        static Texture2D SparkleTexture()
        {
            const int N = 128;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float fx = (x + 0.5f) / N * 2f - 1f, fy = (y + 0.5f) / N * 2f - 1f;
                float r = Mathf.Sqrt(fx * fx + fy * fy);
                float cross = Mathf.Max(Mathf.Exp(-Mathf.Abs(fx) * 30f) * Mathf.Exp(-Mathf.Abs(fy) * 3.2f), Mathf.Exp(-Mathf.Abs(fy) * 30f) * Mathf.Exp(-Mathf.Abs(fx) * 3.2f));
                float a = cross + Mathf.Exp(-r * r * 26f) * 0.8f;
                a *= Mathf.Clamp01((1f - r) * 4f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(a)));
            }
            tex.Apply();
            return tex;
        }

        static Texture2D LanternTexture()
        {
            const int N = 64;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float fx = (x + 0.5f) / N * 2f - 1f, fy = (y + 0.5f) / N * 2f - 1f;
                // A tall paper body (rounded box) with a hot core low down, and a soft halo.
                float box = Mathf.Max(Mathf.Abs(fx) / 0.34f, Mathf.Abs(fy + 0.05f) / 0.48f);
                float body = Mathf.Clamp01((1f - box) * 6f);
                float core = Mathf.Exp(-(fx * fx + (fy + 0.22f) * (fy + 0.22f)) * 30f);
                float halo = Mathf.Pow(Mathf.Clamp01(1f - Mathf.Sqrt(fx * fx + fy * fy)), 2f) * 0.35f;
                float a = Mathf.Clamp01(body * 0.75f + core * 0.6f + halo);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
            tex.Apply();
            return tex;
        }

        static Texture2D RibbonTexture()
        {
            const int W = 64, H = 32;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float v = (y + 0.5f) / H - 0.5f;
                float a = Mathf.Exp(-v * v / 0.02f) * 0.7f + Mathf.Exp(-v * v / 0.002f) * 0.3f;
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(a)));
            }
            tex.Apply();
            return tex;
        }

        /// <summary>
        /// White-on-clear magic circle: a double outer ring, a band of rune glyphs, tick marks, an eight-point star
        /// with a small ring on each point, an inner ring holding a hexagram, and a hub.
        /// </summary>
        static Texture2D MagicCircleTexture()
        {
            const int N = 1024;
            var px = new Color[N * N];
            var rng = new System.Random(8);
            const int glyphCount = 40;
            var glyphs = new List<Vector4>[glyphCount];
            float[] grid = { -0.75f, 0f, 0.75f };
            for (int g = 0; g < glyphCount; g++)
            {
                glyphs[g] = new List<Vector4>();
                // A stem plus one to three branches, snapped to a 3x3 grid like carved runes.
                float sx = grid[rng.Next(3)] * 0.4f;
                glyphs[g].Add(new Vector4(sx, -0.85f, sx, 0.85f));
                int branches = 1 + rng.Next(3);
                for (int k = 0; k < branches; k++)
                {
                    var a = new Vector2(sx, grid[rng.Next(3)]);
                    var b = new Vector2(grid[rng.Next(3)], grid[rng.Next(3)]);
                    if ((a - b).sqrMagnitude < 0.1f) b = new Vector2(-a.x + 0.75f, a.y - 0.75f);
                    glyphs[g].Add(new Vector4(a.x, a.y, b.x, b.y));
                }
            }
            var star = new Vector2[8];
            for (int i = 0; i < 8; i++) star[i] = new Vector2(Mathf.Cos(i * Mathf.PI / 4f + Mathf.PI / 8f), Mathf.Sin(i * Mathf.PI / 4f + Mathf.PI / 8f)) * 0.74f;
            var hex = new Vector2[6];
            for (int i = 0; i < 6; i++) hex[i] = new Vector2(Mathf.Cos(i * Mathf.PI / 3f + Mathf.PI / 2f), Mathf.Sin(i * Mathf.PI / 3f + Mathf.PI / 2f)) * 0.38f;
            float aa = 2f / N * 1.2f;
            float Line(Vector2 p, Vector2 a, Vector2 b)
            {
                var ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
                return (p - (a + ab * t)).magnitude;
            }
            float Stroke(float d, float w) => Mathf.Clamp01((w - d) / aa + 0.5f);
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                var p = new Vector2((x + 0.5f) / N * 2f - 1f, (y + 0.5f) / N * 2f - 1f);
                float r = p.magnitude;
                float a = 0f;
                if (r > 1f) { px[y * N + x] = new Color(1f, 1f, 1f, 0f); continue; }
                a = Mathf.Max(a, Stroke(Mathf.Abs(r - 0.975f), 0.011f));
                a = Mathf.Max(a, Stroke(Mathf.Abs(r - 0.945f), 0.004f));
                a = Mathf.Max(a, Stroke(Mathf.Abs(r - 0.862f), 0.004f));
                a = Mathf.Max(a, Stroke(Mathf.Abs(r - 0.76f), 0.007f));
                float theta = Mathf.Atan2(p.y, p.x);
                float cell = (theta + Mathf.PI) / (Mathf.PI * 2f) * glyphCount;
                if (r > 0.872f && r < 0.935f)
                {
                    int g = Mathf.Clamp((int)cell, 0, glyphCount - 1);
                    float cellW = Mathf.PI * 2f * 0.903f / glyphCount;
                    var gp = new Vector2((cell - g - 0.5f) * cellW, r - 0.9035f);
                    var scale = new Vector2(cellW * 0.32f, 0.024f);
                    float d = 10f;
                    foreach (var s in glyphs[g])
                        d = Mathf.Min(d, Line(gp, new Vector2(s.x * scale.x, s.y * scale.y), new Vector2(s.z * scale.x, s.w * scale.y)));
                    a = Mathf.Max(a, Stroke(d, 0.0042f));
                }
                if (r > 0.79f && r < 0.835f)
                {
                    float tick = Mathf.Abs(Mathf.Repeat(theta * Mathf.Rad2Deg + 3.75f, 7.5f) - 3.75f) * Mathf.Deg2Rad * r;
                    a = Mathf.Max(a, Stroke(tick, 0.0028f) * (r > 0.805f && r < 0.82f ? 1f : 0.6f));
                }
                for (int i = 0; i < 8; i++)
                {
                    a = Mathf.Max(a, Stroke(Line(p, star[i], star[(i + 3) % 8]), 0.0045f));
                    a = Mathf.Max(a, Stroke(Mathf.Abs((p - star[i]).magnitude - 0.045f), 0.0035f));
                }
                a = Mathf.Max(a, Stroke(Mathf.Abs(r - 0.42f), 0.005f));
                a = Mathf.Max(a, Stroke(Mathf.Abs(r - 0.395f), 0.0025f));
                for (int i = 0; i < 6; i++) a = Mathf.Max(a, Stroke(Line(p, hex[i], hex[(i + 2) % 6]), 0.004f));
                a = Mathf.Max(a, Stroke(Mathf.Abs(r - 0.13f), 0.006f));
                a = Mathf.Max(a, Stroke(r, 0.03f));
                // A faint fill inside the star so the circle glows as a whole, not only its lines.
                a = Mathf.Max(a, 0.07f * Smooth(0.78f, 0.6f, r));
                px[y * N + x] = new Color(1f, 1f, 1f, a);
            }
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, true);
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        static Texture2D CloudTexture(int w, int h, int seed, bool wisp)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w, v = (y + 0.5f) / h;
                float ex = (u - 0.5f) / 0.5f, ey = (v - (wisp ? 0.5f : 0.42f)) / (wisp ? 0.5f : 0.45f);
                float mask = 1f - Smooth(0.25f, 1f, ex * ex + ey * ey);
                if (!wisp) mask *= Smooth(0.02f, 0.3f, v);
                float n = wisp ? Fbm(u * 7f + seed, v * 1.6f + seed * 1.3f, 5) : Fbm(u * 4f + seed, v * 2.2f + seed * 1.3f, 5);
                float d = Smooth(0.38f, 0.72f, n * (0.55f + 0.6f * mask)) * mask;
                float lit = Mathf.Lerp(0.6f, 1f, Smooth(0.1f, 0.95f, v));
                tex.SetPixel(x, y, new Color(lit, lit, lit, d));
            }
            tex.Apply();
            return tex;
        }

        static Texture2D CloudSeaTexture()
        {
            const int N = 1024;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, true);
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = (float)x / N, v = (float)y / N;
                float n = TileFbm(u, v, 6f, 5);
                float d = Smooth(0.32f, 0.7f, n);
                float lit = Mathf.Lerp(0.5f, 1f, d);
                tex.SetPixel(x, y, new Color(lit, lit, lit, Mathf.Lerp(0.45f, 1f, d)));
            }
            tex.Apply();
            return tex;
        }

        static Texture2D FlagstoneTexture()
        {
            const int N = 512, cells = 4;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, true);
            var rng = new System.Random(2);
            var tone = new float[cells * cells * 2];
            for (int i = 0; i < tone.Length; i++) tone[i] = 0.78f + 0.22f * (float)rng.NextDouble();
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = (float)x / N * cells, v = (float)y / N * cells;
                int row = (int)v;
                float su = u + (row % 2) * 0.5f;
                int col = (int)su % cells;
                float fu = su - Mathf.Floor(su), fv = v - row;
                float edge = Mathf.Min(Mathf.Min(fu, 1f - fu), Mathf.Min(fv, 1f - fv));
                float joint = Smooth(0.01f, 0.035f, edge);
                float grain = TileFbm((float)x / N, (float)y / N, 16f, 3);
                float val = tone[row * cells + col] * (0.75f + 0.4f * grain) * Mathf.Lerp(0.35f, 1f, joint) * (0.92f + 0.08f * Smooth(0.03f, 0.12f, edge));
                tex.SetPixel(x, y, new Color(val, val, val * 1.03f, 1f));
            }
            tex.Apply();
            return tex;
        }

        static Texture2D OrnamentTexture()
        {
            const int W = 1024, H = 32;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            var gold = new Color(1f, 0.84f, 0.52f);
            var blue = new Color(0.6f, 0.83f, 1f);
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float u = (x + 0.5f) / W, v = (y + 0.5f) / H - 0.5f;
                var c = u < 0.5f ? Color.Lerp(gold, Color.white, Smooth(0.15f, 0.5f, u)) : Color.Lerp(Color.white, blue, Smooth(0.5f, 0.85f, u));
                float ends = Smooth(0f, 0.3f, u) * Smooth(1f, 0.7f, u);
                float a = (Mathf.Exp(-v * v / 0.004f) + 0.25f * Mathf.Exp(-v * v / 0.05f)) * ends;
                // A gap round the diamond in the middle.
                a *= Smooth(0.018f, 0.032f, Mathf.Abs(u - 0.5f));
                c.a = Mathf.Clamp01(a);
                tex.SetPixel(x, y, c);
            }
            tex.Apply();
            return tex;
        }

        static Texture2D DiamondTexture()
        {
            const int N = 64;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float fx = (x + 0.5f) / N * 2f - 1f, fy = (y + 0.5f) / N * 2f - 1f;
                float d = Mathf.Abs(fx) + Mathf.Abs(fy);
                float core = Mathf.Clamp01((0.55f - d) * N * 0.25f);
                float glow = Mathf.Pow(Mathf.Clamp01(1f - d), 2f) * 0.5f;
                var c = Color.Lerp(new Color(1f, 0.9f, 0.7f), Color.white, core);
                c.a = Mathf.Clamp01(core + glow);
                tex.SetPixel(x, y, c);
            }
            tex.Apply();
            return tex;
        }
    }
}
