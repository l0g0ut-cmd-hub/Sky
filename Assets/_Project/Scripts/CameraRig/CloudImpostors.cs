using System.Collections.Generic;
using UnityEngine;

namespace SkyTide
{
    /// <summary>
    /// Импосторы объёмных облаков. При старте каждый вариант облака (шаблон башни, высокий или приплюснутый)
    /// снимается объёмным шейдером с 16 сторон в массив текстур. Поле облаков вокруг камеры (бесконечное, дрейфует
    /// по ветру) рисуется одной сеткой: квады к камере, в шейдере — смесь двух ближайших снимков.
    /// Свет запечён под неподвижное солнце, поэтому облака не вращаются — меняется только сторона, с которой смотришь.
    /// </summary>
    public class CloudImpostors
    {
        public const int Frames = 16;
        public const int FrameSize = 288;
        public const float BakeRadius = 200f;
        public const float WidthPerR = 3f;       // весь габарит поля; край не обрезает боковые клубы
        public const float Pad = 0.08f;          // запас под верхние клубы и эрозию

        struct Variant { public int template; public float squash; }
        static readonly Variant[] Variants =
        {
            new Variant { template = 0, squash = 1f },
            new Variant { template = 1, squash = 0.95f },
            new Variant { template = 2, squash = 1.05f },
        };

        class Cloud
        {
            public Vector3 pos;   // центр основания
            public float r;
            public int variant;
            public float drift;
        }

        readonly List<Cloud> clouds = new List<Cloud>();
        RenderTexture tex;
        Texture2DArray cached;   // снимки, загруженные с диска (тогда tex не нужен)
        Texture Current { get { return cached != null ? (Texture)cached : tex; } }

        // ---------- кэш снимков между запусками ----------
        // Снимки зависят от версии сборки (форма, свет, шейдер) — ключ по Application.version.
        static string CachePath { get { return System.IO.Path.Combine(Application.persistentDataPath, "impostors_" + Application.version + "_" + FrameSize + "_" + Frames + ".bin"); } }

        bool TryLoad()
        {
            try
            {
                string path = CachePath;
                int slice = FrameSize * FrameSize * 4;
                int count = Variants.Length * Frames;
                if (!System.IO.File.Exists(path)) return false;
                byte[] all = System.IO.File.ReadAllBytes(path);
                if (all.Length != slice * count) return false;
                Texture2DArray a = new Texture2DArray(FrameSize, FrameSize, count, TextureFormat.RGBA32, true, true);
                a.wrapMode = TextureWrapMode.Clamp;
                a.filterMode = FilterMode.Trilinear;
                a.name = "CloudImpostors (кэш)";
                byte[] one = new byte[slice];
                for (int i = 0; i < count; i++)
                {
                    System.Buffer.BlockCopy(all, i * slice, one, 0, slice);
                    a.SetPixelData(one, 0, i);
                }
                a.Apply(true, true);
                cached = a;
                ready = true;
                Debug.Log("SkyTide: импосторы из кэша " + path);
                return true;
            }
            catch (System.Exception e) { Debug.LogWarning("SkyTide: кэш импосторов не прочитан: " + e.Message); return false; }
        }

