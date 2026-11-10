using System.Collections.Generic;
using UnityEngine;

namespace SkyTide
{
    /// <summary>
    /// Частицы залпа: ядра (баллистика, шлейф дыма), вспышки у дул, пороховой дым.
    /// Все частицы — один динамический меш квадов (шейдер Hidden/SkyTide/Fx), отсортированный от дальних к ближним.
    /// В объёмном режиме рисуется из VolumetricClouds после облаков, иначе — обычной очередью прозрачных.
    /// </summary>
    public class FxSystem : MonoBehaviour
    {
        public static FxSystem Instance { get; set; }

        public Shader shader;
        public float gravity = 9.81f;
        public Vector3 wind = new Vector3(-4f, 0f, 1.8f);
        public float deckTop = 35f;
        public float glowIntensity = 7f;
        public float smokeLight = 1.25f;
        const int MaxParticles = 3000;

        enum Kind { Glow = 0, Smoke = 1, Ball = 2 }

        struct P
        {
            public Vector3 pos, vel;
            public float age, life, size0, size1, rot, spin, drag, rise, seed, alpha;
            public Color color;
            public Kind kind;
            public float trailAcc;
            public bool debris;
        }

        readonly List<P> ps = new List<P>(MaxParticles);
        Material mat;
        Mesh mesh;
        Vector3[] verts = new Vector3[0];
        Vector2[] uv0 = new Vector2[0];
        Vector4[] uv1 = new Vector4[0];
        Color[] cols = new Color[0];
        int[] tris = new int[0];
        float[] keys = new float[0];
        int[] order = new int[0];
        int drawn;
        public VolumetricClouds volumetric;

        void OnEnable() { Instance = this; }
        void OnDisable() { if (Instance == this) Instance = null; }

        // ---------- выстрел ----------

        /// <summary>Выстрел из ствола: точка дула и направление ствола — в мире; скорость корабля добавляется к ядру и дыму.</summary>
        public void Fire(Vector3 muzzle, Vector3 dir, Vector3 shipVel, float muzzleSpeed)
        {
            dir.Normalize();
            float s = Random.value * 10f;
            // Ядро
            Add(new P
            {
                kind = Kind.Ball, pos = muzzle, vel = dir * muzzleSpeed + shipVel, life = 7f,
                size0 = 0.9f, size1 = 0.7f, color = new Color(1f, 0.5f, 0.18f), alpha = 0.55f, seed = s
            });
            Flash(muzzle, 2.5f);
            // Вспышка у дула: ядро пламени и язык по стволу
            Add(new P { kind = Kind.Glow, pos = muzzle + dir * 0.5f, vel = shipVel, life = 0.11f, size0 = 2.2f, size1 = 5.5f, color = new Color(1f, 0.62f, 0.26f), alpha = 1f });
            Add(new P { kind = Kind.Glow, pos = muzzle + dir * 2.2f, vel = shipVel + dir * 25f, life = 0.09f, size0 = 2.8f, size1 = 4.2f, color = new Color(1f, 0.45f, 0.15f), alpha = 0.8f });
            // Пороховой дым: облако у дула, вынос по стволу, медленно поднимается и расползается
            for (int k = 0; k < 8; k++)
            {
                Vector3 jitter = Random.insideUnitSphere;
                Add(new P
                {
                    kind = Kind.Smoke, pos = muzzle + dir * Random.Range(0.3f, 1.8f) + jitter * 0.4f,
                    vel = dir * Random.Range(6f, 24f) + jitter * 2.5f + shipVel * 0.85f,
                    life = Random.Range(2.6f, 4.2f), size0 = Random.Range(1.2f, 2.2f), size1 = Random.Range(7f, 11f),
                    rot = Random.Range(0f, 6.28f), spin = Random.Range(-0.4f, 0.4f), drag = 2.2f, rise = 0.9f,
                    color = new Color(0.68f, 0.62f, 0.6f), alpha = Random.Range(0.45f, 0.7f), seed = Random.value * 10f
                });
            }
        }

