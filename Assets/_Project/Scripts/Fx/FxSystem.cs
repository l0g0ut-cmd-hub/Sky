using System.Collections.Generic;
using UnityEngine;

namespace SkyTide
{
    /// <summary>
    /// Частицы залпа: чугунные ядра по баллистике, вспышки у дул, пороховой дым.
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
        const int MaxFlashes = 4;
        const int MaxLights = 2;   // только vertex light, без теней и дополнительных forward-проходов

        // Значения совпадают с Fx.shader.
        enum Kind { Glow = 0, Smoke = 1, Ball = 2, Muzzle = 3, Spark = 4, Foam = 5, Wave = 6, Splinter = 7, Flame = 8, Aim = 9, Flip = 10 }

        struct P
        {
            public Vector3 pos, vel;
            public float age, life, size0, size1, rot, spin, drag, rise, seed, alpha;
            public Color color;
            public Kind kind;
            public Vector3 axis;
            public float aspect, surfaceY;
            public bool hostile;   // ядро врага: бьёт по кораблю игрока, а не по мишеням
        }

        readonly List<P> ps = new List<P>(MaxParticles);
        Material mat;
        Mesh mesh;
        Vector3[] verts = new Vector3[0];
        Vector2[] uv0 = new Vector2[0];
        Vector4[] uv1 = new Vector4[0];
        Vector4[] uv2 = new Vector4[0];
        Vector4[] uv3 = new Vector4[0];
        Color[] cols = new Color[0];
        int[] tris = new int[0];
        float[] keys = new float[0];
        int[] order = new int[0];
        int drawn;
        public VolumetricClouds volumetric;
        public int CloudSplashCount { get; private set; }
        public Vector3 LastCloudSplash { get; private set; }
        public int ActiveFlames {get {int n=0;foreach(P p in ps)if(p.kind==Kind.Flame)n++;return n;}}
#if UNITY_EDITOR
        bool legacyAppearanceCapture;
        public void SetLegacyAppearanceCapture(bool enabled) { legacyAppearanceCapture = enabled; }
#endif

        void OnEnable() { Instance = this; PublishFlashes(); }
        void OnDisable() { if (Instance == this) { Clear(); Instance = null; } }

        readonly CloudImpactSurface surface = new CloudImpactSurface();
        public void ConfigureSurface(Texture2D weather, byte[] noise) { surface.Configure(weather, noise); }

        public bool TryCloudEntry(Vector3 a, Vector3 b, out Vector3 point)
        {
            return surface.TryEnter(a, b, volumetric, Application.isPlaying ? Time.time : 0f, deckTop, out point);
        }

        // ---------- выстрел ----------

