using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace SkyTide
{
    /// <summary>
    /// Отчёты сами уходят на GitHub (приватный репозиторий l0g0ut-cmd-hub/Sky-reports): полный отчёт раз в 5 минут и при
    /// сворачивании, отчёт прошлой сессии — при запуске, и «МЕТКА» из панели FPS — отчёт + снимок экрана (без панели).
    /// Путь: reports/b{сборка}/{дата-время}-{что}.txt|jpg. Токен вшивается сборкой CI из секрета REPORT_TOKEN
    /// (Resources/ReportToken.txt, в репозиторий не попадает). Нет токена — ничего не отправляется.
    /// Токен должен давать доступ ТОЛЬКО к Sky-reports (fine-grained, Contents: read/write): он извлекается из APK.
    /// </summary>
    public class ReportUploader : MonoBehaviour
    {
        public const string Repo = "l0g0ut-cmd-hub/Sky-reports";
        public static ReportUploader Instance { get; private set; }
        string token;
        float nextUpload = 300f;
        public static string LastStatus = "отправка отчётов: нет токена";
        public bool Ready { get { return !string.IsNullOrEmpty(token); } }

        void Awake()
        {
            Instance = this;
            TextAsset t = Resources.Load<TextAsset>("ReportToken");
            token = t != null ? t.text.Trim() : null;
            if (Ready) LastStatus = "отправка отчётов: готово";
        }

        IEnumerator Start()
        {
            yield return new WaitForSecondsRealtime(8f);
            if (Ready && !string.IsNullOrEmpty(DebugRecorder.PreviousSession)) Upload("previous", Encoding.UTF8.GetBytes(DebugRecorder.PreviousSession), "txt");
        }

        void Update()
        {
            if (!Ready || Time.realtimeSinceStartup < nextUpload) return;
            nextUpload = Time.realtimeSinceStartup + 300f;
            UploadReport("auto");
        }

        void OnApplicationPause(bool paused) { if (paused && Ready && Time.realtimeSinceStartup > 60f) UploadReport("pause"); }

        public void UploadReport(string why)
        {
            if (!Ready) return;
            string r = DebugReport.Build(1f / Mathf.Max(1e-3f, Time.smoothDeltaTime), "", true);
            Upload(why, Encoding.UTF8.GetBytes(r), "txt");
        }

        /// <summary>Метка: отчёт + снимок экрана без панели FPS (панель закрывается на два кадра).</summary>
        public void Mark()
        {
            if (!Ready) { LastStatus = "метка: нет токена — отчёт только в буфер"; return; }
            StartCoroutine(MarkRoutine());
        }

        IEnumerator MarkRoutine()
        {
            DebugReport.Event("МЕТКА игрока");
            bool wasOpen = PerfPanel.Open;
            PerfPanel.Open = false;
            yield return null; yield return new WaitForEndOfFrame();
            Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture();
            PerfPanel.Open = wasOpen;
            string stamp = System.DateTime.Now.ToString("yyyyMMdd-HHmmss");
            if (shot != null)
            {
                // Вдвое меньше, JPG 80 — ~150–300 КБ.
                int w = shot.width / 2, h = shot.height / 2;
                RenderTexture rt = RenderTexture.GetTemporary(w, h, 0);
                Graphics.Blit(shot, rt);
                RenderTexture prev = RenderTexture.active; RenderTexture.active = rt;
                Texture2D small = new Texture2D(w, h, TextureFormat.RGB24, false);
                small.ReadPixels(new Rect(0, 0, w, h), 0, 0); small.Apply();
                RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
                byte[] jpg = small.EncodeToJPG(80);
                Destroy(shot); Destroy(small);
                Upload("mark", jpg, "jpg", stamp);
            }
            Upload("mark", Encoding.UTF8.GetBytes(DebugReport.Build(1f / Mathf.Max(1e-3f, Time.smoothDeltaTime), "", true)), "txt", stamp);
        }

        void Upload(string why, byte[] data, string ext, string stamp = null)
        {
            if (stamp == null) stamp = System.DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string build = Application.version.Replace("0.1.", "");
            string path = "reports/b" + build + "/" + stamp + "-" + why + "." + ext;
            StartCoroutine(Put(path, data, why));
        }

        IEnumerator Put(string path, byte[] data, string why)
        {
            string body = "{\"message\":\"report " + why + "\",\"content\":\"" + System.Convert.ToBase64String(data) + "\"}";
            using (UnityWebRequest req = new UnityWebRequest("https://api.github.com/repos/" + Repo + "/contents/" + path, "PUT"))
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Authorization", "Bearer " + token);
                req.SetRequestHeader("Accept", "application/vnd.github+json");
                req.SetRequestHeader("User-Agent", "SkyTide");
                req.SetRequestHeader("Content-Type", "application/json");
                req.timeout = 30;
                yield return req.SendWebRequest();
                bool ok = req.result == UnityWebRequest.Result.Success;
                LastStatus = (ok ? "отправлено: " : "ошибка отправки (" + req.responseCode + "): ") + path;
                if (ok) DebugReport.Event("отчёт отправлен: " + why);
            }
        }
    }
}
