using UnityEngine;

namespace SkyTide
{
    /// <summary>A real skinned deckhand: planted feet, two-bone arm IK and individual work cycles.</summary>
    public sealed class CrewActor
    {
        public enum Duty {Helmsman,Captain,Gunner,Loader,Rigger,Patrol,Lookout,Carrier}
        public readonly Transform root;
        public readonly Transform[] bones;
        public readonly SkinnedMeshRenderer renderer;
        public readonly Duty duty;
        public readonly Vector3 station;
        public readonly float yaw,phase;
        readonly CrewModelData.CrewRig rig;
        readonly CrewModelData.GripPose grip;
        readonly Mesh openHands,closedHands;
        public DeckRoutine routine;
        public DeckRoutine.Frame activity;
        public Transform cargo;
        public float conversation;
        public Vector3 conversationPartner;
        public int gunIndex=-1;
        public Transform gunBarrel;
        public int commandSide;
        public float ReloadAmount {get;private set;}
        public Vector3 leftGoal,rightGoal;
        public Transform tool;
        public CrewRope rope;
        public readonly Vector3[] ropePoints=new Vector3[6];
        public Vector3 ropeUpper,ropeLower,ropeAxis;
        public float RiggingPull {get;private set;}
        public Quaternion Balance {get;private set;}
        public int side;

        public CrewActor(Transform parent,CrewModelData model,Duty duty,Vector3 position,float yaw,int index)
        {
            this.duty=duty;station=position;this.yaw=yaw;phase=index*1.731f;rig=model.Rig;
            bool usesGrip=duty==Duty.Helmsman||duty==Duty.Carrier||duty==Duty.Gunner||duty==Duty.Loader||duty==Duty.Rigger;
            grip=usesGrip?model.Grip:null;openHands=model.Mesh;closedHands=usesGrip?model.MeshFor(true):null;
            root=new GameObject(duty+" "+index).transform;root.SetParent(parent,false);
            root.localPosition=position;root.localRotation=Quaternion.Euler(0,yaw,0);
            bones=new Transform[rig.names.Length];
            for(int i=0;i<bones.Length;i++)
            {
                bones[i]=new GameObject(rig.names[i]).transform;
                bones[i].SetParent(rig.parents[i]<0?root:bones[rig.parents[i]],false);
                bones[i].localPosition=rig.Point(i)-(rig.parents[i]<0?Vector3.zero:rig.Point(rig.parents[i]));
            }
            renderer=root.gameObject.AddComponent<SkinnedMeshRenderer>();renderer.sharedMesh=model.MeshFor(duty==Duty.Helmsman);renderer.sharedMaterial=model.material;
            renderer.bones=bones;renderer.rootBone=root;renderer.quality=SkinQuality.Bone4;
            renderer.localBounds=new Bounds(new Vector3(0,.95f,0),new Vector3(2.3f,2.8f,2.3f));
            renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.On;
            renderer.updateWhenOffscreen=false;
            MaterialPropertyBlock tint=new MaterialPropertyBlock();
            Color[] tones={Color.white,new Color(.90f,.93f,.96f),new Color(.96f,.91f,.85f),new Color(.89f,.88f,.85f)};
            tint.SetColor("_Color",tones[index%tones.Length]);renderer.SetPropertyBlock(tint);
        }

        static void Aim(Transform joint,Vector3 bindDirection,Vector3 direction)
        {
            if(direction.sqrMagnitude<1e-8f)return;
            joint.rotation=Quaternion.FromToRotation(joint.TransformDirection(bindDirection),direction)*joint.rotation;
        }

        void IK(int upper,int lower,int end,Vector3 target,Vector3 hint)
        {
            Vector3 a=bones[upper].position;
            float l=bones[lower].localPosition.magnitude,m=bones[end].localPosition.magnitude;
            Vector3 direction=target-a;float d=Mathf.Clamp(direction.magnitude,Mathf.Abs(l-m)+.004f,l+m-.002f);
            Vector3 axis=direction.sqrMagnitude>.000001f?direction.normalized:root.forward;
            Vector3 bend=Vector3.ProjectOnPlane(hint-a,axis).normalized;
            if(bend.sqrMagnitude<.1f)bend=Vector3.ProjectOnPlane(root.forward,axis).normalized;
            float along=(l*l-m*m+d*d)/(2f*d);
            Vector3 knee=a+axis*along+bend*Mathf.Sqrt(Mathf.Max(0,l*l-along*along));
            Aim(bones[upper],bones[lower].localPosition,knee-a);
            Aim(bones[lower],bones[end].localPosition,a+axis*d-bones[lower].position);
        }