        /// <summary>Взрыв: вспышка, огненный шар, искры-обломки по баллистике, тёмный дым. scale 1 — попадание, 3 — гибель.</summary>
        public void Explosion(Vector3 at, float scale)
        {
            Flash(at, 4f * scale);
            Add(new P { kind = Kind.Glow, pos = at, life = 0.22f * Mathf.Sqrt(scale), size0 = 8f * scale, size1 = 18f * scale, color = new Color(1f, 0.7f, 0.35f), alpha = 1f });
            for (int k = 0; k < 6 * scale; k++)
            {
                Vector3 r = Random.insideUnitSphere;
                Add(new P { kind = Kind.Glow, pos = at + r * 2f * scale, vel = r * 9f * scale, life = Random.Range(0.35f, 0.7f) * Mathf.Sqrt(scale),
                            size0 = 4f * scale, size1 = 9f * scale, color = new Color(1f, 0.42f, 0.12f), alpha = 0.85f });
            }
            for (int k = 0; k < 14 * scale; k++)
            {
                Vector3 r = Random.onUnitSphere;
                r.y = Mathf.Abs(r.y) * 0.8f + 0.2f;
                Add(new P { kind = Kind.Ball, pos = at, vel = r * Random.Range(18f, 45f) * Mathf.Sqrt(scale), life = Random.Range(0.8f, 1.8f),
                            size0 = 0.5f, size1 = 0.5f, color = new Color(1f, 0.55f, 0.2f), alpha = 0.9f, trailAcc = -1e6f, debris = true });
            }
            for (int k = 0; k < 10 * scale; k++)
            {
                Vector3 r = Random.insideUnitSphere;
                Add(new P { kind = Kind.Smoke, pos = at + r * 2.5f * scale, vel = r * 6f * scale + Vector3.up * 2f, life = Random.Range(3.5f, 6f),
                            size0 = 3f * scale, size1 = Random.Range(12f, 18f) * scale, rot = Random.Range(0f, 6.28f), spin = Random.Range(-0.3f, 0.3f),
                            drag = 1.6f, rise = 1.6f, color = new Color(0.28f, 0.22f, 0.22f), alpha = 0.75f, seed = Random.value * 10f });
            }
        }

        void Add(P p)
        {
            if (ps.Count >= MaxParticles) return;
            ps.Add(p);
        }

        // ---------- симуляция ----------

        void Update()
        {
            if (Application.isPlaying) Simulate(Time.deltaTime);
        }

        readonly List<Vector3> splashes = new List<Vector3>();
        Vector4 flash;   // xyz позиция, w сила — глобал _SkyTideFlash для облаков

        void Flash(Vector3 at, float power)
        {
            if (power >= flash.w) flash = new Vector4(at.x, at.y, at.z, power);
        }

        /// <summary>Ядро вошло в море облаков: клуб облачной «пены» (база — развивать: волна, кольцо, подсветка снизу).</summary>
        public void CloudSplash(Vector3 at)
        {
            for (int k = 0; k < 7; k++)
            {
                Vector3 r = Random.insideUnitSphere;
                r.y = Mathf.Abs(r.y);
                Add(new P { kind = Kind.Smoke, pos = at + new Vector3(r.x * 3f, 6f, r.z * 3f), vel = new Vector3(r.x * 4f, 6f + r.y * 6f, r.z * 4f),
                            life = Random.Range(2.5f, 3.8f), size0 = 4f, size1 = Random.Range(12f, 17f), rot = Random.Range(0f, 6.28f),
                            spin = Random.Range(-0.3f, 0.3f), drag = 1.4f, rise = 0.6f, color = new Color(0.95f, 0.8f, 0.84f), alpha = 0.5f,
                            seed = Random.value * 10f });
            }
        }

