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

        [NonSerialized] Mesh cached;

        public Mesh Mesh
        {
            get
            {
                if (cached == null) cached = ShipMeshLoader.Load(mesh, wind);
                return cached;
            }
        }

        public bool IsValid { get { return mesh != null && materials != null && materials.Length > 0; } }
    }
}
