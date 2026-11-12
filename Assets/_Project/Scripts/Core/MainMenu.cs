using UnityEngine;

namespace SkyTide
{
    /// <summary>
    /// Главное меню (Resources/UI/main_menu — картина с заголовком и кнопками). Показывается при запуске под
    /// заставкой студии; мир уже живёт под ним. «В БОЙ» — меню плавно гаснет и управление переходит к кораблю;
    /// «НАСТРОЙКИ», «АНГАР» — пока «скоро»; «ВЫХОД» — закрыть игру.
    /// Оживление до анимированного фона (Resources/UI/menu_bg — чистый фон для будущих слоёв): медленный наезд
    /// и покачивание кадра, «дыхание» солнца, подсветка нажатой кнопки.
    /// </summary>
    public class MainMenu : MonoBehaviour
    {
        public static bool Active { get; private set; }
        static bool shown;
        Texture2D art;
        float start, leaveAt = -1f, toastAt = -100f;
        string toast;
        int pressed = -1;
        GUIStyle toastStyle;

        // Кнопки в пикселях картины 1821×864: В БОЙ, НАСТРОЙКИ, АНГАР, ВЫХОД.
        const float ArtW = 1821f, ArtH = 864f;
        static readonly Rect[] Buttons =
        {
            new Rect(1165, 340, 457, 82), new Rect(1180, 447, 430, 71), new Rect(1180, 543, 430, 69), new Rect(1180, 638, 430, 72)
        };
        static readonly Vector2 Sun = new Vector2(975, 385);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (shown || !Application.isPlaying) return;
            Texture2D tex = Resources.Load<Texture2D>("UI/main_menu");
            if (tex == null) return;
            shown = true;
            GameObject go = new GameObject("MainMenu");
            DontDestroyOnLoad(go);
            go.AddComponent<MainMenu>().art = tex;
            Active = true;
        }

        void Start() { useGUILayout = false; start = Time.unscaledTime; }
        void OnDestroy() { Active = false; }

        /// <summary>Кадр картины на экране (заполнить, обрезая лишнее) с медленным наездом.</summary>
        Rect ArtRect(float t)
        {
            float s = Mathf.Max(Screen.width / ArtW, Screen.height / ArtH);
            float zoom = 1.02f + 0.02f * Mathf.Sin(t * 0.12f);
            float w = ArtW * s * zoom, h = ArtH * s * zoom;
            float sway = Mathf.Sin(t * 0.21f) * Screen.width * 0.004f;
            return new Rect((Screen.width - w) * 0.5f + sway, (Screen.height - h) * 0.5f, w, h);
        }

        Rect ToScreen(Rect art, Rect px)
        {
            float k = art.width / ArtW;
            return new Rect(art.x + px.x * k, art.y + px.y * k, px.width * k, px.height * k);
        }

        void Update()
        {
            if (StudioSplash.Active) { start = Time.unscaledTime; return; }
            float t = Time.unscaledTime - start;
            if (leaveAt >= 0f)
            {
                if (Time.unscaledTime - leaveAt > 0.7f) { Active = false; Destroy(gameObject); }
                return;
            }
            bool down = Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began || Input.GetMouseButtonDown(0);
            bool up = Input.touchCount > 0 && (Input.GetTouch(0).phase == TouchPhase.Ended) || Input.GetMouseButtonUp(0);
            Vector2 p = Input.touchCount > 0 ? Input.GetTouch(0).position : (Vector2)Input.mousePosition;
            p.y = Screen.height - p.y;
            Rect ar = ArtRect(t);
            int hit = -1;
            for (int i = 0; i < Buttons.Length; i++) if (ToScreen(ar, Buttons[i]).Contains(p)) hit = i;
            if (down) pressed = hit;
            if (up)
            {
                if (pressed >= 0 && pressed == hit) Press(hit);
                pressed = -1;
            }
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space)) Press(0);
        }

        void Press(int i)
        {
            switch (i)
            {
                case 0: leaveAt = Time.unscaledTime; break;
                case 1: toast = "Настройки — скоро"; toastAt = Time.unscaledTime; break;
                case 2: toast = "Ангар — скоро"; toastAt = Time.unscaledTime; break;
                case 3: Application.Quit(); break;
            }
        }

        void OnGUI()
        {
            GUI.depth = -900;   // под заставкой студии (-1000), над игрой и HUD
            if (Event.current.type != EventType.Repaint || art == null) return;
            float t = Time.unscaledTime - start;
            float a = leaveAt < 0f ? 1f : 1f - Mathf.SmoothStep(0f, 1f, (Time.unscaledTime - leaveAt) / 0.7f);
            Color oc = GUI.color;
            Rect ar = ArtRect(t);
            GUI.color = new Color(1f, 1f, 1f, a);
            GUI.DrawTexture(ar, art, ScaleMode.StretchToFill);
            // «Дыхание» солнца — тёплая вспышка поверх картины, мягко спадает к краям.
            float k = ar.width / ArtW;
            float pulse = 0.5f + 0.5f * Mathf.Sin(t * 0.9f);
            Vector2 sun = new Vector2(ar.x + Sun.x * k, ar.y + Sun.y * k);
            for (int i = 0; i < 4; i++)
            {
                float r = (60f + i * 55f) * k;
                GUI.color = new Color(1f, 0.62f, 0.25f, 0.035f * pulse * a);
                GUI.DrawTexture(new Rect(sun.x - r, sun.y - r, r * 2f, r * 2f), Glow(), ScaleMode.StretchToFill);
            }
            // Нажатая кнопка — тёплая подсветка поверх нарисованной плашки.
            if (pressed >= 0)
            {
                GUI.color = new Color(1f, 0.6f, 0.2f, 0.22f * a);
                GUI.DrawTexture(ToScreen(ar, Buttons[pressed]), Texture2D.whiteTexture);
            }
            if (Time.unscaledTime - toastAt < 1.8f)
            {
                if (toastStyle == null) { toastStyle = new GUIStyle { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.Max(14, Screen.height / 30) }; toastStyle.normal.textColor = new Color(0.96f, 0.9f, 0.77f); }
                float ta = Mathf.Clamp01((1.8f - (Time.unscaledTime - toastAt)) / 0.4f) * a;
                Rect b = ToScreen(ar, Buttons[3]);
                Rect tr = new Rect(b.x, b.yMax + b.height * 0.35f, b.width, b.height * 0.8f);
                GUI.color = new Color(0f, 0f, 0f, 0.55f * ta); GUI.DrawTexture(tr, Texture2D.whiteTexture);
                GUI.color = new Color(1f, 1f, 1f, ta); GUI.Label(tr, toast, toastStyle);
            }
            GUI.color = oc;
        }

        static Texture2D glow;
        static Texture2D Glow()
        {
            if (glow != null) return glow;
            const int n = 64;
            glow = new Texture2D(n, n, TextureFormat.RGBA32, false);
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                float v = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                glow.SetPixel(x, y, new Color(1f, 1f, 1f, v * v));
            }
            glow.Apply();
            glow.wrapMode = TextureWrapMode.Clamp;
            return glow;
        }
    }
}