        public void Simulate(float dt)
        {
            if (dt <= 0f) return;
            flash.w *= Mathf.Exp(-dt * 14f);
            int n = ps.Count;
            for (int i = 0; i < n; i++)
            {
                P p = ps[i];
                p.age += dt;
                if (p.kind == Kind.Ball)
                {
                    Vector3 prev = p.pos;
                    p.vel.y -= gravity * dt;
                    p.pos += p.vel * dt;
                    // Шлейф: клочки дыма вдоль пути через равные промежутки.
                    p.trailAcc += Vector3.Distance(prev, p.pos);
                    float spacing = 1.1f;
                    while (p.trailAcc > spacing && p.age < 4.5f)
                    {
                        p.trailAcc -= spacing;
                        Vector3 at = Vector3.Lerp(p.pos, prev, p.trailAcc / Mathf.Max(Vector3.Distance(prev, p.pos), 1e-3f));
                        float fade = 1f - p.age / 4.5f;
                        Add(new P
                        {
                            kind = Kind.Smoke, pos = at, vel = Random.insideUnitSphere * 0.6f, life = Random.Range(1.4f, 2.2f),
                            size0 = 1.5f, size1 = Random.Range(3.5f, 5f), rot = Random.Range(0f, 6.28f), spin = Random.Range(-0.6f, 0.6f),
                            drag = 1f, rise = 0.4f, color = new Color(0.62f, 0.57f, 0.56f), alpha = 0.22f * fade, seed = Random.value * 10f
                        });
                    }
                    if (p.pos.y < deckTop - 15f && p.life > 0f)   // ушло в море облаков — «всплеск» облака
                    {
                        p.life = 0f;
                        if (!p.debris) splashes.Add(p.pos);
                    }
                    for (int t = 0; t < TargetShip.All.Count && p.life > 0f && !p.debris; t++)
                    {
                        Vector3 hp;
                        if (TargetShip.All[t].TryHit(prev, p.pos, out hp)) { p.life = 0f; ps[i] = p; TargetShip.All[t].OnHit(hp); }
                    }
                }
                else if (p.kind == Kind.Smoke)
                {
                    p.vel *= Mathf.Exp(-p.drag * dt);
                    p.vel.y += p.rise * dt;
                    p.pos += (p.vel + wind * 0.6f) * dt;
                    p.rot += p.spin * dt;
                }
                else
                {
                    p.pos += p.vel * dt;
                }
                ps[i] = p;
            }
            for (int i = ps.Count - 1; i >= 0; i--)
                if (ps[i].age >= ps[i].life) ps.RemoveAt(i);
            for (int i = 0; i < splashes.Count; i++) CloudSplash(splashes[i]);
            splashes.Clear();
            Shader.SetGlobalVector("_SkyTideFlash", flash);
        }

        // ---------- отрисовка ----------

        bool Ensure()
        {
            if (shader == null) shader = Shader.Find("Hidden/SkyTide/Fx");
            if (shader == null || !shader.isSupported) return false;
            if (mat == null)
            {
                mat = new Material(shader);
                mat.hideFlags = HideFlags.HideAndDontSave;
            }
            if (mesh == null)
            {
                mesh = new Mesh();
                mesh.name = "Fx";
                mesh.MarkDynamic();
                mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);
            }
            return true;
        }

        bool External { get { return volumetric != null && volumetric.isActiveAndEnabled; } }

        void LateUpdate()
        {
            Camera cam = Camera.main;
            if (cam != null) PrepareForRender(cam);
        }