        void Arm(bool left,Vector3 target,Quaternion? palm=null)
        {
            int upper=left?6:10,lower=left?7:11,hand=left?8:12;
            Vector3 hint=duty==Duty.Loader?new Vector3(left?-.55f:.55f,1.25f,.45f):new Vector3(left?-.65f:.65f,1.15f,-.3f);
            IK(upper,lower,hand,target,root.TransformPoint(hint));
            if(palm.HasValue)
            {
                // Spread pronation down the forearm without moving the solved wrist position.
                Vector3 axis=(bones[hand].position-bones[lower].position).normalized;
                Quaternion delta=palm.Value*Quaternion.Inverse(bones[hand].rotation);
                Vector3 projected=Vector3.Project(new Vector3(delta.x,delta.y,delta.z),axis);
                Quaternion twist=new Quaternion(projected.x,projected.y,projected.z,delta.w);
                float length=Mathf.Sqrt(projected.sqrMagnitude+delta.w*delta.w);
                if(length>.0001f)
                {
                    twist=new Quaternion(twist.x/length,twist.y/length,twist.z/length,twist.w/length);
                    bones[lower].rotation=Quaternion.Slerp(Quaternion.identity,twist,.65f)*bones[lower].rotation;
                }
                bones[hand].rotation=palm.Value;
            }
        }

        void BalanceOnDeck()
        {
            // Counter the real deck tilt in the sailor's facing direction, before solving contacts.
            Vector3 upright=root.InverseTransformDirection(Vector3.up);
            bool held=duty==Duty.Helmsman||duty==Duty.Gunner||duty==Duty.Loader||(duty==Duty.Carrier&&activity.grip>.001f);
            Quaternion correction=Quaternion.FromToRotation(Vector3.up,upright);
            Balance=Quaternion.RotateTowards(Quaternion.identity,Quaternion.Slerp(Quaternion.identity,correction,held?.18f:.65f),4.5f);
            bones[1].localRotation=Balance*bones[1].localRotation;
        }

        public static float RiggingStroke(float time,float offset)
        {
            float cycle=Mathf.Repeat(time/5.6f+offset*.13f,1f);
            // A deliberate pull, a short hold, then a gentler recovery. No sharp wrap at the seam.
            if(cycle<.42f)return Mathf.SmoothStep(0,1,cycle/.42f);
            if(cycle<.56f)return 1f;
            return 1f-Mathf.SmoothStep(0,1,(cycle-.56f)/.44f);
        }