        /// <summary>Выстрел из ствола: точка дула и направление ствола — в мире; скорость корабля добавляется к ядру и дыму.</summary>
        public void Fire(Vector3 muzzle, Vector3 dir, Vector3 shipVel, float muzzleSpeed, bool hostile = false)
        {
            dir.Normalize();
            float s = Random.value * 10f;
            // Ядро
            Add(new P
            {
                kind = Kind.Ball, pos = muzzle, vel = dir * muzzleSpeed + shipVel, life = 7f, hostile = hostile,
                size0 = 0.85f, size1 = 0.7f, color = new Color(0.2f, 0.15f, 0.12f), alpha = 1f, seed = s
            });
            Flash(muzzle, 2.5f, 32f);
            SkySound.CannonFired(muzzle, hostile);
            // Короткий направленный факел с горячей сердцевиной, затем остаётся только дым.
            Add(new P { kind = Kind.Glow, pos = muzzle + dir * 0.6f, vel = shipVel, life = 0.07f,
                        size0 = 1.4f, size1 = 2.4f, color = new Color(1f, 0.72f, 0.35f), alpha = 0.65f });
            Add(new P { kind = Kind.Muzzle, pos = muzzle + dir * 2f, vel = shipVel + dir * 10f, axis = dir, aspect = 0.34f,
                        life = 0.12f, size0 = 4f, size1 = 6.5f, color = new Color(1f, 0.52f, 0.16f), alpha = 0.5f, seed = s });
            // Нарисованная вспышка у дула (лист 4) и пороховой клуб (лист 2) + два рваных клуба (лист 6).
            Add(new P { kind = Kind.Flip, pos = muzzle + dir * 3.2f, vel = shipVel + dir * 4f, axis = dir, aspect = 0.8f,
                        life = 0.34f, size0 = 7f, size1 = 8.5f, color = Color.white, alpha = 1f, seed = 4f + Random.value * 0.999f });
            Add(new P { kind = Kind.Flip, pos = muzzle + dir * 4.5f, vel = dir * 7f + shipVel * 0.85f, life = 2.9f,
                        size0 = 6.5f, size1 = 11f, rot = Random.Range(0f, 6.28f), spin = Random.Range(-0.3f, 0.3f), drag = 1.5f, rise = 0.9f,
                        color = new Color(0.92f, 0.88f, 0.85f), alpha = 0.85f, seed = 2f + Random.value * 0.999f });
            for (int k = 0; k < 2; k++)
                Add(new P { kind = Kind.Flip, pos = muzzle + dir * Random.Range(3f, 7f) + Random.insideUnitSphere, vel = dir * Random.Range(4f, 9f) + shipVel * 0.85f,
                            age = -0.08f - k * 0.1f, life = Random.Range(2.6f, 3.6f), size0 = 3.5f, size1 = 8f, rot = Random.Range(0f, 6.28f),
                            spin = Random.Range(-0.25f, 0.25f), drag = 1.4f, rise = 1f, color = new Color(0.9f, 0.86f, 0.83f), alpha = 0.6f,
                            seed = 6f + Random.value * 0.999f });
            for (int k = 0; k < 3; k++)
            {
                Vector3 velocity = dir * Random.Range(24f, 42f) + Random.insideUnitSphere * 9f + shipVel;
                Add(new P { kind = Kind.Spark, pos = muzzle, vel = velocity, axis = velocity.normalized, aspect = 0.12f,
                            life = Random.Range(0.15f, 0.35f), size0 = 0.9f, size1 = 0.15f,
                            color = new Color(1f, 0.52f, 0.14f), alpha = 0.65f });
            }
            // Несколько неодинаковых клубов: раскрываются с небольшой задержкой и теряют строй на ветру.
            for (int k = 0; k < 8; k++)
            {
                Vector3 jitter = Random.insideUnitSphere;
                Add(new P
                {
                    kind = Kind.Smoke, pos = muzzle + dir * Random.Range(0.4f, 2f) + jitter * 0.7f,
                    vel = dir * Random.Range(7f, 20f) + jitter * 4f + shipVel * 0.85f,
                    age = -k * 0.009f, life = Random.Range(2.5f, 4.1f), size0 = Random.Range(0.9f, 1.7f), size1 = Random.Range(4.5f, 7.5f),
                    rot = Random.Range(0f, 6.28f), spin = Random.Range(-0.55f, 0.55f), drag = 1.7f, rise = 1.1f,
                    color = new Color(0.48f, 0.44f, 0.42f), alpha = Random.Range(0.5f, 0.72f), seed = Random.value * 10f
                });
            }
        }

