using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SkyTide
{
    /// <summary>
    /// Облегчённая копия корабля (tools/decimate_ship.py, quadric edge collapse): тень-заместитель и дальний LOD.
    /// Полная модель остаётся в MeshFilter — попадания, пробоины и CI-проверки идут по ней; меняется только то,
    /// что рисуется. Тень рисует облегчённая копия (тень на 160 м в 2 каскадах не различает 438k и 70k треугольников,
    /// а по замеру треугольники с тенями — ~2 млн за кадр при бюджете ~0.5 млн вершин из блога Unity+Arm).
    /// Дальше lodDistance копия рисуется и в цвете. Цвета вершин (веса ветра парусов, номер ствола) переносятся
    /// с исходных вершин (_src.bytes), так что паруса в тени тоже колышутся.
    /// </summary>
    public class ShipLod : MonoBehaviour
    {
        /// <summary>Переключатель в панели FPS: выкл — как раньше, полная модель везде.</summary>
        public static bool Enabled = true;
        static readonly List<ShipLod> all = new List<ShipLod>();

        MeshRenderer full, lite, farR;
        float lodDistance, farDistance;

        /// <summary>Цвета вершин полной модели, снятые до UploadMeshData (сетка потом нечитаема).</summary>
        public static readonly Dictionary<Mesh, Color32[]> SourceColors = new Dictionary<Mesh, Color32[]>();

        static void MapColors(Mesh fullMesh, Mesh m, int[] src)
        {
            if (m == null || !m.isReadable) return;
            Color32[] fc;
            if (!SourceColors.TryGetValue(fullMesh, out fc)) fc = fullMesh.isReadable ? fullMesh.colors32 : null;
            if (src != null && src.Length == m.vertexCount && fc != null && fc.Length == fullMesh.vertexCount)
            {
                Color32[] lc = new Color32[src.Length];
                for (int i = 0; i < src.Length; i++) { int s = src[i]; lc[i] = (uint)s < (uint)fc.Length ? fc[s] : new Color32(0, 128, 128, 0); }
                m.colors32 = lc;
            }
            else if (fc != null && fc.Length > 0)
            {
                // без карты — нейтральные цвета (без ветра и отката), иначе шейдер ткани качал бы весь корпус
                Color32[] lc = new Color32[m.vertexCount];
                for (int i = 0; i < lc.Length; i++) lc[i] = new Color32(0, 128, 128, 0);
                m.colors32 = lc;
            }
            // Сетка общая для всех кораблей типа: цвета один раз, затем выгрузка.
            m.UploadMeshData(true);
        }

        static MeshRenderer Copy(MeshRenderer full, Mesh m, Material[] materials, string name)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(full.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            MeshRenderer r = go.AddComponent<MeshRenderer>();
            Material[] slots = new Material[m.subMeshCount];
            for (int i = 0; i < slots.Length; i++) slots[i] = materials[Mathf.Min(i, materials.Length - 1)];
            r.sharedMaterials = slots;
            r.receiveShadows = full.receiveShadows;
            return r;
        }

        public static void Attach(MeshRenderer full, Mesh fullMesh, Mesh liteMesh, int[] src, Material[] materials, float lodDistance,
            Mesh farMesh = null, int[] farSrc = null, float farDistance = 0f)
        {
            if (full == null || fullMesh == null || liteMesh == null) return;
            MapColors(fullMesh, liteMesh, src);
            MapColors(fullMesh, farMesh, farSrc);
            MeshRenderer r = Copy(full, liteMesh, materials, "Lite (shadow/LOD)");
            r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            MeshRenderer fr = null;
            if (farMesh != null && farDistance > 0f) { fr = Copy(full, farMesh, materials, "Far LOD"); fr.shadowCastingMode = ShadowCastingMode.Off; fr.forceRenderingOff = true; }
            ShipLod l = full.gameObject.AddComponent<ShipLod>();
            l.full = full; l.lite = r; l.lodDistance = lodDistance; l.farR = fr; l.farDistance = farDistance;
            l.Apply(false, false, true);
        }
        void OnEnable() { all.Add(this); if (all.Count == 1) Camera.onPreCull += PreCull; }
        void OnDisable() { all.Remove(this); if (all.Count == 0) Camera.onPreCull -= PreCull; }

        static void PreCull(Camera cam)
        {
            if (cam == null || cam.cameraType == CameraType.Preview) return;
            Vector3 c = cam.transform.position;
            // Подзорная труба (обзор ×0.28): корабль на экране как в 3.5 раза ближе — уровень детализации по размеру на экране,
            // а не по метрам (снимок «труба» 15.11: при 15° дальние шли полными моделями, 3 FPS).
            float zoom = Mathf.Clamp(cam.fieldOfView / 55f, 0.1f, 2f), z2 = zoom * zoom;
            for (int i = 0; i < all.Count; i++)
            {
                ShipLod l = all[i];
                if (l == null || l.full == null) continue;
                float d2 = (l.transform.position - c).sqrMagnitude * z2;
                bool far = l.lodDistance > 0f && d2 > l.lodDistance * l.lodDistance;
                float sd = QualitySettings.shadowDistance + 60f;
                float cs = Mathf.Max(1f, l.lodDistance / 90f);   // крупные классы (адмиральский 260 м) видно дальше — по размеру
                l.Apply(far, d2 > CullDistance * CullDistance * cs * cs, d2 < sd * sd, l.farR != null && d2 > l.farDistance * l.farDistance * FarScale * FarScale);
            }
        }

        /// <summary>Дальше этого корабль не рисуется вовсе (в дымке дальше 2.2 км — точка в пару пикселей, а лёгкая модель —
        /// 60 тыс. треугольников). На радаре и в логике он есть. Владелец 15.11 поднял врагов до 18 — без этого дальние ели ВК.</summary>
        public static float CullDistance = 2200f;

        /// <summary>Множитель дальности третьего уровня (команда set far N).</summary>
        public static float FarScale = 1f;

        void Apply(bool far, bool cull, bool shadowNear, bool veryFar = false)
        {
            if (lite == null) return;
            if (cull)
            {
                full.forceRenderingOff = true; lite.forceRenderingOff = true; if (farR != null) farR.forceRenderingOff = true;
                return;
            }
            bool useFar = veryFar && Enabled && farR != null;
            if (farR != null) farR.forceRenderingOff = !useFar;
            if (useFar)
            {
                // Третий уровень: только он, без тени (дальше дальности теней их всё равно нет).
                full.forceRenderingOff = true; full.shadowCastingMode = ShadowCastingMode.Off;
                lite.forceRenderingOff = true; lite.enabled = true;
                return;
            }
            lite.forceRenderingOff = false;
            if (!Enabled)
            {
                full.forceRenderingOff = cull; full.shadowCastingMode = ShadowCastingMode.On; lite.enabled = false;
                return;
            }
            lite.enabled = true;
            // forceRenderingOff, а не enabled: границы полной модели (по ним ShipDamageVisual ловит уход под облака) остаются живыми
            full.forceRenderingOff = far;
            full.shadowCastingMode = ShadowCastingMode.Off;
            // Тень дальше дальности теней не попадает ни в один каскад — но объект всё равно проходит отбор теней. Выключаем.
            lite.shadowCastingMode = far ? (shadowNear ? ShadowCastingMode.On : ShadowCastingMode.Off) : ShadowCastingMode.ShadowsOnly;
        }

        /// <summary>Карта исходных вершин (int32 подряд) из tools/decimate_ship.py.</summary>
        public static int[] ReadSrc(TextAsset a)
        {
            if (a == null) return null;
            byte[] b = a.bytes;
            int[] r = new int[b.Length / 4];
            System.Buffer.BlockCopy(b, 0, r, 0, r.Length * 4);
            return r;
        }
    }
}
