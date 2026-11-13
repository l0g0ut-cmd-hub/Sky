using System;
using System.Collections.Generic;
using UnityEngine;

namespace SkyTide
{
    /// <summary>The original aft sail and rope perimeter turn together around the fixed mizzen mast.</summary>
    public sealed class ShipSteeringSail : MonoBehaviour
    {
        [Serializable] public class Part {public int slot;public int[] triangles;}
        [Serializable] public class Map {public int vertexCount;public string meshSha256;public float[] pivot;public Part[] parts;}
        public const float MaxAngle=12f;
        Transform sail;
        ShipController ship;
        Vector3 pivot;
        public float Angle {get;private set;}
        public Transform Sail {get {return sail;}}

        public void Configure(ShipController controller,Transform model,ShipVariant variant)
        {
            ship=controller;sail=null;Angle=0;
            if(model==null||variant.steering==null||variant.SteeringMesh==null)return;
            Map map=JsonUtility.FromJson<Map>(variant.steering.text);pivot=new Vector3(map.pivot[0],map.pivot[1],map.pivot[2]);
            sail=new GameObject("Working direction sail").transform;sail.SetParent(model,false);
            sail.gameObject.AddComponent<MeshFilter>().sharedMesh=variant.SteeringMesh;
            sail.gameObject.AddComponent<MeshRenderer>().sharedMaterials=variant.materials;
            Apply(0f);
        }
        void LateUpdate(){long __pt=PerfTimers.Start();LateUpdateTimed();PerfTimers.Stop("Паруса",__pt);}
        void LateUpdateTimed()
        {
            if(Application.isPlaying&&sail!=null)Apply(Mathf.MoveTowards(Angle,-Mathf.Clamp(ship.turn,-1f,1f)*MaxAngle,36f*Time.deltaTime));
        }
        void Apply(float angle)
        {
            Angle=angle;if(sail==null)return;Quaternion q=Quaternion.Euler(0,angle,0);
            sail.localRotation=q;sail.localPosition=pivot-q*pivot;
        }
#if UNITY_EDITOR
        public void SetCapture(float input){Apply(-Mathf.Clamp(input,-1f,1f)*MaxAngle);}
#endif
        public static Mesh Split(Mesh mesh,byte[] source,TextAsset asset)
        {
            if(asset==null)return null;Map map=JsonUtility.FromJson<Map>(asset.text);
            if(map.vertexCount!=mesh.vertexCount||map.parts==null)throw new InvalidOperationException("Steering sail map does not match ship");
#if UNITY_EDITOR
            using(var sha=System.Security.Cryptography.SHA256.Create())
                if(BitConverter.ToString(sha.ComputeHash(source)).Replace("-","").ToLowerInvariant()!=map.meshSha256)
                    throw new InvalidOperationException("Steering sail source changed");
#endif
            Vector3[] positions=mesh.vertices,normals=mesh.normals;Vector2[] uv=mesh.uv;Vector4[] tangents=mesh.tangents;Color32[] colors=mesh.colors32;
            Dictionary<int,int> remap=new Dictionary<int,int>();List<int> ids=new List<int>();
            List<int>[] triangles=new List<int>[mesh.subMeshCount];for(int i=0;i<triangles.Length;i++)triangles[i]=new List<int>();
            foreach(Part part in map.parts)
            {
                int[] all=mesh.GetTriangles(part.slot);HashSet<int> selected=new HashSet<int>(part.triangles);List<int> keep=new List<int>();
                for(int i=0;i<all.Length;i+=3)
                {
                    if(!selected.Contains(i/3)){keep.Add(all[i]);keep.Add(all[i+1]);keep.Add(all[i+2]);continue;}
                    for(int k=0;k<3;k++)
                    {
                        int id=all[i+k],mapped;if(!remap.TryGetValue(id,out mapped)){mapped=ids.Count;remap.Add(id,mapped);ids.Add(id);}
                        triangles[part.slot].Add(mapped);
                    }
                }
                mesh.SetTriangles(keep,part.slot,false);
            }
            Vector3[] p=new Vector3[ids.Count],n=new Vector3[ids.Count];Vector2[] t=new Vector2[ids.Count];Vector4[] tangent=new Vector4[ids.Count];Color32[] c=new Color32[ids.Count];
            for(int i=0;i<ids.Count;i++){int id=ids[i];p[i]=positions[id];n[i]=normals[id];t[i]=uv[id];tangent[i]=tangents[id];c[i]=colors[id];}
            Mesh result=new Mesh{name="Original aft linen and sewn perimeter"};result.vertices=p;result.normals=n;result.uv=t;result.tangents=tangent;result.colors32=c;result.subMeshCount=triangles.Length;
            for(int i=0;i<triangles.Length;i++)result.SetTriangles(triangles[i],i,false);
            result.RecalculateBounds();Bounds bounds=result.bounds;bounds.Expand(new Vector3(1.6f,.02f,.02f));result.bounds=bounds;result.UploadMeshData(true);
            Debug.Log("SkyTide: steering sail — original geometry/PBR/wind, vertices="+ids.Count+", mast fixed, limit="+MaxAngle);
            return result;
        }
    }
}
