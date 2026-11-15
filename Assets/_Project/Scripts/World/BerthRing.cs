using UnityEngine;

namespace SkyTide
{
    /// <summary>
    /// Точка ремонта у причала — светящееся кольцо по радиусу зоны ремонта (berthRadius), а не огонёк-звезда
    /// (владелец 15.11: «должна быть кольцом и больше»). Шейдер — тот же, что у стены безопасной зоны.
    /// Янтарное, мягко пульсирует; когда корабль стоит в кольце и чинится — зеленеет.
    /// </summary>
    public class BerthRing : MonoBehaviour
    {
        Material mat; float docked;
        static readonly Color Idle = new Color(1f, 0.78f, 0.38f, 0.9f), Repair = new Color(0.45f, 1f, 0.6f, 0.95f);

        public static BerthRing Create(Vector3 centre, float radius)
        {
            Shader sh = Shader.Find("SkyTide/ZoneMist");
            if (sh == null) return null;
            GameObject go = new GameObject("SkyTide.BerthRing");
            BerthRing b = go.AddComponent<BerthRing>();
            const int N = 120; const float low = -8f, high = 14f;
            Vector3[] v = new Vector3[(N + 1) * 2]; Vector2[] uv = new Vector2[v.Length]; int[] tri = new int[N * 6];
            float circ = 2f * Mathf.PI * radius;
            for (int i = 0; i <= N; i++)
            {
                float a = i * Mathf.PI * 2f / N;
                Vector3 p = new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
                v[i * 2] = p + Vector3.up * low; v[i * 2 + 1] = p + Vector3.up * high;
                float u = i / (float)N * circ / 40f;
                uv[i * 2] = new Vector2(u, 0f); uv[i * 2 + 1] = new Vector2(u, 1f);
                if (i < N) { int k = i * 6, q = i * 2; tri[k] = q; tri[k + 1] = q + 1; tri[k + 2] = q + 3; tri[k + 3] = q; tri[k + 4] = q + 3; tri[k + 5] = q + 2; }
            }
            Mesh m = new Mesh { name = "BerthRing" };
            m.vertices = v; m.uv = uv; m.triangles = tri; m.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = m;
            MeshRenderer r = go.AddComponent<MeshRenderer>();
            b.mat = new Material(sh); b.mat.color = Idle;
            r.sharedMaterial = b.mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            go.transform.position = centre;
            return b;
        }

        public void Tick(Vector3 centre, bool visible, bool repairing, float time)
        {
            if (gameObject.activeSelf != visible) gameObject.SetActive(visible);
            if (!visible || mat == null) return;
            docked = Mathf.MoveTowards(docked, repairing ? 1f : 0f, Time.deltaTime * 2f);
            Color c = Color.Lerp(Idle, Repair, docked);
            c.a *= 0.75f + 0.25f * Mathf.Sin(time * 2.4f);
            mat.color = c;
            transform.position = centre;
        }
    }
}
