using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Waaang.EditorTools
{
    public static class WaaangSceneSetup
    {
        const string MaterialDir = "Assets/Waaang/Materials";

        [MenuItem("Waaang/Build Connection And Game Scenes")]
        public static void Build()
        {
            Directory.CreateDirectory(MaterialDir);
            AssetDatabase.Refresh();
            var connection = BuildConnection();
            var game = BuildGame();
            var scenes = new EditorBuildSettingsScene[2];
            scenes[0] = new EditorBuildSettingsScene(connection, true);
            scenes[1] = new EditorBuildSettingsScene(game, true);
            EditorBuildSettings.scenes = scenes;
            AssetDatabase.SaveAssets();
            Debug.Log("Built " + connection + " and " + game);
        }

        static string BuildConnection()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = CreateCamera("Main Camera", new Vector3(0f, 1.2f, -4f), Quaternion.identity, Color.black);
            camera.clearFlags = CameraClearFlags.SolidColor;
            CreateSun();

            var host = new GameObject("Phone Link");
            var receiver = host.AddComponent<PhonePointer.PhonePointerReceiver>();
            receiver.MaxPhones = 1;
            var view = host.AddComponent<PhonePointer.PhonePointerRegistrationView>();
            view.Receiver = receiver;
            view.enabled = false;
            var link = host.AddComponent<PhoneLink>();
            link.Receiver = receiver;
            link.requiredPhones = 1;
            host.AddComponent<ConnectionFlow>();

            return Save(scene, "Assets/Scenes/Connection.unity");
        }

        static string BuildGame()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.16f, 0.15f, 0.2f);

            CreateRoom();
            var character = CreateCharacter();
            var staff = CreateStaff(character.transform);
            var camera = CreateCamera("Main Camera", Vector3.zero, Quaternion.identity, FogColor);
            StyleKeyLight(CreateSun());
            CreateCutsceneVolume();

            var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = "Hit Marker";
            marker.transform.localScale = Vector3.one * 0.08f;
            Object.DestroyImmediate(marker.GetComponent<Collider>());
            marker.GetComponent<Renderer>().sharedMaterial = Solid("Hit.mat", new Color(1f, 0.25f, 0.85f));

            var beam = CreateBeam();
            var hitLight = marker.AddComponent<Light>();
            hitLight.type = LightType.Point;
            hitLight.color = new Color(1f, 0.72f, 0.35f);
            hitLight.intensity = 6f;
            hitLight.range = 3.2f;
            hitLight.shadows = LightShadows.None;
            AddLightData(marker);

            var rigObject = new GameObject("Wand Pointer");
            var rig = rigObject.AddComponent<WandPointerRig>();
            rig.viewCamera = camera;
            rig.character = character.transform;
            rig.staff = staff.transform;
            rig.rightGrip = staff.transform.Find("Right Grip");
            rig.leftGrip = staff.transform.Find("Left Grip");
            rig.hitMarker = marker.transform;
            rig.aimLine = beam[0];
            rig.glowLine = beam[1];
            rigObject.AddComponent<SecondCaster>().primary = rig;

            ApplyCutsceneLook(character.transform, camera, rig);
            AddNox(rig);
            return Save(scene, "Assets/Scenes/Game.unity");
        }

        // ---------- 2P: Nox ----------

        static readonly Color NoxColor = new Color(0.45f, 0.75f, 1f); // matches the 2P crosshair
        public static float NoxStaffScale = 0.85f;

        [MenuItem("Waaang/Add Nox 2P To Open Game Scene")]
        public static void AddNoxToOpenScene()
        {
            var primary = GameObject.Find("Wand Pointer");
            var rig = primary != null ? primary.GetComponent<WandPointerRig>() : null;
            if (rig == null || rig.character == null)
            {
                Debug.LogError("Open Assets/Scenes/Game.unity first (needs the 1P \"Wand Pointer\" rig).");
                return;
            }
            AddNox(rig);
            EditorSceneManager.MarkSceneDirty(rig.gameObject.scene);
        }

        // Builds Nox, her own wand and a full 2P rig into the scene, switched off. SecondCaster switches them on
        // when a second phone connects. Built here because aligning the wand needs its mesh, which is not
        // readable at runtime.
        static void AddNox(WandPointerRig primary)
        {
            foreach (var old in new[] { "Nox", "Wand Pointer 2", "Laser 2", "Hit Marker 2", "Hit Light 2" })
            {
                var existing = FindInScene(old);
                if (existing != null)
                    Object.DestroyImmediate(existing);
            }

            var caster = primary.GetComponent<SecondCaster>();
            if (caster == null)
                caster = primary.gameObject.AddComponent<SecondCaster>();
            var amelia = primary.character;

            var nox = CreateCharacter(NoxFbx, "Nox", "Nox.mat");
            nox.transform.rotation = amelia.rotation;
            float floorY = nox.transform.position.y;
            Vector3 place = amelia.position + amelia.right * caster.sideOffset;
            nox.transform.position = new Vector3(place.x, floorY, place.z);
            var staff = CreateStaff(nox.transform, NoxWandFbx, NoxWandMat, "NoxWand.mat");
            // Her twisted wand reads a little long next to Amelia's; scale the whole staff (wand, tip, grips).
            staff.transform.localScale = Vector3.one * NoxStaffScale;
            var tipLight = staff.transform.Find("Staff Tip").GetComponent<Light>();
            if (tipLight != null)
                tipLight.color = Color.Lerp(NoxColor, Color.white, 0.3f);

            var pose = nox.AddComponent<CasterPose>();
            pose.idle = CasterIdle();
            var body = nox.GetComponentInChildren<SkinnedMeshRenderer>();
            if (body != null)
                body.sharedMaterial = RimToon("Nox_Rim.mat", body.sharedMaterial);

            var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = "Hit Marker 2";
            marker.transform.localScale = Vector3.one * 0.08f;
            Object.DestroyImmediate(marker.GetComponent<Collider>());
            marker.GetComponent<Renderer>().sharedMaterial = Solid("Hit2.mat", NoxColor);

            var lightObject = new GameObject("Hit Light 2");
            var hitLight = lightObject.AddComponent<Light>();
            hitLight.type = LightType.Point;
            hitLight.color = NoxColor;
            hitLight.intensity = 6f;
            hitLight.range = 3.2f;
            hitLight.shadows = LightShadows.None;
            hitLight.enabled = false;
            AddLightData(lightObject);

            var beam = CreateBeam();
            beam[0].gameObject.name = "Laser 2";

            var rigObject = new GameObject("Wand Pointer 2");
            var rig = rigObject.AddComponent<WandPointerRig>();
            rig.phoneSlot = 1;
            rig.mouseMode = false;
            rig.viewCamera = primary.viewCamera;
            rig.character = nox.transform;
            rig.staff = staff.transform;
            rig.rightGrip = staff.transform.Find("Right Grip");
            rig.leftGrip = staff.transform.Find("Left Grip");
            rig.hitMarker = marker.transform;
            rig.hitLight = hitLight;
            rig.aimLine = beam[0];
            rig.glowLine = beam[1];
            rig.roomMask = primary.roomMask;
            rig.lightSurfaceOffset = primary.lightSurfaceOffset;
            rig.beamPrefab = primary.beamPrefab;
            SecondCaster.CopySettings(primary, rig);
            // Nox stands on the right, so she holds the staff on her left side, toward the middle of the screen.
            rig.leftHanded = true;
            rig.beamTint = new Color(0.55f, 0.65f, 1f, 1f);
            rig.trailColor = new Color(0.45f, 0.85f, 1.6f, 1f);

            caster.primary = primary;
            caster.second = rig;
            FrameBoth(caster, primary.viewCamera, amelia, nox.transform);

            nox.SetActive(false);
            rigObject.SetActive(false);
            beam[0].gameObject.SetActive(false);
            marker.SetActive(false);
            EditorUtility.SetDirty(caster);
            EditorUtility.SetDirty(rig);
        }

        // ---------- Shared: one caster from a profile ----------

        /// <summary>
        /// Builds a complete caster into the open scene from <paramref name="profile"/>: the character (1.5x, facing
        /// +Z, on the floor at <paramref name="position"/>), her staff, laser, hit marker and light, CasterPose idle
        /// and a WandPointerRig on its own object (returned). Same parts and settings as the Game scene's casters, so
        /// other scenes (the period cutscenes) play identically.
        /// </summary>
        public static WandPointerRig CreateCaster(AiHackathon.Game.CasterProfile profile, Camera camera, Vector3 position, int phoneSlot)
        {
            string fbx = AssetDatabase.GetAssetPath(profile.characterModel);
            string name = profile.displayName;
            var character = CreateCharacter(fbx, name, name + ".mat");
            character.transform.position = new Vector3(position.x, character.transform.position.y + position.y, position.z);
            var body = character.GetComponentInChildren<SkinnedMeshRenderer>();
            if (body != null && profile.bodyMaterial != null)
                body.sharedMaterial = profile.bodyMaterial;

            string wandMat = AssetDatabase.GetAssetPath(profile.wandMaterial);
            var staff = CreateStaff(character.transform, AssetDatabase.GetAssetPath(profile.wandModel), wandMat,
                string.IsNullOrEmpty(wandMat) ? name + "Wand.mat" : System.IO.Path.GetFileName(wandMat));
            // Grip a quarter of the way up the wand, like ApplyCutsceneLook.
            var tip = staff.transform.Find("Staff Tip");
            var lead = staff.transform.Find("Right Grip");
            if (tip != null && lead != null)
                lead.localPosition = tip.localPosition * 0.2f;
            FitStaffTip(staff.transform);
            staff.transform.localScale = Vector3.one * profile.staffScale;
            var tipLight = tip != null ? tip.GetComponent<Light>() : null;
            if (tipLight != null)
            {
                tipLight.color = profile.tipLightColor;
                tipLight.intensity = profile.tipLightIntensity;
                tipLight.range = 1.2f;
                tipLight.enabled = profile.tipLightIntensity > 0f;
            }

            var pose = character.AddComponent<CasterPose>();
            pose.idle = profile.idle != null ? profile.idle : FindClip(MageFbx, "Idle stance");

            var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = "Hit Marker " + name;
            marker.transform.localScale = Vector3.one * 0.08f;
            Object.DestroyImmediate(marker.GetComponent<Collider>());
            marker.GetComponent<Renderer>().sharedMaterial = Solid("Hit_" + name + ".mat", profile.playerColor);
            marker.SetActive(false);

            var lightObject = new GameObject("Hit Light " + name);
            var hitLight = lightObject.AddComponent<Light>();
            hitLight.type = LightType.Point;
            hitLight.color = profile.playerColor;
            hitLight.intensity = profile.hitLightIntensity;
            hitLight.range = 2.4f;
            hitLight.shadows = LightShadows.None;
            hitLight.enabled = false;
            AddLightData(lightObject);

            var beam = CreateBeam();
            beam[0].gameObject.name = "Laser " + name;

            var rigObject = new GameObject("Wand Pointer " + name);
            var rig = rigObject.AddComponent<WandPointerRig>();
            rig.phoneSlot = phoneSlot;
            rig.mouseMode = phoneSlot == 0;
            rig.viewCamera = camera;
            rig.character = character.transform;
            rig.staff = staff.transform;
            rig.rightGrip = lead;
            rig.leftGrip = staff.transform.Find("Left Grip");
            rig.hitMarker = marker.transform;
            rig.hitLight = hitLight;
            rig.aimLine = beam[0];
            rig.glowLine = beam[1];
            rig.useLeftHand = true;
            rig.holdOffset = new Vector3(0.32f, 0f, 0.12f);
            rig.holdReach = 0.04f;
            rig.fingerCurl = 1f;
            rig.staffUpright = 0f;
            rig.leftHanded = profile.leftHanded;
            rig.beamTint = profile.beamTint;
            rig.trailColor = profile.trailColor;
            if (profile.beamPrefab != null)
                rig.beamPrefab = profile.beamPrefab;
            rig.beamThickness = profile.beamThickness;
            rig.impactSize = 0.05f;
            return rig;
        }

        // Duo shot: centred behind both players and a little higher, so Amelia's back sits lower-left, Nox's
        // lower-right and the room they aim into fills the middle.
        public static Vector3 DuoCamBack = new Vector3(0f, 0.55f, -1.45f);
        public static float DuoCamPitch = 9f;
        public static float DuoCamFov = 62f;

        static void FrameBoth(SecondCaster caster, Camera camera, Transform first, Transform second)
        {
            var headA = Find(first, "Head");
            var headB = Find(second, "Head");
            Vector3 a = headA != null ? headA.position : first.position + Vector3.up * 1.4f;
            Vector3 b = headB != null ? headB.position : second.position + Vector3.up * 1.4f;
            Vector3 mid = (a + b) * 0.5f;
            Vector3 forward = first.forward;
            Vector3 right = first.right;
            caster.duoCameraPosition = mid + right * DuoCamBack.x + Vector3.up * DuoCamBack.y + forward * DuoCamBack.z;
            caster.duoCameraEuler = (Quaternion.LookRotation(forward) * Quaternion.Euler(DuoCamPitch, 0f, 0f)).eulerAngles;
            caster.duoCameraFov = DuoCamFov;
        }

        static GameObject FindInScene(string name)
        {
            var roots = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i].name == name)
                    return roots[i];
            }
            return null;
        }

        // ---------- Cutscene look (Assets/Cutscene/Editor/CutsceneKit.cs) ----------

        const string MageFbx = "Assets/TripoModels/fantasy_character_3d_model_1/fantasy_character_3d_model_1.fbx";
        static readonly Color FogColor = new Color(0.12f, 0.1f, 0.15f);

        [MenuItem("Waaang/Apply Cutscene Look To Open Game Scene")]
        public static void ApplyCutsceneLookToOpenScene()
        {
            var character = GameObject.Find("Amelia");
            var cameraObject = GameObject.Find("Main Camera");
            var rig = Object.FindAnyObjectByType<WandPointerRig>();
            if (character == null || cameraObject == null || rig == null)
            {
                Debug.LogError("Open Assets/Scenes/Game.unity first (needs Amelia, Main Camera and a WandPointerRig).");
                return;
            }
            var sun = GameObject.Find("Directional Light");
            if (sun != null)
                StyleKeyLight(sun.GetComponent<Light>());
            ApplyCutsceneLook(character.transform, cameraObject.GetComponent<Camera>(), rig);
            EditorSceneManager.MarkSceneDirty(character.scene);
        }

        static void ApplyCutsceneLook(Transform character, Camera camera, WandPointerRig rig)
        {
            // Same grade as the cutscenes: purple fog that the camera clears to, brighter cool ambient.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = 0.045f;
            RenderSettings.fogColor = FogColor;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.42f, 0.4f, 0.5f);
            RenderSettings.skybox = null;

            // The cutscene mage idles with a wand in the right hand; same Tripo skeleton, so it drives Amelia too.
            var pose = character.GetComponent<CasterPose>();
            if (pose == null)
                pose = character.gameObject.AddComponent<CasterPose>();
            pose.idle = CasterIdle();

            var body = character.GetComponentInChildren<SkinnedMeshRenderer>();
            if (body != null)
                body.sharedMaterial = RimToon("Amelia_Rim.mat", body.sharedMaterial);
            CreateRimLight(character);

            // Grip a quarter of the way up the wand, like the mage's fist, not at its very end.
            var staff = rig.staff;
            var tip = staff != null ? staff.Find("Staff Tip") : null;
            if (tip != null && rig.rightGrip != null)
                rig.rightGrip.localPosition = tip.localPosition * 0.2f;
            FitStaffTip(staff);
            // Both hands on the shaft, pointing straight at the target.
            rig.useLeftHand = true;
            rig.holdOffset = new Vector3(0.32f, 0f, 0.12f);
            rig.holdReach = 0.04f;
            rig.fingerCurl = 1f;
            rig.staffUpright = 0f;

            FrameOverShoulder(camera, character);
            EditorUtility.SetDirty(rig);
            EditorUtility.SetDirty(pose);
        }

        // Seamless two-handed idle (Tools/Cutscene/Build Period Pose Clips); the old mage idle jumped at its loop point.
        const string CasterIdlePath = "Assets/Cutscene/Periods/Anim/Caster_Idle.anim";

        static AnimationClip CasterIdle()
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(CasterIdlePath);
            return clip != null ? clip : FindClip(MageFbx, "Idle stance");
        }

        static AnimationClip FindClip(string fbx, string prefix)
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath(fbx);
            for (int i = 0; i < assets.Length; i++)
            {
                var clip = assets[i] as AnimationClip;
                if (clip != null && clip.name.StartsWith(prefix))
                    return clip;
            }
            Debug.LogWarning("No clip starting with '" + prefix + "' in " + fbx);
            return null;
        }

        // lilToon copy of the body material with the cutscene rim (CutsceneKit.RimMaterial), so her back reads
        // against the room from the over-the-shoulder camera.
        static Material RimToon(string file, Material source)
        {
            string path = MaterialDir + "/" + file;
            var shader = Shader.Find("lilToon");
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = shader != null ? new Material(shader) : new Material(source);
                AssetDatabase.CreateAsset(material, path);
            }
            Texture texture = null;
            if (source != null)
            {
                texture = source.mainTexture;
                if (texture == null && source.HasProperty("_BaseMap"))
                    texture = source.GetTexture("_BaseMap");
            }
            if (texture != null)
            {
                material.mainTexture = texture;
                if (material.HasProperty("_MainTex"))
                    material.SetTexture("_MainTex", texture);
            }
            material.SetFloat("_UseRim", 1f);
            material.SetColor("_RimColor", new Color(0.85f, 0.75f, 1f, 1f));
            material.SetFloat("_RimBorder", 0.6f);
            material.SetFloat("_RimBlur", 0.25f);
            material.SetFloat("_RimFresnelPower", 2.5f);
            material.SetFloat("_RimMainStrength", 0f);
            material.SetFloat("_RimShadowMask", 0.3f);
            EditorUtility.SetDirty(material);
            return material;
        }

        static void StyleKeyLight(Light light)
        {
            if (light == null)
                return;
            // CutsceneKit.StandardLights key: cool, soft shadows; here from behind the camera so it lights her back
            // and the walls she is aiming at.
            light.color = new Color(0.72f, 0.72f, 0.9f);
            light.intensity = 1.15f;
            light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(38f, 25f, 0f);
        }

        // Purple rim spot from in front of her, aimed back at her shoulders, like the cutscene rim on the boss.
        static void CreateRimLight(Transform character)
        {
            var old = GameObject.Find("Rim Light");
            if (old != null)
                Object.DestroyImmediate(old);
            var head = Find(character, "Head");
            Vector3 focus = head != null ? head.position - Vector3.up * 0.25f : character.position + Vector3.up * 1.2f;
            var go = new GameObject("Rim Light");
            var light = go.AddComponent<Light>();
            light.type = LightType.Spot;
            light.color = new Color(0.65f, 0.3f, 1f);
            light.intensity = 7f;
            light.range = 6f;
            light.spotAngle = 55f;
            light.shadows = LightShadows.None;
            go.transform.position = focus + character.forward * 1.6f - character.right * 0.9f + Vector3.up * 0.7f;
            go.transform.LookAt(focus);
            AddLightData(go);
        }

        static void CreateRoom()
        {
            var floorMat = Solid("Floor.mat", new Color(0.35f, 0.36f, 0.38f));
            var wallMat = Solid("Wall.mat", new Color(0.62f, 0.58f, 0.52f));
            var ceilingMat = Solid("Ceiling.mat", new Color(0.75f, 0.76f, 0.78f));
            Box("Floor", new Vector3(0f, -0.1f, 4f), new Vector3(8f, 0.2f, 8f), floorMat);
            Box("Ceiling", new Vector3(0f, 3.1f, 4f), new Vector3(8f, 0.2f, 8f), ceilingMat);
            Box("Wall Left", new Vector3(-4f, 1.5f, 4f), new Vector3(0.2f, 3f, 8f), wallMat);
            Box("Wall Right", new Vector3(4f, 1.5f, 4f), new Vector3(0.2f, 3f, 8f), wallMat);
            Box("Wall Front", new Vector3(0f, 1.5f, 8f), new Vector3(8f, 3f, 0.2f), wallMat);
            Box("Wall Back", new Vector3(0f, 1.5f, 0f), new Vector3(8f, 3f, 0.2f), wallMat);
            Box("Crate Red", new Vector3(-2.3f, 0.35f, 5.1f), new Vector3(0.7f, 0.7f, 0.7f), Solid("CrateRed.mat", new Color(0.75f, 0.22f, 0.18f)));
            Box("Crate Blue", new Vector3(1.5f, 0.55f, 5.2f), new Vector3(0.8f, 1.1f, 0.8f), Solid("CrateBlue.mat", new Color(0.18f, 0.38f, 0.75f)));
            Box("Crate Green", new Vector3(0.2f, 0.9f, 6.8f), new Vector3(1.1f, 1.8f, 0.45f), Solid("CrateGreen.mat", new Color(0.2f, 0.62f, 0.32f)));
            Box("Crate Yellow", new Vector3(2.2f, 0.25f, 2.6f), new Vector3(0.5f, 0.5f, 0.5f), Solid("CrateYellow.mat", new Color(0.85f, 0.72f, 0.18f)));
        }

        const string AmeliaFbx = "Assets/TripoModels/amelia_polaris_3d_model/amelia_polaris_3d_model.fbx";
        const string AmeliaWandFbx = "Assets/TripoModels/amelia_weapon/amelia_weapon.fbx";
        const string AmeliaWandMat = "Assets/TripoModels/amelia_weapon/Materials/magical_wand_3d_model.mat";
        const string NoxFbx = "Assets/TripoModels/nox/nox.fbx";
        const string NoxWandFbx = "Assets/TripoModels/nox_weapon/twisted_magic_wand_3d_model.fbx";
        const string NoxWandMat = "Assets/TripoModels/nox_weapon/Materials/twisted_magic_wand_3d_model.mat";

        static GameObject CreateCharacter()
        {
            var character = CreateCharacter(AmeliaFbx, "Amelia", "Amelia.mat");
            var placed = character.transform.position;
            character.transform.position = new Vector3(-1.23f, placed.y, 3.84f);
            return character;
        }

        // Instantiates a Tripo character at 1.5x, turned to face +Z and standing on the floor (y = 0 at its feet).
        static GameObject CreateCharacter(string fbx, string name, string materialFile)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            var character = Object.Instantiate(prefab);
            character.name = name;
            character.transform.localScale = Vector3.one * 1.5f;

            var head = Find(character.transform, "Head");
            var leftEye = Find(character.transform, "Left_Eye");
            var rightEye = Find(character.transform, "Right_Eye");
            if (head != null && leftEye != null && rightEye != null)
            {
                Vector3 face = (leftEye.position + rightEye.position) * 0.5f - head.position;
                face.y = 0f;
                if (face.sqrMagnitude > 0.000001f)
                    character.transform.rotation = Quaternion.FromToRotation(face, Vector3.forward) * character.transform.rotation;
            }

            var renderer = character.GetComponentInChildren<SkinnedMeshRenderer>();
            if (renderer != null)
            {
                var texture = renderer.sharedMaterial != null ? renderer.sharedMaterial.mainTexture : null;
                var lit = Solid(materialFile, Color.white);
                if (texture != null)
                {
                    lit.mainTexture = texture;
                    lit.SetTexture("_BaseMap", texture);
                }
                renderer.sharedMaterial = lit;
                float lift = -renderer.bounds.min.y;
                character.transform.position += new Vector3(0f, lift, 0f);
            }
            return character;
        }

        static GameObject CreateStaff(Transform character)
        {
            return CreateStaff(character, AmeliaWandFbx, AmeliaWandMat, "Wand.mat");
        }

        static GameObject CreateStaff(Transform character, string wandFbx, string wandSourceMaterial, string wandMaterialFile)
        {
            var chest = Find(character, "Chest");
            Vector3 chestLocal = chest != null
                ? character.InverseTransformPoint(chest.position)
                : new Vector3(0f, 0.7f, 0f);

            var staff = new GameObject("Staff");
            staff.transform.SetParent(character, false);
            staff.transform.localPosition = chestLocal + new Vector3(0.03f, -0.04f, 0.15f);
            staff.transform.localRotation = Quaternion.identity;

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(wandFbx);
            var model = Object.Instantiate(prefab, staff.transform, false);
            model.name = "Magical Wand";
            float scale = Mathf.Max(0.001f, character.lossyScale.y);
            model.transform.localScale = Vector3.one / scale;
            Vector3 tipLocal = AlignWand(model.transform);

            var source = AssetDatabase.LoadAssetAtPath<Material>(wandSourceMaterial);
            var texture = source != null ? source.mainTexture : null;
            var wandMaterial = Solid(wandMaterialFile, Color.white);
            if (texture != null)
            {
                wandMaterial.mainTexture = texture;
                wandMaterial.SetTexture("_BaseMap", texture);
            }
            var renderers = model.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
                renderers[i].sharedMaterial = wandMaterial;

            var tip = new GameObject("Staff Tip").transform;
            tip.SetParent(staff.transform, false);
            tip.localPosition = tipLocal;
            FitStaffTip(staff.transform);
            var tipLight = tip.gameObject.AddComponent<Light>();
            tipLight.type = LightType.Point;
            tipLight.color = new Color(1f, 0.78f, 0.45f);
            tipLight.intensity = 3.5f;
            tipLight.range = 1.8f;
            tipLight.shadows = LightShadows.None;
            AddLightData(tip.gameObject);

            var right = new GameObject("Right Grip").transform;
            right.SetParent(staff.transform, false);
            right.localPosition = new Vector3(0f, 0.01f, 0f);
            var left = new GameObject("Left Grip").transform;
            left.SetParent(staff.transform, false);
            left.localPosition = new Vector3(0f, 0.06f, 0f);
            return staff;
        }

        const float StaffTipInset = 0.02f;

        [MenuItem("Waaang/Fit Staff Tip To Wand End In Open Scene")]
        public static void FitStaffTipInOpenScene()
        {
            var rigs = Object.FindObjectsByType<WandPointerRig>(FindObjectsInactive.Include);
            for (int i = 0; i < rigs.Length; i++)
                FitStaffTip(rigs[i].staff);
            if (rigs.Length > 0)
                EditorSceneManager.MarkSceneDirty(rigs[0].gameObject.scene);
        }

        // AlignWand returns the middle of the wand head, ~18 cm short of the end, so the beam left from inside the
        // ornament. Put "Staff Tip" on the staff axis just inside the mesh's far end instead. Done here in the editor
        // because the wand mesh is not Read/Write, so its vertices cannot be read at runtime.
        static void FitStaffTip(Transform staff)
        {
            if (staff == null)
                return;
            var tip = staff.Find("Staff Tip");
            if (tip == null)
                return;
            float top = float.MinValue;
            var filters = staff.GetComponentsInChildren<MeshFilter>(true);
            for (int f = 0; f < filters.Length; f++)
            {
                var mesh = filters[f].sharedMesh;
                if (mesh == null)
                    continue;
                var vertices = mesh.vertices;
                var toStaff = staff.worldToLocalMatrix * filters[f].transform.localToWorldMatrix;
                for (int i = 0; i < vertices.Length; i++)
                    top = Mathf.Max(top, toStaff.MultiplyPoint3x4(vertices[i]).y);
            }
            if (top == float.MinValue)
                return;
            float inset = StaffTipInset / Mathf.Max(0.0001f, staff.lossyScale.y);
            Undo.RecordObject(tip, "Fit Staff Tip");
            tip.localPosition = new Vector3(0f, Mathf.Max(0f, top - inset), 0f);
            EditorUtility.SetDirty(tip);
        }

        static Vector3 AlignWand(Transform model)
        {
            var filter = model.GetComponentInChildren<MeshFilter>();
            var vertices = filter.sharedMesh.vertices;
            Vector3 handle = Vector3.zero;
            Vector3 head = Vector3.zero;
            int handleCount = 0;
            int headCount = 0;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 vertex = vertices[i];
                if (vertex.y < 0.25f)
                {
                    handle += vertex;
                    handleCount++;
                }
                else if (vertex.y > 0.7f)
                {
                    head += vertex;
                    headCount++;
                }
            }

            handle /= Mathf.Max(1, handleCount);
            head /= Mathf.Max(1, headCount);
            Vector3 axis = head - handle;
            Quaternion align = Quaternion.FromToRotation(axis, Vector3.up);
            float inv = model.localScale.x;
            model.localRotation = align;
            model.localPosition = -(align * (handle * inv));
            return align * ((head - handle) * inv);
        }

        static LineRenderer[] CreateBeam()
        {
            var root = new GameObject("Laser");
            var core = root.AddComponent<LineRenderer>();
            var glowObject = new GameObject("Laser Glow");
            glowObject.transform.SetParent(root.transform, false);
            var glow = glowObject.AddComponent<LineRenderer>();
            ConfigureBeam(core, LaserMaterial("LaserCore.mat", new Color(1.6f, 0.95f, 0.55f, 1f), false), 0.018f, 0.012f);
            ConfigureBeam(glow, LaserMaterial("LaserGlow.mat", new Color(1f, 0.55f, 0.15f, 0.35f), true), 0.09f, 0.05f);
            return new[] { core, glow };
        }

        static void ConfigureBeam(LineRenderer line, Material material, float start, float end)
        {
            line.material = material;
            line.startWidth = start;
            line.endWidth = end;
            line.positionCount = 2;
            line.useWorldSpace = true;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.numCapVertices = 4;
        }

        // Behind her and off to the right: her back and head fill the lower-left corner, the staff rises
        // through the lower middle and the rest of the frame is the room she is aiming into.
        public static Vector3 ShoulderCamBack = new Vector3(0.6f, 0.3f, -1.45f);
        public static float ShoulderCamPitch = -4f;
        public static float ShoulderCamYaw = 2f;
        public static float ShoulderCamFov = 50f;

        static void FrameOverShoulder(Camera camera, Transform character)
        {
            var head = Find(character, "Head");
            Vector3 headPosition = head != null ? head.position : character.position + Vector3.up * 1.4f;
            camera.fieldOfView = ShoulderCamFov;
            camera.nearClipPlane = 0.02f;
            camera.allowHDR = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = FogColor;
            camera.transform.position = headPosition
                + character.right * ShoulderCamBack.x
                + Vector3.up * ShoulderCamBack.y
                + character.forward * ShoulderCamBack.z;
            camera.transform.rotation = Quaternion.LookRotation(character.forward)
                * Quaternion.Euler(ShoulderCamPitch, ShoulderCamYaw, 0f);
            EnablePost(camera);
        }

        static void EnablePost(Camera camera)
        {
            var dataType = System.Type.GetType(
                "UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime");
            if (dataType == null)
                return;
            var data = camera.GetComponent(dataType);
            if (data == null)
                data = camera.gameObject.AddComponent(dataType);
            var post = dataType.GetProperty("renderPostProcessing");
            if (post != null)
                post.SetValue(data, true, null);
            var antialiasing = dataType.GetProperty("antialiasing");
            if (antialiasing != null)
                antialiasing.SetValue(data, System.Enum.Parse(antialiasing.PropertyType, "SubpixelMorphologicalAntiAliasing"), null);
        }

        static void CreateCutsceneVolume()
        {
            var profile = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.VolumeProfile>(
                "Assets/Cutscene/Volumes/CutsceneBase.asset");
            if (profile == null)
                return;
            var go = new GameObject("Cutscene Volume");
            var volume = go.AddComponent<UnityEngine.Rendering.Volume>();
            volume.isGlobal = true;
            volume.priority = 1f;
            volume.weight = 1f;
            volume.sharedProfile = profile;
        }

        static Camera CreateCamera(string name, Vector3 position, Quaternion rotation, Color background)
        {
            var go = new GameObject(name);
            go.tag = "MainCamera";
            var camera = go.AddComponent<Camera>();
            camera.transform.position = position;
            camera.transform.rotation = rotation;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = background;
            var data = System.Type.GetType(
                "UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime");
            if (data != null)
                go.AddComponent(data);
            return camera;
        }

        static Light CreateSun()
        {
            var go = new GameObject("Directional Light");
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(0.86f, 0.82f, 0.9f);
            light.intensity = 1.6f;
            light.shadows = LightShadows.Soft;
            go.transform.rotation = Quaternion.LookRotation(new Vector3(0.25f, -0.55f, 1f));
            var data = System.Type.GetType(
                "UnityEngine.Rendering.Universal.UniversalAdditionalLightData, Unity.RenderPipelines.Universal.Runtime");
            if (data != null)
                go.AddComponent(data);
            return light;
        }

        static GameObject Box(string name, Vector3 position, Vector3 scale, Material material)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.position = position;
            box.transform.localScale = scale;
            box.GetComponent<Renderer>().sharedMaterial = material;
            return box;
        }

        static Material Solid(string file, Color color)
        {
            string path = MaterialDir + "/" + file;
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                existing.color = color;
                existing.SetColor("_BaseColor", color);
                return existing;
            }

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var material = new Material(shader);
            material.color = color;
            material.SetColor("_BaseColor", color);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        static Material LaserMaterial(string file, Color color, bool additive)
        {
            string path = MaterialDir + "/" + file;
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            material.color = color;
            material.SetColor("_BaseColor", color);
            if (additive)
            {
                material.SetFloat("_Surface", 1f);
                material.SetFloat("_Blend", 1f);
                material.SetOverrideTag("RenderType", "Transparent");
                material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)BlendMode.One);
                material.SetInt("_ZWrite", 0);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.EnableKeyword("_BLENDMODE_ADD");
                material.renderQueue = (int)RenderQueue.Transparent;
            }
            EditorUtility.SetDirty(material);
            return material;
        }

        static void AddLightData(GameObject go)
        {
            var data = System.Type.GetType(
                "UnityEngine.Rendering.Universal.UniversalAdditionalLightData, Unity.RenderPipelines.Universal.Runtime");
            if (data != null && go.GetComponent(data) == null)
                go.AddComponent(data);
        }

        static Transform Find(Transform root, string boneName)
        {
            var all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].name == boneName)
                    return all[i];
            }
            return null;
        }

        static string Save(Scene scene, string path)
        {
            EditorSceneManager.SaveScene(scene, path);
            return path;
        }
    }
}
