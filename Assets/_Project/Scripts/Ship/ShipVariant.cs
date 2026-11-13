using System;
using UnityEngine;

namespace SkyTide
{
    /// <summary>Вариант модели корабля для переключателя: меш (.bytes из tools/convert_glb*.py) и материалы по подмешам.</summary>
    [Serializable]
    public class ShipVariant
    {
        public string name;
        public TextAsset mesh;
        public Material[] materials;
        public TextAsset cannons;   // <модель>_cannons.json — дула и оси стволов (tools/extract_cannons.py)
        public TextAsset wind;      // sparse cloth weights; no changes to source positions/UV/triangles
        public TextAsset recoil;    // sparse complete barrel IDs in the same Color32 stream
        public CrewModelData crew;
        public TextAsset upperGun;
        public TextAsset steering;
        // Облегчённая копия (tools/decimate_ship.py): тень-заместитель, а дальше lodDistance — и сама модель.
        public TextAsset lite, liteSrc;
        public float lodDistance;   // 0 — только тень
        [NonSerialized] Mesh cachedLite;
        public Mesh LiteMesh { get { if (cachedLite == null && lite != null) cachedLite = ShipMeshLoader.Load(lite); return cachedLite; } }

        [NonSerialized] Mesh cached,cachedSteering;
        public Mesh SteeringMesh {get {if(cached==null){Mesh unused=Mesh;}return cachedSteering;}}

        public Mesh Mesh
        {
            get
            {
                if (cached == null) cached = ShipMeshLoader.Load(mesh, wind, recoil,steering,out cachedSteering);
                return cached;
            }
        }

        public bool IsValid { get { return mesh != null && materials != null && materials.Length > 0; } }
    }
}
