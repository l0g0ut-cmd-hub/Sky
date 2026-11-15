using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace SkyTide
{
    /// <summary>
    /// Обновление внутри игры (OTA для сайдлоада): CI кладёт каждый APK в релиз «apk» репозитория Sky-reports
    /// (название релиза «build N»). При запуске игра сверяет N со своим номером; новее — надпись и кнопка
    /// «СКАЧАТЬ НОВУЮ СБОРКУ» (панель FPS → «Части» → «Тест»), браузер качает APK, установка — одним касанием.
    /// </summary>
    public class UpdateCheck : MonoBehaviour
    {
        public static bool Available { get; private set; }
        public static int LatestBuild { get; private set; }
        public static string Status = "обновление: не проверено";
        static string url;

        IEnumerator Start()
        {
            if (CiBattle.Active) yield break;
            yield return new WaitForSecondsRealtime(10f);
            TextAsset t = Resources.Load<TextAsset>("ReportToken");
            string token = t != null ? t.text.Trim() : null;
            if (string.IsNullOrEmpty(token)) { Status = "обновление: нет токена"; yield break; }
            using (UnityWebRequest req = UnityWebRequest.Get("https://api.github.com/repos/" + ReportUploader.Repo + "/releases/tags/apk"))
            {
                req.SetRequestHeader("Authorization", "Bearer " + token);
                req.SetRequestHeader("Accept", "application/vnd.github+json");
                req.SetRequestHeader("User-Agent", "SkyTide");
                req.timeout = 15;
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success) { Status = "обновление: релиза нет (" + req.responseCode + ")"; yield break; }
                string j = req.downloadHandler.text;
                int n = 0;
                int i = j.IndexOf("\"name\":\"build ", System.StringComparison.Ordinal);
                if (i >= 0) { i += 14; int k = i; while (k < j.Length && char.IsDigit(j[k])) k++; int.TryParse(j.Substring(i, k - i), out n); }
                int u = j.IndexOf("\"browser_download_url\":\"", System.StringComparison.Ordinal);
                if (u >= 0) { u += 24; int e = j.IndexOf('"', u); if (e > u) url = j.Substring(u, e - u); }
                LatestBuild = n;
                int mine = DebugReport.BuildNo;
                Available = n > mine && !string.IsNullOrEmpty(url);
                Status = Available ? "обновление: есть сборка " + n + " (у тебя " + mine + ")" : "обновление: актуальная (" + mine + ")";
                if (Available) { HudNotice.Show("НОВАЯ СБОРКА " + n + ": FPS → Части → Тест → СКАЧАТЬ", 0, 8f); DebugReport.Event("доступна сборка " + n); }
            }
        }

        public static void Download()
        {
            if (string.IsNullOrEmpty(url)) return;
            DebugReport.Event("скачивание сборки " + LatestBuild);
            Application.OpenURL(url);
        }
    }
}
