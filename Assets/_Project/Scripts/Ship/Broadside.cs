using System;
using System.Collections.Generic;
using UnityEngine;

namespace SkyTide
{
    /// <summary>Пушка модели: дуло и ось ствола в координатах модели (tools/extract_cannons.py).</summary>
    [Serializable]
    public class CannonMount
    {
        public string name;
        public float side;       // -1 левый борт, +1 правый
        public float[] muzzle;
        public float[] dir;
        public float length;

        public Vector3 Muzzle { get { return new Vector3(muzzle[0], muzzle[1], muzzle[2]); } }
        public Vector3 Dir { get { return new Vector3(dir[0], dir[1], dir[2]); } }
    }

    [Serializable]
    class CannonList { public CannonMount[] cannons; }

    /// <summary>
    /// Бортовой залп: пушки одного борта стреляют волной от носа к корме. Ядро вылетает из дула строго по оси ствола
    /// (с учётом крена и качки модели) со скоростью корабля в придачу; дальше — баллистика (FxSystem).
    /// Отдача кренит корабль от стреляющего борта.
    /// </summary>
    public class Broadside : MonoBehaviour
    {
        public float muzzleSpeed = 85f;
        public float rippleDelay = 0.085f;
        public float cooldown = 2.2f;
        [Tooltip("true — волной от носа к корме, false — весь борт разом")]
        public bool ripple = true;
        [NonSerialized] public ShipCrew crew;

        ShipController ship;
        Transform model;
        CannonMount[] mounts = new CannonMount[0];
        readonly float[] ready = new float[2];
        readonly List<KeyValuePair<float, CannonMount>> queue = new List<KeyValuePair<float, CannonMount>>();
        Vector3 lastPos;
        Vector3 velocity;
        MeshRenderer gunRenderer;
        MaterialPropertyBlock gunProperties;
        readonly float[] firedAt = new float[16];
        readonly Vector4[] gunOffsets = new Vector4[16];
        static readonly int GunOffsetsId = Shader.PropertyToID("_ShipGunOffsets");
        bool recoilEnabled, recoilWasActive;
#if UNITY_EDITOR
        bool recoilCapture;
#endif

        public bool HasCannons { get { return mounts.Length > 0; } }

        public static CannonMount[] Parse(TextAsset json)
        {
            if (json == null) return new CannonMount[0];
            CannonList l = JsonUtility.FromJson<CannonList>(json.text);
            return l != null && l.cannons != null ? l.cannons : new CannonMount[0];
        }

        public void SetModel(ShipController ctrl, Transform modelTransform, CannonMount[] cannons, TextAsset recoil = null)
        {
            ClearRecoil();
            ship = ctrl;
            model = modelTransform;
            mounts = cannons ?? new CannonMount[0];
            queue.Clear();
            gunRenderer = model != null ? model.GetComponent<MeshRenderer>() : null;
            recoilEnabled = recoil != null && gunRenderer != null;
            if (recoilEnabled)
            {
                ShipGunWeights.Map map = JsonUtility.FromJson<ShipGunWeights.Map>(recoil.text);
                if (map.names == null || map.names.Length != mounts.Length || mounts.Length != firedAt.Length)
                    throw new InvalidOperationException("Cannon recoil IDs do not match mounts");
                for (int i = 0; i < mounts.Length; i++)
                    if (map.names[i] != mounts[i].name) throw new InvalidOperationException("Cannon recoil mount order changed");
                if (gunProperties == null) gunProperties = new MaterialPropertyBlock();
                ClearRecoil();
            }
        }

        /// <summary>Fast inward kick followed by a damped run-out; fully home before reload completes.</summary>
        public static float CannonRecoil(float age)
        {
            if (age < 0f || age >= 0.9f) return 0f;
            float t = age < 0.06f ? age / 0.06f : (age - 0.06f) / 0.84f;
            float smooth = t * t * (3f - 2f * t);
            return 0.32f * (age < 0.06f ? smooth : 1f - smooth);
        }

        void ClearRecoil()
        {
            for (int i = 0; i < firedAt.Length; i++) { firedAt[i] = -100f; gunOffsets[i] = Vector4.zero; }
            recoilWasActive = false;
            PublishRecoil();
        }

