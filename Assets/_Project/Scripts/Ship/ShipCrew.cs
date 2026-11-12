using System.Collections.Generic;
using UnityEngine;

namespace SkyTide
{
    public class ShipCrew : MonoBehaviour
    {
        public const float MainDeck=6.444f,QuarterDeck=10.414f,PlatformTop=6.824f;
        // Keep the complete station clear of the aft sail's sweep and the quarter-deck edge.
        public static readonly Vector3 HelmStation=new Vector3(-1.8f,QuarterDeck,-9.5f);
        public static readonly Vector3 RiggingAnchor=new Vector3(0,13.1f,-1.78f);
        public static readonly Vector3 RiggingCleat=new Vector3(0,.254f,.47f);
        readonly List<CrewActor> actors=new List<CrewActor>();
        readonly CannonMount[] upperMounts=new CannonMount[4];
        readonly Transform[] barrels=new Transform[4];
        readonly float[] firedAt={-100f,-100f,-100f,-100f};
        readonly DeckRoutine patrolRoutine=new DeckRoutine(false),cargoRoutine=new DeckRoutine(true);
        Transform storedCargo;
        readonly Transform[] lockerDoors=new Transform[2];
        CrewActor patrol,lookout,carrier,captain;
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
            for(int i=0;i<4;i++){upperMounts[i]=null;barrels[i]=null;firedAt[i]=-100f;}
            if(variant==null||variant.crew==null||!variant.crew.IsValid||variant.upperGun==null)return;
            group=new GameObject("Upper deck crew").transform;group.SetParent(visual,false);
            Material wood=variant.materials[5],iron=variant.materials[1],bronze=variant.materials[6];
            Material hat=MaterialFactory.Opaque("Captain dark tricorn",new Color(.035f,.025f,.025f),.18f);
            CrewParts.Object("Helm stand",group,CrewParts.Stand(),wood,bronze,HelmStation+new Vector3(0,0,.28f));
            wheel=CrewParts.Object("Working helm",group,CrewParts.Wheel(),wood,bronze,HelmStation+new Vector3(0,CrewParts.WheelHeight,.22f));
            Mesh platform=CrewParts.GunPlatform(),stand=CrewParts.GunStand(),barrel=ShipMeshLoader.Load(variant.upperGun);
            Mesh rammer=CrewParts.Ramrod(),breechHandles=CrewParts.BreechHandles();
            for(int sideIndex=0;sideIndex<2;sideIndex++)for(int gun=0;gun<2;gun++)
            {
                int i=sideIndex*2+gun;float side=sideIndex==0?-1:1,z=1.5f-gun*3f;
                CrewParts.Object("Upper gun platform "+i,group,platform,wood,bronze,new Vector3(side*5.65f,MainDeck,z+.35f));
                Transform mount=CrewParts.Object("Upper swivel gun "+i,group,stand,wood,iron,new Vector3(side*6.30f,PlatformTop,z));
                mount.localRotation=Quaternion.Euler(0,side<0?180f:0,0);
                barrels[i]=CrewParts.Object("Recoiling barrel",mount,barrel,iron,iron,new Vector3(0,1.68f,0));
                CrewParts.Object("Breech grips",barrels[i],breechHandles,wood,bronze,Vector3.zero);
                upperMounts[i]=new CannonMount{name="UpperDeck_"+i,side=side,muzzle=new[]{side*6.719f,PlatformTop+1.68f,z},dir=new[]{side,0f,0f},length=1.195f};
            }
            Add(variant.crew,CrewActor.Duty.Helmsman,HelmStation,0);
            captain=Add(variant.crew,CrewActor.Duty.Captain,new Vector3(-2f,QuarterDeck,-11.4f),10f);
            CrewParts.Object("Captain tricorn",captain.bones[4],CrewParts.Hat(),hat,bronze,new Vector3(0,-.06f,0));
            for(int sideIndex=0;sideIndex<2;sideIndex++)for(int gun=0;gun<2;gun++)
            {
                int side=sideIndex==0?-1:1,index=sideIndex*2+gun;float z=1.5f-gun*3f;
                CrewActor gunner=Add(variant.crew,CrewActor.Duty.Gunner,new Vector3(side*5.30f,PlatformTop,z),side*90f);gunner.side=side;gunner.gunIndex=index;
                gunner.gunBarrel=barrels[index];
                CrewActor loader=Add(variant.crew,CrewActor.Duty.Loader,new Vector3(side*5.30f,PlatformTop,z+1.15f),side*80f);loader.side=side;loader.gunIndex=index;
                loader.tool=CrewParts.Object("Rammer",group,rammer,wood,bronze,Vector3.zero);
            }
            Mesh cleat=CrewParts.BelayingCleat(),belay=CrewParts.BelayingTurn();
            AddRigger(variant.crew,new Vector3(-3f,MainDeck,-1f),100f,variant.materials[2],cleat,belay,wood,bronze);
            AddRigger(variant.crew,new Vector3(3f,MainDeck,-1f),-100f,variant.materials[2],cleat,belay,wood,bronze);
            patrol=Add(variant.crew,CrewActor.Duty.Patrol,new Vector3(-2.4f,MainDeck,-5.6f),0);patrol.routine=patrolRoutine;
            lookout=Add(variant.crew,CrewActor.Duty.Lookout,DeckRoutine.LookoutStation,0);
            carrier=Add(variant.crew,CrewActor.Duty.Carrier,DeckRoutine.CargoSource,180f);carrier.routine=cargoRoutine;
            Mesh crate=CrewParts.CargoCrate();
            carrier.cargo=CrewParts.Object("Carried cargo",carrier.root,crate,wood,bronze,Vector3.zero);
            CrewParts.Object("Cargo pickup pallet",group,CrewParts.CargoPallet(),wood,bronze,DeckRoutine.CargoSource+Vector3.back*.5f);
            Vector3 locker=DeckRoutine.CargoDestination+Vector3.back*.5f;
            Transform cabinet=CrewParts.Object("Deck stores",group,CrewParts.Locker(),wood,bronze,locker);
            for(int i=0;i<2;i++)
            {
                int side=i==0?-1:1;
                lockerDoors[i]=CrewParts.Object("Stores door "+side,cabinet,CrewParts.LockerDoor(side),wood,bronze,new Vector3(side*.44f,0,.28f));
            }
            storedCargo=CrewParts.Object("Delivered cargo",group,crate,wood,bronze,locker+Vector3.up*.29f);
            storedCargo.localRotation=Quaternion.Euler(0,180f,0);
            for(int i=0;i<3;i++)CrewParts.Object("Cargo reserve "+i,group,crate,wood,bronze,
                DeckRoutine.CargoSource+new Vector3(.78f,.21f+(i==2?.44f:0),-.35f-(i==1?.55f:0)));
            Pose(0f);SetVisible(Application.isPlaying);
            Debug.Log("SkyTide: upper crew — 15 sailors, two independent walking jobs, conversations, cargo stores, 4 working upper guns; lower decks empty");
        }

