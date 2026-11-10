using UnityEngine;
using UnityEngine.Rendering;

namespace SkyTide
{
    /// <summary>
    /// Мир прототипа: море облаков (плотный нижний слой + полупрозрачный верхний, оба медленно плывут),
    /// облачные гряды из сплюснутых сфер и скалы с замками на горизонте. Текстуры облаков — процедурный шум
    /// (временная замена, пока нет нарисованных).
    /// </summary>
    public class CloudSea : MonoBehaviour
    {
        Material seaMat;
        Material layerMat;
        float time;

        public void Build(GameMaterials m)
        {
            Transform root = new GameObject("World").transform;

            seaMat = new Material(m.cloudSea);
            seaMat.mainTexture = MakeNoise(256, 4, 11, false);
            seaMat.mainTextureScale = new Vector2(30f, 30f);
            Plane(root, "Cloud Sea", new Vector3(0f, -14f, 0f), 600f, seaMat);

            layerMat = new Material(m.cloudLayer);
            layerMat.mainTexture = MakeNoise(256, 3, 23, true);
            layerMat.mainTextureScale = new Vector2(20f, 20f);
            Plane(root, "Cloud Layer", Vector3.zero, 600f, layerMat);

            System.Random rnd = new System.Random(77);
            for (int i = 0; i < 36; i++)
            {
                float ang = (float)(rnd.NextDouble() * Mathf.PI * 2.0);
                float r = 180f + (float)rnd.NextDouble() * 1300f;
                bool high = i % 3 == 0;
                float y = high ? 75f + (float)rnd.NextDouble() * 60f : 2f + (float)rnd.NextDouble() * 18f;
                Vector3 c = new Vector3(Mathf.Cos(ang) * r, y, Mathf.Sin(ang) * r);
                int parts = 3 + rnd.Next(3);
                for (int p = 0; p < parts; p++)
                {
                    Vector3 off = new Vector3(Range(rnd, -30f, 30f), Range(rnd, -3f, 4f), Range(rnd, -30f, 30f));
                    Vector3 scale = new Vector3(Range(rnd, 35f, 70f), Range(rnd, 10f, 18f), Range(rnd, 30f, 60f));
                    GameObject s = Prim(PrimitiveType.Sphere, root, c + off, Quaternion.Euler(0f, Range(rnd, 0f, 360f), 0f), scale, m.cloudPuff);
                    s.name = "Cloud";
                    MeshRenderer mr = s.GetComponent<MeshRenderer>();
                    mr.shadowCastingMode = ShadowCastingMode.Off;
                    mr.receiveShadows = false;
                }
            }

            for (int i = 0; i < 6; i++)
            {
                float ang = (i / 6f) * Mathf.PI * 2f + 0.4f;
                float r = 1350f + Range(rnd, 0f, 350f);
                BuildIsland(root, m, new Vector3(Mathf.Cos(ang) * r, 0f, Mathf.Sin(ang) * r), rnd, i % 2 == 0);
            }
        }

        void BuildIsland(Transform root, GameMaterials m, Vector3 pos, System.Random rnd, bool withCastle)
        {
            float w = Range(rnd, 90f, 150f);
            float top = Range(rnd, 40f, 80f);
            Prim(PrimitiveType.Cylinder, root, new Vector3(pos.x, top * 0.5f - 90f, pos.z), Quaternion.identity,
                new Vector3(w, top * 0.5f + 90f, w), m.rock);
            Prim(PrimitiveType.Sphere, root, new Vector3(pos.x, top, pos.z), Quaternion.identity, new Vector3(w * 1.1f, 30f, w * 1.1f), m.rock);
            Prim(PrimitiveType.Cylinder, root, new Vector3(pos.x + w * 0.3f, top * 0.3f, pos.z - w * 0.2f), Quaternion.Euler(0f, 0f, 8f),
                new Vector3(w * 0.4f, top * 0.6f + 40f, w * 0.4f), m.rock);
            if (!withCastle) return;

            float b = top + 12f;
            Prim(PrimitiveType.Cube, root, new Vector3(pos.x, b + 12f, pos.z), Quaternion.identity, new Vector3(34f, 26f, 28f), m.castle);
            Prim(PrimitiveType.Cube, root, new Vector3(pos.x, b + 30f, pos.z), Quaternion.Euler(0f, 45f, 0f), new Vector3(16f, 12f, 16f), m.castle);
            for (int t = 0; t < 4; t++)
            {
                float tx = (t % 2 == 0 ? -1f : 1f) * 17f;
                float tz = (t < 2 ? -1f : 1f) * 14f;
                float th = Range(rnd, 34f, 50f);
                Prim(PrimitiveType.Cylinder, root, new Vector3(pos.x + tx, b + th * 0.5f, pos.z + tz), Quaternion.identity, new Vector3(7f, th * 0.5f, 7f), m.castle);
                Prim(PrimitiveType.Cube, root, new Vector3(pos.x + tx, b + th + 3f, pos.z + tz), Quaternion.Euler(0f, 45f, 0f), new Vector3(5f, 6f, 5f), m.castle);
                Prim(PrimitiveType.Cube, root, new Vector3(pos.x + tx, b + th * 0.7f, pos.z + tz), Quaternion.identity, new Vector3(7.4f, 2.2f, 1.2f), m.windowGlow);
            }
            for (int k = 0; k < 5; k++)
            {
                Prim(PrimitiveType.Cube, root, new Vector3(pos.x - 12f + k * 6f, b + 14f, pos.z), Quaternion.identity, new Vector3(1.6f, 3.5f, 28.4f), m.windowGlow);
            }
        }

        void Update()
        {
            time += Time.deltaTime;
            if (seaMat != null) seaMat.mainTextureOffset = new Vector2(time * 0.0015f, time * 0.0008f);
            if (layerMat != null) layerMat.mainTextureOffset = new Vector2(-time * 0.003f, time * 0.002f);
        }

        static float Range(System.Random r, float a, float b)
        {
            return a + (float)r.NextDouble() * (b - a);
        }

        static void Plane(Transform root, string name, Vector3 pos, float scale, Material mat)
        {
            GameObject go = Prim(PrimitiveType.Plane, root, pos, Quaternion.identity, new Vector3(scale, 1f, scale), mat);
            go.name = name;
            MeshRenderer mr = go.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = ShadowCastingMode.Off;
        }

        static GameObject Prim(PrimitiveType type, Transform parent, Vector3 pos, Quaternion rot, Vector3 scale, Material mat)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            Collider col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, rot);
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        /// <summary>Бесшовный фрактальный шум. alpha=false — цветная «вата» облаков; true — белые клочья с прозрачностью.</summary>
        static Texture2D MakeNoise(int size, int baseCells, int seed, bool alpha)
        {
            System.Random rnd = new System.Random(seed);
            float[] buf = new float[size * size];
            float amp = 1f, total = 0f;
            int cells = baseCells;
            for (int o = 0; o < 5; o++)
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

            Color shadow = new Color(0.5f, 0.4f, 0.48f);
            Color lit = new Color(1f, 0.84f, 0.7f);
            Color32[] px = new Color32[size * size];
            for (int i = 0; i < px.Length; i++)
            {
                float n = Mathf.Clamp01((buf[i] / total - 0.5f) * 1.9f + 0.5f);
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
            tex.anisoLevel = 4;
            tex.SetPixels32(px);
            tex.Apply(true);
            return tex;
        }
    }
}