        public void Pose(float time,Transform wheel,float shotAge)
        {
            for(int i=0;i<bones.Length;i++)
            {
                bones[i].localRotation=Quaternion.identity;
                bones[i].localPosition=rig.Point(i)-(rig.parents[i]<0?Vector3.zero:rig.Point(rig.parents[i]));
            }
            float t=time+phase;root.localPosition=station;root.localRotation=Quaternion.Euler(0,yaw,0);
            if(routine!=null)activity=routine.Sample(time);
            bool walking=routine!=null&&activity.speed>.001f;
            float gait=0f;
            if(routine!=null)
            {
                root.localPosition=activity.position;root.localRotation=Quaternion.LookRotation(activity.forward,Vector3.up);
                gait=activity.distance*Mathf.PI*2f/.44f;
            }
            if(conversation>.001f&&duty==Duty.Lookout)
                root.localRotation=Quaternion.Slerp(root.localRotation,Quaternion.LookRotation(conversationPartner-root.localPosition,Vector3.up),conversation);
            bool closed=duty==Duty.Helmsman||duty==Duty.Gunner||duty==Duty.Loader||duty==Duty.Rigger||(duty==Duty.Carrier&&activity.grip>.001f);
            Mesh desired=closed?closedHands:openHands;if(renderer.sharedMesh!=desired)renderer.sharedMesh=desired;
            float work=Mathf.Sin(t*2.1f),breathe=Mathf.Sin(t*1.7f);
            float kick=duty==Duty.Gunner&&shotAge>=0?Broadside.CannonRecoil(shotAge)/.32f:0;
            float crouch=kick*.10f;
            // Keep breathing below the fully extended bind stance so IK never lifts planted boots.
            bones[0].localPosition+=Vector3.up*((breathe-1f)*.005f-.003f-crouch);
            if(duty==Duty.Gunner)bones[0].localPosition+=Vector3.back*(kick*.10f);
            bones[1].localRotation=Quaternion.Euler(breathe*.7f,0,Mathf.Sin(t*.65f)*.8f);
            bones[4].localRotation=Quaternion.Euler(Mathf.Sin(t*.8f)*3f,Mathf.Sin(t*.45f)*13f,0);
            ReloadAmount=duty==Duty.Loader&&shotAge>=.9f&&shotAge<2.15f?Mathf.Sin((shotAge-.9f)/1.25f*Mathf.PI):0;
            if(duty==Duty.Gunner)bones[1].localRotation=Quaternion.Euler(-3f-kick*7f,0,0);
            if(duty==Duty.Loader)
            {
                bones[1].localRotation=Quaternion.Euler(6f+ReloadAmount*5f,0,0);
                bones[0].localPosition+=Vector3.forward*(ReloadAmount*.02f);
            }
            RiggingPull=duty==Duty.Rigger?RiggingStroke(time,phase):0;
            if(duty==Duty.Rigger)
            {
                bones[1].localRotation=Quaternion.Euler(8f-14f*RiggingPull,0,0);
                bones[0].localPosition+=Vector3.forward*(.015f-.04f*RiggingPull);
            }
            float order=duty==Duty.Captain&&shotAge>=0&&shotAge<1.2f?Mathf.Sin(shotAge/1.2f*Mathf.PI):0;
            if(order>0)
            {
                root.localRotation=Quaternion.Euler(0,yaw+commandSide*28f*order,0);
                bones[2].localRotation=Quaternion.Euler(0,commandSide*6f*order,0);
                bones[4].localRotation=Quaternion.Euler(-order*2f,commandSide*12f*order,0);
            }
            if(duty==Duty.Carrier)
            {
                bones[0].localPosition-=Vector3.up*(activity.crouch*.64f);
                bones[1].localRotation=Quaternion.Euler(activity.crouch*28f+breathe*.7f,0,0);
                if(cargo!=null){cargo.gameObject.SetActive(activity.cargo);cargo.localPosition=DeckRoutine.CrateCenter(activity.lift);}
            }
            BalanceOnDeck();
            Vector3 l=new Vector3(-.29f,1.025f,.015f),r=new Vector3(.29f,1.025f,.015f);
            if(duty==Duty.Helmsman&&wheel!=null)
            {
                Quaternion leftPalm=wheel.rotation*Quaternion.Euler(0,0,-90f)*Quaternion.Euler(-90f,0,0);
                Quaternion rightPalm=wheel.rotation*Quaternion.Euler(0,0,90f)*Quaternion.Euler(-90f,0,0);
                leftGoal=wheel.TransformPoint(new Vector3(-CrewParts.GripRadius,0,0))-leftPalm*grip.Center(true);
                rightGoal=wheel.TransformPoint(new Vector3(CrewParts.GripRadius,0,0))-rightPalm*grip.Center(false);
                Arm(true,leftGoal,leftPalm);Arm(false,rightGoal,rightPalm);
            }
            else if(duty==Duty.Gunner&&gunBarrel!=null)
            {
                Quaternion palm=root.rotation;
                leftGoal=gunBarrel.TransformPoint(new Vector3(-.66f,-.28f,.24f))-palm*grip.Center(true);
                rightGoal=gunBarrel.TransformPoint(new Vector3(-.66f,-.28f,-.24f))-palm*grip.Center(false);
                Arm(true,leftGoal,palm);Arm(false,rightGoal,palm);
            }
            else if(duty==Duty.Loader)
            {
                Quaternion palm=root.rotation*Quaternion.Euler(0,-90f,0)*Quaternion.Euler(0,0,180f);
                Vector3 axis=root.TransformPoint(new Vector3(0,1.50f+ReloadAmount*.10f,.32f+ReloadAmount*.05f));
                Vector3 direction=palm*Vector3.forward;
                leftGoal=axis+direction*.18f-palm*grip.Center(true);
                rightGoal=axis-direction*.18f-palm*grip.Center(false);
                Arm(true,leftGoal,palm);Arm(false,rightGoal,palm);
                if(tool!=null){tool.position=axis;tool.rotation=palm;}
            }
            else if(duty==Duty.Rigger)
            {
                Vector3 center=root.TransformPoint(new Vector3(0,1.32f-.14f*RiggingPull,.44f-.16f*RiggingPull));
                Vector3 anchor=root.parent.parent.TransformPoint(ShipCrew.RiggingAnchor);
                ropeAxis=(anchor-center).normalized;ropeUpper=center+ropeAxis*.14f;ropeLower=center-ropeAxis*.14f;
                Quaternion palm=Quaternion.LookRotation(ropeAxis,-root.forward);
                leftGoal=ropeUpper-palm*grip.Center(true);rightGoal=ropeLower-palm*grip.Center(false);
                Arm(true,leftGoal,palm);Arm(false,rightGoal,palm);
            }
            else
            {
                if(duty==Duty.Gunner)
                {
                    l=new Vector3(-.12f,1.55f,.23f-kick*.35f);r=new Vector3(.12f,1.55f,.23f-kick*.35f);
                    bones[2].localRotation=Quaternion.Euler(-3f+work*2f,0,0);
                }
                else if(duty==Duty.Loader)
                {
                    l=new Vector3(-.14f,1.10f,.24f+work*.07f);r=new Vector3(.12f,1.22f,.28f+work*.07f);
                    bones[1].localRotation=Quaternion.Euler(5f+work*4f,0,0);
                }
                else if(duty==Duty.Captain)
                {
                    l=new Vector3(-.20f,1.02f,.10f);
                    if(commandSide<0)l=Vector3.Lerp(l,new Vector3(-.22f,1.42f,.30f),order);
                    else r=Vector3.Lerp(r,new Vector3(.22f,1.45f,.30f),order);
                }
                else if(duty==Duty.Lookout)
                {
                    float scan=.5f+.5f*Mathf.Sin(t*.4f);
                    r=Vector3.Lerp(r,new Vector3(.13f,1.56f,.19f),scan);
                }
                else if(walking)
                {
                    l.y=r.y=1.08f;l.z+=Mathf.Sin(gait)*.08f;r.z-=Mathf.Sin(gait)*.08f;
                    bones[0].localRotation=Quaternion.Euler(0,Mathf.Sin(gait)*2f,0);
                }
                float talk=Mathf.Max(conversation,routine!=null?activity.talk:0f);
                if(talk>.001f)
                {
                    float gesture=.5f+.5f*Mathf.Sin(t*2.5f);
                    r=Vector3.Lerp(r,new Vector3(.23f,1.27f+gesture*.08f,.23f),talk);
                    bones[4].localRotation=Quaternion.Euler(Mathf.Sin(t*2.2f)*talk*4f,Mathf.Sin(t*.7f)*5f,0);
                }
                if(duty==Duty.Carrier&&activity.grip>.001f)
                {
                    Vector3 center=DeckRoutine.CrateCenter(activity.lift);
                    l=Vector3.Lerp(l,center+new Vector3(-.32f,.09f,0)-grip.Center(true),activity.grip);
                    r=Vector3.Lerp(r,center+new Vector3(.32f,.09f,0)-grip.Center(false),activity.grip);
                }
                leftGoal=root.TransformPoint(l);rightGoal=root.TransformPoint(r);
                if(duty==Duty.Carrier&&activity.grip>.001f){Arm(true,leftGoal,root.rotation);Arm(false,rightGoal,root.rotation);}
                else{Arm(true,leftGoal);Arm(false,rightGoal);}
            }
            for(int sideIndex=0;sideIndex<2;sideIndex++)
            {
                int upper=sideIndex==0?13:17,lower=upper+1,foot=upper+2;
                Vector3 goal=rig.Point(foot);
                if(walking)
                {
                    float cycle=Mathf.Repeat(gait/(Mathf.PI*2f)+sideIndex*.5f,1f);
                    if(cycle<.4f)
                    {
                        float swing=cycle/.4f;
                        goal.z+=Mathf.Lerp(-.13f,.13f,swing*swing*(3f-2f*swing));goal.y+=Mathf.Sin(swing*Mathf.PI)*.075f;
                    }
                    else goal.z+=Mathf.Lerp(.13f,-.13f,(cycle-.4f)/.6f);
                    goal=Vector3.Lerp(rig.Point(foot),goal,Mathf.Clamp01(activity.speed/.4f));
                }
                Vector3 world=root.TransformPoint(goal);
                IK(upper,lower,foot,world,root.TransformPoint(new Vector3(sideIndex==0?-.3f:.3f,.5f,.5f)));
                bones[foot].rotation=root.rotation;
            }
            if(tool!=null&&duty!=Duty.Loader)
            {
                Vector3 a=bones[8].position,b=bones[12].position;
                tool.position=(a+b)*.5f-root.up*.05f;tool.rotation=Quaternion.LookRotation(b-a,root.forward);
            }
        }

        public float HandError { get { return Mathf.Max(Vector3.Distance(bones[8].position,leftGoal),Vector3.Distance(bones[12].position,rightGoal)); } }
    }
}
