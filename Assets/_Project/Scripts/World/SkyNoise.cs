using UnityEngine;

namespace SkyTide
{
    /// <summary>Процедурные бесшовные текстуры шума (фрактальный value noise).</summary>
    public static class SkyNoise
    {
        /// <summary>Шум для анимации облаков: R и G — два независимых поля. Ставится глобально как _SkyTideNoise.</summary>
        public static Texture2D Flow(int size)
        {
            float[] r = Field(size, 4, 101, 5);
            float[] g = Field(size, 4, 202, 5);
            Color32[] px = new Color32[size * size];
            for (int i = 0; i < px.Length; i++)
                px[i] = new Color32((byte)(r[i] * 255f), (byte)(g[i] * 255f), 128, 255);
            Texture2D t = new Texture2D(size, size, TextureFormat.RGBA32, true, true);
            t.name = "SkyTideNoise";
            t.wrapMode = TextureWrapMode.Repeat;
            t.filterMode = FilterMode.Bilinear;
            t.SetPixels32(px);
            t.Apply(true);
            return t;
        }

        /// <summary>Запасные текстуры облаков, если нарисованных нет: alpha=false — цветная «вата», true — белые клочья.</summary>
        public static Texture2D Clouds(int size, int baseCells, int seed, bool alpha)
        {
            float[] f = Field(size, baseCells, seed, 5);
            Color shadow = new Color(0.5f, 0.4f, 0.48f);
            Color lit = new Color(1f, 0.84f, 0.7f);
            Color32[] px = new Color32[size * size];
            for (int i = 0; i < px.Length; i++)
            {
                float n = f[i];
                if (!alpha)
                {
                    px[i] = Color.Lerp(shadow, lit, n);
                }
                else
                {
                    float t = Mathf.Clamp01((n - 0.48f) / 0.3f);
                    t = t * t * (3f - 2f * t);
                    Color c = Color.Lerp(new Color(0.85f, 0.72f, 0.72f), new Color(1f, 0.9f, 0.82f), n);
                    c.a = t * 0.9f;
                    px[i] = c;
                }
            }
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Trilinear;
            tex.SetPixels32(px);
            tex.Apply(true);
            return tex;
        }

        /// <summary>Фрактальный шум 0..1 с повышенным контрастом, бесшовный по обеим осям.</summary>
        static float[] Field(int size, int baseCells, int seed, int octaves)
        {
            System.Random rnd = new System.Random(seed);
            float[] buf = new float[size * size];
            float amp = 1f, total = 0f;
            int cells = baseCells;
            for (int o = 0; o < octaves; o++)
            {
                float[] lat = new float[cells * cells];
                for (int i = 0; i < lat.Length; i++) lat[i] = (float)rnd.NextDouble();
                for (int y = 0; y < size; y++)
                {
                    float fy = (float)y / size * cells;
                    int y0 = (int)fy;
                    float ty = fy - y0;
                    ty = ty * ty * (3f - 2f * ty);
                    int y1 = (y0 + 1) % cells;
                    y0 %= cells;
                    for (int x = 0; x < size; x++)
                    {
                        float fx = (float)x / size * cells;
                        int x0 = (int)fx;
                        float tx = fx - x0;
                        tx = tx * tx * (3f - 2f * tx);
                        int x1 = (x0 + 1) % cells;
                        x0 %= cells;
                        float a = Mathf.Lerp(lat[y0 * cells + x0], lat[y0 * cells + x1], tx);
                        float b = Mathf.Lerp(lat[y1 * cells + x0], lat[y1 * cells + x1], tx);
                        buf[y * size + x] += Mathf.Lerp(a, b, ty) * amp;
                    }
                }
                total += amp;
                amp *= 0.5f;
                cells *= 2;
            }
            for (int i = 0; i < buf.Length; i++)
                buf[i] = Mathf.Clamp01((buf[i] / total - 0.5f) * 1.9f + 0.5f);
            return buf;
        }
    }
}
