using UnityEngine;

namespace SkyTide
{
    /// <summary>
    /// Контакт с морем облаков для эффектов: те же mip-0 «погода» и 3D-шум, что у DeckDensity.
    /// Читает только маленькую 2D-текстуру и уже загруженные байты; GPU readback в кадре не нужен.
    /// Коридор видимости камера→корабль не меняет физическую поверхность.
    /// </summary>
    public sealed class CloudImpactSurface
    {
        Color32[] weather;
        int width, height;
        byte[] noise;

        public void Configure(Texture2D texture, byte[] noiseBytes)
        {
            weather = null;
            noise = noiseBytes != null && noiseBytes.Length == 64 * 64 * 64 * 4 ? noiseBytes : null;
            if (texture == null || !texture.isReadable) return;
            width = texture.width;
            height = texture.height;
            weather = texture.GetPixels32(0);
        }

        static int Wrap(int i, int size) { return (i % size + size) % size; }

        float Weather(float u, float v, bool green)
        {
            float x = Mathf.Repeat(u, 1f) * width - 0.5f;
            float y = Mathf.Repeat(v, 1f) * height - 0.5f;
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
            float fx = x - ix, fy = y - iy;
            Color32 a = weather[Wrap(iy, height) * width + Wrap(ix, width)];
            Color32 b = weather[Wrap(iy, height) * width + Wrap(ix + 1, width)];
            Color32 c = weather[Wrap(iy + 1, height) * width + Wrap(ix, width)];
            Color32 d = weather[Wrap(iy + 1, height) * width + Wrap(ix + 1, width)];
            return Mathf.Lerp(Mathf.Lerp(green ? a.g : a.r, green ? b.g : b.r, fx),
                              Mathf.Lerp(green ? c.g : c.r, green ? d.g : d.r, fx), fy) / 255f;
        }

        float Voxel(int x, int y, int z, int channel)
        {
            return noise[(((z & 63) * 64 + (y & 63)) * 64 + (x & 63)) * 4 + channel] / 255f;
        }

        float Noise(Vector3 p, float scale, int channel)
        {
            p = p * (64f / Mathf.Max(scale, 0.01f)) - Vector3.one * 0.5f;
            int x = Mathf.FloorToInt(p.x), y = Mathf.FloorToInt(p.y), z = Mathf.FloorToInt(p.z);
            float fx = p.x - x, fy = p.y - y, fz = p.z - z;
            float a = Mathf.Lerp(Mathf.Lerp(Voxel(x, y, z, channel), Voxel(x + 1, y, z, channel), fx),
                                 Mathf.Lerp(Voxel(x, y + 1, z, channel), Voxel(x + 1, y + 1, z, channel), fx), fy);
            float b = Mathf.Lerp(Mathf.Lerp(Voxel(x, y, z + 1, channel), Voxel(x + 1, y, z + 1, channel), fx),
                                 Mathf.Lerp(Voxel(x, y + 1, z + 1, channel), Voxel(x + 1, y + 1, z + 1, channel), fx), fy);
            return Mathf.Lerp(a, b, fz);
        }

        float Density(Vector3 p, VolumetricClouds v, float time)
        {
            float h = Mathf.Clamp01((p.y - v.deckBase) / Mathf.Max(v.deckTop - v.deckBase, 0.01f));
            float dx = v.wind.x * time, dz = v.wind.z * time;
            // Та же «погода», что в DeckDensity шейдера (три повёрнутые октавы канала R с искажением).
            float qx = p.x + dx, qz = p.z + dz, S = v.deckWeatherScale;
            float wx = Weather(qx / (S * 2.3f) + 0.13f, qz / (S * 2.3f) + 0.13f, false) - 0.5f;
            float wz = Weather(qz / (S * 2.9f) + 0.57f, qx / (S * 2.9f) + 0.57f, false) - 0.5f;
            float w = Weather((qx * 0.819f - qz * 0.574f) / S + wx * 0.55f, (qx * 0.574f + qz * 0.819f) / S + wz * 0.55f, false) * 0.55f
                    + Weather((qx * -0.666f - qz * 0.746f) / (S * 0.43f) + 0.31f, (qx * 0.746f + qz * -0.666f) / (S * 0.43f) + 0.31f, false) * 0.3f
                    + Weather((qx * 0.122f - qz * 0.993f) / (S * 0.19f) + 0.71f, (qx * 0.993f + qz * 0.122f) / (S * 0.19f) + 0.71f, false) * 0.15f;
            float cov = Mathf.Clamp01((w - v.deckCoverage) / 0.35f);
            Vector3 wp = p + new Vector3(dx, time * 0.4f, dz);
            float top = cov * (0.45f + 0.55f * Noise(wp, v.deckLobeScale, 0)) + 0.12f;
            float m = Mathf.Clamp01((top - h) * 6f) * Mathf.Clamp01(h * 5f + 0.2f);
            if (m > 0f && m < 0.9f)
                m = Mathf.Clamp01(m - Noise(wp, v.deckDetailScale, 1) * 0.45f * (1f - m));
            return m;
        }

        /// <summary>Первый вход нисходящего отрезка в заметную плотность; в просвете всплеска нет.</summary>
        public bool TryEnter(Vector3 a, Vector3 b, VolumetricClouds v, float time, float fallbackTop, out Vector3 point)
        {
            point = b;
            if (b.y >= a.y) return false;
            if (v == null || weather == null || noise == null)
            {
                float top = fallbackTop - 5f;
                if (a.y < top || b.y > top) return false;
                point = Vector3.Lerp(a, b, (a.y - top) / (a.y - b.y));
                return true;
            }
            if (b.y > v.deckTop || a.y < v.deckBase) return false;
            float lo = Mathf.Clamp01((a.y - v.deckTop) / (a.y - b.y));
            float end = Mathf.Clamp01((a.y - v.deckBase) / (a.y - b.y));
            if (Density(Vector3.Lerp(a, b, lo), v, time) >= 0.06f)
            {
                point = Vector3.Lerp(a, b, lo);
                return true;
            }
            for (int k = 1; k <= 4; k++)
            {
                float hi = Mathf.Lerp(lo, end, 1f / (5 - k));
                if (Density(Vector3.Lerp(a, b, hi), v, time) >= 0.06f)
                {
                    for (int j = 0; j < 5; j++)
                    {
                        float mid = (lo + hi) * 0.5f;
                        if (Density(Vector3.Lerp(a, b, mid), v, time) >= 0.06f) hi = mid; else lo = mid;
                    }
                    point = Vector3.Lerp(a, b, hi);
                    return true;
                }
                lo = hi;
            }
            return false;
        }
    }
}