        void PublishRecoil()
        {
            if (gunRenderer == null || gunProperties == null) return;
            gunRenderer.GetPropertyBlock(gunProperties);
            gunProperties.SetVectorArray(GunOffsetsId, gunOffsets);
            gunRenderer.SetPropertyBlock(gunProperties);
        }

        void ApplyRecoil(float now)
        {
            if (!recoilEnabled) return;
            bool animate = Application.isPlaying;
#if UNITY_EDITOR
            animate |= recoilCapture;
#endif
            bool active = false;
            for (int i = 0; i < mounts.Length; i++)
            {
                float amount = animate ? CannonRecoil(now - firedAt[i]) : 0f;
                Vector3 offset = -mounts[i].Dir.normalized * amount;
                gunOffsets[i] = new Vector4(offset.x, offset.y, offset.z, 0f);
                active |= amount > 0f;
            }
            if (active || recoilWasActive) PublishRecoil();
            recoilWasActive = active;
        }

        void OnDisable() { ClearRecoil(); queue.Clear(); }

        public float Readiness(int side)
        {
            int k = side < 0 ? 0 : 1;
            float now = Application.isPlaying ? Time.time : 0f;
            return Mathf.Clamp01(1f - (ready[k] - now) / cooldown);
        }

#if UNITY_EDITOR
        public void SetRecoilCapture(bool on) { recoilCapture = on; ClearRecoil(); }
        public Vector4 RecoilCapture(int index) { return gunOffsets[index]; }

        public void SetReadinessCapture(float port, float starboard)
        {
            ready[0] = (1f - Mathf.Clamp01(port)) * cooldown;
            ready[1] = (1f - Mathf.Clamp01(starboard)) * cooldown;
            queue.Clear();
        }
#endif

        /// <summary>Залп бортом: side = -1 левый, +1 правый.</summary>
        public bool Fire(int side)
        {
            if (model == null || mounts.Length == 0) return false;
            int k = side < 0 ? 0 : 1;
            float now = Application.isPlaying ? Time.time : 0f;
            if (now < ready[k]) return false;
            ready[k] = now + cooldown;
            if(Application.isPlaying&&Camera.main!=null)
            {
                OrbitCamera view=Camera.main.GetComponent<OrbitCamera>();
                if(view!=null&&view.target==transform)view.SalvoImpulse(side);
            }
            List<CannonMount> list = new List<CannonMount>();
            foreach (CannonMount m in mounts) if (Mathf.Sign(m.side) == Mathf.Sign(side)) list.Add(m);
            if(crew!=null)for(int gun=0;gun<2;gun++){CannonMount upper=crew.UpperMount(side,gun);if(upper!=null)list.Add(upper);}
            if(crew!=null)crew.NotifySalvo(now,side);
            list.Sort((a, b) => b.Muzzle.z.CompareTo(a.Muzzle.z));   // от носа к корме
            for (int i = 0; i < list.Count; i++)
                queue.Add(new KeyValuePair<float, CannonMount>(now + (ripple ? i * rippleDelay + UnityEngine.Random.Range(0f, 0.03f) : UnityEngine.Random.Range(0f, 0.025f)), list[i]));
            return true;
        }

        void Update(){long __pt=PerfTimers.Start();UpdateTimed();PerfTimers.Stop("Пушки",__pt);}
        void UpdateTimed()
        {
            float dt = Time.deltaTime;
            if (dt > 0f)
            {
                velocity = (transform.position - lastPos) / dt;
                lastPos = transform.position;
            }
            Pump(Application.isPlaying ? Time.time : float.MaxValue);
            if (Application.isPlaying) { AimPreview(); ReloadSound(); }
        }

        bool[] wasReady = { true, true };
        void ReloadSound()
        {
            if (mounts.Length == 0) return;
            for (int k = 0; k < 2; k++)
            {
                bool r = Readiness(k == 0 ? -1 : 1) >= 0.999f;
                if (r && !wasReady[k]) SkySound.Reloaded(transform.position + transform.right * (k == 0 ? -5f : 5f));
                wasReady[k] = r;
            }
        }

        [Tooltip("Прицельная дуга борта, обращённого к цели")]
        public bool showAim = true;