        /// <summary>Взрыв: вспышка, огненный шар, искры-обломки по баллистике, тёмный дым. scale 1 — попадание, 3 — гибель.</summary>
        public void Explosion(Vector3 at, float scale)
        {
            Flash(at, 4f * scale, 45f * Mathf.Sqrt(scale));
            // Нарисованный взрыв (лист 0, гибель — лист 1) и тяжёлый клуб дыма после него (лист 2).
            Add(new P { kind = Kind.Flip, pos = at, vel = Vector3.up * 1.5f, life = 1.25f * Mathf.Sqrt(scale), size0 = 9f * scale, size1 = 11.5f * scale,
                        rot = Random.Range(-0.4f, 0.4f), color = Color.white, alpha = 1f, seed = (scale >= 2f ? 1f : 0f) + Random.value * 0.999f });
            Add(new P { kind = Kind.Flip, pos = at + Vector3.up * 2f * scale, vel = Vector3.up * 2.5f, age = -0.35f * Mathf.Sqrt(scale),
                        life = 4.5f * Mathf.Sqrt(scale), size0 = 8f * scale, size1 = 17f * scale, rot = Random.Range(0f, 6.28f), spin = Random.Range(-0.15f, 0.15f),
                        drag = 1f, rise = 1.2f, color = new Color(0.5f, 0.45f, 0.43f), alpha = 0.9f, seed = 2f + Random.value * 0.999f });
            // Процедурное свечение — только короткая вспышка под нарисованным шаром (раньше белый пузырь поверх него).
            Add(new P { kind = Kind.Glow, pos = at, life = 0.1f * Mathf.Sqrt(scale), size0 = 3f * scale, size1 = 6f * scale, color = new Color(1f, 0.6f, 0.28f), alpha = 0.55f });
            for (int k = 0; k < 6 * scale; k++)
            {
                Vector3 r = Random.insideUnitSphere;
                Add(new P { kind = Kind.Glow, pos = at + r * 1.5f * scale, vel = r * 8f * scale, life = Random.Range(0.2f, 0.45f) * Mathf.Sqrt(scale),
                            size0 = 1.5f * scale, size1 = 3.5f * scale, color = new Color(1f, 0.36f, 0.08f), alpha = 0.3f });
            }
            for (int k = 0; k < 14 * scale; k++)
            {
                Vector3 r = Random.onUnitSphere;
                r.y = Mathf.Abs(r.y) * 0.8f + 0.2f;
                Vector3 velocity = r * Random.Range(18f, 40f) * Mathf.Sqrt(scale);
                Add(new P { kind = Kind.Spark, pos = at, vel = velocity, axis = r, aspect = 0.14f, life = Random.Range(0.6f, 1.4f),
                            size0 = 1.1f, size1 = 0.25f, color = new Color(1f, 0.48f, 0.12f), alpha = 0.8f });
            }
            for (int k = 0; k < 9 * scale; k++)
            {
                Vector3 velocity = (Random.insideUnitSphere + Vector3.up * 0.45f) * Random.Range(9f, 24f) * Mathf.Sqrt(scale);
                Add(new P { kind = Kind.Splinter, pos = at, vel = velocity, axis = Random.onUnitSphere, aspect = Random.Range(0.12f, 0.25f),
                            life = Random.Range(3f, 4.5f), size0 = Random.Range(0.6f, 1.3f) * Mathf.Sqrt(scale),
                            color = new Color(0.26f, 0.13f, 0.06f), alpha = 1f, spin = Random.Range(-8f, 8f), seed = Random.value * 10f });
            }
            for (int k = 0; k < 8 * scale; k++)
            {
                Vector3 r = Random.insideUnitSphere;
                Add(new P { kind = Kind.Smoke, pos = at + r * 2.5f * scale, vel = r * 6f * scale + Vector3.up * 2f, life = Random.Range(3.5f, 6f),
                            size0 = 2f * scale, size1 = Random.Range(8f, 12f) * scale, rot = Random.Range(0f, 6.28f), spin = Random.Range(-0.3f, 0.3f),
                            drag = 1.4f, rise = 1.6f, color = new Color(0.23f, 0.19f, 0.18f), alpha = 0.78f, seed = Random.value * 10f });
            }
        }

        void Add(P p)
        {
            if (ps.Count >= MaxParticles) return;
            ps.Add(p);
        }

        public void HullImpact(Vector3 at,Vector3 outward,Vector3 inheritedVelocity)
        {
            // Огненный шар — снаружи борта: из точки на корпусе половина листа уходила внутрь модели.
            Explosion(at+outward*1.6f,1f);
            // Splinters leave the struck face instead of scattering equally into the hull.
            for(int i=0;i<9;i++)
            {
                Vector3 direction=(outward+Random.insideUnitSphere*.55f+Vector3.up*.2f).normalized;
                Add(new P{kind=Kind.Splinter,pos=at+outward*.2f,vel=inheritedVelocity+direction*Random.Range(8f,18f),axis=Random.onUnitSphere,
                    aspect=Random.Range(.07f,.16f),life=Random.Range(1.2f,2.4f),size0=Random.Range(.45f,1.2f),
                    color=new Color(.34f,.16f,.055f),alpha=1,spin=Random.Range(-7f,7f),seed=Random.value*10});
            }
        }

