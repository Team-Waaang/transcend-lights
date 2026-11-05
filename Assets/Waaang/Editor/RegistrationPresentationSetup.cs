using UnityEditor;
using UnityEngine;

namespace Waaang.Editor
{
    public static class RegistrationPresentationSetup
    {
        const string Root = "Assets/Waaang/Registration";
        [MenuItem("Waaang/Refresh Registration Presentation")]
        public static void EnsureAssets()
        {
            if (!AssetDatabase.IsValidFolder(Root)) AssetDatabase.CreateFolder("Assets/Waaang", "Registration");
            if (!AssetDatabase.IsValidFolder("Assets/Waaang/Resources")) AssetDatabase.CreateFolder("Assets/Waaang", "Resources");
            var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/TripoModels/runic_stone_for_arcore/runic_stone_for_arcore.fbx");
            var texturePath = "Assets/TripoModels/runic_stone_for_arcore/runic_stone_for_arcore.fbm/runic_stone_for_arcore_basecolor.jpg";
            var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
            importer.maxTextureSize = 4096; importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            var stone = Material("Runic Stone", "Universal Render Pipeline/Unlit", Color.white);
            stone.SetTexture("_BaseMap", texture);
            EditorUtility.SetDirty(stone);
            var go = new GameObject("Runic Registration Stage");
            var stage = go.AddComponent<RegistrationStage>();
            stage.stoneAsset = model; stage.stoneMaterial = stone;
            PrefabUtility.SaveAsPrefabAsset(go, "Assets/Waaang/Resources/RunicRegistrationStage.prefab");
            UnityEngine.Object.DestroyImmediate(go);
            AssetDatabase.SaveAssets();
        }

        static Material Material(string name, string shader, Color color)
        {
            var path = Root + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material) { material = new Material(Shader.Find(shader)); AssetDatabase.CreateAsset(material, path); }
            material.SetColor("_BaseColor", color);
            return material;
        }
    }
}
