using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SkyTide
{
    /// <summary>
    /// Запись боя для повтора (replay): 2 раза в секунду положение, курс, прочность всех кораблей; последние 15 минут.
    /// Уходит в Sky-reports/reports/replay.bin (перезапись) раз в 10 минут и при сворачивании. tools/replay.py
    /// рисует из него видео боя сверху (траектории, курсы, гибели).
    /// Формат: "SKYR", байт версии 1, int сборка, short x/z крепости (2 м); кадры: float t, байт n, n × 9 байт
    /// {id, вид (0 флагман, 1 враг, 2 тяжёлый, 3 страж, 4 ведомый, 5 торговец), short x, short z (2 м), курс (1.5°), %, флаги (1 жив, 2 автобой)}.
    /// </summary>
    public class ReplayRecorder : MonoBehaviour
    {
        readonly Queue<byte[]> frames = new Queue<byte[]>();
        float nextFrame, nextUpload = 600f;
        readonly MemoryStream ms = new MemoryStream(256);

        void Update()
        {
            if (Time.time < nextFrame) return;
            nextFrame = Time.time + 0.5f;
            ms.SetLength(0);
            BinaryWriter w = new BinaryWriter(ms);
            w.Write(Time.timeSinceLevelLoad);
            long countAt = ms.Position; w.Write((byte)0);
            int n = 0;
            PlayerHull ph = PlayerHull.Instance;
            SkyTideBootstrap g = SkyTideBootstrap.Instance;
            TouchControls tc = g != null ? g.GetComponent<TouchControls>() : null;
            if (ph != null) { Ship(w, 0, 0, ph.transform, ph.Health01, ph.Alive, tc != null && tc.AutoBattle); n++; }
            int id = 1;
            foreach (TargetShip t in TargetShip.All)
            {
                if (t != null && t.gameObject.activeInHierarchy && n < 250) { Ship(w, id, t.Heavy ? 2 : 1, t.transform, t.Health01, t.Alive, false); n++; }
                id++;
            }
            id = 100;
            foreach (AllyShip a in AllyShip.All)
            {
                if (a != null && a.gameObject.activeInHierarchy && n < 250) { Ship(w, id, 3 + (int)a.role, a.transform, a.Health01, a.Alive, false); n++; }
                id++;
            }
            w.Flush();
            byte[] f = ms.ToArray(); f[countAt] = (byte)n;
            frames.Enqueue(f);
            while (frames.Count > 1800) frames.Dequeue();
            if (Time.realtimeSinceStartup >= nextUpload) { nextUpload = Time.realtimeSinceStartup + 600f; Upload(); }
        }

        static short C(float v) { return (short)Mathf.Clamp(Mathf.RoundToInt(v / 2f), short.MinValue, short.MaxValue); }

        static void Ship(BinaryWriter w, int id, int kind, Transform t, float health, bool alive, bool auto)
        {
            Vector3 p = t.position;
            w.Write((byte)id); w.Write((byte)kind); w.Write(C(p.x)); w.Write(C(p.z));
            w.Write((byte)(Mathf.Repeat(t.eulerAngles.y, 360f) / 1.5f));
            w.Write((byte)Mathf.Clamp(Mathf.RoundToInt(health * 100f), 0, 100));
            w.Write((byte)((alive ? 1 : 0) | (auto ? 2 : 0)));
        }

        public byte[] Bytes()
        {
            MemoryStream o = new MemoryStream();
            BinaryWriter w = new BinaryWriter(o);
            w.Write(new[] { (byte)'S', (byte)'K', (byte)'Y', (byte)'R' }); w.Write((byte)1); w.Write(DebugReport.BuildNo);
            SkyFortress f = SkyFortress.Instance;
            w.Write(f != null ? C(f.Center.x) : (short)0); w.Write(f != null ? C(f.Center.z) : (short)0);
            foreach (byte[] fr in frames) w.Write(fr);
            w.Flush();
            return o.ToArray();
        }

        void Upload()
        {
            if (frames.Count < 60 || ReportUploader.Instance == null || CiBattle.Active) return;
            ReportUploader.Instance.Put("reports/replay.bin", Bytes());
        }

        void OnApplicationPause(bool paused) { if (paused && Time.realtimeSinceStartup > 90f) Upload(); }
    }
}
