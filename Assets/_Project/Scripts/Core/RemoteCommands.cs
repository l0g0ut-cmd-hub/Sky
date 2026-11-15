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
                PlayerPrefs.SetInt(LastKey, b.id); PlayerPrefs.Save();
                if (b.expires > 0 && now > b.expires) { Status = "команды: #" + b.id + " просрочена"; DebugReport.Event("команды #" + b.id + " просрочены — пропущены"); yield break; }
                StringBuilder ack = new StringBuilder("команды #" + b.id + " · " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " · сборка " + DebugReport.BuildNo + "\n");
                foreach (string c in b.cmds)
                {
                    if (string.IsNullOrEmpty(c)) continue;
                    if (c.StartsWith("wait"))
                    {
                        float s = 2f; float.TryParse(c.Substring(4).Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out s);
                        yield return new WaitForSecondsRealtime(Mathf.Clamp(s, 0.1f, 60f));
                        ack.AppendLine(c + " → ок"); continue;
                    }
                    ack.AppendLine(c + " → " + DevCommands.Run(c));
                    yield return null;
                }
                Status = "команды: выполнена #" + b.id;
                LiveUntil = Time.realtimeSinceStartup + 600f;
                ReportUploader u = ReportUploader.Instance;
                if (u != null) { u.Put("reports/ack.txt", Encoding.UTF8.GetBytes(ack.ToString())); u.UploadReport(); }
            }
        }
    }
}
