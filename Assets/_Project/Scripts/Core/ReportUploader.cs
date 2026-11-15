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
    /// Автометки (сами, не чаще раза в 60 с и не больше 15 за сессию): гибель флагмана, ошибка Unity, FPS ниже 15 дольше
    /// 10 с, найдено пересечение, сорванный абордаж; враг потоплен, приз, свой потоплен, тяжёлый рядом, гроза. Плюс ручная «МЕТКА».
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
        int marksThisSession, lastErrors, lastDeaths, lastAborts, lastKills, lastPrizes, lastAllyLost;
        bool lastHeavy; WeatherSystem.Kind lastWeather = WeatherSystem.Kind.Clear;
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
            if (CiBattle.Active) token = null;   // прогон в CI не трогает отчёты игрока
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
            if (Time.realtimeSinceStartup >= nextOutbox) { nextOutbox = Time.realtimeSinceStartup + 60f; RetryOutbox(); }
            if (Time.realtimeSinceStartup >= nextUpload) { nextUpload = Time.realtimeSinceStartup + (Time.realtimeSinceStartup < RemoteCommands.LiveUntil ? 30f : 300f); UploadReport(); }
            else if (Time.realtimeSinceStartup < RemoteCommands.LiveUntil && nextUpload - Time.realtimeSinceStartup > 30f) nextUpload = Time.realtimeSinceStartup + 30f;
            // ---------- автометки ----------
            float fps = 1f / Mathf.Max(1e-3f, Time.unscaledDeltaTime);
            lowFpsFor = fps < 15f && Time.timeSinceLevelLoad > 20f ? lowFpsFor + Time.unscaledDeltaTime : 0f;
            if (lowFpsFor > 10f) { lowFpsFor = -290f; AutoMark("FPS ниже 15 дольше 10 с"); }
            if (DebugReport.ErrorCount > lastErrors) { lastErrors = DebugReport.ErrorCount; AutoMark("ошибка Unity"); }
            int deaths = DebugReport.CountOf("флагман потоплен");
            if (deaths > lastDeaths) { lastDeaths = deaths; AutoMark("флагман потоплен"); }
            int aborts = DebugReport.CountOf("абордажей сорвано");
            if (aborts > lastAborts) { lastAborts = aborts; AutoMark("абордаж сорван"); }
            // События боя: снимок через 1.2 с — чтобы в кадр попал взрыв/потопление, а не момент до него.
            int kills = DebugReport.CountOf("врагов потоплено");
            if (kills > lastKills) { lastKills = kills; AutoMark("враг потоплен", 1.2f); }
            int prizes = DebugReport.CountOf("призов взято");
            if (prizes > lastPrizes) { lastPrizes = prizes; AutoMark("приз взят"); }
            int allyLost = DebugReport.CountOf("своих потоплено");
            if (allyLost > lastAllyLost) { lastAllyLost = allyLost; AutoMark("свой корабль потоплен", 1.2f); }
            bool heavy = false;
            foreach (TargetShip t in TargetShip.All) if (t != null && t.Alive && t.Heavy && PlayerHull.Instance != null && Vector3.Distance(t.transform.position, PlayerHull.Instance.transform.position) < 1500f) { heavy = true; break; }
            if (heavy && !lastHeavy) AutoMark("тяжёлый враг рядом");
            lastHeavy = heavy;
            WeatherSystem ws = WeatherSystem.Instance;
            if (ws != null && ws.Current != lastWeather) { lastWeather = ws.Current; if (ws.Current == WeatherSystem.Kind.Storm) AutoMark("гроза", 45f); }
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

        // Важные (поломки) — не чаще раза в 60 с; события боя — раз в 60 с; всего не больше 15 за сессию.
        void AutoMark(string why, float delay = 0f)
        {
            if (Time.realtimeSinceStartup - lastMark < 60f || marksThisSession >= 15) return;
            lastMark = Time.realtimeSinceStartup;
            DebugReport.Event("АВТОМЕТКА: " + why);
            if (delay > 0f) StartCoroutine(Later(why, delay)); else Mark(why);
        }
        IEnumerator Later(string why, float delay) { yield return new WaitForSecondsRealtime(delay); Mark(why); }

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
            PerfPanel.Open = false; ControlBanner.HiddenForShot = true;
            yield return null; yield return new WaitForEndOfFrame();
            Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture();
            PerfPanel.Open = wasOpen; ControlBanner.HiddenForShot = false;
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

        // ---------- снимки ракурсов для ассистента (команда photo) ----------
        // reports/photo0..19.jpg по кругу + reports/photos.txt — подпись, время, поза камеры. Без отчёта, без ограничения частоты.
        int photoSlot = -1;
        readonly System.Collections.Generic.Queue<string> photoIndex = new System.Collections.Generic.Queue<string>();
        public void Photo(string label, bool full)
        {
            if (!Ready) return;
            StartCoroutine(PhotoRoutine(label, full));
        }
        IEnumerator PhotoRoutine(string label, bool full)
        {
            if (photoSlot < 0) photoSlot = 0;
            int slot = photoSlot; photoSlot = (photoSlot + 1) % 20;
            bool wasOpen = PerfPanel.Open; PerfPanel.Open = false; ControlBanner.HiddenForShot = true;
            yield return null; yield return new WaitForEndOfFrame();
            Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture();
            PerfPanel.Open = wasOpen; ControlBanner.HiddenForShot = false;
            if (shot == null) yield break;
            int w = full ? shot.width : shot.width / 2, h = full ? shot.height : shot.height / 2;
            RenderTexture rt = RenderTexture.GetTemporary(w, h, 0);
            Graphics.Blit(shot, rt);
            RenderTexture prev = RenderTexture.active; RenderTexture.active = rt;
            Texture2D small = new Texture2D(w, h, TextureFormat.RGB24, false);
            small.ReadPixels(new Rect(0, 0, w, h), 0, 0); small.Apply();
            RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
            Enqueue("reports/photo" + slot + ".jpg", small.EncodeToJPG(full ? 88 : 80));
            Destroy(shot); Destroy(small);
            Camera cam = Camera.main;
            string pose = cam != null ? " · кам " + Vec(cam.transform.position) + " курс " + Mathf.RoundToInt(cam.transform.eulerAngles.y) + " накл " + Mathf.RoundToInt(Mathf.DeltaAngle(0f, cam.transform.eulerAngles.x)) + " обзор " + Mathf.RoundToInt(cam.fieldOfView) : "";
            photoIndex.Enqueue("photo" + slot + " · " + System.DateTime.Now.ToString("HH:mm:ss") + " · " + label + pose + " · FPS " + (1f / Mathf.Max(1e-3f, Time.smoothDeltaTime)).ToString("F0"));
            while (photoIndex.Count > 20) photoIndex.Dequeue();
            Enqueue("reports/photos.txt", Encoding.UTF8.GetBytes(string.Join("\n", photoIndex.ToArray()) + "\n"));
        }
        static string Vec(Vector3 v) { return Mathf.RoundToInt(v.x) + "," + Mathf.RoundToInt(v.y) + "," + Mathf.RoundToInt(v.z); }

        void Enqueue(string path, byte[] data)
        {
            queue.Enqueue(new KeyValuePair<string, byte[]>(path, data));
            if (!busy) StartCoroutine(Pump());
        }

        // ---------- без сети: исходящие ждут на телефоне ----------
        // Неотправленное (нет сети, ошибка сервера) кладётся в persistentDataPath/outbox — один файл на путь в репозитории
        // (новый отчёт заменяет старый неотправленный, ничего не копится). Сеть появилась — отправка раз в 60 с.
        // Переживает перезапуск игры. Предел 25 МБ: сверх — старые снимки удаляются первыми.
        static string OutboxDir { get { return System.IO.Path.Combine(Application.persistentDataPath, "outbox"); } }
        float nextOutbox = 30f;
        static string OutName(string path) { return path.Replace('/', '~'); }

        void SaveOutbox(string path, byte[] data)
        {
            try
            {
                System.IO.Directory.CreateDirectory(OutboxDir);
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(OutboxDir, OutName(path)), data);
                long total = 0; System.IO.FileInfo[] files = new System.IO.DirectoryInfo(OutboxDir).GetFiles();
                System.Array.Sort(files, (a, b) => a.LastWriteTimeUtc.CompareTo(b.LastWriteTimeUtc));
                foreach (System.IO.FileInfo f in files) total += f.Length;
                for (int i = 0; i < files.Length && total > 25L * 1024 * 1024; i++) { total -= files[i].Length; files[i].Delete(); }
            }
            catch (System.Exception) { }
        }

        public static int OutboxCount
        {
            get { try { return System.IO.Directory.Exists(OutboxDir) ? System.IO.Directory.GetFiles(OutboxDir).Length : 0; } catch (System.Exception) { return 0; } }
        }

        void RetryOutbox()
        {
            if (busy || Application.internetReachability == NetworkReachability.NotReachable) return;
            try
            {
                if (!System.IO.Directory.Exists(OutboxDir)) return;
                foreach (string f in System.IO.Directory.GetFiles(OutboxDir))
                {
                    string path = System.IO.Path.GetFileName(f).Replace('~', '/');
                    byte[] data = System.IO.File.ReadAllBytes(f);
                    System.IO.File.Delete(f);   // не ушло — PutRoutine положит обратно
                    queue.Enqueue(new KeyValuePair<string, byte[]>(path, data));
                }
            }
            catch (System.Exception) { }
            if (queue.Count > 0 && !busy) StartCoroutine(Pump());
        }

        IEnumerator Pump()
        {
            busy = true;
            while (queue.Count > 0)
            {
                KeyValuePair<string, byte[]> job = queue.Dequeue();
                if (Application.internetReachability == NetworkReachability.NotReachable)
                {
                    SaveOutbox(job.Key, job.Value);
                    LastStatus = "нет сети: ждут отправки " + OutboxCount + " · " + System.DateTime.Now.ToString("HH:mm:ss");
                    continue;
                }
                yield return PutRoutine(job.Key, job.Value);
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

        /// <summary>Перезаписать файл в Sky-reports (в очередь).</summary>
        public void Put(string path, byte[] data) { if (Ready) Enqueue(path, data); }

        IEnumerator PutRoutine(string path, byte[] data)
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
                // Сеть пропала или сервер упал (не 4xx от GitHub) — подождёт в outbox.
                if (!ok && (put.result == UnityWebRequest.Result.ConnectionError || put.responseCode == 0 || put.responseCode >= 500 || put.responseCode == 409))
                    SaveOutbox(path, data);
                int wait = OutboxCount;
                LastStatus = (ok ? "отправлено: " : "ошибка отправки (" + put.responseCode + "): ") + path + " · " + System.DateTime.Now.ToString("HH:mm:ss") + (wait > 0 ? " · ждут " + wait : "");
            }
        }
    }
}