        /// <summary>Ядро в стену крепости: вспышка, каменная крошка по баллистике, пыль.</summary>
        public void StoneImpact(Vector3 at, Vector3 outward)
        {
            Explosion(at + outward * 1.6f, 0.8f);
            for (int i = 0; i < 10; i++)
            {
                Vector3 direction = (outward + Random.insideUnitSphere * 0.7f).normalized;
                Add(new P { kind = Kind.Splinter, pos = at + outward * 0.3f, vel = direction * Random.Range(7f, 20f), axis = Random.onUnitSphere,
                    aspect = Random.Range(0.45f, 0.9f), life = Random.Range(1.4f, 2.6f), size0 = Random.Range(0.35f, 0.9f),
                    color = new Color(0.33f, 0.31f, 0.3f), alpha = 1f, spin = Random.Range(-6f, 6f), seed = Random.value * 10f });
            }
            for (int i = 0; i < 5; i++)
                Add(new P { kind = Kind.Smoke, pos = at + outward * 0.8f + Random.insideUnitSphere, vel = outward * Random.Range(2f, 6f) + Random.insideUnitSphere * 2f,
                    age = -i * 0.03f, life = Random.Range(2.4f, 3.8f), size0 = Random.Range(1.2f, 2f), size1 = Random.Range(6f, 9f),
                    rot = Random.Range(0f, 6.28f), spin = Random.Range(-0.4f, 0.4f), drag = 1.6f, rise = 0.6f,
                    color = new Color(0.55f, 0.52f, 0.5f), alpha = Random.Range(0.45f, 0.65f), seed = Random.value * 10f });
        }

        public void DamageFire(Vector3 at,Vector3 outward,float severity,Vector3 inheritedVelocity)
        {
            float height=Mathf.Lerp(2.2f,3.8f,severity)*Random.Range(.8f,1.1f);
            Vector3 axis=(Vector3.up+wind*.035f).normalized;
            Add(new P{kind=Kind.Flame,pos=at+axis*(height*.42f)+Random.insideUnitSphere*.16f,
                vel=inheritedVelocity+axis*.5f,axis=axis,aspect=.5f,life=Random.Range(.55f,.9f),size0=height,size1=height*1.2f,
                color=new Color(1,.37f,.065f),alpha=.35f,seed=Random.value*10});
            // Нарисованный огонь (лист 5, петля): каждый раз новый язык — разной высоты, со сдвигом вдоль борта и наклоном по ветру;
            // живут внахлёст (~7 на пробоину), поэтому пламя густое и не повторяется.
            Vector3 along=Vector3.Cross(outward,Vector3.up); if(along.sqrMagnitude<1e-4f)along=Vector3.right; along.Normalize();
            float tongue=height*Random.Range(1.2f,2f);
            Vector2 lean=new Vector2(Vector3.Dot(wind,along),0f);
            Add(new P{kind=Kind.Flip,pos=at+outward*.45f+along*Random.Range(-.2f,.2f)*height+Vector3.up*tongue*.42f,
                vel=inheritedVelocity+Vector3.up*.6f,life=Random.Range(1f,1.5f),size0=tongue*.85f,size1=tongue,
                rot=Mathf.Clamp(-lean.x*.04f,-.3f,.3f)+Random.Range(-.08f,.08f),color=Color.white,alpha=.9f,seed=5f+Random.value*.999f});
            Vector3 velocity=inheritedVelocity+Vector3.up*Random.Range(2f,4f)+wind*.18f+outward*.6f;
            Add(new P{kind=Kind.Spark,pos=at+Vector3.up*.4f,vel=velocity,axis=Vector3.up,aspect=.2f,
                life=Random.Range(.65f,1.2f),size0=.22f,size1=.05f,color=new Color(1,.5f,.08f),alpha=.65f});
        }

        /// <summary>Дым из закреплённой на повреждённом корпусе точки; не из летящего ядра.</summary>
        public void DamageSmoke(Vector3 at, float severity)
        {
            Add(new P { kind = Kind.Smoke, pos = at + Random.insideUnitSphere * 0.3f,
                vel = Vector3.up * Random.Range(1.2f, 2.5f), life = Random.Range(2.1f, 3.1f),
                size0 = Random.Range(0.7f, 1.2f), size1 = Mathf.Lerp(3f, 5.5f, severity),
                rot = Random.Range(0f, 6.28f), spin = Random.Range(-0.35f, 0.35f), drag = 0.8f, rise = 1.7f,
                color = new Color(0.22f, 0.2f, 0.19f), alpha = Mathf.Lerp(0.34f, 0.56f, severity), seed = Random.value * 10f });
        }

