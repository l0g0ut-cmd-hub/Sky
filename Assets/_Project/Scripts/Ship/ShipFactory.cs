using UnityEngine;
using UnityEngine.Rendering;

namespace SkyTide
{
    /// <summary>
    /// Собирает корабль: корень с ShipController и дочерний Visual.
    /// Если есть меш модели (сконвертированный из GLB), ставится он; иначе — заглушка из примитивов
    /// по концепту: корпус, кормовая надстройка с горящими окнами, мачты-стойки, три воздушных мешка,
    /// паруса, бушприт, руль, пушки по бортам, красные вымпелы. Нос смотрит в +Z.
    /// </summary>
    public static class ShipFactory
    {
        public const int GunsPerSide = 8;

        public static ShipController Build(string name, Vector3 position, float yaw, GameMaterials m, ShipVariant variant)
        {
            GameObject root = new GameObject(name);
            Transform visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);

            ShipController ctrl = root.AddComponent<ShipController>();
            ctrl.visual = visual;
            SetModel(ctrl, m, variant);
            root.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            return ctrl;
        }

        /// <summary>Меняет модель корабля на лету (переключатель): всё внутри Visual пересобирается.</summary>
        public static void SetModel(ShipController ctrl, GameMaterials m, ShipVariant variant)
        {
            Transform visual = ctrl.visual;
            for (int i = visual.childCount - 1; i >= 0; i--) SkyTideBootstrap.SafeDestroy(visual.GetChild(i).gameObject);
            Mesh mesh = variant != null && variant.IsValid ? variant.Mesh : null;
            Transform model = null;
            if (mesh != null) model = BuildFromMesh(visual, mesh, variant.materials);
            else BuildPrimitive(visual, m);
            Broadside b = ctrl.GetComponent<Broadside>();
            if (b == null) b = ctrl.gameObject.AddComponent<Broadside>();
            b.SetModel(ctrl, model, model != null && variant != null ? Broadside.Parse(variant.cannons) : null);
        }

