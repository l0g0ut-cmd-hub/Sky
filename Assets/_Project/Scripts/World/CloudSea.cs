using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SkyTide
{
    /// <summary>
    /// Мир облаков.
    /// • Море облаков: плотный нижний слой + полупрозрачный верхний (шейдер SkyTide/CloudSea), плоскости едут
    ///   за камерой, UV мировые — море бесконечное, течёт и искажается шумом.
    /// • Облачное поле: облака-спрайты собраны в «кучи» из 2–3 слоёв со сдвигом по глубине (параллакс даёт объём),
    ///   всегда повёрнуты к камере, дрейфуют по ветру, медленно «дышат», края клубятся в шейдере.
    ///   Поле бесконечное: ушедшие далеко облака переносятся на противоположную сторону вокруг камеры.
    /// • Скалы с замками на горизонте.
    /// Без нарисованных текстур — процедурный шум и облака из сфер.
    /// </summary>
    public class CloudSea : MonoBehaviour
    {
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

        [Header("Ветер и поле")]
        public Vector3 wind = new Vector3(-3.2f, 0f, 1.4f);
        public float fieldRadius = 2100f;
        public int clusterCount = 52;

        class Cloud
        {
            public Transform t;
            public float baseScale;
            public float phase;
            public float breathe;
            public float driftMul;
        }

        readonly List<Cloud> clouds = new List<Cloud>();
        readonly List<Transform> seaPlanes = new List<Transform>();
        Material seaMat;
        Material layerMat;
        bool shaderSea;
        float time;

        public void Build(GameMaterials m)
        {
            Transform root = new GameObject("World").transform;

            seaMat = new Material(m.cloudSea);
            layerMat = new Material(m.cloudLayer);
            shaderSea = seaMat.HasProperty("_TileSize");
            if (seaMat.mainTexture == null) seaMat.mainTexture = SkyNoise.Clouds(256, 4, 11, false);
            if (layerMat.mainTexture == null) layerMat.mainTexture = SkyNoise.Clouds(256, 3, 23, true);
            if (!shaderSea)
            {
                seaMat.mainTextureScale = new Vector2(16f, 16f);
                layerMat.mainTextureScale = new Vector2(11f, 11f);
            }
            seaPlanes.Add(Plane(root, "Cloud Sea", -14f, 600f, seaMat));
            seaPlanes.Add(Plane(root, "Cloud Layer", 0f, 600f, layerMat));

            System.Random rnd = new System.Random(77);
            if (m.cloudSprite != null && m.cloudSprite.mainTexture != null) BuildSpriteField(root, m.cloudSprite, rnd);
            else BuildSphereClouds(root, m, rnd);

            for (int i = 0; i < 6; i++)
            {
                float ang = (i / 6f) * Mathf.PI * 2f + 0.4f;
                float r = 1350f + Range(rnd, 0f, 350f);
                BuildIsland(root, m, new Vector3(Mathf.Cos(ang) * r, 0f, Mathf.Sin(ang) * r), rnd, i % 2 == 0);
            }
        }

        // ---------- облачное поле ----------

        void BuildSpriteField(Transform root, Material mat, System.Random rnd)
        {
            Mesh[] meshes = new Mesh[SpriteRects.Length];
            for (int i = 0; i < meshes.Length; i++) meshes[i] = SpriteQuad(SpriteRects[i]);

            for (int i = 0; i < clusterCount; i++)
            {
                // Ярусы: низкие гряды растут из моря, средние — на высоте полёта (сквозь них можно пролететь),
                // высокие — над кораблём.
                float roll = (float)rnd.NextDouble();
                float width, y;
                if (roll < 0.55f) { width = Range(rnd, 260f, 560f); y = Range(rnd, -28f, 0f); }
                else if (roll < 0.8f) { width = Range(rnd, 110f, 240f); y = Range(rnd, 35f, 85f); }
                else { width = Range(rnd, 220f, 420f); y = Range(rnd, 140f, 230f); }

                float ang = (float)(rnd.NextDouble() * Mathf.PI * 2.0);
                float r = Mathf.Sqrt((float)rnd.NextDouble()) * fieldRadius;
                if (r < 160f) r = 160f + r;
                Vector3 center = new Vector3(Mathf.Cos(ang) * r, y, Mathf.Sin(ang) * r);

                int layers = 2 + rnd.Next(2);
                for (int l = 0; l < layers; l++)
                {
                    int k = rnd.Next(meshes.Length);
                    float w = l == 0 ? width : width * Range(rnd, 0.45f, 0.8f);
                    Vector3 off = l == 0 ? Vector3.zero : new Vector3(Range(rnd, -0.4f, 0.4f) * width, Range(rnd, -0.06f, 0.12f) * width, Range(rnd, -0.4f, 0.4f) * width);

                    GameObject go = new GameObject("Cloud");
                    go.transform.SetParent(root, false);
                    go.transform.position = center + off;
                    go.transform.localScale = Vector3.one * w;
                    go.AddComponent<MeshFilter>().sharedMesh = meshes[k];
                    MeshRenderer mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = mat;
                    mr.shadowCastingMode = ShadowCastingMode.Off;
                    mr.receiveShadows = false;

                    Cloud c = new Cloud();
                    c.t = go.transform;
                    c.baseScale = w;
                    c.phase = Range(rnd, 0f, 100f);
                    c.breathe = Range(rnd, 0.015f, 0.035f);
                    c.driftMul = Range(rnd, 0.8f, 1.2f) * (y > 120f ? 1.6f : 1f);   // наверху ветер сильнее
                    clouds.Add(c);
                }
            }
        }

        /// <summary>Квад шириной 1 с опорой снизу по центру, UV — облако в атласе. Цвет вершин — лёгкая вариация тона.</summary>
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
            Color bottom = new Color(0.92f, 0.86f, 0.9f, 1f);   // низ чуть холоднее
            Color top = Color.white;
            mesh.colors = new Color[] { bottom, top, top, bottom };
            mesh.triangles = new int[] { 0, 1, 2, 0, 2, 3 };
            mesh.normals = new Vector3[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            mesh.RecalculateBounds();
            // Облако дышит и поворачивается — запас по границам, чтобы не отсекалось по краю кадра.
            Bounds b = mesh.bounds;
            b.Expand(0.3f);
            mesh.bounds = b;
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

        // ---------- анимация ----------

        void Update()
        {
            time += Time.deltaTime;
            if (!shaderSea)
            {
                if (seaMat != null) seaMat.mainTextureOffset = new Vector2(time * 0.0015f, time * 0.0008f);
                if (layerMat != null) layerMat.mainTextureOffset = new Vector2(-time * 0.003f, time * 0.002f);
            }
        }

        void LateUpdate()
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            Vector3 cp = cam.transform.position;
            float dt = Time.deltaTime;

            // Море едет за камерой (UV мировые — рисунок стоит на месте).
            if (shaderSea)
            {
                for (int i = 0; i < seaPlanes.Count; i++)
                {
                    Vector3 p = seaPlanes[i].position;
                    seaPlanes[i].position = new Vector3(cp.x, p.y, cp.z);
                }
            }

            float r2 = fieldRadius * fieldRadius;
            for (int i = 0; i < clouds.Count; i++)
            {
                Cloud c = clouds[i];
                Vector3 p = c.t.position + wind * (c.driftMul * dt);

                // Бесконечное поле: облако, ушедшее за радиус, переносится на другую сторону от камеры.
                Vector3 d = p - cp;
                d.y = 0f;
                if (d.sqrMagnitude > r2)
                {
                    Vector3 back = -d.normalized * (fieldRadius * 0.97f);
                    p.x = cp.x + back.x;
                    p.z = cp.z + back.z;
                    d = p - cp;
                    d.y = 0f;
                }
                c.t.position = p;

                float s = c.baseScale * (1f + c.breathe * Mathf.Sin(time * 0.13f + c.phase));
                c.t.localScale = new Vector3(s, s * (1f + c.breathe * 0.6f * Mathf.Sin(time * 0.09f + c.phase * 1.7f)), s);
                if (d.sqrMagnitude > 0.01f) c.t.rotation = Quaternion.LookRotation(d, Vector3.up);
            }
        }

        // ---------- острова ----------

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

        // ---------- утилиты ----------

        static float Range(System.Random r, float a, float b)
        {
            return a + (float)r.NextDouble() * (b - a);
        }

        static Transform Plane(Transform root, string name, float y, float scale, Material mat)
        {
            GameObject go = Prim(PrimitiveType.Plane, root, new Vector3(0f, y, 0f), Quaternion.identity, new Vector3(scale, 1f, scale), mat);
            go.name = name;
            MeshRenderer mr = go.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = ShadowCastingMode.Off;
            return go.transform;
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
    }
}
