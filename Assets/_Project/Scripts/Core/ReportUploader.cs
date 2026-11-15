using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace SkyTide
{
    /// <summary>
    /// Отчёты сами уходят на GitHub (l0g0ut-cmd-hub/Sky-reports) — без нажатий, и репозиторий не разрастается:
    /// файлы ПЕРЕЗАПИСЫВАЮТСЯ по кругу, а не копятся.
    ///   reports/latest.txt          — последний полный отчёт (раз в 5 мин, при сворачивании, при автометке);
    ///   reports/session0..4.txt     — последний отчёт каждой из 5 последних сессий (номер сессии по кругу);
    ///   reports/mark0..9.jpg / .txt — 10 последних меток: снимок экрана + отчёт в этот момент.
    /// Автометки (сами, не чаще раза в 90 с и не больше 8 за сессию): гибель флагмана, ошибка Unity, FPS ниже 15 дольше
    /// 10 с, найдено пересечение (корабль в камне, облако в крепости…), сорванный абордаж. Плюс ручная «МЕТКА» в панели FPS.
    /// Перезапись по GitHub Contents API: GET (sha старого файла) → PUT с sha. Токен — секрет REPORT_TOKEN, вшивает CI
    /// (Resources/ReportToken.txt, в репозиторий не попадает). Нет токена — ничего не отправляется.
    /// </summary>
    public class ReportUploader : MonoBehaviour
    {
        public const string Repo = "l0g0ut-cmd-hub/Sky-reports";
        public static ReportUploader Instance { get; private set; }
        public static string LastStatus = "отправка отчётов: нет токена";
        string token;
        float nextUpload = 300f, lastMark = -1000f, lowFpsFor, nextScan = 20f;
        int marksThisSession, lastErrors, lastDeaths, lastAborts;
        int sessionSlot, markSlot;
        readonly HashSet<string> seenIntersections = new HashSet<string>();
        bool busy;
        readonly Queue<KeyValuePair<string, byte[]>> queue = new Queue<KeyValuePair<string, byte[]>>();
        public bool Ready { get { return !string.IsNullOrEmpty(token); } }

        void Awake()
        {
            Instance = this;
            TextAsset t = Resources.Load<TextAsset>("ReportToken");
            token = t != null ? t.text.Trim() : null;
            if (!Ready) return;
            LastStatus = "отправка отчётов: готово";
            int session = PlayerPrefs.GetInt("SkyTide.ReportSession", -1) + 1;
            PlayerPrefs.SetInt("SkyTide.ReportSession", session); PlayerPrefs.Save();
            sessionSlot = session % 5;
            markSlot = PlayerPrefs.GetInt("SkyTide.ReportMark", 0);
        }

        IEnumerator Start()
        {
            yield return new WaitForSecondsRealtime(8f);
            // Прошлая сессия (сохранена на телефоне перед выходом/вылетом) — в свой слот, если она новее того, что на GitHub.
            if (Ready && !string.IsNullOrEmpty(DebugRecorder.PreviousSession))
                Enqueue("reports/session" + ((sessionSlot + 4) % 5) + ".txt", Encoding.UTF8.GetBytes(DebugRecorder.PreviousSession));
        }

        void Update()
        {
            if (!Ready) return;
            if (Time.realtimeSinceStartup >= nextUpload) { nextUpload = Time.realtimeSinceStartup + 300f; UploadReport(); }
            // ---------- автометки ----------
            float fps = 1f / Mathf.Max(1e-3f, Time.unscaledDeltaTime);
            lowFpsFor = fps < 15f && Time.timeSinceLevelLoad > 20f ? lowFpsFor + Time.unscaledDeltaTime : 0f;
            if (lowFpsFor > 10f) { lowFpsFor = -290f; AutoMark("FPS ниже 15 дольше 10 с"); }
            if (DebugReport.ErrorCount > lastErrors) { lastErrors = DebugReport.ErrorCount; AutoMark("ошибка Unity"); }
            int deaths = DebugReport.CountOf("флагман потоплен");
            if (deaths > lastDeaths) { lastDeaths = deaths; AutoMark("флагман потоплен"); }
            int aborts = DebugReport.CountOf("абордажей сорвано");
            if (aborts > lastAborts) { lastAborts = aborts; AutoMark("абордаж сорван"); }
            if (Time.realtimeSinceStartup >= nextScan)
            {
                nextScan = Time.realtimeSinceStartup + 30f;
                foreach (string s in DebugReport.FindIntersections())
                {
                    string key = s.Split('(')[0];   // одно и то же пересечение — одна метка за сессию
                    if (seenIntersections.Add(key)) { AutoMark("пересечение: " + s); break; }
                }
            }
        }

        void OnApplicationPause(bool paused) { if (paused && Ready && Time.realtimeSinceStartup > 60f) UploadReport(); }

        string Report() { return DebugReport.Build(1f / Mathf.Max(1e-3f, Time.smoothDeltaTime), "", true); }

        public void UploadReport()
        {
            if (!Ready) return;
            byte[] r = Encoding.UTF8.GetBytes(Report());
            Enqueue("reports/latest.txt", r);
            Enqueue("reports/session" + sessionSlot + ".txt", r);
        }

        void AutoMark(string why)
        {
            if (Time.realtimeSinceStartup - lastMark < 90f || marksThisSession >= 8) return;
            DebugReport.Event("АВТОМЕТКА: " + why);
            Mark(why);
        }

        /// <summary>Метка: снимок экрана (без панели FPS) + отчёт в слот mark0..9 по кругу.</summary>
        public void Mark(string why = "метка игрока")
        {
            if (!Ready) { LastStatus = "метка: нет токена"; return; }
            lastMark = Time.realtimeSinceStartup; marksThisSession++;
            StartCoroutine(MarkRoutine(why));
        }

        IEnumerator MarkRoutine(string why)
        {
            int slot = markSlot; markSlot = (markSlot + 1) % 10;
            PlayerPrefs.SetInt("SkyTide.ReportMark", markSlot);
            bool wasOpen = PerfPanel.Open;
            PerfPanel.Open = false;
            yield return null; yield return new WaitForEndOfFrame();
            Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture();
            PerfPanel.Open = wasOpen;
            if (shot != null)
            {
                int w = shot.width / 2, h = shot.height / 2;
                RenderTexture rt = RenderTexture.GetTemporary(w, h, 0);
                Graphics.Blit(shot, rt);
                RenderTexture prev = RenderTexture.active; RenderTexture.active = rt;
                Texture2D small = new Texture2D(w, h, TextureFormat.RGB24, false);
                small.ReadPixels(new Rect(0, 0, w, h), 0, 0); small.Apply();
                RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
                Enqueue("reports/mark" + slot + ".jpg", small.EncodeToJPG(75));
                Destroy(shot); Destroy(small);
            }
            string r = "МЕТКА: " + why + " · " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\n" + Report();
            byte[] bytes = Encoding.UTF8.GetBytes(r);
            Enqueue("reports/mark" + slot + ".txt", bytes);
            Enqueue("reports/latest.txt", bytes);
        }

        void Enqueue(string path, byte[] data)
        {
            queue.Enqueue(new KeyValuePair<string, byte[]>(path, data));
            if (!busy) StartCoroutine(Pump());
        }

        IEnumerator Pump()
        {
            busy = true;
            while (queue.Count > 0)
            {
                KeyValuePair<string, byte[]> job = queue.Dequeue();
                yield return Put(job.Key, job.Value);
            }
            busy = false;
        }

        UnityWebRequest Request(string url, string method)
        {
            UnityWebRequest req = new UnityWebRequest(url, method);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Authorization", "Bearer " + token);
            req.SetRequestHeader("Accept", "application/vnd.github+json");
            req.SetRequestHeader("User-Agent", "SkyTide");
            req.timeout = 30;
            return req;
        }

        IEnumerator Put(string path, byte[] data)
        {
            string url = "https://api.github.com/repos/" + Repo + "/contents/" + path;
            // sha старого файла — без него GitHub не даст перезаписать.
            string sha = null;
            using (UnityWebRequest get = Request(url, "GET"))
            {
                yield return get.SendWebRequest();
                if (get.result == UnityWebRequest.Result.Success)
                {
                    string txt = get.downloadHandler.text;
                    int i = txt.IndexOf("\"sha\":\"", System.StringComparison.Ordinal);
                    if (i >= 0) { i += 7; int j = txt.IndexOf('"', i); if (j > i) sha = txt.Substring(i, j - i); }
                }
            }
            string body = "{\"message\":\"" + path + "\",\"content\":\"" + System.Convert.ToBase64String(data) + "\"" + (sha != null ? ",\"sha\":\"" + sha + "\"" : "") + "}";
            using (UnityWebRequest put = Request(url, "PUT"))
            {
                put.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                put.SetRequestHeader("Content-Type", "application/json");
                yield return put.SendWebRequest();
                bool ok = put.result == UnityWebRequest.Result.Success;
                LastStatus = (ok ? "отправлено: " : "ошибка отправки (" + put.responseCode + "): ") + path + " · " + System.DateTime.Now.ToString("HH:mm:ss");
            }
        }
    }
}
