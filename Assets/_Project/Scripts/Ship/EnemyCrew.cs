using System.Collections.Generic;
using UnityEngine;

namespace SkyTide
{
    /// <summary>
    /// Экипаж на палубе врага (П1): капитан и помощник на юте, четверо впередсмотрящих по бортам главной палубы.
    /// Облегчённый путь — без пушек, штурвала и маршрутов: только те обязанности CrewActor, которым не нужен реквизит
    /// (Captain, Lookout). Впередсмотрящие поворачивают голову к кораблю игрока и подносят руку к глазам, капитан
    /// отмахивает рукой на каждый залп врага.
    /// Координаты — в пространстве модели врага, по профилю высот из журнала сборки 105: главная палуба y 6.1
    /// при |x| ≤ 3.2 и z −8…+8, ют y 9.1 при z −12…−16, нос в +Z.
    /// Матросы рисуются только ближе 170 м (дальше это пара пикселей, а позы и скиннинг стоят кадра) и без теней.
    /// </summary>
    public class EnemyCrew : MonoBehaviour
    {
        const float MainDeck = 6.1f, Poop = 9.1f, ShowRange = 170f, HideRange = 190f;   // было 280: провалы FPS 15.11 — экипажи 52 рендерера · 139k треуг.
        readonly List<CrewActor> actors = new List<CrewActor>();
        TargetShip target;
        Transform group;
        CrewActor captain;
        bool shown = true;

        public int Count { get { return actors.Count; } }
        public bool Shown { get { return group != null && group.gameObject.activeSelf; } }

        public void Setup(TargetShip target, CrewModelData model)
        {
            this.target = target;
            Transform hull = target != null && target.DamageVisual != null ? target.DamageVisual.Model : null;
            if (hull == null || model == null || !model.IsValid) return;
            // Модель может быть отмасштабирована: группу держим в метрах мира, координаты палубы переводим масштабом.
            float s = Mathf.Max(1e-3f, hull.lossyScale.y);
            group = new GameObject("Enemy deck crew").transform;
            group.SetParent(hull, false);
            group.localScale = Vector3.one / s;
            captain = Add(model, CrewActor.Duty.Captain, new Vector3(-2.6f, Poop, -12.6f), 0f, s);
            Add(model, CrewActor.Duty.Lookout, new Vector3(2.6f, Poop, -13.6f), 20f, s);
            Add(model, CrewActor.Duty.Lookout, new Vector3(-3.0f, MainDeck, 4.0f), -90f, s);
            Add(model, CrewActor.Duty.Lookout, new Vector3(3.0f, MainDeck, -2.0f), 90f, s);
            Add(model, CrewActor.Duty.Lookout, new Vector3(-3.0f, MainDeck, -5.5f), -75f, s);
            Add(model, CrewActor.Duty.Lookout, new Vector3(3.0f, MainDeck, 6.0f), 105f, s);
            // Враги темнее своих: та же модель, приглушённый тон.
            MaterialPropertyBlock tint = new MaterialPropertyBlock();
            for (int i = 0; i < actors.Count; i++)
            {
                float k = 0.62f + 0.06f * (i % 3);
                tint.SetColor("_Color", new Color(k, k * 0.9f, k * 0.86f));
                actors[i].renderer.SetPropertyBlock(tint);
                actors[i].renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                actors[i].renderer.receiveShadows = false;
            }
            Pose(0f);
            Debug.Log("SkyTide: экипаж врага — " + actors.Count + " матросов (капитан и впередсмотрящие)");
        }

        CrewActor Add(CrewModelData model, CrewActor.Duty duty, Vector3 deck, float yaw, float s)
        {
            CrewActor a = new CrewActor(group, model, duty, deck * s, yaw, actors.Count + 7);
            actors.Add(a);
            return a;
        }

        void LateUpdate()
        {
            if (group == null || target == null) return;
            long pt = PerfTimers.Start();
            Camera cam = Camera.main;
            float d = cam != null ? (cam.transform.position - group.position).magnitude : 0f;
            bool want = target.Alive && (shown ? d < HideRange : d < ShowRange);
            if (want != shown) { shown = want; group.gameObject.SetActive(want); }
            if (shown) Pose(Time.time);
            PerfTimers.Stop("Экипаж врага", pt);
        }

        void Pose(float time)
        {
            Transform player = target.player;
            float volleyAge = time - EnemyGunner.LastVolleyTime;
            if (captain != null) captain.commandSide = 0;
            foreach (CrewActor a in actors)
            {
                // Голова — к кораблю игрока (если он в пределах 700 м и не за спиной больше чем на 100°).
                a.report = 0f;
                if (player != null)
                {
                    Vector3 local = a.root.InverseTransformPoint(player.position);
                    float bearing = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
                    if (local.sqrMagnitude < 700f * 700f && Mathf.Abs(bearing) < 100f)
                    {
                        a.reportYaw = Mathf.Clamp(bearing, a.duty == CrewActor.Duty.Captain ? -45f : -60f, a.duty == CrewActor.Duty.Captain ? 45f : 60f);
                        a.report = a.duty == CrewActor.Duty.Captain ? 0.8f : 1f;
                    }
                }
                a.Pose(time, null, a.duty == CrewActor.Duty.Captain ? volleyAge : -1f);
            }
        }
    }
}
