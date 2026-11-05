using UnityEngine;

namespace Waaang
{
    public static class ToonLook
    {
        static bool sceneApplied;

        public static void ApplyScene()
        {
            if (sceneApplied)
                return;
            sceneApplied = true;
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
                Apply(roots[i]);
        }

        public static void Apply(GameObject root)
        {
            if (root == null)
                return;
            var shader = Shader.Find("lilToon");
            if (shader == null)
                return;

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer is LineRenderer || renderer is TrailRenderer || renderer is ParticleSystemRenderer)
                    continue;
                var source = renderer.sharedMaterial;
                if (source != null && source.shader == shader)
                    continue;

                var material = new Material(shader);
                if (source != null)
                {
                    var texture = source.mainTexture;
                    if (texture == null && source.HasProperty("_BaseMap"))
                        texture = source.GetTexture("_BaseMap");
                    if (texture != null)
                    {
                        material.mainTexture = texture;
                        material.SetTexture("_MainTex", texture);
                    }

                    Color color = Color.white;
                    if (source.HasProperty("_BaseColor"))
                        color = source.GetColor("_BaseColor");
                    else if (source.HasProperty("_Color"))
                        color = source.color;
                    material.color = color;
                    if (material.HasProperty("_Color"))
                        material.SetColor("_Color", color);
                }
                renderer.sharedMaterial = material;
            }
        }
    }
}
