using UnityEngine;
using UnityEngine.Rendering;

namespace SkyTide
{
    /// <summary>
    /// Мир прототипа: море облаков (плотный нижний слой + полупрозрачный верхний, оба медленно плывут),
    /// облака-спрайты, которые всегда повёрнуты к камере, и скалы с замками на горизонте.
    /// Без нарисованных текстур — процедурный шум и облака из сфер.
    /// </summary>
    public class CloudSea : MonoBehaviour
    {
        Material seaMat;
        Material layerMat;
        float time;

        /// <summary>Прямоугольники облаков в атласе CloudSprites.png (UV, начало снизу слева).</summary>
        static readonly Rect[] SpriteRects =
        {
            new Rect(0.0028f, 0.6032f, 0.4025f, 0.2368f),
            new Rect(0.3833f, 0.5062f, 0.3732f, 0.4622f),
            new Rect(0.7655f, 0.6065f, 0.2215f, 0.2356f),
            new Rect(0.0045f, 0.0552f, 0.3168f, 0.4363f),
            new Rect(0.3269f, 0.1037f, 0.4442f, 0.2864f),
            new Rect(0.7841f, 0.1319f, 0.2086f, 0.2604f),
        };
        const float AtlasAspect = 2f;   // атлас 2048×1024

        readonly System.Collections.Generic.List<Transform> billboards = new System.Collections.Generic.List<Transform>();

        public void Build(GameMaterials m)
        {
            Transform root = new GameObject("World").transform;

            seaMat = new Material(m.cloudSea);
            if (seaMat.mainTexture == null) seaMat.mainTexture = MakeNoise(256, 4, 11, false);
            seaMat.mainTextureScale = new Vector2(16f, 16f);
            Plane(root, "Cloud Sea", new Vector3(0f, -14f, 0f), 600f, seaMat);

            layerMat = new Material(m.cloudLayer);
            if (layerMat.mainTexture == null) layerMat.mainTexture = MakeNoise(256, 3, 23, true);
            layerMat.mainTextureScale = new Vector2(11f, 11f);
            Plane(root, "Cloud Layer", Vector3.zero, 600f, layerMat);

            System.Random rnd = new System.Random(77);
            if (m.cloudSprite != null && m.cloudSprite.mainTexture != null) BuildSpriteClouds(root, m.cloudSprite, rnd);
            else BuildSphereClouds(root, m, rnd);

            for (int i = 0; i < 6; i++)
            {
                float ang = (i / 6f) * Mathf.PI * 2f + 0.4f;
                float r = 1350f + Range(rnd, 0f, 350f);
                BuildIsland(root, m, new Vector3(Mathf.Cos(ang) * r, 0f, Mathf.Sin(ang) * r), rnd, i % 2 == 0);
            }
        }

        void BuildSpriteClouds(Transform root, Material mat, System.Random rnd)
        {
            Mesh[] meshes = new Mesh[SpriteRects.Length];
            for (int i = 0; i < meshes.Length; i++) meshes[i] = SpriteQuad(SpriteRects[i]);

            for (int i = 0; i < 56; i++)
            {
                float ang = (float)(rnd.NextDouble() * Mathf.PI * 2.0);
                float r = 220f + (float)rnd.NextDouble() * 1500f;
                bool high = i % 4 == 0;
                int k = rnd.Next(meshes.Length);
                float width = high ? Range(rnd, 120f, 260f) : Range(rnd, 220f, 520f);
                float y = high ? Range(rnd, 90f, 170f) : Range(rnd, -25f, 5f);

                GameObject go = new GameObject("Cloud");
                go.transform.SetParent(root, false);
                go.transform.position = new Vector3(Mathf.Cos(ang) * r, y, Mathf.Sin(ang) * r);
                go.transform.localScale = Vector3.one * width;
                go.AddComponent<MeshFilter>().sharedMesh = meshes[k];
                MeshRenderer mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                billboards.Add(go.transform);
            }
        }

        /// <summary>Квад шириной 1 с опорой снизу по центру, UV — прямоугольник облака в атласе. Лицо смотрит в −Z.</summary>
        static Mesh SpriteQuad(Rect uv)
        {
            float h = uv.height / (uv.width * AtlasAspect);
            Mesh mesh = new Mesh();
            mesh.name = "CloudQuad";
            mesh.vertices = new Vector3[]
            {
                new Vector3(-0.5f, 0f, 0f), new Vector3(-0.5f, h, 0f), new Vector3(0.5f, h, 0f), new Vector3(0.5f, 0f, 0f)
            };
            mesh.uv = new Vector2[]
            {
                new Vector2(uv.xMin, uv.yMin), new Vector2(uv.xMin, uv.yMax), new Vector2(uv.xMax, uv.yMax), new Vector2(uv.xMax, uv.yMin)
            };
            Color c = new Color(1f, 0.82f, 0.72f, 1f);   // подтягиваем светлые облака к тону заката
            mesh.colors = new Color[] { c, c, c, c };
            mesh.triangles = new int[] { 0, 1, 2, 0, 2, 3 };
            mesh.normals = new Vector3[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            mesh.RecalculateBounds();
            return mesh;
        }

        void BuildSphereClouds(Transform root, GameMaterials m, System.Random rnd)
        {
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
        }

        void LateUpdate()
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            Vector3 cp = cam.transform.position;
            for (int i = 0; i < billboards.Count; i++)
            {
                Transform t = billboards[i];
                Vector3 d = t.position - cp;
                d.y = 0f;
                if (d.sqrMagnitude > 0.01f) t.rotation = Quaternion.LookRotation(d, Vector3.up);
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