        /// <summary>
        /// Прицельная дуга: путь ядра средней пушки борта, обращённого к ближайшей цели (до 1.2 км, курсовой угол
        /// 35–145°), — пунктир светящихся точек до входа в море облаков или до корпуса цели; там — крупная отметка.
        /// Янтарная — борт готов, серая — перезарядка.
        /// </summary>
        public void AimPreview()
        {
            FxSystem fx = FxSystem.Instance;
            if (fx == null) return;
            fx.ClearPreview();
            if (!showAim || model == null || mounts.Length == 0) return;
            TargetShip best = null; float bestD = float.MaxValue;
            foreach (TargetShip t in TargetShip.All)
            {
                if (t == null || !t.Alive) continue;
                float d = Vector3.Distance(t.transform.position, transform.position);
                if (d < bestD) { bestD = d; best = t; }
            }
            // Дуга видна всегда — у борта, обращённого к ближайшей цели (нет цели — правый борт):
            // игрок видит, куда лягут ядра, и доворачивает. Раньше дуга пряталась, если цель не на траверзе 35–145°.
            float side = 1f;
            if (best != null) side = transform.InverseTransformPoint(best.transform.position).x >= 0f ? 1f : -1f;
            // Средняя по длине пушка этого борта.
            CannonMount mid = null; float midZ = 0f; int count = 0;
            foreach (CannonMount m in mounts) if (Mathf.Sign(m.side) == side) { midZ += m.Muzzle.z; count++; }
            if (count == 0) return;
            midZ /= count; float bestDz = float.MaxValue;
            foreach (CannonMount m in mounts) if (Mathf.Sign(m.side) == side && Mathf.Abs(m.Muzzle.z - midZ) < bestDz) { bestDz = Mathf.Abs(m.Muzzle.z - midZ); mid = m; }
            bool ready = Readiness((int)side) >= 0.999f;
            Vector3 p = model.TransformPoint(mid.Muzzle);
            Vector3 v = model.TransformDirection(mid.Dir).normalized * muzzleSpeed + velocity;
            float alpha = ready ? 0.95f : 0.55f;
            fx.AddAim(p + v.normalized * 2.5f, v, 3.2f, 4, 187f / 158f, alpha);   // шеврон у дула
            float g = fx.gravity, step = 0.06f;
            Vector3 segStart = p;
            for (int i = 0; i < 120; i++)
            {
                Vector3 prev = p;
                v.y -= g * step; p += v * step;
                Vector3 hp;
                if (best != null && (p - best.transform.position).sqrMagnitude < 45f * 45f && best.TryHit(prev, p, out hp))
                {
                    fx.AddAim(hp, Vector3.up, 9f, 3, 1f, ready ? 1f : 0.6f);   // прицел на корпусе цели
                    return;
                }
                if (p.y < fx.deckTop - 5f)
                {
                    fx.AddAim(prev, Vector3.up, 20f, 2, 1f, alpha);           // кольцо падения на облаках
                    return;
                }
                if (i % 3 == 2)
                {
                    // Штрих на отрезке пути (голова — вперёд по полёту), с промежутком.
                    Vector3 c = Vector3.Lerp(segStart, p, 0.5f);
                    float len = Vector3.Distance(segStart, p) * 0.8f;
                    fx.AddAim(c, p - segStart, len, ready ? 0 : 1, 104f / 523f * 1.6f, alpha);
                    segStart = p;
                }
            }
        }

        /// <summary>Выпустить ядра, чьё время пришло; CI тоже может прокручивать волну по времени.</summary>
        public void Pump(float now)
        {
            for (int i = 0; i < queue.Count;)
            {
                if (queue[i].Key > now) { i++; continue; }
                Shoot(queue[i].Value, now);
                queue.RemoveAt(i);
            }
            ApplyRecoil(now);
        }

        void Shoot(CannonMount m, float now)
        {
            if (model == null || FxSystem.Instance == null) return;
            Vector3 muzzle = model.TransformPoint(m.Muzzle);
            Vector3 dir = model.TransformDirection(m.Dir).normalized;
            FxSystem.Instance.Fire(muzzle, dir, velocity, muzzleSpeed);
            if(crew!=null)crew.NotifyShot(m,now);
            if (recoilEnabled)
            {
                int id = Array.IndexOf(mounts, m);
                if (id >= 0) firedAt[id] = now;
            }
            if (ship != null) ship.Recoil(m.side);
        }
    }
}
