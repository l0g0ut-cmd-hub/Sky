using UnityEngine;

namespace SkyTide
{
    /// <summary>
    /// Заставка студии «HELLFIRE DOJO presents» (Resources/UI/splash_studio — та же картина, что в «Пути клинка»):
    /// один раз при запуске, сразу после заставки Unity. Чёрный экран → картина проявляется → держится →
    /// медленный наезд и «дыхание» огня → гаснет. Внизу мелко — версия и номер сборки. Касание — пропустить.
    /// Под заставкой игра уже грузится (снимки облаков, корабль) — заминка старта спрятана за ней.
    /// </summary>
    public class StudioSplash : MonoBehaviour
    {
        const float FadeIn = 0.25f, Hold = 4.6f, FadeOut = 0.3f;   // быстрые проявление и затухание (по просьбе владельца)
        static bool shown;
        Texture2D art;
        float start = -1f, fadeAt = -1f;
        string version;
        GUIStyle verStyle;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (shown || !Application.isPlaying) return;
            Texture2D tex = Resources.Load<Texture2D>("UI/splash_studio");
            if (tex == null) return;
            shown = true;
            GameObject go = new GameObject("StudioSplash");
            DontDestroyOnLoad(go);
            go.AddComponent<StudioSplash>().art = tex;
        }

        /// <summary>Заставка ещё закрывает экран (ввод игры можно игнорировать).</summary>
        public static bool Active { get; private set; }

        void Start()
        {
            useGUILayout = false;
            Active = true;
            TextAsset bn = Resources.Load<TextAsset>("BuildNumber");
            string num = bn != null ? bn.text.Trim() : "";
            version = "НЕБЕСНЫЙ ПРИЛИВ  ·  версия " + Application.version + (num.Length > 0 ? "  ·  сборка #" + num : "");
        }

        void Update()
        {
            // Первый кадр может быть долгим (загрузка) — отсчёт с первого обычного кадра.
            if (start < 0f) { start = Time.unscaledTime; return; }
            float t = Time.unscaledTime - start;
            bool tap = Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began || Input.GetMouseButtonDown(0);
            if (tap && t > 0.4f && fadeAt < 0f) fadeAt = t;
            if (fadeAt < 0f && t >= FadeIn + Hold) fadeAt = t;
            if (fadeAt >= 0f && t >= fadeAt + FadeOut) { Active = false; Destroy(gameObject); }
        }

        void OnDestroy() { Active = false; }

        void OnGUI()
        {
            GUI.depth = -1000;
            if (Event.current.type != EventType.Repaint) return;
            float t = start < 0f ? 0f : Time.unscaledTime - start;
            float a = t < FadeIn ? Mathf.SmoothStep(0f, 1f, t / FadeIn) : fadeAt < 0f ? 1f : 1f - Mathf.SmoothStep(0f, 1f, (t - fadeAt) / FadeOut);
            float black = fadeAt < 0f ? 1f : 1f - Mathf.SmoothStep(0f, 1f, (t - fadeAt) / FadeOut);
            Rect screen = new Rect(0f, 0f, Screen.width, Screen.height);
            Color oc = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, black);
            GUI.DrawTexture(screen, Texture2D.whiteTexture);
            if (art != null)
            {
                float s = Mathf.Min(Screen.width / (float)art.width, Screen.height / (float)art.height);
                float zoom = 1f + 0.025f * Mathf.Clamp01(t / (FadeIn + Hold + FadeOut));
                float w = art.width * s * zoom, h = art.height * s * zoom;
                Rect r = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
                GUI.color = new Color(1f, 1f, 1f, a);
                GUI.DrawTexture(r, art, ScaleMode.StretchToFill);
                float pulse = 0.5f + 0.5f * Mathf.Sin(t * 3.2f) * Mathf.Sin(t * 1.3f + 0.7f);
                GUI.color = new Color(1f, 0.55f, 0.2f, 0.07f * pulse * a);
                GUI.DrawTexture(r, art, ScaleMode.StretchToFill);
            }
            if (verStyle == null)
            {
                verStyle = new GUIStyle { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.Max(11, Screen.height / 48) };
                verStyle.normal.textColor = new Color(0.7f, 0.62f, 0.55f, 1f);
            }
            GUI.color = new Color(1f, 1f, 1f, 0.75f * a);
            GUI.Label(new Rect(0f, Screen.height * 0.92f, Screen.width, Screen.height * 0.06f), version, verStyle);
            GUI.color = oc;
        }
    }
}
