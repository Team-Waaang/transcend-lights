using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Waaang.EditorTools
{
    /// <summary>
    /// Toon shading look-dev for Amelia and Nox (D is the one the game uses). Builds Assets/Waaang/Lookbook/ToonLookbook.unity: one pair of the
    /// two per shading variant (lilToon shadow, rim, rim shade, backlight, outline), each a copy of the game's
    /// Amelia_Rim / Nox_Rim with the variant on top. The game materials are never touched.
    /// Render writes front / face / game-camera stills per variant plus a contact sheet to Temp/ToonLookbook/;
    /// RenderInGame swaps each variant onto the casters of the period scenes (in memory, not saved) and renders them.
    /// </summary>
    public static class ToonLookbook
    {
        const string Root = "Assets/Waaang/Lookbook";
        const string MatDir = Root + "/Materials";
        public const string ScenePath = Root + "/ToonLookbook.unity";
        public const string OutDir = "Temp/ToonLookbook";

        const string AmeliaFbx = "Assets/TripoModels/amelia_polaris_3d_model/amelia_polaris_3d_model.fbx";
        const string NoxFbx = "Assets/TripoModels/nox/nox.fbx";
        const string AmeliaRim = "Assets/Waaang/Materials/Amelia_Rim.mat";
        const string NoxRim = "Assets/Waaang/Materials/Nox_Rim.mat";
        const string IdleClip = "Assets/Cutscene/Periods/Anim/Caster_Idle.anim";
        const string PostProfile = "Assets/Cutscene/Period1/Volumes/Period1Base.asset";

        const float GroupSpacing = 3.2f;
        const float PairHalfGap = 0.42f;

        // ---------- Variants ----------

        sealed class Variant
        {
            public string id;
            public string title;
            public bool outline;
            public Action<Material, bool> apply; // (material, isNox)
        }

        static readonly Variant[] Variants =
        {
            new Variant { id = "A_Flat", title = "A  Flat (before)", apply = (m, nox) => { } },
            new Variant { id = "B_SoftCel", title = "B  Soft Cel", apply = SoftCel },
            new Variant { id = "C_CrispAnime", title = "C  Crisp 2-Tone", apply = CrispAnime },
            new Variant { id = "D_RimShade", title = "D  Rim Shade (in game)", apply = RimShadeVolume },
            new Variant { id = "E_Backlit", title = "E  Dramatic Backlit", apply = DramaticBacklit },
            new Variant { id = "F_Outline", title = "F  Soft Cel + Outline", outline = true, apply = OutlineSoftCel },
        };

        // Soft single-step cel shadow, lavender-tinted, with a warm band at the terminator. Rim kept as is.
        static void SoftCel(Material m, bool nox)
        {
            Shadow(m, strength: 0.8f, border: 0.45f, blur: 0.35f, color: new Color(0.8f, 0.74f, 0.92f), receive: 0.4f);
            m.SetColor("_ShadowBorderColor", new Color(0.9f, 0.4f, 0.3f, 1f));
            m.SetFloat("_ShadowBorderRange", 0.1f);
            m.SetFloat("_ShadowEnvStrength", 0.2f);
        }

        // Hard anime cel with a second, deeper purple tone in the core shadow and a crisp rim line.
        static void CrispAnime(Material m, bool nox)
        {
            Shadow(m, strength: 1f, border: 0.5f, blur: 0.04f, color: new Color(0.74f, 0.68f, 0.9f), receive: 0.75f);
            m.SetColor("_Shadow2ndColor", new Color(0.52f, 0.44f, 0.74f, 1f));
            m.SetFloat("_Shadow2ndBorder", 0.2f);
            m.SetFloat("_Shadow2ndBlur", 0.05f);
            m.SetColor("_ShadowBorderColor", new Color(1f, 0.35f, 0.25f, 1f));
            m.SetFloat("_ShadowBorderRange", 0.04f);
            Rim(m, nox, new Color(0.9f, 0.82f, 1f, 0.9f), border: 0.7f, blur: 0.06f, fresnel: 3f);
            m.SetFloat("_RimShadowMask", 0.6f);
        }

        // The look the casters use in game (WaaangSceneSetup.ApplyToonShading).
        static void RimShadeVolume(Material m, bool nox) => WaaangSceneSetup.ApplyToonShading(m, nox);

        // The casters before toon shading: no shadow, no rim shade, rim on every side.
        static void Flat(Material m)
        {
            m.SetFloat("_UseShadow", 0f);
            m.SetFloat("_UseRimShade", 0f);
            m.SetFloat("_RimDirStrength", 0f);
        }

        // Deeper shadow, stronger saturated purple rim and a backlight glow where light comes from behind.
        static void DramaticBacklit(Material m, bool nox)
        {
            Shadow(m, strength: 1f, border: 0.55f, blur: 0.12f, color: new Color(0.58f, 0.52f, 0.8f), receive: 0.85f);
            m.SetColor("_ShadowBorderColor", new Color(0.8f, 0.3f, 0.5f, 1f));
            m.SetFloat("_ShadowBorderRange", 0.06f);
            Rim(m, nox, new Color(0.78f, 0.55f, 1f, 1f), border: 0.5f, blur: 0.3f, fresnel: 2f);
            m.SetFloat("_RimEnableLighting", 0.6f);
            m.SetFloat("_RimShadowMask", 0.2f);
            m.SetFloat("_UseBacklight", 1f);
            m.SetColor("_BacklightColor", new Color(1f, 0.82f, 0.95f, 1f));
            m.SetFloat("_BacklightBorder", 0.35f);
            m.SetFloat("_BacklightBlur", 0.05f);
            m.SetFloat("_BacklightDirectivity", 5f);
            m.SetFloat("_UseRimShade", 1f);
            m.SetColor("_RimShadeColor", new Color(0.6f, 0.5f, 0.75f, 0.6f));
            m.SetFloat("_RimShadeBorder", 0.55f);
            m.SetFloat("_RimShadeBlur", 0.45f);
        }

        // B on lilToon's outline shader: a thin dark-plum line around the silhouette.
        static void OutlineSoftCel(Material m, bool nox)
        {
            SoftCel(m, nox);
            m.SetFloat("_OutlineWidth", nox ? 0.16f : 0.2f);
            m.SetColor("_OutlineColor", new Color(0.24f, 0.17f, 0.33f, 1f));
            m.SetFloat("_OutlineEnableLighting", 0.6f);
        }

        static void Shadow(Material m, float strength, float border, float blur, Color color, float receive)
        {
            m.SetFloat("_UseShadow", 1f);
            m.SetFloat("_ShadowStrength", strength);
            m.SetFloat("_ShadowBorder", border);
            m.SetFloat("_ShadowBlur", blur);
            m.SetColor("_ShadowColor", new Color(color.r, color.g, color.b, 1f));
            m.SetFloat("_ShadowMainStrength", 0f);
            m.SetColor("_Shadow2ndColor", new Color(0.68f, 0.66f, 0.79f, 0f)); // alpha 0 = no second tone
            m.SetFloat("_ShadowReceive", receive);
        }

        // Nox's thin hair strands catch every rim edge (see WaaangSceneSetup.NoxRim), so hers is weaker and tighter.
        static void Rim(Material m, bool nox, Color color, float border, float blur, float fresnel)
        {
            m.SetFloat("_UseRim", 1f);
            if (nox)
            {
                color.a *= 0.55f;
                border = Mathf.Min(0.95f, border + 0.1f);
                blur *= 0.75f;
                fresnel += 1f;
            }
            m.SetColor("_RimColor", color);
            m.SetFloat("_RimBorder", border);
            m.SetFloat("_RimBlur", blur);
            m.SetFloat("_RimFresnelPower", fresnel);
        }

        static Material VariantMaterial(Variant v, bool nox)
        {
            var source = AssetDatabase.LoadAssetAtPath<Material>(nox ? NoxRim : AmeliaRim);
            if (source == null) throw new Exception("Missing " + (nox ? NoxRim : AmeliaRim));
            string path = $"{MatDir}/{v.id}_{(nox ? "Nox" : "Amelia")}.mat";
            AssetDatabase.DeleteAsset(path);
            var m = new Material(source) { name = Path.GetFileNameWithoutExtension(path) };
            if (v.outline)
            {
                var outline = Shader.Find("Hidden/lilToonOutline");
                if (outline == null) throw new Exception("lilToon outline shader not found");
                m.shader = outline;
            }
            // Every look starts from the flat caster, so they stay independent of what the game uses.
            Flat(m);
            v.apply(m, nox);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        // ---------- Scene ----------

        [MenuItem("Waaang/Toon Lookbook/Build Scene")]
        static void BuildMenu() => Debug.Log("[ToonLookbook] " + Build());

        public static string Build()
        {
            RefuseDirty();
            EnsureFolder(Root);
            EnsureFolder(MatDir);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.fog = false;
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.42f, 0.4f, 0.5f); // Game scene ambient

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.16f, 0.14f, 0.2f);
            cam.nearClipPlane = 0.02f;
            var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

            var post = AssetDatabase.LoadAssetAtPath<VolumeProfile>(PostProfile);
            if (post != null)
            {
                var volume = new GameObject("Post").AddComponent<Volume>();
                volume.isGlobal = true;
                volume.sharedProfile = post;
            }

            var keyGo = new GameObject("Key Light");
            var key = keyGo.AddComponent<Light>();
            key.type = LightType.Directional;
            key.shadows = LightShadows.Soft;
            LightData(keyGo);

            var floorMat = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/Floor.mat");
            if (floorMat == null)
            {
                floorMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(floorMat, MatDir + "/Floor.mat");
            }
            floorMat.SetColor("_BaseColor", new Color(0.36f, 0.34f, 0.4f));
            floorMat.SetFloat("_Smoothness", 0.15f);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.position = new Vector3(GroupSpacing * (Variants.Length - 1) * 0.5f, 0f, 0f);
            floor.transform.localScale = new Vector3(Variants.Length * GroupSpacing / 10f + 1f, 1f, 1.2f);
            floor.GetComponent<Renderer>().sharedMaterial = floorMat;

            var idle = AssetDatabase.LoadAssetAtPath<AnimationClip>(IdleClip);
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            for (int i = 0; i < Variants.Length; i++)
            {
                var v = Variants[i];
                var group = new GameObject("Variant " + v.id).transform;
                group.position = new Vector3(i * GroupSpacing, 0f, 0f);
                Character(AmeliaFbx, "Amelia", VariantMaterial(v, false), group, new Vector3(-PairHalfGap, 0f, 0f), idle);
                Character(NoxFbx, "Nox", VariantMaterial(v, true), group, new Vector3(PairHalfGap, 0f, 0f), idle);

                var rim = new GameObject("Rim Light").AddComponent<Light>();
                rim.transform.SetParent(group, false);
                rim.type = LightType.Spot;
                rim.color = new Color(0.65f, 0.3f, 1f); // WaaangSceneSetup.CreateRimLight
                rim.intensity = 7f;
                rim.range = 6f;
                rim.spotAngle = 60f;
                rim.shadows = LightShadows.None;
                LightData(rim.gameObject);

                var label = new GameObject("Label").AddComponent<TextMesh>();
                label.transform.SetParent(group, false);
                label.transform.localPosition = new Vector3(0f, 0.01f, 0.55f);
                label.transform.localRotation = Quaternion.Euler(80f, 180f, 0f);
                label.text = v.title;
                label.font = font;
                label.fontSize = 64;
                label.characterSize = 0.012f;
                label.anchor = TextAnchor.MiddleCenter;
                label.alignment = TextAlignment.Center;
                label.color = new Color(1f, 0.92f, 0.75f);
                label.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            }

            // Open on the front lighting with the camera on the first pair.
            ApplyLighting(key, Variants.Length, front: true);
            Frame(cam, GameObject.Find("Variant " + Variants[0].id).transform, "front");

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            return ScenePath;
        }

        static GameObject Character(string fbx, string name, Material body, Transform parent, Vector3 local, AnimationClip pose)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(fbx));
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            go.transform.localScale = Vector3.one * 1.5f; // the game's character scale
            // Face +Z (WaaangSceneSetup.CreateCharacter): the Tripo models do not share a forward axis.
            var head = Bone(go.transform, "Head");
            var leftEye = Bone(go.transform, "Left_Eye");
            var rightEye = Bone(go.transform, "Right_Eye");
            if (head != null && leftEye != null && rightEye != null)
            {
                Vector3 face = (leftEye.position + rightEye.position) * 0.5f - head.position;
                face.y = 0f;
                if (face.sqrMagnitude > 1e-6f)
                    go.transform.rotation = Quaternion.FromToRotation(face, Vector3.forward) * go.transform.rotation;
            }
            if (pose != null) pose.SampleAnimation(go, pose.length * 0.25f);

            var smr = go.GetComponentInChildren<SkinnedMeshRenderer>();
            smr.sharedMaterial = body;
            smr.updateWhenOffscreen = true;
            // Stand on the floor in the sampled pose.
            var baked = new Mesh();
            smr.BakeMesh(baked, false);
            float minY = baked.vertices.Select(p => smr.transform.TransformPoint(p).y).Min();
            Object.DestroyImmediate(baked);
            go.transform.position += Vector3.up * (parent.position.y - minY);
            return go;
        }

        // front: warm key from the front-left, purple rim from behind (portrait view of the shading).
        // game: the Game scene's lights (cool key from behind the camera, purple rim from in front of the casters).
        static void ApplyLighting(Light key, int groups, bool front)
        {
            if (front)
            {
                key.color = new Color(1f, 0.95f, 0.88f);
                key.intensity = 1.25f;
                key.transform.rotation = Quaternion.Euler(35f, 215f, 0f);
            }
            else
            {
                key.color = new Color(0.72f, 0.72f, 0.9f); // WaaangSceneSetup.StyleKeyLight
                key.intensity = 1.15f;
                key.transform.rotation = Quaternion.Euler(38f, 25f, 0f);
            }
            foreach (var rim in Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (rim.name != "Rim Light") continue;
                var group = rim.transform.parent;
                Vector3 focus = group.position + Vector3.up * 1.25f;
                rim.transform.position = front
                    ? focus + new Vector3(0.9f, 0.9f, -1.8f)
                    : focus + new Vector3(-0.9f, 0.7f, 1.6f);
                rim.transform.LookAt(focus);
            }
        }

        static readonly string[] Views = { "front", "face", "game" };

        static void Frame(Camera cam, Transform group, string view)
        {
            var heads = group.GetComponentsInChildren<Transform>().Where(t => t.name == "Head").ToArray();
            Vector3 mid = heads.Length > 0 ? heads.Aggregate(Vector3.zero, (s, t) => s + t.position) / heads.Length : group.position + Vector3.up * 1.5f;
            Vector3 c = group.position;
            switch (view)
            {
                case "front":
                    cam.fieldOfView = 27f;
                    cam.transform.position = c + new Vector3(-0.6f, 1.05f, 3.6f);
                    cam.transform.LookAt(c + new Vector3(0f, 0.8f, 0f));
                    break;
                case "face":
                    cam.fieldOfView = 20f;
                    cam.transform.position = mid + new Vector3(-0.2f, 0.02f, 2.45f);
                    cam.transform.LookAt(mid + Vector3.down * 0.06f);
                    break;
                default: // WaaangSceneSetup.FrameBoth duo camera
                    cam.fieldOfView = 62f;
                    cam.transform.position = mid + new Vector3(0f, 0.55f, -1.45f);
                    cam.transform.rotation = Quaternion.Euler(9f, 0f, 0f);
                    break;
            }
        }

        // ---------- Rendering ----------

        /// <summary>Renders every variant from each view into Temp/ToonLookbook and builds lookbook_sheet.png.</summary>
        [MenuItem("Waaang/Toon Lookbook/Render Stills")]
        static void RenderMenu() => Debug.Log("[ToonLookbook] " + Render());

        public static string Render()
        {
            RefuseDirty();
            var previous = EditorSceneManager.GetActiveScene().path;
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Directory.CreateDirectory(OutDir);
            var rows = new List<List<string>>();
            try
            {
                var cam = Camera.main;
                var key = GameObject.Find("Key Light").GetComponent<Light>();
                var groups = Variants.Select(v => GameObject.Find("Variant " + v.id)).ToArray();
                for (int i = 0; i < Variants.Length; i++)
                {
                    for (int g = 0; g < groups.Length; g++) groups[g].SetActive(g == i);
                    var row = new List<string>();
                    foreach (var view in Views)
                    {
                        ApplyLighting(key, groups.Length, front: view != "game");
                        groups[i].transform.Find("Label").gameObject.SetActive(view == "front");
                        Frame(cam, groups[i].transform, view);
                        string path = $"{OutDir}/{Variants[i].id}_{view}.png";
                        Snap(cam, path);
                        row.Add(path);
                    }
                    rows.Add(row);
                }
            }
            finally
            {
                // Discard the toggles: the scene on disk stays as built.
                if (!string.IsNullOrEmpty(previous)) EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
            }
            return Path.GetFullPath(Sheet($"{OutDir}/lookbook_sheet.png", rows));
        }

        // Timeline scenes only: Period 3 spawns its casters at runtime, so there is nothing to swap in edit mode.
        static readonly (string scene, string times)[] InGameShots =
        {
            ("Assets/Cutscene/Period1/Period1Cutscene.unity", "4,7,11.5"),
            ("Assets/Cutscene/Period2/Period2Cutscene.unity", "4,11.5"),
        };

        /// <summary>
        /// For each variant, opens each period scene, puts the variant on the casters (in memory only) and renders
        /// the listed timeline times. One sheet per variant: rows = shots.
        /// </summary>
        [MenuItem("Waaang/Toon Lookbook/Render In Period Scenes")]
        static void RenderInGameMenu() => Debug.Log("[ToonLookbook] " + RenderAllInGame());

        public static string RenderAllInGame() => RenderInGame(string.Join(",", Variants.Select(v => v.id)));

        public static string RenderInGame(string variantIds)
        {
            RefuseDirty();
            var previous = EditorSceneManager.GetActiveScene().path;
            var ameliaRim = AssetDatabase.LoadAssetAtPath<Material>(AmeliaRim);
            var noxRim = AssetDatabase.LoadAssetAtPath<Material>(NoxRim);
            Directory.CreateDirectory(OutDir);
            var sheets = new List<string>();
            try
            {
                foreach (var id in variantIds.Split(','))
                {
                    var v = Variants.First(x => x.id == id.Trim());
                    var amelia = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{v.id}_Amelia.mat");
                    var nox = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{v.id}_Nox.mat");
                    if (amelia == null || nox == null) throw new Exception("Build the lookbook scene first (missing " + v.id + " materials).");
                    var rows = new List<string>();
                    foreach (var (scenePath, times) in InGameShots)
                    {
                        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null) continue;
                        EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                        foreach (var r in Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                        {
                            var mats = r.sharedMaterials;
                            for (int k = 0; k < mats.Length; k++)
                            {
                                if (mats[k] == ameliaRim) mats[k] = amelia;
                                else if (mats[k] == noxRim) mats[k] = nox;
                            }
                            r.sharedMaterials = mats;
                        }
                        string sceneName = Path.GetFileNameWithoutExtension(scenePath);
                        var parsed = times.Split(',').Select(s => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                        foreach (var written in AiHackathon.Cutscene.Editor.CutscenePreview.Render("lookbook_tmp", parsed))
                        {
                            string dest = $"{OutDir}/{v.id}_{sceneName}_{Path.GetFileName(written).Substring("lookbook_tmp_".Length)}";
                            File.Copy(written, dest, true);
                            File.Delete(written);
                            rows.Add(dest);
                        }
                    }
                    // Lay the shots out three per row.
                    var grid = rows.Select((p, i) => (p, i)).GroupBy(x => x.i / 3).Select(g => g.Select(x => x.p).ToList()).ToList();
                    sheets.Add(Path.GetFullPath(Sheet($"{OutDir}/ingame_{v.id}.png", grid)));
                }
            }
            finally
            {
                // The swapped scenes are discarded unsaved.
                if (!string.IsNullOrEmpty(previous)) EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
            }
            return string.Join("\n", sheets);
        }

        static void Snap(Camera cam, string path)
        {
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            var prev = cam.targetTexture;
            try
            {
                cam.targetTexture = rt;
                cam.Render();
                var active = RenderTexture.active;
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                RenderTexture.active = active;
                File.WriteAllBytes(path, tex.EncodeToPNG());
            }
            finally
            {
                cam.targetTexture = prev;
                Object.DestroyImmediate(tex);
                rt.Release();
                Object.DestroyImmediate(rt);
            }
        }

        /// <summary>Tiles PNGs (rows top to bottom) at half size into one image.</summary>
        static string Sheet(string path, List<List<string>> rows)
        {
            const int w = 640, h = 360, gap = 4;
            int cols = rows.Max(r => r.Count);
            var sheet = new Texture2D(cols * (w + gap) - gap, rows.Count * (h + gap) - gap, TextureFormat.RGB24, false);
            var fill = Enumerable.Repeat(new Color32(20, 18, 24, 255), sheet.width * sheet.height).ToArray();
            sheet.SetPixels32(fill);
            var small = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32);
            var tile = new Texture2D(w, h, TextureFormat.RGB24, false);
            var src = new Texture2D(2, 2);
            try
            {
                for (int r = 0; r < rows.Count; r++)
                for (int c = 0; c < rows[r].Count; c++)
                {
                    src.LoadImage(File.ReadAllBytes(rows[r][c]));
                    var active = RenderTexture.active;
                    Graphics.Blit(src, small);
                    RenderTexture.active = small;
                    tile.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                    tile.Apply();
                    RenderTexture.active = active;
                    sheet.SetPixels(c * (w + gap), (rows.Count - 1 - r) * (h + gap), w, h, tile.GetPixels());
                }
                sheet.Apply();
                File.WriteAllBytes(path, sheet.EncodeToPNG());
            }
            finally
            {
                Object.DestroyImmediate(sheet);
                Object.DestroyImmediate(tile);
                Object.DestroyImmediate(src);
                small.Release();
                Object.DestroyImmediate(small);
            }
            return path;
        }

        // ---------- Helpers ----------

        static void RefuseDirty()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Exit Play mode first.");
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
                if (EditorSceneManager.GetSceneAt(i).isDirty)
                    throw new Exception("Open scene '" + EditorSceneManager.GetSceneAt(i).path + "' has unsaved changes; save it first.");
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\', '/'), Path.GetFileName(path));
        }

        static void LightData(GameObject go)
        {
            if (go.GetComponent<UniversalAdditionalLightData>() == null) go.AddComponent<UniversalAdditionalLightData>();
        }

        static Transform Bone(Transform root, string name) =>
            root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);
    }
}
