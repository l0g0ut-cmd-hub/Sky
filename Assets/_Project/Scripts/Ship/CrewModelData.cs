using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace SkyTide
{
    [Serializable]
    public class CrewModelData
    {
        public TextAsset geometry, skeleton, helmGeometry, helmPose;
        public Material material;
        public Texture2D captainAlbedo;   // та же развёртка, торс и рукава перекрашены в красный китель (tools: см. sky-log)
        [NonSerialized] Mesh cached, cachedHelm;
        [NonSerialized] CrewRig cachedRig;
        [NonSerialized] GripPose cachedGrip;

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

        [Serializable]
        public class GripPose
        {
            public int vertices, triangles, bones;
            public float[] leftCenter, rightCenter;
            public int[] leftTriangles, rightTriangles;
            // Offset from the wrist joint to the actual cylinder inside the curled fingers.
            public Vector3 Center(bool left)
            {
                float[] p=left?leftCenter:rightCenter;return new Vector3(p[0],p[1],p[2]);
            }
        }

        public CrewRig Rig { get { if(cachedRig==null) cachedRig=JsonUtility.FromJson<CrewRig>(skeleton.text);return cachedRig; } }
        public GripPose Grip { get { if(cachedGrip==null) cachedGrip=JsonUtility.FromJson<GripPose>(helmPose.text);return cachedGrip; } }
        public bool IsValid { get { return geometry!=null&&skeleton!=null&&material!=null&&helmGeometry!=null&&helmPose!=null; } }

        public Mesh Mesh { get {return MeshFor(false);} }
        public Mesh MeshFor(bool helm)
        {
            Mesh result=helm?cachedHelm:cached;if(result!=null)return result;
            byte[] b=(helm?helmGeometry:geometry).bytes;
            if(b.Length<16||System.Text.Encoding.ASCII.GetString(b,0,4)!="STC1")throw new InvalidOperationException("Invalid crew geometry");
            int vc=BitConverter.ToInt32(b,4),tc=BitConverter.ToInt32(b,8),bc=BitConverter.ToInt32(b,12),off=16;
            if(vc!=(helm?Grip.vertices:Rig.vertices)||tc!=(helm?Grip.triangles:Rig.triangles)
                ||bc!=Rig.names.Length||(helm&&bc!=Grip.bones)||b.Length!=16+vc*52+tc*12)
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
            result=new Mesh{name=helm?"Helmsman closed grip":"Deckhand skin",indexFormat=IndexFormat.UInt16};
            result.vertices=p;result.normals=n;result.uv=uv;result.triangles=triangles;
            result.boneWeights=weights;result.bindposes=bind;result.RecalculateBounds();result.RecalculateTangents();result.UploadMeshData(true);
            if(helm)cachedHelm=result;else cached=result;
            Debug.Log("SkyTide: crew skin — vertices="+vc+", triangles="+tc+", bones="+bc+", closed grip="+helm+", shared 2K PBR");
            return result;
        }
        static float Read(byte[] b,ref int offset){float f=BitConverter.ToSingle(b,offset);offset+=4;return f;}
        static Vector3 Read3(byte[] b,ref int offset){return new Vector3(Read(b,ref offset),Read(b,ref offset),Read(b,ref offset));}
    }
}
