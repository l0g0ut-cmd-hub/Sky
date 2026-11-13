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

        MeshRenderer full, lite;
        float lodDistance;

        public static void Attach(MeshRenderer full, Mesh fullMesh, Mesh liteMesh, int[] src, Material[] materials, float lodDistance)
        {
            if (full == null || fullMesh == null || liteMesh == null) return;
            if (src != null && src.Length == liteMesh.vertexCount && fullMesh.colors32 != null && fullMesh.colors32.Length == fullMesh.vertexCount)
            {
                Color32[] fc = fullMesh.colors32, lc = new Color32[src.Length];
                for (int i = 0; i < src.Length; i++) { int s = src[i]; lc[i] = (uint)s < (uint)fc.Length ? fc[s] : new Color32(0, 128, 128, 0); }
                liteMesh.colors32 = lc;
            }
            else if (fullMesh.colors32 != null && fullMesh.colors32.Length > 0)
            {
                // без карты — нейтральные цвета (без ветра и отката), иначе шейдер ткани качал бы весь корпус
                Color32[] lc = new Color32[liteMesh.vertexCount];
                for (int i = 0; i < lc.Length; i++) lc[i] = new Color32(0, 128, 128, 0);
                liteMesh.colors32 = lc;
            }
            GameObject go = new GameObject("Lite (shadow/LOD)");
            go.transform.SetParent(full.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = liteMesh;
            MeshRenderer r = go.AddComponent<MeshRenderer>();
            Material[] slots = new Material[liteMesh.subMeshCount];
            for (int i = 0; i < slots.Length; i++) slots[i] = materials[Mathf.Min(i, materials.Length - 1)];
            r.sharedMaterials = slots;
            r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            r.receiveShadows = full.receiveShadows;
            ShipLod l = full.gameObject.AddComponent<ShipLod>();
            l.full = full; l.lite = r; l.lodDistance = lodDistance;
            l.Apply(false);
        }

        void OnEnable() { all.Add(this); if (all.Count == 1) Camera.onPreCull += PreCull; }
        void OnDisable() { all.Remove(this); if (all.Count == 0) Camera.onPreCull -= PreCull; }

        static void PreCull(Camera cam)
        {
            if (cam == null || cam.cameraType == CameraType.Preview) return;
            Vector3 c = cam.transform.position;
            for (int i = 0; i < all.Count; i++)
            {
                ShipLod l = all[i];
                if (l == null || l.full == null) continue;
                bool far = l.lodDistance > 0f && (l.transform.position - c).sqrMagnitude > l.lodDistance * l.lodDistance;
                l.Apply(far);
            }
        }

        void Apply(bool far)
        {
            if (lite == null) return;
            if (!Enabled)
            {
                full.forceRenderingOff = false; full.shadowCastingMode = ShadowCastingMode.On; lite.enabled = false;
                return;
            }
            lite.enabled = true;
            // forceRenderingOff, а не enabled: границы полной модели (по ним ShipDamageVisual ловит уход под облака) остаются живыми
            full.forceRenderingOff = far;
            full.shadowCastingMode = ShadowCastingMode.Off;
            lite.shadowCastingMode = far ? ShadowCastingMode.On : ShadowCastingMode.ShadowsOnly;
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