        void Save()
        {
            try
            {
                int slice = FrameSize * FrameSize * 4;
                int count = Variants.Length * Frames;
                byte[] all = new byte[slice * count];
                RenderTexture one = RenderTexture.GetTemporary(FrameSize, FrameSize, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                Texture2D tmp = new Texture2D(FrameSize, FrameSize, TextureFormat.RGBA32, false, true);
                for (int i = 0; i < count; i++)
                {
                    Graphics.CopyTexture(tex, i, 0, one, 0, 0);
                    RenderTexture.active = one;
                    tmp.ReadPixels(new Rect(0, 0, FrameSize, FrameSize), 0, 0);
                    tmp.Apply(false);
                    System.Buffer.BlockCopy(tmp.GetRawTextureData(), 0, all, i * slice, slice);
                }
                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(one);
                SkyTideBootstrap.SafeDestroy(tmp);
                System.IO.File.WriteAllBytes(CachePath, all);
                Debug.Log("SkyTide: импосторы сохранены " + CachePath);
            }
            catch (System.Exception e) { Debug.LogWarning("SkyTide: кэш импосторов не записан: " + e.Message); }
        }
        Material mat;
        Mesh mesh;
        Vector3[] verts;
        Vector2[] uv0;
        Vector4[] uv1;
        float[] keys;
        int[] order;
        bool placed;
        readonly System.Random wrapRnd = new System.Random(4242);

        public float fieldRadius = 3200f;
        public bool useTowerMask = true;   // маска башен на облако (ускорение); выключается для диагностики
        public int tallCount = 12;
        public int lowCount = 26;
        public float deckTop = 35f;
        public bool ready;

        public static float HeightPerR(int variant, float templateHeight)
        {
            return templateHeight * Variants[variant].squash;
        }

        // ---------- запекание ----------

        /// <summary>Снять все варианты с 16 сторон. volMat уже настроен на кадр (свет, шум, форма).</summary>
        public bool Bake(Material volMat, Texture field, float templateHeight, Vector4 fieldBox, int steps)
        {
            if (!SystemInfo.supports2DArrayTextures) return false;
            if (Application.isPlaying && TryLoad()) return true;
            if (tex == null || !tex.IsCreated())
            {
                if (tex != null) tex.Release();
                tex = new RenderTexture(FrameSize, FrameSize, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                tex.dimension = UnityEngine.Rendering.TextureDimension.Tex2DArray;
                tex.volumeDepth = Variants.Length * Frames;
                tex.useMipMap = true;
                tex.autoGenerateMips = false;
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.filterMode = FilterMode.Trilinear;
                tex.name = "CloudImpostors";
                if (!tex.Create()) return false;
            }

            Vector4[] towers = new Vector4[8];
            Vector4[] rot = new Vector4[8];
            volMat.SetFloat("_Bake", 1f);
            volMat.SetFloat("_TowerCount", 1f);
            volMat.SetVector("_NearClear", new Vector4(-2f, -1f, 0f, 0f));
            volMat.SetVector("_ShipClear", Vector4.zero);
            volMat.SetVector("_Aerial", Vector4.zero);
            volMat.SetVector("_DensityParams", new Vector4(volMat.GetVector("_DensityParams").x, volMat.GetVector("_DensityParams").y, steps, 1e6f));
            volMat.SetVector("_LowRes", new Vector4(FrameSize, FrameSize, 1f / FrameSize, 1f / FrameSize));
            volMat.SetFloat("_Debug", 0f);

            const float R = BakeRadius;
            for (int v = 0; v < Variants.Length; v++)
            {
                // Каждый вариант — в своём месте мира: другой рисунок шума на поверхности.
                Vector3 c = new Vector3(6000f * (v + 1), 0f, -4000f * v);
                float hR = HeightPerR(v, templateHeight);
                towers[0] = new Vector4(c.x, c.y, c.z, R);
                rot[0] = new Vector4(1f, 0f, Variants[v].template, hR);
                volMat.SetVectorArray("_Towers", towers);
                volMat.SetVectorArray("_TowerRot", rot);
                float W = WidthPerR * R;
                float H = hR * R;
                for (int k = 0; k < Frames; k++)
                {
                    float az = k * Mathf.PI * 2f / Frames;
                    Vector3 toCam = new Vector3(Mathf.Cos(az), 0f, Mathf.Sin(az));
                    Vector3 fwd = -toCam;
                    Vector3 right = new Vector3(fwd.z, 0f, -fwd.x);
                    Vector3 origin = c - right * (W * 0.5f) + Vector3.up * (-Pad * H) - fwd * (2.2f * R);
                    volMat.SetVector("_BakeOrigin", origin);
                    volMat.SetVector("_BakeRight", right * W);
                    volMat.SetVector("_BakeUp", Vector3.up * (H * (1f + 2f * Pad)));
                    volMat.SetVector("_BakeFwd", fwd);
                    Graphics.Blit(Texture2D.blackTexture, tex, volMat, 4, v * Frames + k);
                }
            }
            // Снимки без размытия кадра дают зерно и полосы — сглаживаем каждый тентом 3×3 дважды.
            volMat.SetVector("_LowRes", new Vector4(FrameSize, FrameSize, 1f / FrameSize, 1f / FrameSize));
            RenderTexture tmpA = RenderTexture.GetTemporary(FrameSize, FrameSize, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            RenderTexture tmpB = RenderTexture.GetTemporary(FrameSize, FrameSize, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            tmpA.filterMode = FilterMode.Bilinear;
            tmpB.filterMode = FilterMode.Bilinear;
            for (int sl = 0; sl < Variants.Length * Frames; sl++)
            {
                Graphics.CopyTexture(tex, sl, 0, tmpA, 0, 0);
                Graphics.Blit(tmpA, tmpB, volMat, 3);
                Graphics.Blit(tmpB, tmpA, volMat, 3);
                Graphics.CopyTexture(tmpA, 0, 0, tex, sl, 0);
            }
            RenderTexture.ReleaseTemporary(tmpA);
            RenderTexture.ReleaseTemporary(tmpB);
            volMat.SetFloat("_Bake", 0f);
            tex.GenerateMips();
            ready = true;
            if (Application.isPlaying) Save();
            return true;
        }

        public Texture2D Sheet()
        {
            if (!ready || tex == null) return null;
            int cols = Frames / 2, rows = Variants.Length, s = FrameSize / 2;
            Texture2D outTex = new Texture2D(cols * s, rows * s, TextureFormat.RGB24, false);
            RenderTexture one = new RenderTexture(FrameSize, FrameSize, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            one.Create();
            Texture2D tmp = new Texture2D(FrameSize, FrameSize, TextureFormat.RGBA32, false, true);
            for (int v = 0; v < rows; v++)
                for (int c = 0; c < cols; c++)
                {
                    Graphics.CopyTexture(tex, v * Frames + c * 2, 0, one, 0, 0);
                    RenderTexture.active = one;
                    tmp.ReadPixels(new Rect(0, 0, FrameSize, FrameSize), 0, 0);
                    tmp.Apply();
                    for (int y = 0; y < s; y++)
                        for (int x = 0; x < s; x++)
                        {
                            Color p = tmp.GetPixel(x * 2, y * 2);
                            // фон — тёмно-синий, облако — свет поверх (как в кадре)
                            Color bg = new Color(0.12f, 0.1f, 0.25f);
                            float L = 1f;
                            Color lit = new Color(p.r * p.r * 6f, p.g * p.g * 6f, p.b * p.b * 6f) * L;
                            Color o = bg * (1f - p.a) + lit;
                            o.r = o.r / (1f + o.r); o.g = o.g / (1f + o.g); o.b = o.b / (1f + o.b);
                            outTex.SetPixel(c * s + x, (rows - 1 - v) * s + y, new Color(Mathf.Pow(o.r, 0.4545f), Mathf.Pow(o.g, 0.4545f), Mathf.Pow(o.b, 0.4545f)));
                        }
                }
            RenderTexture.active = null;
            one.Release();
            outTex.Apply();
            return outTex;
        }

        public bool NeedsBake { get { return !ready || (cached == null && (tex == null || !tex.IsCreated())); } }

        // ---------- поле ----------

        public void Place(Vector3 center, float templateHeight)
        {
            clouds.Clear();
            System.Random r = new System.Random(2027);
            for (int i = 0; i < tallCount; i++)
            {
                Cloud c = new Cloud();
                c.variant = r.Next(3);
                c.r = U(r, 110f, 230f);
                c.pos = VolumetricClouds.AroundFortress(RandomPoint(r, center, 700f, fieldRadius, U(r, -40f, -25f)));
                c.drift = U(r, 0.8f, 1.1f);
                clouds.Add(c);
            }
            for (int i = 0; i < lowCount; i++)
            {
                Cloud c = new Cloud();
                // Небольшие кучевые, растущие из моря облаков (низ тонет в объёмном слое).
                c.variant = r.Next(3);
                c.r = U(r, 45f, 95f);
                c.pos = VolumetricClouds.AroundFortress(RandomPoint(r, center, 350f, fieldRadius, deckTop - U(r, 30f, 55f)));
                c.drift = U(r, 0.9f, 1.3f);
                clouds.Add(c);
            }
            placed = true;
        }

        static Vector3 RandomPoint(System.Random r, Vector3 center, float minR, float maxR, float y)
        {
            float a = (float)(r.NextDouble() * Mathf.PI * 2.0);
            float d = Mathf.Sqrt(U(r, (minR * minR) / (maxR * maxR), 1f)) * maxR;
            return new Vector3(center.x + Mathf.Cos(a) * d, y, center.z + Mathf.Sin(a) * d);
        }

        static float U(System.Random r, float a, float b) { return a + (float)r.NextDouble() * (b - a); }

        public void Advance(Vector3 cam, Vector3 wind, float dt, float templateHeight)
        {
            if (!placed) Place(cam, templateHeight);
            float lim = fieldRadius * 1.05f;
            for (int i = 0; i < clouds.Count; i++)
            {
                Cloud c = clouds[i];
                Vector3 p = c.pos + wind * (c.drift * dt);
                Vector3 d = p - cam;
                d.y = 0f;
                if (d.magnitude > lim)
                {
                    // Ушло далеко — переносим на противоположную сторону, у горизонта.
                    Vector3 back = -d.normalized * (fieldRadius * U(wrapRnd, 0.9f, 1f));
                    p.x = cam.x + back.x;
                    p.z = cam.z + back.z;
                }
                // Нарисованные облака не встают внутрь небесной крепости (иначе их плоскость режет стены прямой линией).
                c.pos = VolumetricClouds.AroundFortress(p);
            }
        }

        // ---------- отрисовка ----------

        public void Draw(Camera cam, RenderTexture target, Shader shader, RenderTexture heroClouds,
                         Vector4[] heroTowers, Vector4[] heroRot, int heroCount, float templateHeight,
                         float aerial, Vector2 nearClear)
        {
            if (!ready || clouds.Count == 0 || shader == null || !shader.isSupported) return;
            if (mat == null)
            {
                mat = new Material(shader);
                mat.hideFlags = HideFlags.HideAndDontSave;
            }
            Vector3 cp = cam.transform.position;
            if (!placed) Place(cp, templateHeight);
            BuildMesh(cp, templateHeight, heroTowers, heroRot, heroCount);

            Matrix4x4 view = cam.worldToCameraMatrix;
            mat.SetMatrix("_ImpVP", GL.GetGPUProjectionMatrix(cam.projectionMatrix, target != null) * view);
            mat.SetMatrix("_CamVP", cam.projectionMatrix * view);
            mat.SetMatrix("_CamV", view);
            mat.SetVector("_ImpCam", cp);
            mat.SetVector("_ImpParams", new Vector4(aerial, nearClear.x, nearClear.y, Pad));
            mat.SetFloat("_ImpDeckTop", deckTop);
            mat.SetFloat("_ImpUseMask", useTowerMask ? 1f : 0f);
            mat.SetTexture("_ImpTex", Current);
            mat.SetTexture("_CloudTex", heroClouds);
            mat.SetVectorArray("_Towers", heroTowers);
            mat.SetVectorArray("_TowerRot", heroRot);
            mat.SetFloat("_TowerCount", heroCount);

            RenderTexture.active = target;
            if (mat.SetPass(0)) Graphics.DrawMeshNow(mesh, Matrix4x4.identity);
        }

        /// <summary>Биты башен, которые могут закрыть облако: башня ближе облака и пересекается с ним по направлению.
        /// В шейдере остальные башни не проверяются — у большинства облаков маска 0 и цикл не выполняется.</summary>
        static float TowerMask(Vector3 cam, Vector3 center, float halfSize, Vector4[] towers, Vector4[] rot, int count)
        {
            if (towers == null || rot == null) return 0f;
            Vector3 dc = center - cam; float distC = dc.magnitude; if (distC < 1e-3f) return 0f;
            Vector3 dirC = dc / distC;
            int mask = 0;
            for (int i = 0; i < count && i < 8 && i < towers.Length && i < rot.Length; i++)
            {
                Vector4 T = towers[i]; float hh = rot[i].w;
                Vector3 tc = new Vector3(T.x, T.y + T.w * hh * 0.5f, T.z);
                float r = T.w * Mathf.Max(1.5f, hh * 0.62f);
                Vector3 dt = tc - cam; float distT = dt.magnitude;
                if (distT - r > distC + halfSize) continue;                  // башня целиком за облаком
                if (distT <= r) { mask |= 1 << i; continue; }               // камера внутри сферы башни
                float angT = Mathf.Asin(Mathf.Clamp01(r / distT)), angC = Mathf.Atan2(halfSize, distC);
                float ang = Mathf.Acos(Mathf.Clamp(Vector3.Dot(dt / distT, dirC), -1f, 1f));
                if (ang <= angT + angC + 0.05f) mask |= 1 << i;
            }
            return mask;
        }

        void BuildMesh(Vector3 cam, float templateHeight, Vector4[] heroTowers, Vector4[] heroRot, int heroCount)
        {
            int n = clouds.Count;
            if (mesh == null || verts == null || verts.Length != n * 4)
            {
                mesh = new Mesh();
                mesh.name = "CloudImpostors";
                mesh.MarkDynamic();
                verts = new Vector3[n * 4];
                uv0 = new Vector2[n * 4];
                uv1 = new Vector4[n * 4];
                keys = new float[n];
                order = new int[n];
                int[] tris = new int[n * 6];
                for (int i = 0; i < n; i++)
                {
                    tris[i * 6 + 0] = i * 4 + 0; tris[i * 6 + 1] = i * 4 + 1; tris[i * 6 + 2] = i * 4 + 2;
                    tris[i * 6 + 3] = i * 4 + 0; tris[i * 6 + 4] = i * 4 + 2; tris[i * 6 + 5] = i * 4 + 3;
                }
                mesh.vertices = verts;
                mesh.SetUVs(0, new List<Vector2>(uv0));
                mesh.SetUVs(1, new List<Vector4>(uv1));
                mesh.triangles = tris;
                mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);
            }
            // Сортировка от дальних к ближним — полупрозрачные облака ложатся друг на друга правильно.
            for (int i = 0; i < n; i++)
            {
                order[i] = i;
                Vector3 d = clouds[i].pos - cam;
                keys[i] = -(d.x * d.x + d.z * d.z);
            }
            System.Array.Sort(keys, order);
            for (int j = 0; j < n; j++)
            {
                Cloud c = clouds[order[j]];
                float W = WidthPerR * c.r;
                float H = HeightPerR(c.variant, templateHeight) * c.r;
                float half = 0.5f * Mathf.Sqrt(W * W + H * H * (1f + 2f * Pad) * (1f + 2f * Pad));
                Vector4 data = new Vector4(W, H, c.variant, TowerMask(cam, c.pos + Vector3.up * (0.5f * H), half, heroTowers, heroRot, heroCount));
                for (int q = 0; q < 4; q++)
                {
                    verts[j * 4 + q] = c.pos;
                    uv1[j * 4 + q] = data;
                }
                uv0[j * 4 + 0] = new Vector2(0f, 0f);
                uv0[j * 4 + 1] = new Vector2(0f, 1f);
                uv0[j * 4 + 2] = new Vector2(1f, 1f);
                uv0[j * 4 + 3] = new Vector2(1f, 0f);
            }
            mesh.vertices = verts;
            mesh.uv = uv0;
            mesh.SetUVs(1, uv1);
        }

        public void Release()
        {
            if (tex != null) { tex.Release(); SkyTideBootstrap.SafeDestroy(tex); tex = null; }
            if (cached != null) { SkyTideBootstrap.SafeDestroy(cached); cached = null; }
            if (mat != null) { SkyTideBootstrap.SafeDestroy(mat); mat = null; }
            if (mesh != null) { SkyTideBootstrap.SafeDestroy(mesh); mesh = null; }
            ready = false;
        }
    }
}