        // ---------- симуляция ----------

        void Update(){long __pt=PerfTimers.Start();UpdateTimed();PerfTimers.Stop("Эффекты: расчёт",__pt);}
        void UpdateTimed()
        {
            if (Application.isPlaying) Simulate(Time.deltaTime);
        }

        struct Splash { public Vector3 pos; public float scale; }
        readonly List<Splash> splashes = new List<Splash>(32);
        readonly List<Vector4> pendingStone = new List<Vector4>(8);
        readonly List<Vector3> pendingStoneOut = new List<Vector3>(8);
        readonly Vector4[] flashes = new Vector4[MaxFlashes];
        readonly Vector4[] flashColors = new Vector4[MaxFlashes];  // rgb цвет, w радиус
        readonly Light[] lights = new Light[MaxLights];
        int flashCursor;

        void Flash(Vector3 at, float power, float range)
        {
            int slot = flashCursor;
            for (int k = 0; k < MaxFlashes; k++)
            {
                int j = (flashCursor + k) % MaxFlashes;
                if (flashes[j].w < flashes[slot].w) slot = j;
            }
            if (flashes[slot].w > power) return;   // выстрел не вытесняет более сильный взрыв
            flashes[slot] = new Vector4(at.x, at.y, at.z, power);
            flashColors[slot] = new Vector4(1f, 0.48f, 0.16f, range);
            flashCursor = (slot + 1) % MaxFlashes;
            PublishFlashes();
        }

        void PublishFlashes()
        {
            int count = 0;
            int first = -1, second = -1;
            for (int i = 0; i < MaxFlashes; i++)
            {
                if (flashes[i].w < 0.02f) { flashes[i].w = 0f; continue; }
                count++;
                if (first < 0 || flashes[i].w > flashes[first].w) { second = first; first = i; }
                else if (second < 0 || flashes[i].w > flashes[second].w) second = i;
            }
            Shader.SetGlobalFloat("_SkyTideFlashCount", count);
            Shader.SetGlobalVectorArray("_SkyTideFlashes", flashes);
            Shader.SetGlobalVectorArray("_SkyTideFlashColors", flashColors);
            for (int i = 0; i < MaxLights; i++)
            {
                int slot = i == 0 ? first : second;
                if (slot < 0) { if (lights[i] != null) lights[i].enabled = false; continue; }
                if (lights[i] == null)
                {
                    GameObject go = new GameObject("Broadside flash " + i);
                    go.hideFlags = HideFlags.HideAndDontSave;
                    go.transform.SetParent(transform, false);
                    lights[i] = go.AddComponent<Light>();
                    lights[i].type = LightType.Point;
                    lights[i].shadows = LightShadows.None;
                    lights[i].renderMode = LightRenderMode.ForceVertex;
                }
                Vector4 f = flashes[slot], c = flashColors[slot];
                lights[i].transform.position = new Vector3(f.x, f.y, f.z);
                lights[i].color = new Color(c.x, c.y, c.z);
                lights[i].range = c.w;
                lights[i].intensity = Mathf.Min(10f, f.w * 1.6f);
                lights[i].enabled = true;
            }
        }

        /// <summary>Сброс эффектов для изолированных кадров CI; убирает и оставшийся свет.</summary>
        public void Clear()
        {
            ps.Clear();
            splashes.Clear();
            drawn = 0;
            CloudSplashCount = 0;
            for (int i = 0; i < MaxFlashes; i++) flashes[i] = Vector4.zero;
            PublishFlashes();
        }

