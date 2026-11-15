using UnityEngine;

namespace SkyTide
{
    /// <summary>
    /// Граница безопасной зоны — стена светящегося тумана по кругу safeRadius (выбор владельца 15.11: вариант 4, зелёный).
    /// Вместо кольца из 56 огоньков. Один меш (кольцо из 240 сегментов, высота 70 м), один вызов отрисовки.
    /// При тревоге крепости плавно краснеет.
    /// </summary>
    public class ZoneMist : MonoBehaviour
    {
        Material mat; float red;
        static readonly Color Safe = new Color(0.42f, 1f, 0.58f, 0.85f), Alarm = new Color(1f, 0.32f, 0.22f, 0.95f);

        public static ZoneMist Create(SkyFortress f, float baseY)
        {
            Shader sh = Shader.Find("SkyTide/ZoneMist");
            if (sh == null) return null;
            GameObject go = new GameObject("SkyTide.ZoneMist");
            ZoneMist z = go.AddComponent<ZoneMist>();
            const int N = 240; const float height = 70f;
            Vector3[] v = new Vector3[(N + 1) * 2]; Vector2[] uv = new Vector2[v.Length]; int[] tri = new int[N * 6];
            float circ = 2f * Mathf.PI * f.safeRadius;
            for (int i = 0; i <= N; i++)
            {
                float a = i * Mathf.PI * 2f / N;
                Vector3 p = new Vector3(Mathf.Cos(a) * f.safeRadius, 0f, Mathf.Sin(a) * f.safeRadius);
                v[i * 2] = p + Vector3.up * -4f; v[i * 2 + 1] = p + Vector3.up * height;
                float u = i / (float)N * circ / 100f;
                uv[i * 2] = new Vector2(u, 0f); uv[i * 2 + 1] = new Vector2(u, 1f);
                if (i < N) { int k = i * 6, b = i * 2; tri[k] = b; tri[k + 1] = b + 1; tri[k + 2] = b + 3; tri[k + 3] = b; tri[k + 4] = b + 3; tri[k + 5] = b + 2; }
            }
            Mesh m = new Mesh { name = "ZoneMist" };
            m.vertices = v; m.uv = uv; m.triangles = tri; m.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = m;
            MeshRenderer r = go.AddComponent<MeshRenderer>();
            z.mat = new Material(sh); z.mat.color = Safe;
            r.sharedMaterial = z.mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            go.transform.position = new Vector3(f.Center.x, baseY, f.Center.z);
            return z;
        }

        public void Tick(bool alarm, Vector3 centre, float baseY)
        {
            red = Mathf.MoveTowards(red, alarm ? 1f : 0f, Time.deltaTime * 1.5f);
            if (mat != null) mat.color = Color.Lerp(Safe, Alarm, red);
            transform.position = new Vector3(centre.x, baseY, centre.z);
        }
    }
}
