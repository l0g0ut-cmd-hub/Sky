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

        /// <summary>Корпус корабля: Standard BRDF с теми же картами + контровой свет заката и свет вспышек (SkyTide/ShipHull).</summary>
        public static Material ShipSurface(string name, Color color, float smoothness)
        {
            Shader shader = Shader.Find("SkyTide/ShipHull");
            if (shader == null) throw new System.InvalidOperationException("ShipHull shader is missing");
            Material m = new Material(shader);
            m.name = name;
            m.color = color;
            m.SetFloat("_Glossiness", smoothness);
            m.SetFloat("_Metallic", 0f);
            return m;
        }

        public static Material Cloth(string name, Color color, float smoothness, float transmission)
        {
            Shader shader = Shader.Find("SkyTide/ShipCloth");
            if (shader == null) throw new System.InvalidOperationException("ShipCloth shader is missing");
            Material m = new Material(shader);
            m.name = name;
            m.color = color;
            m.SetFloat("_Glossiness", smoothness);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Translucency", transmission);
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
            Material m = ShipSurface("ShipBody", Color.white, 0.18f);
            m.mainTexture = albedo;
            if (normal != null)
            {
                m.SetTexture("_BumpMap", normal);
                m.SetFloat("_BumpScale", 1f);
                m.EnableKeyword("_NORMALMAP");
            }
            if (emission != null)
            {
                WithEmission(m, new Color(2.3f, 2.3f, 2.3f));   // фонари и окна дают ореол свечения
                m.SetTexture("_EmissionMap", emission);
            }
            return m;
        }

        /// <summary>Небесная крепость: Standard + растворение низа в облаках и воздушная дымка (SkyTide/Fortress).</summary>
        public static Material Fortress(Texture albedo, Texture emission, Texture wall, Texture deck, Texture rock, float dissolveTop, float dissolveRange)
        {
            Shader sh = Shader.Find("SkyTide/Fortress");
            if (sh == null) throw new System.InvalidOperationException("Fortress shader is missing");
            Material m = new Material(sh);
            m.name = "Fortress";
            m.mainTexture = albedo;
            m.SetFloat("_Glossiness", 0.18f);
            if (emission != null) { m.SetTexture("_EmissionMap", emission); m.SetColor("_EmissionColor", new Color(2.2f, 2.2f, 2.2f)); }
            // Плитки материалов (кладка, доски, скала): без них шейдер берёт серую заглушку — «как в модели».
            if (wall != null) m.SetTexture("_WallTex", wall);
            if (deck != null) m.SetTexture("_DeckTex", deck);
            if (rock != null) m.SetTexture("_RockTex", rock);
            m.SetFloat("_DissolveTop", dissolveTop);
            m.SetFloat("_DissolveRange", dissolveRange);
            return m;
        }

        /// <summary>Облака-спрайты на своём шейдере (клубление краёв, свет от солнца, растворение вдали и вблизи).</summary>
        public static Material CloudSprites(Texture atlas)
        {
            Shader sh = Shader.Find("SkyTide/CloudSprite");
            if (sh == null) sh = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
            Material m = new Material(sh);
            m.name = "CloudSprites";
            m.mainTexture = atlas;
            if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
            m.renderQueue = (int)RenderQueue.Transparent;
            return m;
        }

        /// <summary>Море облаков на своём шейдере. transparent=true — верхний полупрозрачный слой.</summary>
        public static Material CloudSea(string name, Texture tex, bool transparent)
        {
            Shader sh = Shader.Find("SkyTide/CloudSea");
            if (sh == null) return null;
            Material m = new Material(sh);
            m.name = name;
            m.mainTexture = tex;
            m.color = transparent ? new Color(0.92f, 0.86f, 0.88f, 1f) : new Color(0.8f, 0.72f, 0.76f, 1f);
            if (transparent)
            {
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
                m.SetFloat("_TileSize", 560f);
                m.SetFloat("_AlphaMul", 0.9f);
                m.SetVector("_Flow", new Vector4(-0.005f, 0.0032f, 0.0038f, 0.0021f));
                m.renderQueue = (int)RenderQueue.Transparent - 10;   // под облаками-спрайтами
            }
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
