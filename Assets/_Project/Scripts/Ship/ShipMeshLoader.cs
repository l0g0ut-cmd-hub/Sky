using System;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace SkyTide
{
    /// <summary>
    /// Читает меш корабля из бинарного файла (.bytes → TextAsset), который делает tools/convert_glb.py:
    /// "STM1", int vertexCount, int triangleCount, float3 pos[], float3 normal[], float2 uv[], int3 tri[].
    /// Координаты уже в Unity (левая СК, нос в +Z, метры), так что импортёр GLB не нужен.
    /// </summary>
    public static class ShipMeshLoader
    {
        public static Mesh Load(TextAsset asset)
        {
            if (asset == null) return null;
            byte[] b = asset.bytes;
            if (b.Length < 12 || Encoding.ASCII.GetString(b, 0, 4) != "STM1")
            {
                Debug.LogError("SkyTide: неверный формат меша корабля: " + asset.name);
                return null;
            }
            int vc = BitConverter.ToInt32(b, 4);
            int tc = BitConverter.ToInt32(b, 8);
            int off = 12;

            float[] pos = new float[vc * 3];
            float[] nrm = new float[vc * 3];
            float[] uv = new float[vc * 2];
            int[] idx = new int[tc * 3];
            Buffer.BlockCopy(b, off, pos, 0, pos.Length * 4); off += pos.Length * 4;
            Buffer.BlockCopy(b, off, nrm, 0, nrm.Length * 4); off += nrm.Length * 4;
            Buffer.BlockCopy(b, off, uv, 0, uv.Length * 4); off += uv.Length * 4;
            Buffer.BlockCopy(b, off, idx, 0, idx.Length * 4);

            Vector3[] v = new Vector3[vc];
            Vector3[] n = new Vector3[vc];
            Vector2[] t = new Vector2[vc];
            for (int i = 0; i < vc; i++)
            {
                v[i] = new Vector3(pos[i * 3], pos[i * 3 + 1], pos[i * 3 + 2]);
                n[i] = new Vector3(nrm[i * 3], nrm[i * 3 + 1], nrm[i * 3 + 2]);
                t[i] = new Vector2(uv[i * 2], uv[i * 2 + 1]);
            }

            Mesh mesh = new Mesh();
            mesh.name = asset.name;
            mesh.indexFormat = vc > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.vertices = v;
            mesh.normals = n;
            mesh.uv = t;
            mesh.triangles = idx;
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            mesh.UploadMeshData(true);
            return mesh;
        }
    }
}
