using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SkyTide
{
    /// <summary>Повреждения мишени: отдача удара, дым из корпуса и потеря высоты при гибели.</summary>
    public class ShipDamageVisual : MonoBehaviour
    {
        Transform model;
        MeshRenderer hull;
        Light[] hullLights;
        Vector3[] lightOffsets;
        Bounds box;
        Vector3 restPosition;
        Quaternion restRotation;
        Vector2 angle, angularVelocity;
        readonly Vector3[] vents = new Vector3[3];
        readonly Vector3[] ventNormals=new Vector3[3];
        ShipImpactMarks marks;
        int impactSerial;
        ShipController controller;
        int ventCount, ventCursor;
        float damage, smokeClock, sinkAge = -1f, sinkSide = 1f;
        bool cloudContact;
        WreckDebris debris;

        public bool Sinking { get { return sinkAge >= 0f; } }
        public bool Hidden { get; private set; }
        public int VentCount { get { return ventCount; } }
        public int DebrisCount { get { return debris != null ? debris.Count : 0; } }
        public Transform Model { get { return model; } }
        public int ScarCount {get {return marks!=null?marks.Count:0;}}
        public int ScarVertices {get {return marks!=null?marks.VertexCount:0;}}
        public bool Burning {get {return !Hidden&&damage>=.4f&&ventCount>0;}}
        public int HullCloudContacts {get;private set;}

        public void Configure(Transform body, Bounds bounds, Material wood,ShipHitSurface surface=null)
        {
            model = body;
            controller=GetComponent<ShipController>();
            box = bounds;
            if (model == null) return;
            restPosition = model.localPosition;
            restRotation = model.localRotation;
            hull = model.GetComponent<MeshRenderer>();
            hullLights = model.parent.GetComponentsInChildren<Light>(true);
            lightOffsets = new Vector3[hullLights.Length];
            for (int i = 0; i < hullLights.Length; i++) lightOffsets[i] = model.InverseTransformPoint(hullLights[i].transform.position);
            debris = new WreckDebris(wood);
            if(surface!=null)marks=new ShipImpactMarks(model,surface);
        }

        public void Hit(Vector3 at, float fraction, Vector3 shipVelocity,Vector3 normal)
        {
            if (model == null || Sinking) return;
            Vector3 local = model.InverseTransformPoint(at) - box.center;
            float side = local.x >= 0f ? 1f : -1f;
            angularVelocity.x = Mathf.Clamp(angularVelocity.x - local.z / Mathf.Max(1f, box.extents.z) * 6f, -18f, 18f);
            angularVelocity.y = Mathf.Clamp(angularVelocity.y + side * 10f, -28f, 28f);
            damage = Mathf.Clamp01(fraction);
            if(marks!=null)marks.Add(at,normal,damage,++impactSerial);
            if (damage >= 0.1f)
            {
                vents[ventCursor] = model.InverseTransformPoint(at);
                ventNormals[ventCursor]=model.InverseTransformDirection(normal).normalized;
                ventCursor = (ventCursor + 1) % vents.Length;
                ventCount = Mathf.Min(ventCount + 1, vents.Length);
            }
            if (debris != null) debris.Emit(at, model.TransformDirection(new Vector3(side, 0.2f, 0f)), shipVelocity, 3, false);
        }

        public void BeginSink(Vector3 at, Vector3 shipVelocity)
        {
            if (model == null || Sinking) return;
            sinkSide = model.InverseTransformPoint(at).x >= box.center.x ? 1f : -1f;
            sinkAge = 0f;
            damage = 1f;
            cloudContact = false;
            if (ventCount == 0) { vents[0] = box.center; ventCount = 1; }
            Vector3 hullCenter = box.center - Vector3.up * (box.extents.y * 0.65f);
            if (debris != null) debris.Emit(model.TransformPoint(hullCenter), Vector3.up, shipVelocity, 20, true);
        }

        void Update()
        {
            if (Application.isPlaying) Simulate(Time.deltaTime, FxSystem.Instance);
        }

        // Точная критически затухающая пружина: одинаково устойчива при 30/60 FPS и длинном кадре.
        static void Spring(ref float x, ref float v, float dt)
        {
            const float w = 4.5f;
            float b = v + w * x;
            float e = Mathf.Exp(-w * dt);
            x = (x + b * dt) * e;
            v = (v - w * b * dt) * e;
        }

        /// <summary>Общий путь игры и одиночных кадров CI; не требует Play Mode.</summary>
        public void Simulate(float dt, FxSystem fx)
        {
            if (dt <= 0f || model == null) return;
            Vector3 oldKeel = model.TransformPoint(new Vector3(box.center.x, box.min.y, box.center.z));
            Spring(ref angle.x, ref angularVelocity.x, dt);
            Spring(ref angle.y, ref angularVelocity.y, dt);
            float fall = 0f, sinkPitch = 0f, sinkRoll = 0f;
            if (Sinking)
            {
                sinkAge += dt;
                fall = 4f * sinkAge + 7.5f * sinkAge * sinkAge;
                sinkPitch = -24f * Mathf.SmoothStep(0f, 1f, sinkAge / 3.2f);
                sinkRoll = sinkSide * 68f * Mathf.SmoothStep(0f, 1f, sinkAge / 3.6f);
            }
            Quaternion tilt = Quaternion.Euler(angle.x + sinkPitch, 0f, angle.y + sinkRoll);
            // Поворот вокруг центра меша, а не вокруг нижней точки модели.
            model.localRotation = restRotation * tilt;
            model.localPosition = restPosition + restRotation * (box.center - tilt * box.center) - Vector3.up * fall;

            if (Sinking && !Hidden)
            {
                Vector3 keel = model.TransformPoint(new Vector3(box.center.x, box.min.y, box.center.z));
                Vector3 contact;
                if (!cloudContact && fx != null && fx.TryCloudEntry(oldKeel, keel, out contact))
                {
                    cloudContact = true;
                    HullCloudContacts++;
                    fx.CloudSplash(contact, 2.4f);
                }
                float bottom = fx != null && fx.volumetric != null ? fx.volumetric.deckBase : -60f;
                // Корабль скрывается только целиком под слоем; взрыв больше не удаляет его мгновенно.
                if (hull != null && hull.bounds.max.y < bottom - 3f)
                {
                    model.gameObject.SetActive(false);
                    Hidden = true;
                }
            }
            if (!Hidden && ventCount > 0 && fx != null)
            {
                smokeClock += dt;
                if (smokeClock >= 0.18f)
                {
                    smokeClock %= 0.18f;
                    for (int i = 0; i < ventCount; i++)
                    {
                        Vector3 at=model.TransformPoint(vents[i]),normal=model.TransformDirection(ventNormals[i]);
                        fx.DamageSmoke(at+normal*.25f,damage);
                        // При падении языки огня не догоняют разгоняющийся корпус и висят в воздухе — горит только до гибели, падает с дымом.
                        if(Burning&&!Sinking)fx.DamageFire(at+normal*.4f,normal,damage,controller!=null?controller.transform.forward*controller.Speed:Vector3.zero);
                    }
                }
            }
            UpdateLights();
            if (debris != null) debris.Simulate(dt, fx);
        }

        void UpdateLights()
        {
            if (hullLights == null) return;
            for (int i = 0; i < hullLights.Length; i++)
            {
                if (hullLights[i] == null) continue;
                hullLights[i].transform.position = model.TransformPoint(lightOffsets[i]);
                hullLights[i].enabled = !Hidden;
            }
        }

        public void ResetDamage()
        {
            angle = angularVelocity = Vector2.zero;
            damage = smokeClock = 0f;
            ventCount = ventCursor = 0;
            impactSerial=0;if(marks!=null)marks.Clear();
            sinkAge = -1f;
            cloudContact = Hidden = false;
            HullCloudContacts=0;
            if (model != null)
            {
                model.localPosition = restPosition;
                model.localRotation = restRotation;
                model.gameObject.SetActive(true);
                UpdateLights();
            }
            if (debris != null) debris.Clear();
        }

        void OnDestroy() { if (debris != null) debris.Dispose();if(marks!=null)marks.Dispose(); }
    }

    /// <summary>До 32 объёмных деревянных обломков одним мешем, без Rigidbody и отдельных GameObject на щепку.</summary>
    sealed class WreckDebris
    {
        struct Piece
        {
            public Vector3 position, velocity, size, spin;
            public Quaternion rotation;
            public float age, life;
        }
        const int Limit = 32;
        readonly List<Piece> pieces = new List<Piece>(Limit);
        readonly List<Vector3> vertices = new List<Vector3>(Limit * 24);
        readonly List<Vector3> normals = new List<Vector3>(Limit * 24);
        readonly List<Vector2> uvs = new List<Vector2>(Limit * 24);
        readonly List<int> indices = new List<int>(Limit * 36);
        readonly Vector3[] corners = new Vector3[8];
        // Каждый face — против часовой стрелки снаружи, отдельные нормали на торцах.
        static readonly int[] faces = { 0, 2, 3, 1, 4, 5, 7, 6, 0, 4, 6, 2, 1, 3, 7, 5, 0, 1, 5, 4, 2, 6, 7, 3 };
        readonly Material material;
        GameObject go;
        Mesh mesh;
        public int Count { get { return pieces.Count; } }

        public WreckDebris(Material wood) { material = wood; }

        public void Emit(Vector3 at, Vector3 outward, Vector3 inheritedVelocity, int count, bool wreck)
        {
            int cap = wreck ? Limit : 12; // Для гибели всегда остаётся место под крупные обломки.
            for (int i = 0; i < count && pieces.Count < cap; i++)
            {
                float s = wreck ? 1.7f : 1f;
                Vector3 radial = Random.insideUnitSphere;
                pieces.Add(new Piece {
                    position = at + radial * (wreck ? 2.6f : 0.45f),
                    velocity = inheritedVelocity + outward * Random.Range(3f, 7f) + radial * Random.Range(3f, 11f) + Vector3.up * 3f,
                    size = new Vector3(Random.Range(0.12f, 0.3f), Random.Range(0.07f, 0.16f), Random.Range(0.7f, 1.9f)) * s,
                    rotation = Random.rotationUniform, spin = Random.onUnitSphere * Random.Range(70f, 230f),
                    life = Random.Range(3.2f, 4.7f)
                });
            }
        }

        public void Simulate(float dt, FxSystem fx)
        {
            for (int i = pieces.Count - 1; i >= 0; i--)
            {
                Piece p = pieces[i];
                p.age += dt;
                Vector3 old = p.position;
                p.position += p.velocity * dt + Vector3.down * (4.905f * dt * dt);
                p.velocity += Vector3.down * (9.81f * dt);
                p.rotation = Quaternion.Euler(p.spin * dt) * p.rotation;
                Vector3 contact;
                bool entered = fx != null && fx.TryCloudEntry(old, p.position, out contact);
                if (p.age >= p.life || entered) { pieces.RemoveAt(i); continue; }
                pieces[i] = p;
            }
            Rebuild();
        }

        void Rebuild()
        {
            if (pieces.Count == 0) { if (go != null) go.SetActive(false); return; }
            if (material == null) return;
            if (mesh == null)
            {
                go = new GameObject("Wreck debris");
                go.hideFlags = HideFlags.DontSave;
                mesh = new Mesh(); mesh.name = "Wood fragments (combined)"; mesh.MarkDynamic();
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = material;
                r.shadowCastingMode = ShadowCastingMode.On;
            }
            go.SetActive(true);
            vertices.Clear(); normals.Clear(); uvs.Clear(); indices.Clear();
            for (int i = 0; i < pieces.Count; i++)
            {
                Piece p = pieces[i];
                float fade = Mathf.Clamp01((p.life - p.age) / 0.65f);
                for (int k = 0; k < 8; k++)
                {
                    Vector3 c = new Vector3((k & 1) == 0 ? -0.5f : 0.5f, (k & 2) == 0 ? -0.5f : 0.5f, (k & 4) == 0 ? -0.5f : 0.5f);
                    // Сколотый торец: щепки не выглядят одинаковыми прямоугольными брусками.
                    if ((k & 4) != 0) c.z += (k & 1) == 0 ? -0.14f : 0.06f;
                    corners[k] = p.position + p.rotation * Vector3.Scale(c, p.size * fade);
                }
                for (int f = 0; f < 6; f++)
                {
                    int v = vertices.Count;
                    Vector3 a = corners[faces[f * 4]], b = corners[faces[f * 4 + 1]], c = corners[faces[f * 4 + 2]];
                    Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
                    for (int k = 0; k < 4; k++)
                    {
                        vertices.Add(corners[faces[f * 4 + k]]); normals.Add(normal);
                        uvs.Add(new Vector2(k == 1 || k == 2 ? 1f : 0f, k >= 2 ? 1f : 0f));
                    }
                    indices.Add(v); indices.Add(v + 1); indices.Add(v + 2);
                    indices.Add(v); indices.Add(v + 2); indices.Add(v + 3);
                }
            }
            mesh.Clear(false);
            mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0, uvs); mesh.SetTriangles(indices, 0, true);
        }

        public void Clear() { pieces.Clear(); if (go != null) go.SetActive(false); }
        public void Dispose() { if (go != null) SkyTideBootstrap.SafeDestroy(go); if (mesh != null) SkyTideBootstrap.SafeDestroy(mesh); }
    }
}
