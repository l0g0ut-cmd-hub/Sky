using UnityEngine;

namespace SkyTide
{
    /// <summary>
    /// Сцена в пониженном разрешении, HUD — в родном. Главная камера рисует в свою текстуру (доля экрана),
    /// вся цепочка OnRenderImage (облака, пост) идёт в этом размере; отдельная камера-«показ» без объектов
    /// растягивает результат на экран и поверх рисует HUD в полном разрешении — надписи и плашки остаются чёткими.
    /// При 100% ничего этого нет: камера рисует прямо на экран, как раньше.
    /// Это тот же путь, каким снимаются кадры CI (камера в RenderTexture + HUD).
    /// </summary>
    public class SceneScaler : MonoBehaviour
    {
        static SceneScaler instance;
        /// <summary>Сцена рисуется в текстуру, HUD рисует SceneScaler (а не SkyTidePost).</summary>
        public static bool Active { get { return instance != null && instance.rt != null; } }
        public static float Scale { get { return instance != null ? instance.scale : 1f; } }

        Camera scene, presenter;
        TouchControls controls;
        RenderTexture rt;
        float scale = 1f;
        int forW, forH;

        public static void SetScale(float s)
        {
            s = Mathf.Clamp(s, 0.4f, 1f);
            if (instance == null)
            {
                if (s >= 0.999f) return;
                Camera main = Camera.main;
                if (main == null) return;
                GameObject go = new GameObject("Presenter");
                instance = go.AddComponent<SceneScaler>();
                instance.scene = main;
                instance.controls = FindFirstObjectByType<TouchControls>();
                Camera p = go.AddComponent<Camera>();
                p.depth = main.depth + 10f;
                p.cullingMask = 0;
                p.clearFlags = CameraClearFlags.SolidColor;
                p.backgroundColor = Color.black;
                p.allowHDR = false; p.allowMSAA = false; p.useOcclusionCulling = false;
                instance.presenter = p;
            }
            instance.scale = s;
            instance.Apply();
        }

        void Apply()
        {
            if (scene == null) return;
            if (scale >= 0.999f)
            {
                scene.targetTexture = null;
                if (presenter != null) presenter.enabled = false;
                if (rt != null) { rt.Release(); Destroy(rt); rt = null; }
                return;
            }
            int w = Mathf.Max(16, Mathf.RoundToInt(Screen.width * scale)) & ~1, h = Mathf.Max(16, Mathf.RoundToInt(Screen.height * scale)) & ~1;
            if (rt == null || rt.width != w || rt.height != h)
            {
                scene.targetTexture = null;
                if (rt != null) { rt.Release(); Destroy(rt); }
                rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
                rt.name = "Scene (scaled)"; rt.filterMode = FilterMode.Bilinear; rt.wrapMode = TextureWrapMode.Clamp;
                rt.Create();
            }
            forW = Screen.width; forH = Screen.height;
            scene.targetTexture = rt;
            presenter.enabled = true;
        }

        void Update()
        {
            if (rt != null && (forW != Screen.width || forH != Screen.height)) Apply();   // поворот/смена размера окна
        }

        // Камера-показ: объектов нет, после её «отрисовки» кладём сцену на экран и рисуем HUD в родном разрешении.
        void OnPostRender()
        {
            if (rt == null) return;
            Graphics.Blit(rt, (RenderTexture)null);
            if (controls != null) controls.DrawHud(null, Screen.width, Screen.height);
        }

        void OnDestroy()
        {
            if (scene != null) scene.targetTexture = null;
            if (rt != null) { rt.Release(); Destroy(rt); rt = null; }
            if (instance == this) instance = null;
        }
    }
}