        /// <summary>Неогненный всплеск: рваная волна, выброс пены вверх и оседание. Обломок даёт маленький клуб.</summary>
        public void CloudSplash(Vector3 at, float scale = 1f)
        {
            if (scale >= 0.7f) { CloudSplashCount++; LastCloudSplash = at; SkySound.CloudMiss(at); }
            Color tint = volumetric != null ? Color.Lerp(volumetric.ambientTop, volumetric.sunGold, 0.35f) : new Color(0.62f, 0.5f, 0.55f);
            tint = Color.Lerp(tint, new Color(0.7f, 0.65f, 0.68f), 0.4f);
            // Лист 3 (нарисованный всплеск) не используется: на облаках читался как розовый кристалл. Всплеск — процедурный.
            if (scale >= 0.7f)
                Add(new P { kind = Kind.Wave, pos = at + Vector3.up * 1.3f, surfaceY = at.y, life = 1.35f,
                            size0 = 3f, size1 = 48f * scale, color = tint, alpha = 0.38f, seed = Random.value * 10f });
            int count = scale < 0.7f ? 3 : 10;
            for (int k = 0; k < count; k++)
            {
                float a = k * 2.39996f + Random.Range(-0.3f, 0.3f);
                Vector3 side = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                bool column = k < 3;
                Add(new P { kind = Kind.Foam, pos = at + side * Random.Range(0.2f, 2f) * scale + Vector3.up * (1f + k * 0.15f), surfaceY = at.y,
                            vel = side * (column ? 2f : 7f) * scale + Vector3.up * (column ? 23f : 9f) * scale,
                            life = Random.Range(1.8f, 2.6f), size0 = Random.Range(2f, 3.5f) * scale, size1 = Random.Range(8f, 12f) * scale,
                            rot = Random.Range(0f, 6.28f), spin = Random.Range(-0.5f, 0.5f), drag = 0.9f,
                            color = tint, alpha = column ? 0.62f : 0.45f, seed = Random.value * 10f });
            }
        }

