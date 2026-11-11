using System;
using UnityEngine;

namespace SkyTide
{
    /// <summary>Sparse, offline-baked cloth freedom/gradient stream. Source geometry stays intact.</summary>
    public static class ShipWindWeights
    {
        [Serializable]
        public class Map
        {
            public int vertexCount, sails, flags, pinnedVertices;
            public string meshSha256;
            public int[] indices, packed;
        }

        public static void Apply(Mesh mesh, byte[] source, TextAsset weights)
        {
            if (weights == null) return;
            Map map = JsonUtility.FromJson<Map>(weights.text);
            if (map == null || map.vertexCount != mesh.vertexCount || map.indices == null || map.packed == null
                || map.indices.Length != map.packed.Length || map.sails != 3 || map.flags != 1)
                throw new InvalidOperationException("Cloth wind map does not match mesh");
#if UNITY_EDITOR
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                string actual = BitConverter.ToString(sha.ComputeHash(source)).Replace("-", "").ToLowerInvariant();
                if (actual != map.meshSha256) throw new InvalidOperationException("Cloth mesh changed: regenerate its wind mask");
            }
#endif
            Color32[] colors = new Color32[mesh.vertexCount];
            for (int i = 0; i < colors.Length; i++) colors[i] = new Color32(0,128,128,0);
            for (int i = 0; i < map.indices.Length; i++)
            {
                int index = map.indices[i];
                if (index < 0 || index >= colors.Length) throw new InvalidOperationException("Invalid cloth vertex index");
                uint p = unchecked((uint)map.packed[i]);
                colors[index] = new Color32((byte)p,(byte)(p>>8),(byte)(p>>16),(byte)(p>>24));
            }
            mesh.colors32 = colors;
            Debug.Log("SkyTide: cloth wind — moving=" + map.indices.Length + ", pinned=" + map.pinnedVertices
                + ", sails=" + map.sails + ", pennant=" + map.flags + ", source mesh verified");
        }
    }
}