        CrewActor Add(CrewModelData model,CrewActor.Duty duty,Vector3 position,float yaw)
        {
            CrewActor a=new CrewActor(group,model,duty,position,yaw,actors.Count);actors.Add(a);return a;
        }
        void AddRigger(CrewModelData model,Vector3 position,float yaw,Material material,Mesh cleat,Mesh belay,Material wood,Material bronze)
        {
            CrewActor a=Add(model,CrewActor.Duty.Rigger,position,yaw);
            a.rope=new CrewRope(group,material);
            Transform fitting=CrewParts.Object("Rigging belaying cleat",group,cleat,wood,bronze,position+Quaternion.Euler(0,yaw,0)*new Vector3(0,0,RiggingCleat.z));
            fitting.localRotation=Quaternion.Euler(0,yaw,0);
            CrewParts.Object("Belayed rope turn",fitting,belay,material,material,Vector3.up*.20f).localRotation=Quaternion.Euler(0,90f,0);
        }
        public void SetVisible(bool on){if(group!=null)group.gameObject.SetActive(on);}
        public CannonMount UpperMount(int side,int gun=0){return Visible?upperMounts[(side<0?0:2)+gun]:null;}
        public void NotifySalvo(float now,int side=0){commandAt=now;if(captain!=null)captain.commandSide=side;}
        public void NotifyShot(CannonMount mount,float now)
        {
            for(int i=0;i<4;i++)if(object.ReferenceEquals(mount,upperMounts[i]))firedAt[i]=now;
        }
        void LateUpdate(){if(Application.isPlaying&&Visible)Pose(Time.time);}
        public void Pose(float time)
        {
            if(wheel==null)return;
            WheelAngle=-Mathf.Clamp(ship.turn,-1f,1f)*50f;
            wheel.localRotation=Quaternion.Euler(0,0,WheelAngle);
            DeckRoutine.Frame conversation=patrolRoutine.Sample(time);
            lookout.conversation=conversation.talk;lookout.conversationPartner=conversation.position;
            float open=cargoRoutine.LockerOpen(time);
            for(int i=0;i<2;i++)lockerDoors[i].localRotation=Quaternion.Euler(0,(i==0?-1:1)*open*115f,0);
            storedCargo.gameObject.SetActive(cargoRoutine.Sample(time).stored);
            for(int i=0;i<4;i++)if(barrels[i]!=null)
                barrels[i].localPosition=Vector3.up*1.68f-Vector3.right*(Broadside.CannonRecoil(time-firedAt[i])*.65f);
            foreach(CrewActor actor in actors)
            {
                float age=actor.duty==CrewActor.Duty.Captain?time-commandAt:actor.gunIndex<0?-1f:time-firedAt[actor.gunIndex];
                actor.Pose(time,wheel,age);
                if(actor.rope!=null)
                {
                    actor.ropePoints[0]=group.parent.TransformPoint(RiggingAnchor);
                    actor.ropePoints[1]=actor.ropeUpper+actor.ropeAxis*.14f;
                    actor.ropePoints[2]=actor.ropeUpper;actor.ropePoints[3]=actor.ropeLower;
                    actor.ropePoints[4]=actor.ropeLower-actor.ropeAxis*.14f;
                    actor.ropePoints[5]=actor.root.TransformPoint(RiggingCleat);
                    actor.rope.SetPositions(actor.ropePoints);
                }
            }
        }
#if UNITY_EDITOR
        public IReadOnlyList<CrewActor> ActorsCapture {get {return actors;}}
        public Vector3 BarrelOffsetCapture(int side,int gun=0){return barrels[(side<0?0:2)+gun].localPosition-Vector3.up*1.68f;}
        public DeckRoutine CargoRoutineCapture {get {return cargoRoutine;}}
        public DeckRoutine PatrolRoutineCapture {get {return patrolRoutine;}}
        public CrewActor CarrierCapture {get {return carrier;}}
        public CrewActor PatrolCapture {get {return patrol;}}
        public CrewActor LookoutCapture {get {return lookout;}}
#endif
    }
}
