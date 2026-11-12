using System.Collections.Generic;
using UnityEngine;

namespace SkyTide
{
    public class ShipCrew : MonoBehaviour
    {
        public const float MainDeck=6.444f,QuarterDeck=10.414f,PlatformTop=6.824f;
        // Keep the complete station clear of the aft sail's sweep and the quarter-deck edge.
        public static readonly Vector3 HelmStation=new Vector3(-1.8f,QuarterDeck,-9.5f);
        readonly List<CrewActor> actors=new List<CrewActor>();
        readonly CannonMount[] upperMounts=new CannonMount[2];
        readonly Transform[] barrels=new Transform[2];
        readonly float[] firedAt={-100f,-100f};
        Transform group,wheel;
        ShipController ship;
        float commandAt=-100f;
        public bool Visible {get {return group!=null&&group.gameObject.activeSelf;}}
        public int Count {get {return actors.Count;}}
        public Transform Wheel {get {return wheel;}}
        public float WheelAngle {get;private set;}
        public CrewActor Helmsman {get {return actors.Count>0?actors[0]:null;}}

        public void Configure(ShipController ship,Transform visual,ShipVariant variant)
        {
            this.ship=ship;actors.Clear();group=null;wheel=null;
            commandAt=-100f;WheelAngle=0f;
            for(int i=0;i<2;i++){upperMounts[i]=null;barrels[i]=null;firedAt[i]=-100f;}
            if(variant==null||variant.crew==null||!variant.crew.IsValid||variant.upperGun==null)return;
            group=new GameObject("Upper deck crew").transform;group.SetParent(visual,false);
            Material wood=variant.materials[5],iron=variant.materials[1],bronze=variant.materials[6];
            Material hat=MaterialFactory.Opaque("Captain dark tricorn",new Color(.035f,.025f,.025f),.18f);
            CrewParts.Object("Helm stand",group,CrewParts.Stand(),wood,bronze,HelmStation+new Vector3(0,0,.28f));
            wheel=CrewParts.Object("Working helm",group,CrewParts.Wheel(),wood,bronze,HelmStation+new Vector3(0,1.24f,.22f));
            Mesh platform=CrewParts.GunPlatform(),stand=CrewParts.GunStand(),barrel=ShipMeshLoader.Load(variant.upperGun);
            Mesh rammer=CrewParts.Ramrod();
            for(int i=0;i<2;i++)
            {
                float side=i==0?-1:1;
                CrewParts.Object("Upper gun platform "+side,group,platform,wood,bronze,new Vector3(side*5.65f,MainDeck,1.85f));
                Transform mount=CrewParts.Object("Upper swivel gun "+side,group,stand,wood,iron,new Vector3(side*6.30f,PlatformTop,1.50f));
                mount.localRotation=Quaternion.Euler(0,side<0?180f:0,0);
                barrels[i]=CrewParts.Object("Recoiling barrel",mount,barrel,iron,iron,new Vector3(0,1.68f,0));
                upperMounts[i]=new CannonMount{name="UpperDeck_"+i,side=side,muzzle=new[]{side*6.719f,PlatformTop+1.68f,1.50f},dir=new[]{side,0f,0f},length=1.195f};
            }
            Add(variant.crew,CrewActor.Duty.Helmsman,HelmStation,0);
            CrewActor captain=Add(variant.crew,CrewActor.Duty.Captain,new Vector3(-2f,QuarterDeck,-11.4f),10f);
            CrewParts.Object("Captain tricorn",captain.bones[4],CrewParts.Hat(),hat,bronze,new Vector3(0,.03f,0));
            for(int i=0;i<2;i++)
            {
                int side=i==0?-1:1;
                CrewActor gunner=Add(variant.crew,CrewActor.Duty.Gunner,new Vector3(side*5.30f,PlatformTop,1.50f),side*90f);gunner.side=side;
                CrewActor loader=Add(variant.crew,CrewActor.Duty.Loader,new Vector3(side*5.30f,PlatformTop,2.65f),side*80f);loader.side=side;
                loader.tool=CrewParts.Object("Rammer",group,rammer,wood,bronze,Vector3.zero);
            }
            AddRigger(variant.crew,new Vector3(-3f,MainDeck,-1f),100f,variant.materials[2]);
            AddRigger(variant.crew,new Vector3(3f,MainDeck,-1f),-100f,variant.materials[2]);
            Add(variant.crew,CrewActor.Duty.Patrol,new Vector3(-2.4f,MainDeck,-5f),0);
            Add(variant.crew,CrewActor.Duty.Lookout,new Vector3(2.5f,MainDeck,7.3f),0);
            Pose(0f);SetVisible(Application.isPlaying);
            Debug.Log("SkyTide: upper crew — 10 sailors, captain/helmsman, 2 working upper guns; lower decks empty");
        }

        CrewActor Add(CrewModelData model,CrewActor.Duty duty,Vector3 position,float yaw)
        {
            CrewActor a=new CrewActor(group,model,duty,position,yaw,actors.Count);actors.Add(a);return a;
        }
        void AddRigger(CrewModelData model,Vector3 position,float yaw,Material material)
        {
            CrewActor a=Add(model,CrewActor.Duty.Rigger,position,yaw);
            a.rope=new GameObject("Running rigging").AddComponent<LineRenderer>();a.rope.transform.SetParent(group,false);
            a.rope.sharedMaterial=material;a.rope.useWorldSpace=true;a.rope.positionCount=4;
            a.rope.startWidth=a.rope.endWidth=.025f;a.rope.numCornerVertices=2;a.rope.generateLightingData=true;
        }
        public void SetVisible(bool on){if(group!=null)group.gameObject.SetActive(on);}
        public CannonMount UpperMount(int side){return Visible?upperMounts[side<0?0:1]:null;}
        public void NotifySalvo(float now){commandAt=now;}
        public void NotifyShot(CannonMount mount,float now)
        {
            for(int i=0;i<2;i++)if(object.ReferenceEquals(mount,upperMounts[i]))firedAt[i]=now;
        }
        void LateUpdate(){if(Application.isPlaying&&Visible)Pose(Time.time);}
        public void Pose(float time)
        {
            if(wheel==null)return;
            WheelAngle=-Mathf.Clamp(ship.turn,-1f,1f)*50f;
            wheel.localRotation=Quaternion.Euler(0,0,WheelAngle);
            for(int i=0;i<2;i++)if(barrels[i]!=null)
                barrels[i].localPosition=Vector3.up*1.68f-Vector3.right*(Broadside.CannonRecoil(time-firedAt[i])*.65f);
            foreach(CrewActor actor in actors)
            {
                float age=actor.duty==CrewActor.Duty.Captain?time-commandAt:actor.side==0?-1f:time-firedAt[actor.side<0?0:1];
                actor.Pose(time,wheel,age);
                if(actor.rope!=null)
                {
                    Vector3 a=actor.bones[8].position,b=actor.bones[12].position;
                    actor.ropePoints[0]=group.parent.TransformPoint(new Vector3(0,13.1f,-1.78f));
                    actor.ropePoints[1]=a.y>b.y?a:b;actor.ropePoints[2]=a.y>b.y?b:a;
                    actor.ropePoints[3]=actor.root.TransformPoint(new Vector3(0,.15f,.3f));actor.rope.SetPositions(actor.ropePoints);
                }
            }
        }
#if UNITY_EDITOR
        public IReadOnlyList<CrewActor> ActorsCapture {get {return actors;}}
        public Vector3 BarrelOffsetCapture(int side){return barrels[side<0?0:1].localPosition-Vector3.up*1.68f;}
#endif
    }
}
