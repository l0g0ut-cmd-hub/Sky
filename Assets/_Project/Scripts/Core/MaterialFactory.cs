using UnityEngine;
using UnityEngine.Rendering;

namespace SkyTide
{
    /// <summary>
    /// Создание материалов на шейдере Standard (встроенный рендер).
    /// Используется и редактором (материалы сохраняются ассетами, чтобы шейдерные варианты попали в сборку),
    /// и игрой как запасной путь, если ассетов нет.
    /// </summary>
    public static class MaterialFactory
    {
        public static Material Opaque(string name, Color color, float smoothness)
        {
            Material m = new Material(Shader.Find("Standard"));
            m.name = name;
            m.color = color;
            m.SetFloat("_Glossiness", smoothness);
            m.SetFloat("_Metallic", 0f);
            return m;
        }

        /// <summary>Прозрачный материал (режим Fade): облака, дым, вспышки.</summary>
        public static Material Fade(string name, Color color, float smoothness)
        {
            Material m = Opaque(name, color, smoothness);
            m.SetFloat("_Mode", 2f);
            m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.DisableKeyword("_ALPHATEST_ON");
            m.EnableKeyword("_ALPHABLEND_ON");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = (int)RenderQueue.Transparent;
            return m;
        }

        public static Material WithEmission(Material m, Color emission)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            return m;
        }

        /// <summary>Небо из панорамы 2:1 (equirectangular). Центр картинки (солнце) смотрит в +X.</summary>
        public static Material PanoramicSkybox(Texture tex)
        {
            Material m = new Material(Shader.Find("Skybox/Panoramic"));
            m.name = "SkyPanorama";
            m.SetTexture("_MainTex", tex);
            m.SetFloat("_Mapping", 1f);
            m.EnableKeyword("_MAPPING_LATITUDE_LONGITUDE_LAYOUT");
            m.SetFloat("_ImageType", 0f);
            m.SetFloat("_MirrorOnBack", 0f);
            m.SetFloat("_Layout", 0f);
            m.SetFloat("_Rotation", 0f);
            m.SetFloat("_Exposure", 1f);
            m.SetColor("_Tint", new Color(0.5f, 0.5f, 0.5f, 0.5f));
            return m;
        }

        public static Material ShipBody(Texture albedo, Texture normal, Texture emission)
        {
            Material m = Opaque("ShipBody", Color.white, 0.18f);
            m.mainTexture = albedo;
            if (normal != null)
            {
                m.SetTexture("_BumpMap", normal);
                m.SetFloat("_BumpScale", 1f);
                m.EnableKeyword("_NORMALMAP");
            }
            if (emission != null)
            {
                WithEmission(m, new Color(1.6f, 1.6f, 1.6f));
                m.SetTexture("_EmissionMap", emission);
            }
            return m;
        }

        /// <summary>Облака-спрайты: частичный шейдер с альфой, цветом вершин и туманом, без освещения.</summary>
        public static Material CloudSprites(Texture atlas)
        {
            Shader sh = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
            if (sh == null) sh = Shader.Find("Unlit/Transparent");
            Material m = new Material(sh);
            m.name = "CloudSprites";
            m.mainTexture = atlas;
            if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
            m.renderQueue = (int)RenderQueue.Transparent;
            return m;
        }

        public static Material Skybox()
        {
            Material m = new Material(Shader.Find("Skybox/Procedural"));
            m.name = "SkyDusk";
            m.SetFloat("_SunDisk", 2f);
            m.SetFloat("_SunSize", 0.05f);
            m.SetFloat("_SunSizeConvergence", 4f);
            m.SetFloat("_AtmosphereThickness", 1.7f);
            m.SetColor("_SkyTint", new Color(0.72f, 0.5f, 0.55f));
            m.SetColor("_GroundColor", new Color(0.62f, 0.45f, 0.42f));
            m.SetFloat("_Exposure", 1.15f);
            return m;
        }
    }
}