        static Transform BuildFromMesh(Transform visual, Mesh mesh, Material[] mats)
        {
            GameObject go = new GameObject("Model");
            go.transform.SetParent(visual, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer r = go.AddComponent<MeshRenderer>();
            Material[] slots = new Material[mesh.subMeshCount];
            for (int i = 0; i < slots.Length; i++) slots[i] = mats[Mathf.Min(i, mats.Length - 1)];
            r.sharedMaterials = slots;
            r.shadowCastingMode = ShadowCastingMode.On;

            // Тёплый свет из кормовых окон и фонарей, как на концепте
            Light lamp = new GameObject("Stern Glow").AddComponent<Light>();
            lamp.transform.SetParent(visual, false);
            lamp.transform.localPosition = new Vector3(0f, 8f, -17f);
            lamp.type = LightType.Point;
            lamp.color = new Color(1f, 0.68f, 0.32f);
            lamp.range = 16f;
            lamp.intensity = 1.4f;
            lamp.shadows = LightShadows.None;
            return go.transform;
        }

        static void BuildPrimitive(Transform v, GameMaterials m)
        {
            // Корпус
            Prim(PrimitiveType.Cube, v, new Vector3(0f, 0f, 0f), Vector3.zero, new Vector3(7f, 4.5f, 26f), m.hull);
            Prim(PrimitiveType.Cube, v, new Vector3(0f, -2.8f, 0.5f), Vector3.zero, new Vector3(5f, 2.5f, 23f), m.hull);
            Prim(PrimitiveType.Cube, v, new Vector3(0f, -4.3f, 1f), Vector3.zero, new Vector3(2.4f, 1.2f, 18f), m.darkWood);
            Prim(PrimitiveType.Cube, v, new Vector3(0f, 0f, 12.6f), new Vector3(0f, 45f, 0f), new Vector3(4.95f, 4.5f, 4.95f), m.hull);
            Prim(PrimitiveType.Cube, v, new Vector3(0f, -2.6f, 11.4f), new Vector3(0f, 45f, 0f), new Vector3(3.4f, 2.2f, 3.4f), m.hull);
            // Пояса-накладки (латунь и тёмное дерево)
            Prim(PrimitiveType.Cube, v, new Vector3(0f, 2.15f, 0f), Vector3.zero, new Vector3(7.2f, 0.3f, 26.1f), m.brass);
            Prim(PrimitiveType.Cube, v, new Vector3(0f, -1.2f, 0f), Vector3.zero, new Vector3(7.15f, 0.35f, 25.8f), m.darkWood);
            // Фальшборт
            Prim(PrimitiveType.Cube, v, new Vector3(-3.35f, 2.8f, 0.5f), Vector3.zero, new Vector3(0.3f, 0.9f, 24f), m.darkWood);
            Prim(PrimitiveType.Cube, v, new Vector3(3.35f, 2.8f, 0.5f), Vector3.zero, new Vector3(0.3f, 0.9f, 24f), m.darkWood);

            // Кормовая надстройка с окнами и фонарём
            Prim(PrimitiveType.Cube, v, new Vector3(0f, 4.6f, -10.3f), Vector3.zero, new Vector3(7.6f, 5f, 7.4f), m.darkWood);
            Prim(PrimitiveType.Cube, v, new Vector3(0f, 7.25f, -10.3f), Vector3.zero, new Vector3(7.9f, 0.35f, 7.7f), m.brass);
            Prim(PrimitiveType.Cube, v, new Vector3(0f, 5.2f, -14.02f), Vector3.zero, new Vector3(6f, 2f, 0.1f), m.windowGlow);
            Prim(PrimitiveType.Cube, v, new Vector3(-3.82f, 5.2f, -10.3f), Vector3.zero, new Vector3(0.1f, 1.8f, 5.6f), m.windowGlow);
            Prim(PrimitiveType.Cube, v, new Vector3(3.82f, 5.2f, -10.3f), Vector3.zero, new Vector3(0.1f, 1.8f, 5.6f), m.windowGlow);
            Prim(PrimitiveType.Cylinder, v, new Vector3(0f, 8.3f, -13.3f), Vector3.zero, new Vector3(0.25f, 0.9f, 0.25f), m.iron);
            Prim(PrimitiveType.Cube, v, new Vector3(0f, 9.6f, -13.3f), Vector3.zero, new Vector3(0.9f, 1.1f, 0.9f), m.windowGlow);
            Light lamp = new GameObject("Lantern Light").AddComponent<Light>();
            lamp.transform.SetParent(v, false);
            lamp.transform.localPosition = new Vector3(0f, 9.6f, -14.3f);
            lamp.type = LightType.Point;
            lamp.color = new Color(1f, 0.7f, 0.35f);
            lamp.range = 14f;
            lamp.intensity = 1.6f;
            lamp.shadows = LightShadows.None;

            // Руль и бушприт
            Prim(PrimitiveType.Cube, v, new Vector3(0f, -3.6f, -13.6f), Vector3.zero, new Vector3(0.45f, 4.5f, 2.6f), m.darkWood);
            Prim(PrimitiveType.Cylinder, v, new Vector3(0f, 3.6f, 17.4f), new Vector3(72f, 0f, 0f), new Vector3(0.4f, 5.5f, 0.4f), m.darkWood);

            // Мачты-стойки, на которых держатся мешки
            float[] mastZ = { 8f, 1f, -6f };
            for (int i = 0; i < mastZ.Length; i++)
            {
                Prim(PrimitiveType.Cylinder, v, new Vector3(0f, 9.5f, mastZ[i]), Vector3.zero, new Vector3(0.6f, 7.5f, 0.6f), m.darkWood);
                Prim(PrimitiveType.Cylinder, v, new Vector3(0f, 12f, mastZ[i]), Vector3.zero, new Vector3(0.75f, 0.15f, 0.75f), m.brass);
            }
            Prim(PrimitiveType.Cylinder, v, new Vector3(-5.5f, 8f, -1f), new Vector3(0f, 0f, -12f), new Vector3(0.4f, 6f, 0.4f), m.darkWood);
            Prim(PrimitiveType.Cylinder, v, new Vector3(5.5f, 8f, -1f), new Vector3(0f, 0f, 12f), new Vector3(0.4f, 6f, 0.4f), m.darkWood);

            // Воздушные мешки: большой центральный и два боковых
            Prim(PrimitiveType.Sphere, v, new Vector3(0f, 21f, 0f), Vector3.zero, new Vector3(9f, 8.5f, 30f), m.balloon);
            Prim(PrimitiveType.Sphere, v, new Vector3(-6.8f, 16.5f, -1f), Vector3.zero, new Vector3(5.5f, 5.2f, 15f), m.balloon);
            Prim(PrimitiveType.Sphere, v, new Vector3(6.8f, 16.5f, -1f), Vector3.zero, new Vector3(5.5f, 5.2f, 15f), m.balloon);
            // Канатные обручи на мешках
            for (int i = 0; i < 4; i++)
            {
                float z = -9f + i * 6f;
                float s = Mathf.Sqrt(Mathf.Max(0.05f, 1f - (z / 15f) * (z / 15f)));
                Prim(PrimitiveType.Cylinder, v, new Vector3(0f, 21f, z), new Vector3(90f, 0f, 0f), new Vector3(9.15f * s, 0.12f, 8.65f * s), m.brass);
            }
            // Латунные наконечники
            Prim(PrimitiveType.Sphere, v, new Vector3(0f, 21f, 15.1f), Vector3.zero, new Vector3(0.9f, 0.9f, 1.2f), m.iron);
            Prim(PrimitiveType.Sphere, v, new Vector3(0f, 21f, -15.1f), Vector3.zero, new Vector3(0.9f, 0.9f, 1.2f), m.iron);

            // Паруса между стойками и кливер на носу
            Prim(PrimitiveType.Cube, v, new Vector3(0f, 10.5f, 4.5f), new Vector3(0f, 8f, 0f), new Vector3(0.08f, 5f, 5.6f), m.canvas);
            Prim(PrimitiveType.Cube, v, new Vector3(0f, 10.5f, -2.5f), new Vector3(0f, -8f, 0f), new Vector3(0.08f, 5f, 5.6f), m.canvas);
            Prim(PrimitiveType.Cube, v, new Vector3(0f, 7.5f, 13.5f), new Vector3(-38f, 0f, 0f), new Vector3(0.08f, 6.5f, 4.5f), m.canvas);
            Prim(PrimitiveType.Cube, v, new Vector3(0f, 11f, -12f), new Vector3(0f, 0f, 0f), new Vector3(0.08f, 3.5f, 3.5f), m.canvas);

            // Флагштоки и вымпелы
            Flag(v, m, new Vector3(0f, 25.3f, 4f), 4f);
            Flag(v, m, new Vector3(-6.8f, 19f, -4f), 2.4f);
            Flag(v, m, new Vector3(6.8f, 19f, -4f), 2.4f);

            // Пушки по бортам
            for (int i = 0; i < GunsPerSide; i++)
            {
                float z = -6f + i * 2.1f;
                Prim(PrimitiveType.Cube, v, new Vector3(-3.52f, 0.5f, z), Vector3.zero, new Vector3(0.1f, 1.3f, 1.3f), m.darkWood);
                Prim(PrimitiveType.Cube, v, new Vector3(3.52f, 0.5f, z), Vector3.zero, new Vector3(0.1f, 1.3f, 1.3f), m.darkWood);
                Prim(PrimitiveType.Cylinder, v, new Vector3(-3.9f, 0.5f, z), new Vector3(0f, 0f, 90f), new Vector3(0.5f, 0.6f, 0.5f), m.iron);
                Prim(PrimitiveType.Cylinder, v, new Vector3(3.9f, 0.5f, z), new Vector3(0f, 0f, 90f), new Vector3(0.5f, 0.6f, 0.5f), m.iron);
            }
        }

        static void Flag(Transform v, GameMaterials m, Vector3 basePos, float length)
        {
            Prim(PrimitiveType.Cylinder, v, basePos, Vector3.zero, new Vector3(0.15f, 1.2f, 0.15f), m.iron);
            Prim(PrimitiveType.Cube, v, basePos + new Vector3(0f, 0.85f, -length * 0.5f - 0.1f), Vector3.zero,
                new Vector3(0.05f, 0.7f, length), m.flag);
        }

        static GameObject Prim(PrimitiveType type, Transform parent, Vector3 localPos, Vector3 localEuler, Vector3 scale, Material mat)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            Collider col = go.GetComponent<Collider>();
            if (col != null) SkyTideBootstrap.SafeDestroy(col);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(localEuler);
            go.transform.localScale = scale;
            MeshRenderer r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            if (mat != null && mat.renderQueue >= (int)RenderQueue.Transparent) r.shadowCastingMode = ShadowCastingMode.Off;
            return go;
        }
    }
}