        public void Simulate(float dt)
        {
            if (dt <= 0f) return;
            for (int i = 0; i < MaxFlashes; i++) flashes[i].w *= Mathf.Exp(-dt * 18f);
            int n = ps.Count;
            for (int i = 0; i < n; i++)
            {
                P p = ps[i];
                p.age += dt;
                if (p.age < 0f || p.age >= p.life) { ps[i] = p; continue; }
                if (p.kind == Kind.Ball || p.kind == Kind.Spark || p.kind == Kind.Splinter)
                {
                    Vector3 prev = p.pos;
                    p.vel.y -= gravity * dt;
                    p.pos += p.vel * dt;
                    if (p.kind == Kind.Spark) p.axis = p.vel.normalized;
                    if (p.kind == Kind.Splinter) p.rot += p.spin * dt;
                    Vector3 cloudPoint;
                    bool entered = TryCloudEntry(prev, p.pos, out cloudPoint);
                    Vector3 end = entered ? cloudPoint : p.pos;
                    // Попадание до входа в облака: ядро исчезает именно на корпусе/поверхности.
                    bool hit = false;
                    if (p.kind == Kind.Ball)
                    {
                        TargetShip target = null;
                        Vector3 hitPoint = end;
                        float nearest = float.MaxValue;
                        if (p.hostile)
                        {
                            Vector3 hp; PlayerHull ph = PlayerHull.Instance;
                            if (ph != null && ph.TryHit(prev, end, out hp)) { end = hp; hit = true; ph.OnHit(hp); }
                        }
                        else for (int j = 0; j < TargetShip.All.Count; j++)
                        {
                            Vector3 hp;
                            if (TargetShip.All[j].TryHit(prev, end, out hp) && (hp - prev).sqrMagnitude < nearest)
                            {
                                nearest = (hp - prev).sqrMagnitude;
                                hitPoint = hp;
                                target = TargetShip.All[j];
                            }
                        }
                        if (target != null) { end = hitPoint; hit = true; target.OnHit(hitPoint); }
                        // Крепость: ядро не пролетает сквозь стены. Своё ядро крепости первую секунду не проверяется (дуло на башне).
                        SkyFortress fort = SkyFortress.Instance;
                        Vector3 fp;
                        if (!hit && fort != null && (!p.hostile || p.age > 1f) && fort.TryHit(prev, end, out fp))
                        {
                            end = fp; hit = true;
                            Vector3 outward = fp - fort.Center; outward.y = Mathf.Abs(outward.y) * 0.3f + 20f;
                            pendingStone.Add(new Vector4(fp.x, fp.y, fp.z, 0f)); pendingStoneOut.Add(outward.normalized);
                        }
                    }
                    p.pos = end;
                    if (entered || hit)
                    {
                        p.life = 0f;
                        if (!hit && p.kind != Kind.Spark) splashes.Add(new Splash { pos = p.pos, scale = p.kind == Kind.Ball ? 1f : 0.32f });
                    }
                    if (volumetric != null && p.pos.y < volumetric.deckBase) p.life = 0f;
                }
                else if (p.kind == Kind.Foam)
                {
                    p.vel *= Mathf.Exp(-p.drag * dt);
                    p.vel.y -= gravity * 0.65f * dt;
                    p.pos += (p.vel + wind * 0.35f) * dt;
                    p.rot += p.spin * dt;
                }
                else if (p.kind == Kind.Flip)
                {
                    p.vel *= Mathf.Exp(-p.drag * dt);
                    p.vel.y += p.rise * dt;
                    p.pos += (p.vel + (p.drag > 0f ? wind * 0.5f : Vector3.zero)) * dt;
                    p.rot += p.spin * dt;
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
            for (int i = 0; i < splashes.Count; i++) CloudSplash(splashes[i].pos, splashes[i].scale);
            splashes.Clear();
            for (int i = 0; i < pendingStone.Count; i++) StoneImpact(pendingStone[i], pendingStoneOut[i]);
            pendingStone.Clear(); pendingStoneOut.Clear();
            PublishFlashes();
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

        void LateUpdate(){long __pt=PerfTimers.Start();LateUpdateTimed();PerfTimers.Stop("Эффекты: сетка",__pt);}
        void LateUpdateTimed()
        {
            Camera cam = Camera.main;
            if (cam != null) PrepareForRender(cam);
        }

        /// <summary>Собрать меш под камеру; в обычном режиме — поставить в очередь отрисовки.</summary>
        [System.NonSerialized] public bool debugHidden;   // отладочная панель: эффекты не рисуются (симуляция идёт)
        public void PrepareForRender(Camera cam)
        {
            if (debugHidden || !Ensure()) return;
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
            if (debugHidden || drawn == 0 || !Ensure()) return;
            SetCommon(cam);
            mat.SetFloat("_FxExternal", 1f);
            mat.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always);
            mat.SetMatrix("_FxVP", GL.GetGPUProjectionMatrix(cam.projectionMatrix, target != null) * cam.worldToCameraMatrix);
            RenderTexture.active = target;
            if (mat.SetPass(0)) Graphics.DrawMeshNow(mesh, Matrix4x4.identity);
        }

        Texture2D flipTex; bool flipTried;
        void SetCommon(Camera cam)
        {
            if (!aimTried) { aimTried = true; aimTex = Resources.Load<Texture2D>("UI/aim_kit"); }
            if (aimTex != null) mat.SetTexture("_FxAimTex", aimTex);
            if (!flipTried) { flipTried = true; flipTex = Resources.Load<Texture2D>("FX/fx_flipbooks"); }
            mat.SetTexture("_FxFlipTex", flipTex != null ? flipTex : Texture2D.blackTexture);
            mat.SetVector("_FxRight", cam.transform.right);
            mat.SetVector("_FxUp", cam.transform.up);
            mat.SetMatrix("_FxCamVP", cam.projectionMatrix * cam.worldToCameraMatrix);
            mat.SetMatrix("_FxCamV", cam.worldToCameraMatrix);
            float appearance = 1f;
#if UNITY_EDITOR
            if (legacyAppearanceCapture) appearance = 0f;
#endif
            mat.SetVector("_FxParams", new Vector4(glowIntensity, smokeLight, deckTop, appearance));
        }

        // ---------- прицельная дуга: вид 9 в Fx.shader, атлас Resources/UI/aim_kit (1254²) ----------
        Texture2D aimTex; bool aimTried;
        public bool HasAimArt { get { if (!aimTried) { aimTried = true; aimTex = Resources.Load<Texture2D>("UI/aim_kit"); } return aimTex != null; } }

        readonly List<P> preview = new List<P>(160);
        /// <summary>Точки прицельной дуги: живут один кадр, не симулируются (Broadside очищает и заполняет каждый кадр).</summary>
        public void ClearPreview() { preview.Clear(); }
        public void AddPreview(Vector3 pos, float size, Color c)
        {
            preview.Add(new P { kind = Kind.Glow, pos = pos, life = 1f, size0 = size, size1 = size, color = c, alpha = c.a });
        }

        /// <summary>Элемент прицельного набора (UI/aim_kit): 0/1 — штрих янтарь/серебро вдоль axis, 2 — кольцо на облаках,
        /// 3 — прицел на корпусе, 4 — шеврон у дула. aspect — ширина/длина для штрихов.</summary>
        public void AddAim(Vector3 pos, Vector3 axis, float size, int element, float aspect, float alpha)
        {
            preview.Add(new P { kind = Kind.Aim, pos = pos, axis = axis.normalized, aspect = aspect, life = 1f, size0 = size, size1 = size,
                                color = Color.white, alpha = alpha, seed = element });
        }

        void BuildMesh(Camera cam)
        {
            int baseCount = ps.Count;
            ps.AddRange(preview);
            try { BuildMeshCore(cam); }
            finally { ps.RemoveRange(baseCount, ps.Count - baseCount); }
        }

        void BuildMeshCore(Camera cam)
        {
            int n = ps.Count;
            drawn = n;
            if (verts.Length < n * 4)
            {
                int cap = Mathf.NextPowerOfTwo(Mathf.Max(n, 64));
                verts = new Vector3[cap * 4];
                uv0 = new Vector2[cap * 4];
                uv1 = new Vector4[cap * 4];
                uv2 = new Vector4[cap * 4];
                uv3 = new Vector4[cap * 4];
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
                if (p.kind == Kind.Smoke || p.kind == Kind.Foam)
                {
                    size = Mathf.Lerp(p.size0, p.size1, 1f - (1f - t) * (1f - t));
                    a = p.alpha * Mathf.Clamp01(p.age / 0.08f) * (1f - t) * (1f - t);
                }
                else if (p.kind == Kind.Glow || p.kind == Kind.Muzzle || p.kind == Kind.Spark || p.kind == Kind.Flame)
                {
                    size = Mathf.Lerp(p.size0, p.size1, t);
                    a = p.alpha * (1f - t);
                }
                else if (p.kind == Kind.Flip)
                {
                    int sheet = (int)p.seed;
                    size = Mathf.Lerp(p.size0, p.size1, 1f - (1f - t) * (1f - t));
                    if (sheet == 5) a = p.alpha * Mathf.Clamp01(p.age / 0.12f) * Mathf.Clamp01((1f - t) / 0.3f);
                    else if (sheet == 2 || sheet == 6) a = p.alpha * Mathf.Clamp01(p.age / 0.15f) * (1f - t);
                    else a = p.alpha * Mathf.Clamp01((1f - t) / 0.1f);   // кадры листа сами гаснут
                }
                else if (p.kind == Kind.Wave)
                {
                    size = Mathf.Lerp(p.size0, p.size1, 1f - (1f - t) * (1f - t));
                    a = p.alpha * Mathf.Clamp01(p.age / 0.06f) * (1f - t) * (1f - t);
                }
                else
                {
                    size = p.size0;
                    a = p.alpha * Mathf.Clamp01((1f - t) / 0.15f);
                }
                if (p.age < 0f) a = 0f;
                Color c = p.color;
                c.a = a;
                Vector4 d = new Vector4(size, p.rot, (float)p.kind, p.seed);
                Vector4 axis = new Vector4(p.axis.x, p.axis.y, p.axis.z, p.aspect);
                Vector4 anim = new Vector4(Mathf.Max(p.age, 0f), t, p.kind == Kind.Foam || p.kind == Kind.Wave || (p.kind == Kind.Flip && (int)p.seed == 3) ? p.surfaceY : deckTop, 0f);
                int v = j * 4;
                for (int q = 0; q < 4; q++)
                {
                    verts[v + q] = p.pos;
                    uv1[v + q] = d;
                    uv2[v + q] = axis;
                    uv3[v + q] = anim;
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
            mesh.SetUVs(2, uv2);
            mesh.SetUVs(3, uv3);
            mesh.colors = cols;
            mesh.triangles = tris;
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);
        }

        void OnDestroy()
        {
            if (Instance == this) { Clear(); Instance = null; }
            for (int i = 0; i < MaxLights; i++) if (lights[i] != null) SkyTideBootstrap.SafeDestroy(lights[i].gameObject);
            if (mat != null) SkyTideBootstrap.SafeDestroy(mat);
            if (mesh != null) SkyTideBootstrap.SafeDestroy(mesh);
        }
    }
}
