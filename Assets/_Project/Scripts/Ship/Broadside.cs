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
            List<CannonMount> list = new List<CannonMount>();
            foreach (CannonMount m in mounts) if (Mathf.Sign(m.side) == Mathf.Sign(side)) list.Add(m);
            list.Sort((a, b) => b.Muzzle.z.CompareTo(a.Muzzle.z));   // от носа к корме
            for (int i = 0; i < list.Count; i++)
                queue.Add(new KeyValuePair<float, CannonMount>(now + (ripple ? i * rippleDelay + UnityEngine.Random.Range(0f, 0.03f) : UnityEngine.Random.Range(0f, 0.025f)), list[i]));
            return true;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt > 0f)
            {
                velocity = (transform.position - lastPos) / dt;
                lastPos = transform.position;
            }
            Pump(Application.isPlaying ? Time.time : float.MaxValue);
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
            if (recoilEnabled)
            {
                int id = Array.IndexOf(mounts, m);
                if (id >= 0) firedAt[id] = now;
            }
            if (ship != null) ship.Recoil(m.side);
        }
    }
}
