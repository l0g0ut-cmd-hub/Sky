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

        public bool HasCannons { get { return mounts.Length > 0; } }

        public static CannonMount[] Parse(TextAsset json)
        {
            if (json == null) return new CannonMount[0];
            CannonList l = JsonUtility.FromJson<CannonList>(json.text);
            return l != null && l.cannons != null ? l.cannons : new CannonMount[0];
        }

        public void SetModel(ShipController ctrl, Transform modelTransform, CannonMount[] cannons)
        {
            ship = ctrl;
            model = modelTransform;
            mounts = cannons ?? new CannonMount[0];
            queue.Clear();
        }

        public float Readiness(int side)
        {
            int k = side < 0 ? 0 : 1;
            return Mathf.Clamp01(1f - (ready[k] - Time.time) / cooldown);
        }

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

        /// <summary>Выпустить ядра, чьё время пришло (для кадров в CI — все сразу).</summary>
        public void Pump(float now)
        {
            for (int i = queue.Count - 1; i >= 0; i--)
            {
                if (queue[i].Key > now) continue;
                Shoot(queue[i].Value);
                queue.RemoveAt(i);
            }
        }

        void Shoot(CannonMount m)
        {
            if (model == null || FxSystem.Instance == null) return;
            Vector3 muzzle = model.TransformPoint(m.Muzzle);
            Vector3 dir = model.TransformDirection(m.Dir).normalized;
            FxSystem.Instance.Fire(muzzle, dir, velocity, muzzleSpeed);
            if (ship != null) ship.Recoil(m.side);
        }
    }
}
