using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SkyTide
{
    /// <summary>Модель строения (tools/convert_props.py, модели Meshy): полная, ближняя (35%) и дальняя (8%) копии.</summary>
    [Serializable]
    public class PropAsset
    {
        public string id;
        public TextAsset mesh, lite, far;
        public Material material;
    }

    /// <summary>
    /// Строения на карте (владелец 16.11: «маяк, сторожевые башни, ещё какие-нибудь строения… и расставил их по карте»).
    /// Ставятся по крепости и границам вод (Relations): маяк — перед причалом, сбоку от подхода; четыре сторожевые башни —
    /// по краю вод гарнизона; склад — за крепостью; причальная мачта с дирижаблем — в водах гарнизона; два обломка — в
    /// нейтральной полосе; буи — цепью по границе вод корсаров; два логова — в водах корсаров.
    /// Корабли их обходят (Traffic.Avoid) и не проходят насквозь (Traffic.Tick), ядра о них разбиваются (FxSystem).
    /// Корпус строения для столкновений — несколько вертикальных цилиндров (круг в плане + высота).
    /// </summary>
    public class WorldProps : MonoBehaviour
    {
        public struct Solid { public Vector3 c; public float r, y0, y1; }
        public static readonly List<Solid> Solids = new List<Solid>();
        public static WorldProps Instance { get; private set; }

        // Габариты моделей (м, после convert_props.py; низ модели — y = 0):
        //   shipY — какая высота модели приходится на высоту кораблей; circles — (x, z, радиус) в плане модели.
        struct Def { public float shipY; public Vector3[] circles; public float lantern; }
        static Def DefOf(string id)
        {
            switch (id)
            {
                case "lighthouse": return new Def { shipY = 41f, lantern = 104f, circles = new[] { new Vector3(0f, 0f, 21f) } };
                case "watchtower": return new Def { shipY = 12f, lantern = 46f, circles = new[] { new Vector3(6f, -1f, 13f), new Vector3(-14f, 0f, 5f) } };
                case "buoy": return new Def { shipY = 11f, lantern = 21f, circles = new[] { new Vector3(0f, 0f, 4f) } };
                case "hideout": return new Def { shipY = 42f, lantern = 0f, circles = new[] { new Vector3(0f, 0f, 27f) } };
                case "wreck": return new Def { shipY = 30f, lantern = 0f, circles = new[] { new Vector3(-26f, 0f, 13f), new Vector3(0f, 0f, 14f), new Vector3(26f, 0f, 13f) } };
                case "depot": return new Def { shipY = 20f, lantern = 0f, circles = new[] { new Vector3(-20f, -12f, 21f), new Vector3(14f, -12f, 21f), new Vector3(-19f, 22f, 18f), new Vector3(42f, 22f, 9f) } };
                case "mooringmast": return new Def { shipY = 95f, lantern = 108f, circles = new[] { new Vector3(-38f, 0f, 7f), new Vector3(18f, 0f, 8f), new Vector3(33f, 0f, 8f), new Vector3(47f, 0f, 6f) } };
            }
            return new Def { shipY = 0f, circles = new Vector3[0] };
        }

        struct Lamp { public Vector3 pos; public float size; public Color color; public float speed; public bool beacon; }
        readonly List<Lamp> lampList = new List<Lamp>();
        Light beaconLight;

        public static WorldProps Create(PropAsset[] assets, SkyFortress fort, Vector3 spawn)
        {
            Solids.Clear();
            if (assets == null || assets.Length == 0 || fort == null) return null;
            WorldProps w = new GameObject("World Props").AddComponent<WorldProps>();
            Instance = w;
            Dictionary<string, PropAsset> byId = new Dictionary<string, PropAsset>();
            foreach (PropAsset a in assets) if (a != null && a.mesh != null && a.material != null) byId[a.id] = a;

            Vector3 F = fort.Center; F.y = 0f;
            Vector3 O = spawn; O.y = 0f;
            Vector3 u = O - F; if (u.sqrMagnitude < 1f) u = Vector3.forward; u.Normalize();   // от крепости к середине карты
            float ua = Mathf.Atan2(u.z, u.x) * Mathf.Rad2Deg;
            Vector3 outv = fort.BerthWorld - fort.Center; outv.y = 0f; outv = outv.sqrMagnitude > 1f ? outv.normalized : u;
            Vector3 across = new Vector3(-outv.z, 0f, outv.x);
            float G = Relations.GarrisonRadius, C = Relations.CorsairRadius, y = spawn.y;
            Func<float, float, Vector3> Polar = (deg, r) => { float a = (ua + deg) * Mathf.Deg2Rad; return F + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r; };

            // Маяк — перед причалом, сбоку от подхода (не на пути к зоне ремонта).
            w.Place(byId, "lighthouse", fort.BerthWorld + outv * 650f + across * 280f, y, outv);
            // Сторожевые башни по краю вод гарнизона.
            foreach (float deg in new[] { -70f, -25f, 25f, 70f }) w.Place(byId, "watchtower", Polar(deg, G - 60f), y, F - Polar(deg, G));
            // Склад — за крепостью.
            w.Place(byId, "depot", fort.Center - outv * (fort.BoundRadius + 140f), y, across);
            // Причальная мачта с дирижаблем — в водах гарнизона, в стороне от точки появления.
            w.Place(byId, "mooringmast", Polar(48f, 1650f), y, Quaternion.Euler(0f, 30f, 0f) * u);
            // Обломки — в нейтральной полосе.
            w.Place(byId, "wreck", Polar(-14f, (G + C) * 0.5f), y, Quaternion.Euler(0f, 70f, 0f) * u);
            w.Place(byId, "wreck", Polar(58f, (G + C) * 0.5f + 80f), y, Quaternion.Euler(0f, -40f, 0f) * u);
            // Буи — цепью по границе вод корсаров (только в пределах карты).
            for (float deg = -60f; deg <= 60.1f; deg += 20f)
            {
                Vector3 p = Polar(deg, C);
                if (new Vector2(p.x - O.x, p.z - O.z).magnitude < 3200f) w.Place(byId, "buoy", p, y, u);
            }
            // Логова — в водах корсаров.
            w.Place(byId, "hideout", Polar(-32f, C + 700f), y, -u);
            w.Place(byId, "hideout", Polar(30f, C + 650f), y, Quaternion.Euler(0f, 120f, 0f) * u);
            return w;
        }

        void Place(Dictionary<string, PropAsset> byId, string id, Vector3 at, float shipLevel, Vector3 face)
        {
            PropAsset a;
            if (!byId.TryGetValue(id, out a)) return;
            Def d = DefOf(id);
            face.y = 0f; if (face.sqrMagnitude < 1e-4f) face = Vector3.forward;
            GameObject go = new GameObject("Prop " + id);
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(at.x, shipLevel - d.shipY, at.z);
            go.transform.rotation = Quaternion.LookRotation(face.normalized, Vector3.up);
            go.isStatic = true;

            Mesh full = ShipMeshLoader.Load(a.mesh), lite = ShipMeshLoader.Load(a.lite), far = ShipMeshLoader.Load(a.far);
            if (full == null) { Destroy(go); return; }
            float height = full.bounds.size.y;
            Renderer r0 = Part(go.transform, "LOD0", full, a.material, ShadowCastingMode.Off);
            Renderer r0s = lite != null ? Part(go.transform, "LOD0 shadow", lite, a.material, ShadowCastingMode.ShadowsOnly) : null;
            Renderer r1 = lite != null ? Part(go.transform, "LOD1", lite, a.material, ShadowCastingMode.On) : null;
            Renderer r2 = far != null ? Part(go.transform, "LOD2", far, a.material, ShadowCastingMode.Off) : null;
            LODGroup g = go.AddComponent<LODGroup>();
            List<LOD> lods = new List<LOD>();
            lods.Add(new LOD(0.30f, r0s != null ? new[] { r0, r0s } : new[] { r0 }));
            if (r1 != null) lods.Add(new LOD(0.08f, new[] { r1 }));
            if (r2 != null) lods.Add(new LOD(0.006f, new[] { r2 }));
            g.SetLODs(lods.ToArray());
            g.RecalculateBounds();

            foreach (Vector3 c in d.circles)
            {
                Vector3 wc = go.transform.TransformPoint(new Vector3(c.x, 0f, c.y));
                Solids.Add(new Solid { c = new Vector3(wc.x, 0f, wc.z), r = c.z, y0 = go.transform.position.y, y1 = go.transform.position.y + height });
            }
            if (d.lantern > 0f)
            {
                Vector3 lp = go.transform.position + Vector3.up * d.lantern;
                bool beacon = id == "lighthouse";
                lampList.Add(new Lamp { pos = lp, beacon = beacon, size = beacon ? 9f : id == "buoy" ? 2.2f : 4f,
                    speed = beacon ? 1.1f : id == "buoy" ? 2.4f : 0.7f,
                    color = id == "buoy" ? new Color(1f, 0.32f, 0.18f) : new Color(1f, 0.78f, 0.42f) });
                if (beacon)
                {
                    beaconLight = new GameObject("Lighthouse Light").AddComponent<Light>();
                    beaconLight.transform.SetParent(go.transform, false);
                    beaconLight.transform.position = lp;
                    beaconLight.type = LightType.Point;
                    beaconLight.color = new Color(1f, 0.78f, 0.45f);
                    beaconLight.range = 70f;
                    beaconLight.intensity = 2f;
                    beaconLight.shadows = LightShadows.None;
                    beaconLight.renderMode = LightRenderMode.ForceVertex;
                }
            }
        }

        static Renderer Part(Transform parent, string name, Mesh m, Material mat, ShadowCastingMode shadows)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            MeshRenderer r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = shadows;
            r.receiveShadows = shadows != ShadowCastingMode.ShadowsOnly;
            return r;
        }

        // Огни строений: ореолы видны издалека (слоты FxSystem 30–39; 0–28 — крепость, 40–95 — её кольцо).
        void LateUpdate()
        {
            FxSystem fx = FxSystem.Instance;
            Camera cam = Camera.main;
            if (fx == null || cam == null) return;
            Vector3 eye = cam.transform.position;
            float t = Time.time;
            int slot = 30;
            for (int i = 0; i < lampList.Count && slot < 40; i++)
            {
                Lamp l = lampList[i];
                float d = Vector3.Distance(eye, l.pos);
                if (!l.beacon && d > 1800f) continue;
                // Маяк — медленная вспышка раз в ~5,7 с; башни и буи — ровное мерцание.
                float pulse = l.beacon ? 0.35f + 0.65f * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(t * l.speed)), 6f)
                                       : 0.75f + 0.25f * Mathf.Sin(t * l.speed + i * 1.3f);
                Color c = l.color; c.a = (l.beacon ? 0.16f : 0.09f) * pulse * Mathf.Clamp01((d - 60f) / 300f);
                fx.SetLamp(slot++, l.pos, Mathf.Clamp(d * (l.beacon ? 0.022f : 0.012f), l.size, l.beacon ? 70f : 26f), c);
                if (l.beacon && beaconLight != null) beaconLight.intensity = 0.6f + 2.2f * pulse;
            }
        }

        /// <summary>Ядро на отрезке a→b входит в строение? Точка входа — первое пересечение с цилиндрами.</summary>
        public static bool TryHit(Vector3 a, Vector3 b, out Vector3 hit)
        {
            hit = b;
            bool found = false; float best = float.MaxValue;
            for (int i = 0; i < Solids.Count; i++)
            {
                Solid s = Solids[i];
                float dx = b.x - s.c.x, dz = b.z - s.c.z, rr = s.r + 40f;
                if (dx * dx + dz * dz > rr * rr) continue;
                Vector2 p0 = new Vector2(a.x - s.c.x, a.z - s.c.z), dd = new Vector2(b.x - a.x, b.z - a.z);
                float A = Vector2.Dot(dd, dd), B = 2f * Vector2.Dot(p0, dd), Cc = Vector2.Dot(p0, p0) - s.r * s.r;
                float tt;
                if (Cc <= 0f) tt = 0f;
                else
                {
                    if (A < 1e-6f) continue;
                    float disc = B * B - 4f * A * Cc;
                    if (disc < 0f) continue;
                    tt = (-B - Mathf.Sqrt(disc)) / (2f * A);
                    if (tt < 0f || tt > 1f) continue;
                }
                Vector3 q = Vector3.Lerp(a, b, tt);
                if (q.y < s.y0 || q.y > s.y1) continue;
                if (tt < best) { best = tt; hit = q; found = true; }
            }
            return found;
        }

        /// <summary>Центр ближайшего строения к точке (для наружного направления осколков).</summary>
        public static Vector3 Nearest(Vector3 p)
        {
            Vector3 best = p; float bd = float.MaxValue;
            foreach (Solid s in Solids) { float d = (new Vector3(p.x, 0f, p.z) - s.c).sqrMagnitude; if (d < bd) { bd = d; best = new Vector3(s.c.x, p.y, s.c.z); } }
            return best;
        }

        void OnDestroy() { if (Instance == this) { Instance = null; Solids.Clear(); } }
    }
}
