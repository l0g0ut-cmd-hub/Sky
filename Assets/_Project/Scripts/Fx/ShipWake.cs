using System.Collections.Generic;
using UnityEngine;

namespace SkyTide
{
    /// <summary>След за кормой у всех кораблей на ходу (владелец 17.11, «только красиво»): одна мягкая лента пара
    /// (TrailRenderer + SkyTide/ShipWake), а не клубы — прежний след из клубов читался как пузыри и убран 15.11.
    /// Ширина и длина — по размеру корабля; на малом ходу и дальше 1,5 км от камеры лента не тянется.</summary>
    public class ShipWake : MonoBehaviour
    {
        public static ShipWake Instance { get; private set; }
        public static bool Enabled = true;
        const float LifeSeconds = 3.2f, MaxDist = 1500f;
        class W { public TrailRenderer tr; public Renderer model; public float halfLen, lowY; public float measureAt, alpha = -1f; public Vector3 last; }
        static readonly int ColorId = Shader.PropertyToID("_Color");
        MaterialPropertyBlock mpb;
        readonly Dictionary<ShipController, W> map = new Dictionary<ShipController, W>();
        readonly List<ShipController> drop = new List<ShipController>();
        Material mat;

        void Awake() { Instance = this; mpb = new MaterialPropertyBlock(); }

        void LateUpdate()
        {
            if (mat == null)
            {
                Shader sh = Shader.Find("SkyTide/ShipWake");
                if (sh == null) { enabled = false; return; }
                mat = new Material(sh) { name = "ShipWake" };
            }
            Camera cam = Camera.main;
            Vector3 cp = cam != null ? cam.transform.position : Vector3.zero;
            foreach (ShipController s in ShipController.All)
            {
                if (s == null) continue;
                W w;
                if (!map.TryGetValue(s, out w)) { w = Make(s); map[s] = w; }
                Transform t = s.transform;
                if (Time.time >= w.measureAt) Measure(s, w);
                Vector3 stern = t.position - t.forward * (w.halfLen * 0.92f); stern.y = w.lowY;
                bool visible = s.visual == null || s.visual.gameObject.activeInHierarchy;
                float speed01 = s.maxSpeed > 0f ? s.Speed / s.maxSpeed : 0f;
                bool on = Enabled && visible && speed01 > 0.3f && (stern - cp).sqrMagnitude < MaxDist * MaxDist;
                // Перенос/возрождение: ленту не тянуть через полкарты.
                if ((stern - w.last).sqrMagnitude > 60f * 60f) w.tr.Clear();
                w.last = stern;
                w.tr.transform.position = stern;
                w.tr.emitting = on;
                float a = Mathf.Clamp01((speed01 - 0.3f) / 0.5f);
                if (Mathf.Abs(a - w.alpha) > 0.02f) { w.alpha = a; mpb.SetColor(ColorId, new Color(0.93f, 0.86f, 0.84f, 0.32f * a)); w.tr.SetPropertyBlock(mpb); }
            }
            drop.Clear();
            foreach (KeyValuePair<ShipController, W> kv in map) if (kv.Key == null) drop.Add(kv.Key);
            foreach (ShipController k in drop) { W w = map[k]; if (w.tr != null) Destroy(w.tr.gameObject); map.Remove(k); }
        }

        W Make(ShipController s)
        {
            GameObject go = new GameObject("Wake " + s.name);
            go.transform.SetParent(transform, false);
            TrailRenderer tr = go.AddComponent<TrailRenderer>();
            tr.sharedMaterial = mat;
            tr.time = LifeSeconds;
            tr.minVertexDistance = 4f;
            tr.alignment = LineAlignment.View;
            tr.textureMode = LineTextureMode.Stretch;
            tr.numCapVertices = 2;
            tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tr.receiveShadows = false;
            tr.widthCurve = new AnimationCurve(new Keyframe(0f, 0.35f), new Keyframe(0.35f, 0.85f), new Keyframe(1f, 1.4f));
            Gradient g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f), new GradientAlphaKey(0.55f, 0.5f), new GradientAlphaKey(0f, 1f) });
            tr.colorGradient = g;
            tr.emitting = false;
            W w = new W { tr = tr, halfLen = 15f, lowY = s.transform.position.y, measureAt = 0f, last = s.transform.position };
            return w;
        }

        void Measure(ShipController s, W w)
        {
            w.measureAt = Time.time + 2f;
            float r; Vector2[] hull = Footprint.Of(s.transform, out r);
            if (hull != null && hull.Length > 0) { float min = 0f; foreach (Vector2 p in hull) min = Mathf.Min(min, p.y); w.halfLen = Mathf.Max(6f, -min); }
            else w.halfLen = Mathf.Max(6f, s.HullRadius);
            if (w.model == null && s.visual != null) w.model = s.visual.GetComponentInChildren<MeshRenderer>();
            // Высота ленты — у киля: нижняя четверть габарита (выше — баллоны).
            if (w.model != null) { Bounds b = w.model.bounds; w.lowY = b.min.y + b.size.y * 0.22f; }
            else w.lowY = s.transform.position.y;
            w.tr.widthMultiplier = Mathf.Clamp(w.halfLen * 0.45f, 4f, 40f);
        }
    }
}
