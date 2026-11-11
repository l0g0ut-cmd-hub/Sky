using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace SkyTide
{
    [Serializable]
    public class CrewModelData
    {
        public TextAsset geometry, skeleton;
        public Material material;
        [NonSerialized] Mesh cached;
        [NonSerialized] CrewRig cachedRig;

        [Serializable]
        public class CrewRig
        {
            public string[] names;
            public int[] parents;
            public float[] positions;
            public int vertices, triangles;
            public float height;
            public Vector3 Point(int i) { return new Vector3(positions[i*3],positions[i*3+1],positions[i*3+2]); }
        }

        public CrewRig Rig { get { if(cachedRig==null) cachedRig=JsonUtility.FromJson<CrewRig>(skeleton.text);return cachedRig; } }
        public bool IsValid { get { return geometry!=null&&skeleton!=null&&material!=null; } }

        public Mesh Mesh
        {
            get
            {
                if(cached!=null)return cached;
                byte[] b=geometry.bytes;
                if(b.Length<16||System.Text.Encoding.ASCII.GetString(b,0,4)!="STC1")throw new InvalidOperationException("Invalid crew geometry");
                int vc=BitConverter.ToInt32(b,4),tc=BitConverter.ToInt32(b,8),bc=BitConverter.ToInt32(b,12),off=16;
                if(vc!=Rig.vertices||tc!=Rig.triangles||bc!=Rig.names.Length||b.Length!=16+vc*52+tc*12)
                    throw new InvalidOperationException("Crew geometry/rig counts do not match");
                Vector3[] p=new Vector3[vc],n=new Vector3[vc];Vector2[] uv=new Vector2[vc];
                for(int i=0;i<vc;i++){p[i]=Read3(b,ref off);}
                for(int i=0;i<vc;i++){n[i]=Read3(b,ref off);}
                for(int i=0;i<vc;i++){uv[i]=new Vector2(Read(b,ref off),Read(b,ref off));}
                int[] triangles=new int[tc*3];Buffer.BlockCopy(b,off,triangles,0,triangles.Length*4);off+=triangles.Length*4;
                BoneWeight[] weights=new BoneWeight[vc];int weightOffset=off+vc*4;
                for(int i=0;i<vc;i++)
                {
                    weights[i]=new BoneWeight{boneIndex0=b[off++],boneIndex1=b[off++],boneIndex2=b[off++],boneIndex3=b[off++],
                        weight0=Read(b,ref weightOffset),weight1=Read(b,ref weightOffset),weight2=Read(b,ref weightOffset),weight3=Read(b,ref weightOffset)};
                    if(Mathf.Abs(weights[i].weight0+weights[i].weight1+weights[i].weight2+weights[i].weight3-1f)>0.001f)
                        throw new InvalidOperationException("Unweighted crew vertex");
                }
                Matrix4x4[] bind=new Matrix4x4[bc];for(int i=0;i<bc;i++)bind[i]=Matrix4x4.Translate(-Rig.Point(i));
                cached=new Mesh{name="Deckhand skin",indexFormat=IndexFormat.UInt16};
                cached.vertices=p;cached.normals=n;cached.uv=uv;cached.triangles=triangles;
                cached.boneWeights=weights;cached.bindposes=bind;cached.RecalculateBounds();cached.RecalculateTangents();cached.UploadMeshData(true);
                Debug.Log("SkyTide: crew skin — vertices="+vc+", triangles="+tc+", bones="+bc+", shared mesh/2K PBR");
                return cached;
            }
        }
        static float Read(byte[] b,ref int offset){float f=BitConverter.ToSingle(b,offset);offset+=4;return f;}
        static Vector3 Read3(byte[] b,ref int offset){return new Vector3(Read(b,ref offset),Read(b,ref offset),Read(b,ref offset));}
    }
}
