using System.Collections.Generic;
using UnityEngine;

namespace SkyTide
{
    /// <summary>
    /// Звук: выстрелы, попадания, падения ядер в облака, гибель корабля, скрип и паруса своего корабля.
    /// Источники — пул AudioSource в мире (3D, логарифмическое затухание). Дальние звуки приходят с задержкой
    /// по скорости звука (343 м/с): вспышку дальнего залпа видно раньше, чем слышно.
    /// Звуки: Resources/Audio (CC0 «naval battle» с OpenGameArt; скрип/паруса/лязг — Oculus Audio Pack 01, CC BY 4.0,
    /// см. Resources/Audio/CREDITS.txt).
    /// </summary>
    public class SkySound : MonoBehaviour
    {
        public static SkySound Instance { get; private set; }
        const int Voices = 20;
        const float SpeedOfSound = 343f;

        AudioClip fire, hit, hitHeavy, miss, destroyed, metal, sail, clank;
        AudioClip[] creaks;
        readonly AudioSource[] pool = new AudioSource[Voices];
        int next;
        AudioSource sailSource, creakSource;
        float nextCreak;
        ShipController ship;
        struct Pending { public float at; public AudioClip clip; public Vector3 pos; public float vol, pitch, minD, maxD; }
        readonly List<Pending> pending = new List<Pending>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!Application.isPlaying || Instance != null) return;
            GameObject go = new GameObject("SkySound");
            DontDestroyOnLoad(go);
            go.AddComponent<SkySound>();
        }

        void Awake()
        {
            Instance = this;
            fire = Load("cannon_fire"); hit = Load("hit_ship"); hitHeavy = Load("hit_ship_heavy"); miss = Load("cannon_miss");
            destroyed = Load("ship_destroyed"); metal = Load("hit_metal"); sail = Load("sail_loop"); clank = Load("reload_clank");
            creaks = new[] { Load("creak_1"), Load("creak_2") };
            for (int i = 0; i < Voices; i++)
            {
                GameObject v = new GameObject("Voice " + i);
                v.transform.SetParent(transform, false);
                AudioSource s = v.AddComponent<AudioSource>();
                s.playOnAwake = false; s.spatialBlend = 1f; s.rolloffMode = AudioRolloffMode.Logarithmic; s.dopplerLevel = 0f;
                pool[i] = s;
            }
            if (FindFirstObjectByType<AudioListener>() == null && Camera.main != null) Camera.main.gameObject.AddComponent<AudioListener>();
        }

        static AudioClip Load(string name) { return Resources.Load<AudioClip>("Audio/" + name); }

        Vector3 Listener { get { AudioListener l = FindListener(); return l != null ? l.transform.position : Vector3.zero; } }
        AudioListener listener;
        AudioListener FindListener() { if (listener == null) listener = FindFirstObjectByType<AudioListener>(); return listener; }

        /// <summary>Сыграть в точке мира; дальнее — с задержкой по скорости звука.</summary>
        public void At(AudioClip clip, Vector3 pos, float volume, float pitchJitter = 0.06f, float minDist = 25f, float maxDist = 1400f)
        {
            if (clip == null) return;
            float d = Vector3.Distance(pos, Listener);
            float pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
            Pending p = new Pending { at = Time.time + (d > 60f ? d / SpeedOfSound : 0f), clip = clip, pos = pos, vol = volume, pitch = pitch, minD = minDist, maxD = maxDist };
            if (p.at <= Time.time) Play(p); else pending.Add(p);
        }

        void Play(Pending p)
        {
            AudioSource s = pool[next]; next = (next + 1) % Voices;
            s.transform.position = p.pos;
            s.clip = p.clip; s.volume = p.vol; s.pitch = p.pitch; s.minDistance = p.minD; s.maxDistance = p.maxD;
            s.Play();
        }

        // ---------- события игры ----------
        public static void CannonFired(Vector3 muzzle, bool hostile) { if (Instance != null) Instance.At(Instance.fire, muzzle, hostile ? 0.9f : 0.75f, 0.08f, 30f, 2000f); }
        public static void HullHit(Vector3 at, bool heavy) { if (Instance != null) Instance.At(heavy ? Instance.hitHeavy : Instance.hit, at, 1f, 0.07f, 30f, 1600f); }
        public static void CloudMiss(Vector3 at) { if (Instance != null) Instance.At(Instance.miss, at, 0.55f, 0.12f, 20f, 900f); }
        public static void ShipDestroyed(Vector3 at) { if (Instance != null) Instance.At(Instance.destroyed, at, 1f, 0.03f, 60f, 2500f); }
        public static void MetalHit(Vector3 at) { if (Instance != null) Instance.At(Instance.metal, at, 0.7f, 0.08f, 20f, 900f); }
        public static void Reloaded(Vector3 at) { if (Instance != null) Instance.At(Instance.clank, at, 0.35f, 0.05f, 8f, 150f); }

        void Update()
        {
            for (int i = pending.Count - 1; i >= 0; i--)
                if (Time.time >= pending[i].at) { Play(pending[i]); pending.RemoveAt(i); }
            if (ship == null)
            {
                PlayerHull h = PlayerHull.Instance;
                ship = h != null ? h.GetComponent<ShipController>() : null;
                if (ship == null) return;
                sailSource = MakeLoop(ship.transform, sail);
                creakSource = ship.gameObject.AddComponent<AudioSource>();
                creakSource.playOnAwake = false; creakSource.spatialBlend = 0.6f; creakSource.minDistance = 10f; creakSource.maxDistance = 200f;
            }
            // Паруса громче на ходу, скрип — чаще на поворотах; на палубе всё это под меню не звучит.
            bool quiet = MainMenu.Active || StudioSplash.Active;
            float speed01 = Mathf.Clamp01(Mathf.Abs(ship.Speed) / Mathf.Max(1f, ship.maxSpeed));
            if (sailSource != null) sailSource.volume = Mathf.MoveTowards(sailSource.volume, quiet ? 0f : 0.12f + 0.35f * speed01, Time.deltaTime * 0.5f);
            if (!quiet && Time.time >= nextCreak && creakSource != null && creaks.Length > 0)
            {
                AudioClip c = creaks[Random.Range(0, creaks.Length)];
                if (c != null) { creakSource.pitch = Random.Range(0.85f, 1.05f); creakSource.PlayOneShot(c, 0.25f + 0.35f * Mathf.Abs(ship.turn)); }
                nextCreak = Time.time + Random.Range(5f, 12f) / (1f + Mathf.Abs(ship.turn) * 1.5f);
            }
        }

        AudioSource MakeLoop(Transform parent, AudioClip clip)
        {
            if (clip == null) return null;
            AudioSource s = parent.gameObject.AddComponent<AudioSource>();
            s.clip = clip; s.loop = true; s.volume = 0f; s.spatialBlend = 0.4f; s.minDistance = 15f; s.maxDistance = 300f;
            s.Play();
            return s;
        }
    }
}
