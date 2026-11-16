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
        /// <summary>Заряд: false — ядра (корпус), true — книппели (баллоны и ход; летят медленнее и ближе).</summary>
        [NonSerialized] public bool chainShot;
        float ShotSpeed { get { return chainShot ? muzzleSpeed * 0.8f : muzzleSpeed; } }
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
        /// <summary>Пушечная палуба в координатах модели: высота дул и их размах по длине корпуса.</summary>
        public bool GunDeck(out float y, out float zMin, out float zMax)
        {
            y = 0f; zMin = float.MaxValue; zMax = float.MinValue;
            if (mounts.Length == 0) return false;
            for (int i = 0; i < mounts.Length; i++)
            {
                Vector3 m = mounts[i].Muzzle;
                y += m.y; zMin = Mathf.Min(zMin, m.z); zMax = Mathf.Max(zMax, m.z);
            }
            y /= mounts.Length;
            return true;
        }

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
            PlayerHull own = GetComponent<PlayerHull>();
            if (own != null && !own.Alive) return false;   // подбитый корабль не стреляет
            int k = side < 0 ? 0 : 1;
            float now = Application.isPlaying ? Time.time : 0f;
            if (now < ready[k]) return false;
            ready[k] = now + cooldown;
            if (Application.isPlaying)
            {
                DebugReport.Count(AimOnTarget ? "залпов флагмана" : "залпов флагмана вне прицела");
                TargetShip nearestFoe = null; float nf = float.MaxValue;
                foreach (TargetShip t in TargetShip.All) { if (t == null || !t.Alive) continue; float d = Vector3.Distance(t.transform.position, transform.position); if (d < nf) { nf = d; nearestFoe = t; } }
                if (GetComponent<PlayerHull>() != null) DebugReport.Volley("флагман " + (side < 0 ? "лев" : "прав") + (nearestFoe != null ? " → " + nearestFoe.name.Replace(" Ship", "") + " " + Mathf.RoundToInt(nf) + " м, угол " + Mathf.RoundToInt(Vector3.SignedAngle(transform.forward, nearestFoe.transform.position - transform.position, Vector3.up)) + "°" : " (врагов нет)") + (AimOnTarget ? ", в прицеле" : ", ВНЕ прицела") + (chainShot ? ", книппели" : ""));
            }
            if (Application.isPlaying) SkyFortress.NotifyPlayerFired(transform.position);   // залп в безопасной зоне — крепость откроет огонь
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

        /// <summary>Борт, у которого сейчас прицельная дуга (-1 левый, +1 правый), и ложится ли его залп в корпус цели —
        /// по этому кнопка борта на HUD пишет «В ПРИЦЕЛЕ».</summary>
        PlayerHull ownHull; bool ownTried; float ownRetry;
        public int AimSide { get; private set; }
        public bool AimOnTarget { get; private set; }

        // ---------- наводка борта ----------
        // Стволы в модели смотрят строго в борт и по горизонту: с высоты полёта ядро падало в облака через ~270 м, а в
        // корпус цели на той же высоте попадало только ближе ~100 м — при том, что враг держит 330 м и сам бьёт
        // навесом до 650 м. Поэтому «прицел не работал»: дуга всегда ложилась в облака под врагом.
        // Теперь расчёт борта, обращённого к цели, сам берёт возвышение по дальности (как наводчик врага) и доворачивает
        // стволы до ±10° в точку упреждения (цель идёт своим курсом, ядро несёт ход своего корабля). Дело игрока —
        // поставить цель на траверз: дальше ±30° от траверза стволы стоят прямо, как раньше.
        [Tooltip("Наводка: доворот стволов по горизонту и пределы возвышения, градусы")]
        public float traverseDeg = 10f, minElevationDeg = -8f, maxElevationDeg = 24f;
        bool solved; int solvedSide; Vector3 aimPoint, targetVel; float flightTime;
        // Второй борт (владелец 15.11: «окружён с двух сторон — бей обоими бортами»): своя цель и своё решение.
        bool offSolved; int offSide; Vector3 offAim, offVel; TargetShip offTarget;
        // Плавный переход «стволы прямо ↔ наведены» по бортам (0 — прямо, 1 — на цель): раньше на границе ±30° от траверза
        // (цель у носа) ствол каждый кадр прыгал между осью и решением — стрелку «ебошило» (видео владельца 15.11).
        readonly float[] aimBlend = new float[2];
        float sideHoldUntil = -1f;
        static int SideIdx(float s) { return s > 0f ? 1 : 0; }
        /// <summary>Борт, противоположный прицельному, и ляжет ли его залп в корпус своей цели.</summary>
        public int OffSide { get { return offSide; } }
        /// <summary>Цель, взятая игроком касанием по кораблю (владелец 15.11: «кого взял в таргет — по тому и целимся»).
        /// Без неё и без автобоя прицела нет: стрелка не рисуется, стволы стоят прямо.</summary>
        /// <summary>Взятая касанием цель — любой корабль: враг или свой (огонь по своим разрешён).</summary>
        public IAimTarget Locked;
        /// <summary>Точка упреждения по взятой цели (где она будет, когда долетит ядро); есть, пока залп не ложится в корпус.</summary>
        [System.NonSerialized] public bool HasLead; [System.NonSerialized] public Vector3 LeadPoint;
        /// <summary>Автобой сам выбирает цель (как раньше — ближайшую к траверзу).</summary>
        public bool AutoAim;
        /// <summary>Игрок держит кнопку борта (прицел как в Black Flag): борт задан, стрелка видна только сейчас; отпустил — залп.</summary>
        [System.NonSerialized] public int HoldSide;
        /// <summary>Цель прицельного борта (радар выделяет её).</summary>
        public TargetShip AimTargetShip { get { return aimTarget as TargetShip; } }
        public IAimTarget AimTarget { get { return aimTarget; } }
        public bool OffOnTarget { get; private set; }
        // Сглаженная точка наводки: стволы доворачиваются, а не прыгают за каждым кадром решения (владелец 15.11: «прицел дёргается»).
        Vector3 aimSmooth; IAimTarget aimSmoothFor; float aimSmoothAt = -1f;
        // Цель прицела с удержанием: меняется, только если новая заметно лучше (раньше — ближайшая каждый кадр, дуга прыгала).
        IAimTarget aimTarget; float aimSide = 1f;
        /// <summary>Дальность, на которую борт достаёт при предельном возвышении (для HUD).</summary>
        public float MaxRange { get { float g = FxSystem.Instance != null ? FxSystem.Instance.gravity : 9.81f; return ShotSpeed * ShotSpeed * Mathf.Sin(2f * maxElevationDeg * Mathf.Deg2Rad) / g; } }
        public bool HasSolution { get { return solved; } }
        public float FlightTime { get { return flightTime; } }

        float LowAngle(float x, float y, float g)
        {
            return Ballistics.ClampedLowAngle(x, y, ShotSpeed, g, minElevationDeg * Mathf.Deg2Rad, maxElevationDeg * Mathf.Deg2Rad);
        }

        void Solve(IAimTarget best, CannonMount mid, float side, float g)
        {
            solved = false;
            if (best == null || mid == null || model == null) return;
            Vector3 muzzle = model.TransformPoint(mid.Muzzle), own = velocity; own.y = 0f;
            Vector3 tpos = best.Center;
            targetVel = best.Velocity;
            Vector3 rel = targetVel - own;   // в системе своего корабля: его ход ядро уносит с собой
            Vector3 d0 = tpos - muzzle;
            float t = Ballistics.Lead(d0.x, d0.y, d0.z, rel.x, rel.y, rel.z, ShotSpeed, g, minElevationDeg * Mathf.Deg2Rad, maxElevationDeg * Mathf.Deg2Rad, out _, out _, out _);
            Vector3 raw = tpos + rel * t;
            if (float.IsNaN(raw.x) || float.IsNaN(raw.y) || float.IsNaN(raw.z) || float.IsNaN(t)) return;   // без решения, без NaN в наводке
            if (aimSmoothFor != best || aimSmoothAt < 0f || Time.time - aimSmoothAt > 0.5f || (raw - aimSmooth).sqrMagnitude > 120f * 120f) aimSmooth = raw;
            else aimSmooth = Vector3.Lerp(aimSmooth, raw, 1f - Mathf.Exp(-6f * Mathf.Max(Time.deltaTime, 1e-4f)));
            aimSmoothFor = best; aimSmoothAt = Time.time;
            aimPoint = aimSmooth; flightTime = t; solvedSide = side > 0f ? 1 : -1; solved = true;
        }

        /// <summary>Куда смотрит ствол с учётом наводки (мир). Без решения — по оси ствола модели.</summary>
        Vector3 GunDir(CannonMount m, float g)
        {
            Vector3 w = model.TransformDirection(m.Dir).normalized;
            int ms = m.side > 0f ? 1 : -1;
            Vector3 rest = Rest(w);
            float blend = aimBlend[SideIdx(ms)];
            if (blend <= 0.001f) return rest;
            Vector3 target;
            if (solved && ms == solvedSide) target = aimPoint;
            else if (offSolved && ms == offSide) target = offAim;
            else return rest;
            Vector3 aimed = AimedDir(m, w, target, g);
            return blend >= 0.999f ? aimed : Vector3.Slerp(rest, aimed, blend).normalized;
        }

        /// <summary>Наведённый ствол без порога: доворот и возвышение ограничены пределами, вне ±30° — прижат к пределу доворота.</summary>
        Vector3 AimedDir(CannonMount m, Vector3 w, Vector3 target, float g)
        {
            Vector3 d = target - model.TransformPoint(m.Muzzle);
            Vector3 h0 = new Vector3(w.x, 0f, w.z), hd = new Vector3(d.x, 0f, d.z);
            float x = hd.magnitude;
            if (x < 20f || h0.sqrMagnitude < 1e-4f) return Rest(w);
            h0.Normalize();
            float ang = Vector3.SignedAngle(h0, hd, Vector3.up);
            // (вне ±30° стволы не наводятся — это решает aimBlend плавно, а не скачком)
            Vector3 h = Quaternion.AngleAxis(Mathf.Clamp(ang, -traverseDeg, traverseDeg), Vector3.up) * h0;
            float theta = LowAngle(x, d.y, g);
            return h * Mathf.Cos(theta) + Vector3.up * Mathf.Sin(theta);
        }

        /// <summary>Ствол без решения: по горизонту борта (без крена корабля — от крена дуга «ныряла» в облака и дёргалась)
        /// и с возвышением на ~⅔ предела — ядро летит на дальность боя, а не в облака под бортом.</summary>
        Vector3 Rest(Vector3 w)
        {
            Vector3 h = new Vector3(w.x, 0f, w.z);
            if (h.sqrMagnitude < 1e-4f) return w;
            h.Normalize();
            float th = maxElevationDeg * 0.45f * Mathf.Deg2Rad;
            return h * Mathf.Cos(th) + Vector3.up * Mathf.Sin(th);
        }

        /// <summary>Путь ядра пушки m до корпуса цели или до моря облаков. hull — попало в корпус.</summary>
        readonly List<Vector3> forePath = new List<Vector3>(130), aftPath = new List<Vector3>(130);
        bool TracePath(CannonMount m, IAimTarget best, FxSystem fx, out Vector3 end, out bool hull, List<Vector3> pts = null, bool off = false)
        {
            hull = false;
            Vector3 p = model.TransformPoint(m.Muzzle);
            if (pts != null) pts.Add(p);
            float g = fx.gravity; const float step = 0.06f;
            Vector3 v = GunDir(m, g) * ShotSpeed + velocity;
            Vector3 tv = off ? (offSolved ? offVel : Vector3.zero) : (solved ? targetVel : Vector3.zero);
            end = p;
            for (int i = 0; i < 120; i++)
            {
                Vector3 prev = p;
                v.y -= g * step; p += v * step;
                if (pts != null) pts.Add(p);
                // Цель за время полёта уйдёт на tv·t: проверяем путь ядра в её системе, точку попадания возвращаем в мир.
                Vector3 hp, shift = tv * ((i + 1) * step);
                if (best != null && (p - shift - best.transform.position).sqrMagnitude < 45f * 45f && best.TryHit(prev - shift, p - shift, out hp)) { end = hp + shift; hull = true; return true; }
                if (p.y < fx.deckTop - 5f) { end = prev; return true; }
            }
            end = p;
            return false;
        }

        /// <summary>
        /// Прицел борта, обращённого к ближайшей цели: тонкий пунктир пути ядра средней пушки, а в конце — накрытие
        /// всего борта. Если ядра ложатся в корпус — перекрестия на корпусе (средняя, носовая и кормовая пушки);
        /// если в облака — эллипс от места падения носового ядра до кормового. Янтарный — борт готов, серый — перезарядка.
        /// </summary>
        /// <summary>Второй борт: ближайшая к траверзу цель с той стороны (в 1.3 дальности, удержание), своё решение
        /// без сглаживания и проверка — ляжет ли залп средней пушки в корпус. Только для огня, прицельная стрелка — у основного.</summary>
        void OffSideSolve(float side, float reach, FxSystem fx)
        {
            offSolved = false; OffOnTarget = false; offSide = side > 0f ? 1 : -1;
            TargetShip best = null; float bestScore = float.MaxValue, cur = float.MaxValue;
            foreach (TargetShip t in TargetShip.All)
            {
                if (t == null || !t.Alive || !t.gameObject.activeInHierarchy || t == aimTarget) continue;
                Vector3 lp = transform.InverseTransformPoint(t.transform.position);
                if (lp.x * side < 12f) continue;   // только с этой стороны
                float d = lp.magnitude; if (d > reach) continue;
                float score = d * (1f + 0.8f * Mathf.Abs(lp.z) / Mathf.Max(1f, d));
                if (t == offTarget) cur = score;
                if (score < bestScore) { bestScore = score; best = t; }
            }
            if (offTarget != null && cur < float.MaxValue && cur < bestScore * 1.25f) best = offTarget;
            offTarget = best;
            if (best == null) { int si0 = SideIdx(side); aimBlend[si0] = Mathf.MoveTowards(aimBlend[si0], 0f, Time.deltaTime * 2.5f); return; }
            CannonMount mid = null; float midZ = 0f; int count = 0;
            foreach (CannonMount m in mounts) if (Mathf.Sign(m.side) == side) { midZ += m.Muzzle.z; count++; }
            if (count == 0) return;
            midZ /= count; float bd = float.MaxValue;
            foreach (CannonMount m in mounts) if (Mathf.Sign(m.side) == side && Mathf.Abs(m.Muzzle.z - midZ) < bd) { bd = Mathf.Abs(m.Muzzle.z - midZ); mid = m; }
            if (mid == null) return;
            float g = fx.gravity;
            Vector3 muzzle = model.TransformPoint(mid.Muzzle), own = velocity; own.y = 0f;
            offVel = best.Controller != null ? best.transform.forward * best.Controller.Speed : Vector3.zero;
            Vector3 rel = offVel - own, d0 = best.Center - muzzle;
            float tt = Ballistics.Lead(d0.x, d0.y, d0.z, rel.x, rel.y, rel.z, ShotSpeed, g, minElevationDeg * Mathf.Deg2Rad, maxElevationDeg * Mathf.Deg2Rad, out _, out _, out _);
            Vector3 aim = best.Center + rel * tt;
            if (float.IsNaN(aim.x) || float.IsNaN(aim.y) || float.IsNaN(aim.z)) return;
            offAim = aim; offSolved = true;
            {
                Vector3 w0 = model.TransformDirection(mid.Dir); w0.y = 0f;
                Vector3 dd = aim - muzzle; dd.y = 0f;
                float wantB = w0.sqrMagnitude > 1e-4f && dd.magnitude > 20f && Mathf.Abs(Vector3.SignedAngle(w0, dd, Vector3.up)) <= 30f ? 1f : 0f;
                int si = SideIdx(side);
                aimBlend[si] = Mathf.MoveTowards(aimBlend[si], wantB, Time.deltaTime * 2.5f);
            }
            Vector3 end; bool hull;
            TracePath(mid, best, fx, out end, out hull, null, true);
            OffOnTarget = hull;
        }

        // Ровная прицельная дуга: начало — середина борта в системе курса (без качки модели), направление и возвышение
        // сглажены по времени; лента — центр ± полуширина накрытия, без перекоса.
        readonly List<Vector3> arcA = new List<Vector3>(48), arcB = new List<Vector3>(48);
        bool arcValid; float arcYaw, arcElev, arcDist; int arcSide; OrbitCamera scopeCam;
        bool BuildArc(CannonMount fore, CannonMount aft, CannonMount mid, float side, float g, FxSystem fx)
        {
            Vector3 mFore = transform.InverseTransformPoint(model.TransformPoint(fore.Muzzle)), mAft = transform.InverseTransformPoint(model.TransformPoint(aft.Muzzle));
            Vector3 mMid = (mFore + mAft) * 0.5f; mMid.y = transform.InverseTransformPoint(model.TransformPoint(mid.Muzzle)).y;
            Vector3 origin = transform.TransformPoint(new Vector3(mMid.x, mMid.y, mMid.z));
            float half = Mathf.Max(5f, Mathf.Abs(mFore.z - mAft.z) * 0.5f + 2f);
            Vector3 to = aimPoint - origin; Vector3 flat = new Vector3(to.x, 0f, to.z);
            float dist = flat.magnitude; if (dist < 15f || float.IsNaN(dist)) return false;
            float yaw = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
            float elev = LowAngle(dist, to.y, g);
            if (float.IsNaN(elev)) return false;
            float k = 1f - Mathf.Exp(-5f * Mathf.Max(Time.deltaTime, 1e-4f));
            if (!arcValid || arcSide != (int)side) { arcYaw = yaw; arcElev = elev; arcDist = dist; arcValid = true; arcSide = (int)side; }
            else { arcYaw += Mathf.DeltaAngle(arcYaw, yaw) * k; arcElev = Mathf.Lerp(arcElev, elev, k); arcDist = Mathf.Lerp(arcDist, dist, k); }
            Vector3 dir = new Vector3(Mathf.Sin(arcYaw * Mathf.Deg2Rad), 0f, Mathf.Cos(arcYaw * Mathf.Deg2Rad));
            Vector3 sideV = Vector3.Cross(Vector3.up, dir);
            float vh = ShotSpeed * Mathf.Cos(arcElev), vv = ShotSpeed * Mathf.Sin(arcElev);
            float tEnd = arcDist / Mathf.Max(1f, vh);
            arcA.Clear(); arcB.Clear();
            const int N = 32;
            for (int i = 0; i <= N; i++)
            {
                float t = tEnd * i / N;
                Vector3 p = origin + dir * (vh * t) + Vector3.up * (vv * t - 0.5f * g * t * t);
                if (p.y < fx.deckTop - 5f && i > 2) break;
                arcA.Add(p - sideV * half); arcB.Add(p + sideV * half);
            }
            return arcA.Count >= 3;
        }

        public void AimPreview()
        {
            FxSystem fx = FxSystem.Instance;
            AimOnTarget = false;
            if (fx == null) return;
            // Прицел рисует только корабль игрока. Раньше Broadside врага (и союзников) в своём Update тоже звал
            // ClearPreview — и, если его Update шёл после нашего, стирал прицельную дугу игрока: дуга мигала или
            // пропадала совсем («прицел не работает»). Порядок Update в Unity не задан — отсюда «то есть, то нет».
            if (ownHull == null && (!ownTried || Time.realtimeSinceStartup >= ownRetry)) { ownTried = true; ownRetry = Time.realtimeSinceStartup + 1f; ownHull = GetComponent<PlayerHull>(); }
            if (ownHull == null) return;
            fx.ClearPreview();
            solved = false;
            if (!showAim || model == null || mounts.Length == 0) return;
            PlayerHull own = ownHull;
            if (own != null && !own.Alive) return;   // корабль подбит — прицела нет
            // Цель: в пределах 1.3 дальности борта, лучше — та, что ближе к траверзу (носом/кормой стволы не наводятся).
            // Удержание: прежняя цель остаётся, пока новая не лучше на 25%.
            float reach = MaxRange * 1.3f;
            HasLead = false;
            if (Locked != null && (Locked.gameObject == null || !Locked.Alive || !Locked.gameObject.activeInHierarchy)) Locked = null;
            IAimTarget best = null; float bestScore = float.MaxValue, curScore = float.MaxValue;
            if (Locked != null) best = Locked;
            if (HoldSide != 0 && best != null && Mathf.Sign(transform.InverseTransformPoint(best.transform.position).x) != HoldSide) best = null;   // взятая цель с другого борта — не эта
            if (best == null && (AutoAim || HoldSide != 0))
            {
                foreach (TargetShip t in TargetShip.All)
                {
                    if (t == null || !t.Alive || !t.gameObject.activeInHierarchy) continue;
                    Vector3 lp = transform.InverseTransformPoint(t.transform.position);
                    if (HoldSide != 0 && Mathf.Sign(lp.x) != HoldSide) continue;   // прицел бортом — только враги с этого борта
                    float d = lp.magnitude;
                    if (d > reach) continue;
                    float score = d * (1f + 0.8f * Mathf.Abs(lp.z) / Mathf.Max(1f, d));
                    if ((IAimTarget)t == aimTarget) curScore = score;
                    if (score < bestScore) { bestScore = score; best = t; }
                }
                if (aimTarget != null && curScore < float.MaxValue && curScore < bestScore * 1.25f) best = aimTarget;
            }
            aimTarget = best;
            // Борт: меняется, только когда цель уверенно ушла на другую сторону (мёртвая зона ±12 м у диаметральной плоскости).
            float side = aimSide;
            if (HoldSide != 0) { side = HoldSide; sideHoldUntil = -1f; }
            // Борт меняется, только если цель уверенно (за 20 м от диаметральной плоскости) на другой стороне 0.6 с подряд.
            if (best != null && HoldSide == 0)
            {
                float lx = transform.InverseTransformPoint(best.transform.position).x;
                float want = lx > 20f ? 1f : lx < -20f ? -1f : side;
                if (want != side) { if (sideHoldUntil < 0f) sideHoldUntil = Time.time + 0.6f; else if (Time.time >= sideHoldUntil) { side = want; sideHoldUntil = -1f; } }
                else sideHoldUntil = -1f;
            }
            aimSide = side;
            AimSide = side > 0f ? 1 : -1;
            // Средняя, носовая и кормовая пушки этого борта.
            CannonMount mid = null, fore = null, aft = null; float midZ = 0f; int count = 0;
            foreach (CannonMount m in mounts)
            {
                if (Mathf.Sign(m.side) != side) continue;
                midZ += m.Muzzle.z; count++;
                if (fore == null || m.Muzzle.z > fore.Muzzle.z) fore = m;
                if (aft == null || m.Muzzle.z < aft.Muzzle.z) aft = m;
            }
            if (count == 0) return;
            midZ /= count; float bestDz = float.MaxValue;
            foreach (CannonMount m in mounts) if (Mathf.Sign(m.side) == side && Mathf.Abs(m.Muzzle.z - midZ) < bestDz) { bestDz = Mathf.Abs(m.Muzzle.z - midZ); mid = m; }
            bool ready = Readiness((int)side) >= 0.999f;
            float alpha = ready ? 0.95f : 0.55f;
            // Пунктир: короткие штрихи вдоль пути средней пушки (раньше — крупные, похожие на летящие ядра).
            float g = fx.gravity; const float step = 0.06f;
            Solve(best, mid, side, g);
            // Наводка борта: 1, если цель в секторе ±30° от траверза средней пушки, иначе 0 — переход за ~0.4 с.
            {
                float wantB = 0f;
                if (solved)
                {
                    Vector3 w0 = model.TransformDirection(mid.Dir); w0.y = 0f;
                    Vector3 dd = aimPoint - model.TransformPoint(mid.Muzzle); dd.y = 0f;
                    if (w0.sqrMagnitude > 1e-4f && dd.magnitude > 20f && Mathf.Abs(Vector3.SignedAngle(w0, dd, Vector3.up)) <= 30f) wantB = 1f;
                }
                int si = SideIdx(side);
                aimBlend[si] = Mathf.MoveTowards(aimBlend[si], wantB, Time.deltaTime * 2.5f);
            }
            Vector3 p = model.TransformPoint(mid.Muzzle);
            Vector3 v = GunDir(mid, g) * ShotSpeed + velocity;
            Vector3 tv = solved ? targetVel : Vector3.zero;
            // Путь средней пушки — для попадания/конца; сама дуга — объёмная стрелка по носовой и кормовой (AimArrow).
            float travelled = 0f; Vector3 midEnd = p; bool midHull = false;
            for (int i = 0; i < 120; i++)
            {
                Vector3 prev = p;
                v.y -= g * step; p += v * step;
                travelled += (p - prev).magnitude;
                Vector3 hp, shift = tv * ((i + 1) * step);
                if (best != null && (p - shift - best.transform.position).sqrMagnitude < 45f * 45f && best.TryHit(prev - shift, p - shift, out hp)) { midEnd = hp + shift; midHull = true; break; }
                if (p.y < fx.deckTop - 5f) { midEnd = prev; break; }
                midEnd = p;
            }
            Vector3 foreEnd = midEnd, aftEnd = midEnd; bool foreHull = false, aftHull = false;
            forePath.Clear(); aftPath.Clear();
            TracePath(fore != null ? fore : mid, best, fx, out foreEnd, out foreHull, forePath);
            TracePath(aft != null ? aft : mid, best, fx, out aftEnd, out aftHull, aftPath);
            AimArrow arrow = AimArrow.Get();
            bool hullAny = midHull || foreHull || aftHull;
            // Стрелка — только при цели (взятой касанием или выбранной автобоем) и только пока борт на неё наведён
            // (цель в секторе ±30° от траверза). Строится не по пушкам качающейся модели, а ровной дугой в системе курса
            // корабля со сглаживанием — раньше её «колбасило» качкой и разными доворотами носовой и кормовой пушек.
            float aimedK = aimBlend[SideIdx(side)];
            if (arrow != null)
            {
                if (scopeCam == null && Camera.main != null) scopeCam = Camera.main.GetComponent<OrbitCamera>();
                bool scoped = scopeCam != null && scopeCam.ScopeOn;   // в трубе лента шла у самой камеры жёлтой стеной (снимки 155)
                // Стрелка — только пока игрок держит кнопку борта. Без цели — куда ляжет залп «прямо» (стволы на упоре).
                bool holding = HoldSide != 0 && HoldSide == (int)side;
                if (holding && (best == null || !solved || aimedK < 0.3f)) { aimPoint = transform.position + transform.right * side * MaxRange * 0.6f + Vector3.down * 15f; aimedK = 1f; }
                if (!scoped && holding && aimedK > 0.02f && BuildArc(fore != null ? fore : mid, aft != null ? aft : mid, mid, side, g, fx))
                    arrow.Draw(arcA, arcB, hullAny ? 2 : ready ? 0 : 1, (ready ? 0.85f : 0.5f) * Mathf.SmoothStep(0f, 1f, aimedK));
                else { arrow.Hide(); arcValid = false; }
            }
            AimOnTarget = midHull || foreHull || aftHull;
            if (AutoAim) OffSideSolve(-side, reach, fx); else { offSolved = false; OffOnTarget = false; }   // второй борт — только в автобое
            // Залп не ложится в цель, но решение есть: малая метка там, где цель окажется к прилёту ядер, —
            // её и надо поставить на траверз (доворот стволов ограничен).
            HasLead = !AimOnTarget && solved && best == Locked; if (HasLead) LeadPoint = best.Center + targetVel * flightTime;   // упреждение рисует HUD: кольцо, пунктир от рамки цели и подпись
            // Ромбы точек попадания на корпусе цели убраны (владелец 16.11): что залп ляжет в корпус, показывает красная рамка цели.
            // Эллипс накрытия на облаках убран: место падения показывает наконечник стрелки.
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
            // Наводка — только в игре: проверки CI сверяют выстрел с осью ствола модели.
            Vector3 dir = Application.isPlaying ? GunDir(m, FxSystem.Instance.gravity) : model.TransformDirection(m.Dir).normalized;
            if (AutoAim && Application.isPlaying) FxSystem.AssistTarget = aimTarget as TargetShip;   // автобой: доводка ядра к цели (точность ×2)
            FxSystem.Instance.Fire(muzzle, dir, velocity, ShotSpeed, false, 7f, 1f, Application.isPlaying && chainShot, Application.isPlaying && GetComponent<PlayerHull>() != null);
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
