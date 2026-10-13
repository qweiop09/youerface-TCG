using System.Collections.Generic;
using UnityEngine;

namespace TCG.Client.Common
{
    /// <summary>
    /// 색만 다른 머티리얼을 만들어 재사용한다. 원본은 Resources/Materials의 BaseLit/BaseUnlit
    /// (에디터 메뉴 TCG/Setup이 만든다. 빌드에 셰이더가 포함되도록 에셋으로 둔다).
    /// </summary>
    public static class MaterialCache
    {
        static readonly Dictionary<(Color, bool), Material> Cache = new Dictionary<(Color, bool), Material>();
        static Material _lit;
        static Material _unlit;

        public static Material Get(Color color, bool unlit = false)
        {
            var key = (color, unlit);
            if (Cache.TryGetValue(key, out var cached) && cached != null)
                return cached;

            var source = unlit ? Unlit() : Lit();
            var material = new Material(source) { color = color, name = $"{source.name} {ColorUtility.ToHtmlStringRGB(color)}" };
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            Cache[key] = material;
            return material;
        }

        static readonly Dictionary<Texture, Material> TextureCache = new Dictionary<Texture, Material>();

        /// <summary>그림(텍스처)을 그대로 보여 주는 조명 없는 머티리얼 (유저 카드 그림).</summary>
        public static Material Textured(Texture texture)
        {
            if (TextureCache.TryGetValue(texture, out var cached) && cached != null)
                return cached;
            var material = new Material(Unlit()) { color = Color.white, mainTexture = texture, name = "Art " + texture.name };
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", Color.white);
            if (material.HasProperty("_BaseMap"))
                material.SetTexture("_BaseMap", texture);
            TextureCache[texture] = material;
            return material;
        }

        static Material Lit() => _lit != null ? _lit : (_lit = Load("Materials/BaseLit", "Universal Render Pipeline/Lit"));
        static Material Unlit() => _unlit != null ? _unlit : (_unlit = Load("Materials/BaseUnlit", "Universal Render Pipeline/Unlit"));

        static Material Load(string resourcePath, string fallbackShader)
        {
            var material = Resources.Load<Material>(resourcePath);
            if (material != null)
                return material;
            Debug.LogWarning($"{resourcePath} 머티리얼이 없어 셰이더로 직접 만듭니다 (빌드에서는 셰이더가 빠질 수 있음).");
            return new Material(Shader.Find(fallbackShader));
        }
    }
}
