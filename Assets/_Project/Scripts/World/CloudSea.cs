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
    /// • Полосы дымки над морем, перистые ленты наверху, грозовые башни вдали.
    /// Без нарисованных текстур — процедурный шум и облака из сфер.
    /// </summary>
    public class CloudSea : MonoBehaviour
    {
        [Header("Ветер и поле")]
        public Vector3 wind = new Vector3(-3.2f, 0f, 1.4f);
        public float fieldRadius = 2100f;
        public int clusterCount = 46;

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
        readonly List<GameObject> midLayer = new List<GameObject>();
        readonly List<GameObject> spriteClouds = new List<GameObject>();
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
        }

        // ---------- облачное поле ----------

        void BuildSpriteField(Transform root, Material mat, System.Random rnd)
        {
            Mesh[] puffs = new Mesh[CloudAtlas.Puffs.Length];
            for (int i = 0; i < puffs.Length; i++) puffs[i] = SpriteQuad(CloudAtlas.Puffs[i][0], CloudAtlas.Puffs[i][1], CloudAtlas.Puffs[i][2]);
            Mesh[] strips = new Mesh[CloudAtlas.Strips.Length];
            for (int i = 0; i < strips.Length; i++) strips[i] = SpriteQuad(CloudAtlas.Strips[i], CloudAtlas.Strips[i], CloudAtlas.Strips[i]);
            Mesh[] towers = new Mesh[CloudAtlas.Towers.Length];
            for (int i = 0; i < towers.Length; i++) towers[i] = SpriteQuad(CloudAtlas.Towers[i], CloudAtlas.Towers[i], CloudAtlas.Towers[i]);

            // Кучевые-спрайты: низкие гряды растут из моря, средние — на высоте полёта (их заменяют объёмные облака,
            // когда те включены), высокие — над кораблём. Каждая куча — 2–3 облака со сдвигом по глубине.
            for (int i = 0; i < clusterCount; i++)
            {
                float roll = (float)rnd.NextDouble();
                float width, y;
                bool mid = false;
                if (roll < 0.5f) { width = Range(rnd, 260f, 560f); y = Range(rnd, -34f, -6f); }
                else if (roll < 0.78f) { width = Range(rnd, 110f, 240f); y = Range(rnd, 35f, 85f); mid = true; }
                else { width = Range(rnd, 220f, 400f); y = Range(rnd, 160f, 260f); }
                Vector3 center = RandomPoint(rnd, 160f, y);
                int layers = 2 + rnd.Next(2);
                for (int l = 0; l < layers; l++)
                {
                    float w = l == 0 ? width : width * Range(rnd, 0.45f, 0.8f);
                    Vector3 off = l == 0 ? Vector3.zero : new Vector3(Range(rnd, -0.4f, 0.4f) * width, Range(rnd, -0.06f, 0.12f) * width, Range(rnd, -0.4f, 0.4f) * width);
                    Transform t = AddCloud(root, mat, puffs[rnd.Next(puffs.Length)], center + off, w, rnd, y > 120f ? 1.6f : 1f);
                    if (mid) midLayer.Add(t.gameObject);
                    spriteClouds.Add(t.gameObject);
                }
            }

            // Полосы: дымка, стелющаяся по морю облаков, и перистые ленты высоко в небе.
            for (int i = 0; i < 26; i++)
            {
                bool high = i % 5 < 2;
                float w = high ? Range(rnd, 600f, 1000f) : Range(rnd, 450f, 1100f);
                float y = high ? Range(rnd, 320f, 400f) : Range(rnd, -12f, 10f);
                AddCloud(root, mat, strips[rnd.Next(strips.Length)], RandomPoint(rnd, 120f, y), w, rnd, high ? 2.2f : 0.7f, true);
            }

            // Грозовые башни на дальнем плане.
            for (int i = 0; i < 7; i++)
            {
                float ang = (i / 7f) * Mathf.PI * 2f + Range(rnd, -0.3f, 0.3f);
                float r = Range(rnd, 1300f, 1900f);
                Vector3 p = new Vector3(Mathf.Cos(ang) * r, Range(rnd, -110f, -60f), Mathf.Sin(ang) * r);
                spriteClouds.Add(AddCloud(root, mat, towers[rnd.Next(towers.Length)], p, Range(rnd, 700f, 1150f), rnd, 0.4f).gameObject);
            }
        }

        Vector3 RandomPoint(System.Random rnd, float minR, float y)
        {
            float ang = (float)(rnd.NextDouble() * Mathf.PI * 2.0);
            float r = Mathf.Sqrt((float)rnd.NextDouble()) * fieldRadius;
            if (r < minR) r += minR;
            return new Vector3(Mathf.Cos(ang) * r, y, Mathf.Sin(ang) * r);
        }

        /// <summary>Кучевые облака-спрайты и дальние башни (полосы дымки остаются всегда).</summary>
        public void SetSpriteCloudsVisible(bool visible)
        {
            for (int i = 0; i < spriteClouds.Count; i++) spriteClouds[i].SetActive(visible);
        }

        /// <summary>Спрайты на высоте полёта: прячутся, когда включены объёмные облака.</summary>
        public void SetMidLayerVisible(bool visible)
        {
            for (int i = 0; i < midLayer.Count; i++) midLayer[i].SetActive(visible);
        }

        Transform AddCloud(Transform root, Material mat, Mesh mesh, Vector3 pos, float width, System.Random rnd, float windMul, bool strip = false)
        {
            GameObject go = new GameObject("Cloud");
            go.transform.SetParent(root, false);
            go.transform.position = pos;
            go.transform.localScale = Vector3.one * width;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            MaterialPropertyBlock mpb = new MaterialPropertyBlock();
            mpb.SetFloat("_CloudPhase", (float)rnd.NextDouble());
            // Полосы низкие и широкие — ребром видны раньше, гасим их уже при пологом взгляде сверху.
            mpb.SetVector("_EdgeOn", strip ? new Vector4(0.08f, 0.24f, 0f, 0f) : new Vector4(0.5f, 0.8f, 0f, 0f));
            mr.SetPropertyBlock(mpb);

            Cloud c = new Cloud();
            c.t = go.transform;
            c.baseScale = width;
            c.phase = Range(rnd, 0f, 100f);
            c.breathe = Range(rnd, 0.015f, 0.035f);
            c.driftMul = Range(rnd, 0.8f, 1.2f) * windMul;
            clouds.Add(c);
            return go.transform;
        }

        /// <summary>
        /// Квад шириной 1 с опорой снизу по центру. UV0/UV1/UV2 — та же форма в трёх вариантах света
        /// (против солнца / нейтральный / в тени). Высота — по нейтральному варианту.
        /// </summary>
        static Mesh SpriteQuad(Rect lit, Rect mid, Rect shadow)
        {
            float h = (mid.height / mid.width) / CloudAtlas.Aspect;
            Mesh mesh = new Mesh();
            mesh.name = "CloudQuad";
            mesh.vertices = new Vector3[]
            {
                new Vector3(-0.5f, 0f, 0f), new Vector3(-0.5f, h, 0f), new Vector3(0.5f, h, 0f), new Vector3(0.5f, 0f, 0f)
            };
            mesh.SetUVs(0, Corners(lit));
            mesh.SetUVs(1, Corners(mid));
            mesh.SetUVs(2, Corners(shadow));
            Color bottom = new Color(0.94f, 0.9f, 0.94f, 1f);   // низ чуть холоднее
            Color top = Color.white;
            mesh.colors = new Color[] { bottom, top, top, bottom };
            mesh.triangles = new int[] { 0, 1, 2, 0, 2, 3 };
            mesh.normals = new Vector3[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            mesh.RecalculateBounds();
            // Разворачивается в шейдере в любую сторону — границы с запасом по всем осям, чтобы не отсекалось.
            mesh.bounds = new Bounds(new Vector3(0f, h * 0.5f, 0f), new Vector3(1.4f, h * 1.4f, 1.4f));
            return mesh;
        }

        static List<Vector2> Corners(Rect r)
        {
            return new List<Vector2>
            {
                new Vector2(r.xMin, r.yMin), new Vector2(r.xMin, r.yMax), new Vector2(r.xMax, r.yMax), new Vector2(r.xMax, r.yMin)
            };
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
            if (cam != null) Step(cam.transform.position, Time.deltaTime, cam.transform.forward);
        }

        /// <summary>Шаг анимации поля (ветер, перенос вокруг камеры, дыхание, поворот к камере). Публичный — для кадров в CI.</summary>
        public void Step(Vector3 cp, float dt)
        {
            Camera cam = Camera.main;
            Step(cp, dt, cam != null ? cam.transform.forward : Vector3.forward);
        }

        /// <summary>Шаг поля: ветер, перенос вокруг камеры, «дыхание». Разворот к камере — в шейдере.</summary>
        public void Step(Vector3 cp, float dt, Vector3 camForward)
        {
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
                // Разворот к камере делает шейдер (SkyTide/CloudSprite) — здесь только позиция и «дыхание».
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
            if (col != null) SkyTideBootstrap.SafeDestroy(col);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, rot);
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }
    }
}
