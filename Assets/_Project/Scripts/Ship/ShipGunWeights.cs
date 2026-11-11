using System;
using UnityEngine;

namespace SkyTide
{
    /// <summary>Barrel IDs share the existing Color32 stream with the cloth mask.</summary>
    public static class ShipGunWeights
    {
        [Serializable]
        public class Map
        {
            public int vertexCount;
            public string meshSha256;
            public string[] names;
            public int[] indices, ids;
        }

        public static void Apply(Mesh mesh, byte[] source, TextAsset weights)
        {
            if (weights == null) return;
            Map map = JsonUtility.FromJson<Map>(weights.text);
            if (map == null || map.vertexCount != mesh.vertexCount || map.names == null || map.names.Length != 16
                || map.indices == null || map.ids == null || map.indices.Length != map.ids.Length)
                throw new InvalidOperationException("Cannon recoil map does not match mesh");
#if UNITY_EDITOR
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                string actual = BitConverter.ToString(sha.ComputeHash(source)).Replace("-", "").ToLowerInvariant();
                if (actual != map.meshSha256) throw new InvalidOperationException("Regenerate cannon recoil map: source mesh changed");
            }
#endif
            Color32[] colors = mesh.colors32;
            if (colors.Length == 0) colors = new Color32[mesh.vertexCount];
            for (int i = 0; i < map.indices.Length; i++)
            {
                int index = map.indices[i], id = map.ids[i];
                if (index < 0 || index >= colors.Length || id < 1 || id > 16 || colors[index].a != 0)
                    throw new InvalidOperationException("Invalid or overlapping cannon recoil vertex");
                colors[index].a = (byte)id;
            }
            mesh.colors32 = colors;
            Debug.Log("SkyTide: cannon recoil — barrels=" + map.names.Length + ", vertices=" + map.indices.Length + ", source mesh verified");
        }
    }
}
