using System.Collections.Generic;
using System.Diagnostics;

namespace SkyTide
{
    /// <summary>
    /// Замер времени процессора по нашим системам прямо в релизной сборке (Stopwatch, без профайлера).
    /// Использование: long t = PerfTimers.Start(); ... PerfTimers.Stop("Экипаж", t);
    /// Панель FPS раз в кадр забирает суммы (Collect) и показывает сглаженные значения в мс.
    /// Пока панель закрыта, замер выключен и ничего не стоит.
    /// </summary>
    public static class PerfTimers
    {
        public static bool Enabled;
        static readonly Dictionary<string, long> frame = new Dictionary<string, long>();
        static readonly Dictionary<string, float> smooth = new Dictionary<string, float>();
        static readonly List<string> keys = new List<string>();
        static readonly double msPerTick = 1000.0 / Stopwatch.Frequency;

        public static long Start() { return Enabled ? Stopwatch.GetTimestamp() : 0L; }

        public static void Stop(string name, long start)
        {
            if (!Enabled || start == 0L) return;
            long d = Stopwatch.GetTimestamp() - start;
            long v; frame.TryGetValue(name, out v); frame[name] = v + d;
        }

        /// <summary>Конец кадра: суммы кадра → сглаженные мс (системы, не вызванные в кадре, плавно уходят к нулю).</summary>
        public static void Collect()
        {
            keys.Clear(); keys.AddRange(smooth.Keys);
            foreach (KeyValuePair<string, long> kv in frame) if (!smooth.ContainsKey(kv.Key)) { smooth[kv.Key] = 0f; keys.Add(kv.Key); }
            foreach (string k in keys)
            {
                long v; frame.TryGetValue(k, out v);
                smooth[k] = smooth[k] * 0.9f + (float)(v * msPerTick) * 0.1f;
            }
            frame.Clear();
        }

        /// <summary>Системы по убыванию времени.</summary>
        public static List<KeyValuePair<string, float>> Sorted(List<KeyValuePair<string, float>> into)
        {
            into.Clear();
            foreach (KeyValuePair<string, float> kv in smooth) into.Add(kv);
            into.Sort((a, b) => b.Value.CompareTo(a.Value));
            return into;
        }

        public static void Reset() { frame.Clear(); smooth.Clear(); }
    }
}