        /// <summary>Собрать меш под камеру; в обычном режиме — поставить в очередь отрисовки.</summary>
        public void PrepareForRender(Camera cam)
        {
            if (!Ensure()) return;
            BuildMesh(cam);
            if (drawn == 0 || External) return;
            SetCommon(cam);
            mat.SetFloat("_FxExternal", 0f);
            mat.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.LessEqual);
            Graphics.DrawMesh(mesh, Matrix4x4.identity, mat, 0, Application.isPlaying ? null : cam);
        }

        /// <summary>Отрисовка поверх объёмных облаков (из VolumetricClouds.OnRenderImage).</summary>
        public void DrawExternal(Camera cam, RenderTexture target)
        {
            if (drawn == 0 || !Ensure()) return;
            SetCommon(cam);
            mat.SetFloat("_FxExternal", 1f);
            mat.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always);
            mat.SetMatrix("_FxVP", GL.GetGPUProjectionMatrix(cam.projectionMatrix, target != null) * cam.worldToCameraMatrix);
            RenderTexture.active = target;
            if (mat.SetPass(0)) Graphics.DrawMeshNow(mesh, Matrix4x4.identity);
        }

        void SetCommon(Camera cam)
        {
            mat.SetVector("_FxRight", cam.transform.right);
            mat.SetVector("_FxUp", cam.transform.up);
            mat.SetMatrix("_FxCamVP", cam.projectionMatrix * cam.worldToCameraMatrix);
            mat.SetMatrix("_FxCamV", cam.worldToCameraMatrix);
            mat.SetVector("_FxParams", new Vector4(glowIntensity, smokeLight, deckTop, 0f));
        }

        void BuildMesh(Camera cam)
        {
            int n = ps.Count;
            drawn = n;
            if (verts.Length < n * 4)
            {
                int cap = Mathf.NextPowerOfTwo(Mathf.Max(n, 64));
                verts = new Vector3[cap * 4];
                uv0 = new Vector2[cap * 4];
                uv1 = new Vector4[cap * 4];
                cols = new Color[cap * 4];
                tris = new int[cap * 6];
                keys = new float[cap];
                order = new int[cap];
            }
            Vector3 cp = cam.transform.position;
            for (int i = 0; i < n; i++)
            {
                order[i] = i;
                keys[i] = -(ps[i].pos - cp).sqrMagnitude;
            }
            System.Array.Sort(keys, order, 0, n);
            for (int j = 0; j < n; j++)
            {
                P p = ps[order[j]];
                float t = Mathf.Clamp01(p.age / Mathf.Max(p.life, 1e-3f));
                float size, a;
                float kind;
                if (p.kind == Kind.Smoke)
                {
                    size = Mathf.Lerp(p.size0, p.size1, 1f - (1f - t) * (1f - t));
                    a = p.alpha * Mathf.Clamp01(p.age / 0.08f) * (1f - t) * (1f - t);
                    kind = 1f;
                }
                else if (p.kind == Kind.Glow)
                {
                    size = Mathf.Lerp(p.size0, p.size1, t);
                    a = p.alpha * (1f - t);
                    kind = 0f;
                }
                else
                {
                    size = p.size0;
                    a = p.alpha * Mathf.Clamp01(1f - (p.age - 5f) / 2f);
                    kind = 0f;
                }
                Color c = p.color;
                c.a = a;
                Vector4 d = new Vector4(size, p.rot, kind, p.seed);
                int v = j * 4;
                for (int q = 0; q < 4; q++)
                {
                    verts[v + q] = p.pos;
                    uv1[v + q] = d;
                    cols[v + q] = c;
                }
                uv0[v] = new Vector2(-0.5f, -0.5f);
                uv0[v + 1] = new Vector2(-0.5f, 0.5f);
                uv0[v + 2] = new Vector2(0.5f, 0.5f);
                uv0[v + 3] = new Vector2(0.5f, -0.5f);
                int ti = j * 6;
                tris[ti] = v; tris[ti + 1] = v + 1; tris[ti + 2] = v + 2;
                tris[ti + 3] = v; tris[ti + 4] = v + 2; tris[ti + 5] = v + 3;
            }
            // Хвост массивов — вырожденные треугольники.
            for (int ti = n * 6; ti < tris.Length; ti++) tris[ti] = 0;
            mesh.Clear(false);
            mesh.vertices = verts;
            mesh.uv = uv0;
            mesh.SetUVs(1, uv1);
            mesh.colors = cols;
            mesh.triangles = tris;
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);
        }

        void OnDestroy()
        {
            if (mat != null) SkyTideBootstrap.SafeDestroy(mat);
            if (mesh != null) SkyTideBootstrap.SafeDestroy(mesh);
        }
    }
}
