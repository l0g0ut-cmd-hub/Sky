using System.Collections.Generic;
using UnityEngine;

namespace SkyTide
{
    /// <summary>
    /// Дула пушек моделей флота (Meshy), в координатах сетки Ship_&lt;класс&gt;: [0] — левый борт, [1] — правый.
    /// Владелец 16.11: «огонь должен идти именно из пушек, а не из произвольных мест моделей». Раньше враги брали точку
    /// на коробке модели (38% высоты), союзники — 28%. Точки сняты с моделей: стволы — по выступу за обвод корпуса у
    /// заданной продольной позиции (срез ствола), порты без стволов — ряд на борту (адмирал, канонерка, катер, шлюп),
    /// кеч — жерло мортиры (стреляет на оба борта). Разметка и проверка — по рендерам моделей (сверху и сбоку).
    /// Залп из N пушек раскладывается по точкам равномерно; точек меньше, чем пушек, — по кругу.
    /// </summary>
    public static class GunPoints
    {
        static readonly Dictionary<string, Vector3[][]> table = new Dictionary<string, Vector3[][]>
        {
            { "Ship_cutter", new[] { new[] { new Vector3(-2.19f, 0.80f, -2.60f) },
                                  new[] { new Vector3(2.20f, 0.80f, -2.60f) } } },
            { "Ship_sloop", new[] { new[] { new Vector3(-3.57f, 1.00f, -1.50f), new Vector3(-3.06f, 1.00f, 1.50f) },
                                  new[] { new Vector3(3.58f, 1.00f, -1.50f), new Vector3(3.06f, 1.00f, 1.50f) } } },
            { "Ship_corvette", new[] { new[] { new Vector3(-5.47f, 1.78f, -6.48f), new Vector3(-5.85f, 1.65f, -4.40f), new Vector3(-5.86f, 1.61f, -2.40f), new Vector3(-5.31f, 2.49f, 0.73f), new Vector3(-5.71f, 1.67f, 3.11f) },
                                  new[] { new Vector3(5.46f, 1.75f, -6.50f), new Vector3(5.84f, 1.67f, -4.40f), new Vector3(5.86f, 1.67f, -2.41f), new Vector3(5.30f, 2.49f, 0.71f), new Vector3(5.70f, 1.66f, 3.11f) } } },
            { "Ship_fireship", new[] { new[] { new Vector3(-3.49f, 1.46f, 5.57f) },
                                  new[] { new Vector3(3.49f, 1.37f, 5.57f) } } },
            { "Ship_brig", new[] { new[] { new Vector3(-5.56f, 4.25f, -13.00f), new Vector3(-6.34f, 2.23f, -11.33f), new Vector3(-5.74f, 3.85f, 1.66f), new Vector3(-5.74f, 3.88f, 2.64f) },
                                  new[] { new Vector3(5.56f, 4.22f, -12.98f), new Vector3(6.33f, 2.24f, -11.44f), new Vector3(5.74f, 3.85f, 2.05f), new Vector3(5.71f, 3.88f, 2.68f) } } },
            { "Ship_ketch", new[] { new[] { new Vector3(0.00f, 14.00f, -2.30f) },
                                  new[] { new Vector3(0.00f, 14.00f, -2.30f) } } },
            { "Ship_frigate", new[] { new[] { new Vector3(-11.94f, 3.82f, -7.38f), new Vector3(-11.77f, 3.84f, -2.72f), new Vector3(-11.72f, 3.81f, 0.95f), new Vector3(-11.80f, 3.79f, 5.45f), new Vector3(-10.25f, 3.80f, 14.62f), new Vector3(-9.14f, 3.72f, 18.67f) },
                                  new[] { new Vector3(11.96f, 3.81f, -7.51f), new Vector3(11.77f, 3.81f, -2.70f), new Vector3(11.75f, 3.82f, 1.03f), new Vector3(11.78f, 3.78f, 5.43f), new Vector3(10.30f, 3.86f, 14.48f), new Vector3(9.26f, 3.69f, 18.71f) } } },
            { "Ship_heavyfrigate", new[] { new[] { new Vector3(-13.50f, 7.75f, -31.96f), new Vector3(-16.05f, 6.71f, -21.01f), new Vector3(-16.66f, 6.65f, -14.03f), new Vector3(-17.04f, 6.41f, -6.27f), new Vector3(-16.75f, 6.45f, 2.38f), new Vector3(-16.72f, 6.48f, 9.36f), new Vector3(-15.49f, 6.78f, 17.09f), new Vector3(-13.48f, 7.17f, 24.59f), new Vector3(-10.29f, 7.55f, 30.26f) },
                                  new[] { new Vector3(13.61f, 7.81f, -31.92f), new Vector3(16.70f, 6.80f, -21.11f), new Vector3(16.63f, 6.44f, -13.96f), new Vector3(17.12f, 6.47f, -6.22f), new Vector3(16.81f, 6.46f, 2.46f), new Vector3(16.71f, 6.50f, 9.41f), new Vector3(15.51f, 6.68f, 17.05f), new Vector3(13.59f, 7.22f, 24.56f), new Vector3(10.29f, 7.47f, 30.25f) } } },
            { "Ship_ship_of_line", new[] { new[] { new Vector3(-22.39f, 15.44f, -52.85f), new Vector3(-24.59f, 7.46f, -38.71f), new Vector3(-22.23f, 6.66f, -18.73f), new Vector3(-27.04f, 7.56f, -12.51f), new Vector3(-26.99f, 7.47f, -2.41f), new Vector3(-26.80f, 8.17f, 9.19f), new Vector3(-24.25f, 8.81f, 25.16f), new Vector3(-18.01f, 8.94f, 28.76f) },
                                  new[] { new Vector3(22.40f, 15.65f, -52.85f), new Vector3(24.62f, 7.47f, -38.62f), new Vector3(22.03f, 6.76f, -18.77f), new Vector3(27.05f, 7.45f, -12.58f), new Vector3(26.84f, 7.45f, -2.46f), new Vector3(26.84f, 8.23f, 9.22f), new Vector3(24.02f, 8.72f, 25.21f), new Vector3(17.87f, 8.96f, 28.76f) } } },
            { "Ship_admiral", new[] { new[] { new Vector3(-25.77f, 11.00f, -70.00f), new Vector3(-28.41f, 11.00f, -53.33f), new Vector3(-31.05f, 11.00f, -36.67f), new Vector3(-31.42f, 11.00f, -20.00f), new Vector3(-34.50f, 11.00f, -3.33f), new Vector3(-31.86f, 11.00f, 13.33f), new Vector3(-32.71f, 11.00f, 30.00f), new Vector3(-29.22f, 11.00f, 46.67f), new Vector3(-24.15f, 11.00f, 63.33f), new Vector3(-20.22f, 11.00f, 80.00f) },
                                  new[] { new Vector3(25.94f, 11.00f, -70.00f), new Vector3(28.89f, 11.00f, -53.33f), new Vector3(31.58f, 11.00f, -36.67f), new Vector3(31.51f, 11.00f, -20.00f), new Vector3(34.84f, 11.00f, -3.33f), new Vector3(32.35f, 11.00f, 13.33f), new Vector3(33.18f, 11.00f, 30.00f), new Vector3(29.81f, 11.00f, 46.67f), new Vector3(25.07f, 11.00f, 63.33f), new Vector3(21.28f, 11.00f, 80.00f) } } },
            { "Ship_clipper", new[] { new[] { new Vector3(-5.94f, 1.42f, -10.26f), new Vector3(-5.96f, 1.40f, -5.47f), new Vector3(-5.99f, 1.36f, -0.39f), new Vector3(-5.92f, 1.37f, 4.43f) },
                                  new[] { new Vector3(5.90f, 1.46f, -10.35f), new Vector3(5.90f, 1.34f, -5.53f), new Vector3(5.90f, 1.39f, -0.40f), new Vector3(5.90f, 1.44f, 4.30f) } } },
            { "Ship_gunboat", new[] { new[] { new Vector3(-11.63f, 7.00f, -21.00f), new Vector3(-12.72f, 7.00f, -14.60f), new Vector3(-13.77f, 7.00f, -8.20f), new Vector3(-14.20f, 7.00f, -1.80f), new Vector3(-14.16f, 7.00f, 4.60f), new Vector3(-13.29f, 7.00f, 11.00f) },
                                  new[] { new Vector3(11.77f, 7.00f, -21.00f), new Vector3(12.69f, 7.00f, -14.60f), new Vector3(13.74f, 7.00f, -8.20f), new Vector3(14.17f, 7.00f, -1.80f), new Vector3(14.13f, 7.00f, 4.60f), new Vector3(13.25f, 7.00f, 11.00f) } } },
        };

        public static bool TryGet(Mesh mesh, int side, int index, int count, out Vector3 local)
        {
            local = Vector3.zero;
            if (mesh == null) return false;
            string n = mesh.name;
            if (n.EndsWith("_lod") || n.EndsWith("_far")) n = n.Substring(0, n.Length - 4);
            Vector3[][] e;
            if (!table.TryGetValue(n, out e)) return false;
            Vector3[] p = e[side > 0 ? 1 : 0];
            if (p.Length == 0) return false;
            count = Mathf.Max(1, count); index = Mathf.Clamp(index, 0, count - 1);
            int k;
            if (count > p.Length) k = index % p.Length;                       // пушек больше, чем стволов на модели — по кругу
            else if (count == 1) k = p.Length / 2;
            else k = Mathf.RoundToInt(index * (p.Length - 1) / (float)(count - 1));   // равномерно вдоль борта
            local = p[Mathf.Clamp(k, 0, p.Length - 1)];
            return true;
        }
    }
}
