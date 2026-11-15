using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace SkyTide
{
    /// <summary>
    /// Удалённые команды во время игры (remote debug console): раз в 15 с игра читает Sky-reports/config/commands.json
    ///   {"id": 7, "expires": 1763190000, "cmds": ["near", "shot враг рядом", "set clouds 1"]}
    /// и выполняет новые (id больше последнего выполненного, срок expires — unix-время — не истёк) через DevCommands.
    /// Итог — в reports/ack.txt и сразу свежий отчёт в latest.txt. 10 минут после команды отчёт уходит раз в 30 с
    /// (живая отладка). Опрос с ETag: неизменённый файл (304) не тратит лимит GitHub.
    /// Плюс сердцебиение reports/alive.txt раз в 2 мин — видно, что игра запущена прямо сейчас.
    /// </summary>
    public class RemoteCommands : MonoBehaviour
    {
        [System.Serializable] class Batch { public int id; public long expires; public string[] cmds; }
        public static string Status = "команды: нет токена";
        public static float LiveUntil = -1f;
        public static bool Running;
        const string LastKey = "SkyTide.RemoteCmdId";
        string token, etag;
        float nextPoll = 12f, nextAlive = 20f;

        void Awake()
        {
            TextAsset t = Resources.Load<TextAsset>("ReportToken");
            token = t != null ? t.text.Trim() : null;
            if (!string.IsNullOrEmpty(token)) Status = "команды: ждём";
        }

        void Update()
        {
            if (string.IsNullOrEmpty(token) || CiBattle.Active) return;
            if (Application.internetReachability == NetworkReachability.NotReachable) { Status = "команды: нет сети"; return; }
            if (Running) return;   // одна партия за раз — 15.11 две партии шли параллельно, первая отпустила управление второй
            if (Time.realtimeSinceStartup >= nextPoll) { nextPoll = Time.realtimeSinceStartup + 15f; StartCoroutine(Poll()); }
            if (Time.realtimeSinceStartup >= nextAlive && ReportUploader.Instance != null)
            {
                nextAlive = Time.realtimeSinceStartup + 120f;
                ReportUploader.Instance.Put("reports/alive.txt", Encoding.UTF8.GetBytes(DebugReport.AliveLine()));
            }
        }

        IEnumerator Poll()
        {
            using (UnityWebRequest req = UnityWebRequest.Get("https://api.github.com/repos/" + ReportUploader.Repo + "/contents/config/commands.json"))
            {
                req.SetRequestHeader("Authorization", "Bearer " + token);
                req.SetRequestHeader("Accept", "application/vnd.github.raw+json");
                req.SetRequestHeader("User-Agent", "SkyTide");
                if (etag != null) req.SetRequestHeader("If-None-Match", etag);
                req.timeout = 12;
                yield return req.SendWebRequest();
                if (req.responseCode == 304) yield break;
                if (req.responseCode == 404) { Status = "команды: файла нет"; yield break; }
                if (req.result != UnityWebRequest.Result.Success) { Status = "команды: сеть " + req.responseCode; yield break; }
                etag = req.GetResponseHeader("ETag");
                Batch b = null;
                try { b = JsonUtility.FromJson<Batch>(req.downloadHandler.text); } catch (System.Exception) { b = null; }
                if (b == null || b.cmds == null) { Status = "команды: файл не разобран"; yield break; }
                int last = PlayerPrefs.GetInt(LastKey, -1);
                long now = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                if (b.id <= last || b.cmds.Length == 0) { Status = "команды: последняя #" + Mathf.Max(last, b.id); if (b.id > last) { PlayerPrefs.SetInt(LastKey, b.id); PlayerPrefs.Save(); } yield break; }
                // В главном меню партия не стартует (иначе табличка «УПРАВЛЯЕТ CLAUDE» висела над меню, а команды шли вхолостую):
                // ждём боя; партия, начинающаяся с play, сама жмёт «В БОЙ».
                if (MainMenu.Active && b.cmds[0] != "play") { Status = "команды: #" + b.id + " ждёт боя"; yield break; }
                PlayerPrefs.SetInt(LastKey, b.id); PlayerPrefs.Save();
                if (b.expires > 0 && now > b.expires) { Status = "команды: #" + b.id + " просрочена"; DebugReport.Event("команды #" + b.id + " просрочены — пропущены"); yield break; }
                // Оценка длительности партии — для таблички «УПРАВЛЯЕТ CLAUDE · ЖДИ M:SS».
                float est = 1f; bool hold = false;
                foreach (string c0 in b.cmds)
                {
                    if (string.IsNullOrEmpty(c0)) continue;
                    string[] p = c0.Split(' ');
                    float n = 0f; if (p.Length > 1) float.TryParse(p[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out n);
                    if (p[0] == "wait") est += Mathf.Clamp(n, 0.1f, 60f);
                    else if (p[0] == "fps") est += 1f;   // замер идёт параллельно — время даёт следующий wait
                    else if (p[0] == "photo" || p[0] == "shot") est += 0.5f;
                    else if (p[0] == "hold") hold = true;
                    else est += 0.05f;
                }
                ControlBanner.BusyUntil = Time.realtimeSinceStartup + est; ControlBanner.Cancelled = false;
                Running = true;
                StringBuilder ack = new StringBuilder("команды #" + b.id + " · " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " · сборка " + DebugReport.BuildNo + "\n");
                foreach (string c in b.cmds)
                {
                    if (string.IsNullOrEmpty(c)) continue;
                    if (ControlBanner.Cancelled) { ack.AppendLine("ОТМЕНЕНО владельцем на: " + c); break; }
                    if (c == "hold") { ack.AppendLine("hold → управление остаётся"); continue; }
                    if (c.StartsWith("wait"))
                    {
                        float s = 2f; float.TryParse(c.Substring(4).Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out s);
                        yield return new WaitForSecondsRealtime(Mathf.Clamp(s, 0.1f, 60f));
                        ack.AppendLine(c + " → ок"); continue;
                    }
                    ack.AppendLine(c + " → " + DevCommands.Run(c));
                    yield return null;
                    // Снимок делается в конце следующего кадра — без паузы следующая команда успевала сдвинуть камеру
                    // (сборка 127: каждый снимок показывал следующий ракурс).
                    if (c.StartsWith("photo") || c.StartsWith("shot") || c.StartsWith("mark")) { yield return null; yield return new WaitForEndOfFrame(); yield return null; yield return null; }
                    // Нажатие: касание-отпускание занимает кадр.
                    if (c.StartsWith("tap")) { yield return null; yield return null; }
                }
                Running = false; ReportUploader.SnapEvery = 0f; ControlBanner.AwaitUpload = true;
                // Конец партии — управление владельцу (кроме hold).
                ControlBanner.BusyUntil = -1f;
                if (!hold) { RemotePilot.Release(); RemoteCam.Off(); TouchControls.HideHud = false; }
                Status = "команды: выполнена #" + b.id;
                LiveUntil = Time.realtimeSinceStartup + 600f;
                ReportUploader u = ReportUploader.Instance;
                if (u != null) { u.Put("reports/ack.txt", Encoding.UTF8.GetBytes(ack.ToString())); u.UploadReport(); }
            }
        }
    }
}
