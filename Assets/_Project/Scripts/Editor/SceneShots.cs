#if UNITY_EDITOR
using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace SkyTide.EditorTools
{
    /// <summary>
    /// Кадры сцены из CI (build/shots/*.jpg → ветка apk-latest): корабль и небо с разных ракурсов.
    /// Нужны, чтобы оценивать картинку без телефона. Мир собирается тем же кодом, что и в игре (SkyTideBootstrap.Init),
    /// но вне Play Mode; после съёмки сцена перезагружается с диска, в сборку ничего не попадает.
    /// Без видеокарты (Null device) кадров нет — только строка в логе.
    /// </summary>
    public static class SceneShots
    {
        struct Shot
        {
            public string name; public float yaw, pitch, dist;
            public Shot(string n, float y, float p, float d) { name = n; yaw = y; pitch = p; dist = d; }
        }

        static void Render(OrbitCamera oc, Camera cam, SkyTideBootstrap boot, RenderTexture rt, Texture2D tex, string dir, Shot s)
        {
            oc.yaw = s.yaw; oc.pitch = s.pitch; oc.distance = s.dist;
            oc.Snap();
            oc.Apply(0f);
            boot.World.Step(cam.transform.position, 0f);
            SaveFrame(cam, boot, rt, tex, dir, s.name);
        }

        static void SaveFrame(Camera cam, SkyTideBootstrap boot, RenderTexture rt, Texture2D tex, string dir, string name)
        {
            float started = Time.realtimeSinceStartup;
            cam.aspect = (float)rt.width / rt.height;
            // Добавленные крупные планы не меняют сдвиг марша у контрольных ракурсов 45.
            int phase = -1;
            switch (name)
            {
                case "30_vol_toward_sun": phase = 0; break;
                case "32_vol_side": phase = 1; break;
                case "36_vol_away_from_sun": phase = 2; break;
                case "37_vol_high": phase = 3; break;
                case "45_vol_muzzle_flash": phase = 4; break;
                case "47_vol_cloud_splashes": phase = 5; break;
                case "40_vol_broadside_side":
                case "62_smoke_legacy_reference": phase = 6; break;
                case "60_ship_materials": phase = 7; break;
                case "61_ship_backlight": phase = 8; break;
                case "63_muzzle_headon":
                case "64_muzzle_legacy_reference": phase = 9; break;
                case "70_target_intact":
                case "71_target_impact":
                case "72_target_damaged":
                case "73_target_foundering":
                case "74_target_cloud_entry":
                case "75_target_restored": phase = 10; break;
                case "80_hud_ready":
                case "81_hud_reload":
                case "82_hud_cutout": phase = 11; break;
                case "83_cloth_rest":
                case "84_wind_phase_a":
                case "85_wind_phase_b":
                case "86_wind_phase_c": phase = 12; break;
                case "90_gun_rest":
                case "91_gun_recoil":
                case "92_gun_return":
                case "93_gun_restored": phase = 13; break;
                case "100_upper_crew": phase = 14; break;
                case "101_helm_left":
                case "102_helm_center":
                case "103_helm_right": phase = 15; break;
                case "104_upper_gun_crew": phase = 16; break;
                case "105_captain_hat": phase = 17; break;
                case "110_cargo_carry":
                case "111_cargo_stores": phase = 18; break;
                case "112_deck_conversation": phase = 19; break;
                case "113_direction_left":
                case "114_direction_right": phase = 20; break;
                case "115_camera_close": phase = 21; break;
                case "116_gunner_grip":
                case "117_gunner_recoil":
                case "118_loader_reload": phase = 22; break;
                case "119_captain_order": phase = 23; break;
                case "120_rigging_reach":
                case "121_rigging_pull": phase = 24; break;
                case "122_crew_bank_left":
                case "123_crew_bank_right": phase = 25; break;
                case "130_deck_supplies": phase = 26; break;
                case "131_target_report": phase = 27; break;
                case "132_hull_impact":
                case "133_battle_scars":
                case "134_hull_fire": phase = 28; break;
            }
            if (phase >= 0 && boot.Volumetric != null) boot.Volumetric.SetStaticCapturePhase(phase);
            if (boot.Fx != null) boot.Fx.PrepareForRender(cam);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            File.WriteAllBytes(Path.Combine(dir, name + ".jpg"), tex.EncodeToJPG(88));
            Debug.Log("SkyTide: кадр " + name + ", секунд=" + (Time.realtimeSinceStartup - started).ToString("F1"));
        }

        static void StepSalvo(Broadside guns, FxSystem fx, ref float time, float until)
        {
            while (time < until - 1e-5f)
            {
                float dt = Mathf.Min(1f / 60f, until - time);
                time += dt;
                guns.Pump(time);
                fx.Simulate(dt);
            }
        }

        static void RenderSplash(OrbitCamera oc, Camera cam, SkyTideBootstrap boot, RenderTexture rt, Texture2D tex, string dir)
        {
            Vector3 oldPosition = cam.transform.position;
            Quaternion oldRotation = cam.transform.rotation;
            try
            {
                Vector3 focus = boot.Fx.LastCloudSplash + Vector3.up * 4f;
                Quaternion rotation = Quaternion.Euler(25f, 45f, 0f);
                cam.transform.SetPositionAndRotation(focus - rotation * Vector3.forward * 72f, rotation);
                // OrbitCamera.target остаётся кораблём: чистый коридор не вырезает дыру вокруг всплеска.
                SaveFrame(cam, boot, rt, tex, dir, "47_vol_cloud_splashes");
            }
            finally { cam.transform.SetPositionAndRotation(oldPosition, oldRotation); }
        }

        static void CaptureBroadside(OrbitCamera oc, Camera cam, SkyTideBootstrap boot, RenderTexture rt, Texture2D tex, string dir, Broadside guns, bool full = true, bool environment = false, bool comparison = false)
        {
            Random.State random = Random.state;
            Random.InitState(74131);
            try
            {
                FxSystem fx = boot.Fx;
                fx.Clear();
                guns.Fire(-1);
                guns.Fire(1);
                float time = 0f;
                StepSalvo(guns, fx, ref time, 0.06f);
                Render(oc, cam, boot, rt, tex, dir, new Shot("45_vol_muzzle_flash", 90f, 6f, 58f));
                if (environment && !full && comparison)
                {
                    Render(oc, cam, boot, rt, tex, dir, new Shot("63_muzzle_headon", 145f, 6f, 52f));
                    fx.SetLegacyAppearanceCapture(true);
                    try { Render(oc, cam, boot, rt, tex, dir, new Shot("64_muzzle_legacy_reference", 145f, 6f, 52f)); }
                    finally { fx.SetLegacyAppearanceCapture(false); }
                }
                if (full)
                {
                    StepSalvo(guns, fx, ref time, 0.26f);
                    Render(oc, cam, boot, rt, tex, dir, new Shot("46_vol_ripple_flashes", 90f, 8f, 70f));
                }
                StepSalvo(guns, fx, ref time, 0.75f);
                Render(oc, cam, boot, rt, tex, dir, new Shot("40_vol_broadside_side", 90f, 8f, 70f));
                if (environment && !full && comparison)
                {
                    fx.SetLegacyAppearanceCapture(true);
                    try { Render(oc, cam, boot, rt, tex, dir, new Shot("62_smoke_legacy_reference", 90f, 8f, 70f)); }
                    finally { fx.SetLegacyAppearanceCapture(false); }
                }
                if (full)
                {
                    Render(oc, cam, boot, rt, tex, dir, new Shot("41_vol_broadside_chase", 10f, 16f, 75f));
                    boot.SetVolumetric(false);
                    Render(oc, cam, boot, rt, tex, dir, new Shot("42_broadside_drawn", 90f, 8f, 70f));
                    boot.SetVolumetric(true);
                    StepSalvo(guns, fx, ref time, 3.8f);
                    Render(oc, cam, boot, rt, tex, dir, new Shot("43_vol_broadside_later", 60f, 16f, 140f));
                }
                StepSalvo(guns, fx, ref time, 4.15f);
                Debug.Log("SkyTide: залп → всплесков в облаках: " + fx.CloudSplashCount);
                if (fx.CloudSplashCount > 0) RenderSplash(oc, cam, boot, rt, tex, dir);
                else Debug.LogWarning("SkyTide: залп не дал всплесков — проверить траектории и поверхность облаков");
                if (boot.Target != null && !environment)
                {
                    fx.Clear();
                    oc.yaw = 60f; oc.pitch = 10f; oc.distance = 90f; oc.Snap(); oc.Apply(0f);
                    Vector3 look = boot.Ship.transform.position - cam.transform.position; look.y = 0f;
                    boot.Target.transform.position = boot.Ship.transform.position + look.normalized * 120f;
                    fx.Explosion(boot.Target.transform.position + Vector3.up * 8f, 1f);
                    fx.Simulate(0.035f);
                    Render(oc, cam, boot, rt, tex, dir, new Shot("44_vol_target_hit", 60f, 10f, 90f));
                    for (int k = 0; k < 24; k++) fx.Simulate(1f / 60f);
                    Render(oc, cam, boot, rt, tex, dir, new Shot("48_vol_hit_smoke_debris", 60f, 10f, 90f));
                }
            }
            finally
            {
                boot.Fx.Clear();
                Random.state = random;
            }
        }

        static Vector3[][] CheckCrewPose(ShipCrew crew,float time)
        {
            crew.Pose(time);
            Mesh baked=new Mesh();
            Vector3[][] result=new Vector3[crew.Count][];int index=0;
            try
            {
                foreach(CrewActor actor in crew.ActorsCapture)
                {
                    if(actor.root.localPosition.y<ShipCrew.MainDeck-.001f)throw new System.InvalidOperationException("Crew below upper deck");
                    actor.renderer.BakeMesh(baked);
                    Bounds reported=baked.bounds;baked.RecalculateBounds();
                    if(actor.duty==CrewActor.Duty.Carrier&&time==0f)
                        Debug.Log("SkyTide: cargo bounds — skinning estimate="+reported+", actual vertices="+baked.bounds
                            +", feet="+actor.root.InverseTransformPoint(actor.bones[15].position)+"/"+actor.root.InverseTransformPoint(actor.bones[19].position));
                    if(baked.vertexCount!=actor.renderer.sharedMesh.vertexCount||baked.bounds.min.y<-.10f||baked.bounds.max.y>2.2f||baked.bounds.size.x>2f||baked.bounds.size.z>2f)
                        throw new System.InvalidOperationException("Crew skin outside anatomical bounds: "+actor.duty+" "+baked.bounds);
                    Vector3[] vertices=baked.vertices;result[index++]=vertices;
                    foreach(Vector3 v in vertices)
                        if(float.IsNaN(v.x)||float.IsInfinity(v.x)||float.IsNaN(v.y)||float.IsInfinity(v.y)||float.IsNaN(v.z)||float.IsInfinity(v.z))
                            throw new System.InvalidOperationException("Non-finite skinned crew vertex");
                }
            }
            finally{Object.DestroyImmediate(baked);}
            return result;
        }

        static Vector3[] CrewHandLandmarks(CrewModelData model,CrewActor actor)
        {
            byte[] data=model.geometry.bytes;
            int vertices=System.BitConverter.ToInt32(data,4),triangles=System.BitConverter.ToInt32(data,8);
            int ids=16+vertices*32+triangles*12,weights=ids+vertices*4;
            int[] joints={System.Array.IndexOf(model.Rig.names,"LHand"),System.Array.IndexOf(model.Rig.names,"RHand")};
            Vector3[] centers=new Vector3[2];float[] totals=new float[2];
            Mesh baked=new Mesh();
            try
            {
                actor.renderer.BakeMesh(baked);Vector3[] positions=baked.vertices;
                for(int i=0;i<vertices;i++)for(int slot=0;slot<4;slot++)
                {
                    int joint=data[ids+i*4+slot];float weight=System.BitConverter.ToSingle(data,weights+(i*4+slot)*4);
                    if(weight<=.25f)continue;
                    for(int hand=0;hand<2;hand++)if(joint==joints[hand]){centers[hand]+=positions[i]*weight;totals[hand]+=weight;}
                }
                for(int hand=0;hand<2;hand++)
                {
                    if(totals[hand]<1f)throw new System.InvalidOperationException("Helm hand skin has no meaningful weights");
                    centers[hand]/=totals[hand];
                }
                return centers;
            }
            finally{Object.DestroyImmediate(baked);}
        }

        static float Cross(Vector2 a,Vector2 b){return a.x*b.y-a.y*b.x;}
        static float EdgeRadius(Vector2 a,Vector2 b)
        {
            Vector2 edge=b-a;float t=Mathf.Clamp01(-Vector2.Dot(a,edge)/Mathf.Max(edge.sqrMagnitude,1e-12f));
            return (a+edge*t).magnitude;
        }
        static float TriangleRadius(Vector2 a,Vector2 b,Vector2 c)
        {
            float ab=Cross(b-a,-a),bc=Cross(c-b,-b),ca=Cross(a-c,-c);
            bool inside=(ab>=0f&&bc>=0f&&ca>=0f)||(ab<=0f&&bc<=0f&&ca<=0f);
            if(inside&&Mathf.Abs(Cross(b-a,c-a))>1e-10f)return 0f;
            return Mathf.Min(EdgeRadius(a,b),EdgeRadius(b,c),EdgeRadius(c,a));
        }

        static void CheckHelmSurface(CrewModelData model,ShipCrew crew)
        {
            CrewActor actor=crew.Helmsman;Mesh baked=new Mesh();
            try
            {
                actor.renderer.BakeMesh(baked);Vector3[] vertices=baked.vertices;
                for(int i=0;i<vertices.Length;i++)vertices[i]=crew.Wheel.InverseTransformPoint(actor.root.TransformPoint(vertices[i]));
                for(int side=0;side<2;side++)
                {
                    int[] triangles=side==0?model.Grip.leftTriangles:model.Grip.rightTriangles;
                    if(triangles==null||triangles.Length<300)throw new System.InvalidOperationException("Closed hand surface is missing");
                    float clearance=float.PositiveInfinity;
                    for(int i=0;i<triangles.Length;i+=3)
                    {
                        Vector3 a=vertices[triangles[i]],b=vertices[triangles[i+1]],c=vertices[triangles[i+2]];
                        foreach(Vector3 point in new[]{a,b,c})
                        {
                            float radial=point.x*(side==0?-1f:1f);
                            if(radial<.285f||radial>.45f)throw new System.InvalidOperationException("Closed fingers left their wooden handle");
                        }
                        clearance=Mathf.Min(clearance,TriangleRadius(new Vector2(a.y,a.z),new Vector2(b.y,b.z),new Vector2(c.y,c.z))-CrewParts.GripWoodRadius);
                    }
                    // Test the baked triangle surfaces against solid wood, not the wrist target.
                    if(clearance<-.0005f||clearance>.004f)
                        throw new System.InvalidOperationException("Helm finger/wood contact failed: side="+side+", clearance="+clearance);
                    Debug.Log("SkyTide: closed helm contact — side="+side+", angle="+crew.WheelAngle.ToString("F1")
                        +", surface clearance="+clearance.ToString("F5")+" m, triangles="+triangles.Length/3);
                }
            }
            finally{Object.DestroyImmediate(baked);}
        }

        static CrewActor CheckCaptainHat(CrewModelData model,ShipCrew crew)
        {
            CrewActor captain=null;foreach(CrewActor actor in crew.ActorsCapture)if(actor.duty==CrewActor.Duty.Captain)captain=actor;
            if(captain==null)throw new System.InvalidOperationException("Captain missing");
            Transform hat=captain.bones[4].Find("Captain tricorn");
            if(hat==null)throw new System.InvalidOperationException("Captain hat missing");
            byte[] data=model.geometry.bytes;int count=System.BitConverter.ToInt32(data,4),triangles=System.BitConverter.ToInt32(data,8);
            int ids=16+count*32+triangles*12,weights=ids+count*4;
            Mesh baked=new Mesh();float hairTop=float.NegativeInfinity;
            try
            {
                captain.renderer.BakeMesh(baked);Vector3[] vertices=baked.vertices;
                for(int i=0;i<count;i++)for(int slot=0;slot<4;slot++)
                    if(data[ids+i*4+slot]==4&&System.BitConverter.ToSingle(data,weights+(i*4+slot)*4)>.5f)
                        hairTop=Mathf.Max(hairTop,captain.bones[4].InverseTransformPoint(captain.root.TransformPoint(vertices[i])).y);
            }
            finally{Object.DestroyImmediate(baked);}
            float crownBottom=hat.localPosition.y+hat.GetComponent<MeshFilter>().sharedMesh.bounds.min.y;
            float overlap=hairTop-crownBottom;
            if(overlap<.03f||overlap>.13f)throw new System.InvalidOperationException("Captain hat floats or covers face: overlap="+overlap);
            Debug.Log("SkyTide: captain hat fitted — hair/crown overlap="+overlap.ToString("F4")+" m, follows animated head");
            return captain;
        }

        static void CaptureCrew(OrbitCamera oc,Camera cam,SkyTideBootstrap boot,string dir,bool render=true)
        {
            ShipCrew crew=boot.Ship.GetComponent<ShipCrew>();
            TouchControls controls=boot.GetComponent<TouchControls>();
            Broadside guns=boot.Ship.GetComponent<Broadside>();Transform visual=boot.Ship.visual;
            if(crew==null||crew.Count!=15||crew.Helmsman==null)throw new System.InvalidOperationException("Crew incomplete");
            RenderTexture rt=new RenderTexture(960,456,24,RenderTextureFormat.ARGB32);
            Texture2D tex=new Texture2D(960,456,TextureFormat.RGB24,false);
            Vector3 oldPos=cam.transform.position;Quaternion oldRot=cam.transform.rotation;
            Random.State random=Random.state;bool ripple=guns.ripple;
            bool[] recalculate=new bool[crew.Count];int rendererIndex=0;
            foreach(CrewActor actor in crew.ActorsCapture)recalculate[rendererIndex++]=actor.renderer.forceMatrixRecalculationPerRender;
            try
            {
                // Camera.Render runs repeatedly within one editor update; refresh the real skin for every view.
                foreach(CrewActor actor in crew.ActorsCapture)actor.renderer.forceMatrixRecalculationPerRender=true;
                crew.SetVisible(true);boot.SetWindCapture(0f,0f);boot.Fx.Clear();
                Vector3[][] before=CheckCrewPose(crew,0f),after=CheckCrewPose(crew,2.15f);int moving=0;
                for(int i=0;i<before.Length;i++)
                {
                    float max=0f;for(int vertex=0;vertex<before[i].Length;vertex++)max=Mathf.Max(max,Vector3.Distance(before[i][vertex],after[i][vertex]));
                    if(max>.002f)moving++;
                }
                if(moving!=crew.Count)throw new System.InvalidOperationException("Crew work cycles are static: moving="+moving);
                Vector3 focus=visual.TransformPoint(new Vector3(0,9f,-4f));
                Vector3 camera=visual.TransformPoint(new Vector3(-16f,15f,-4f));
                cam.transform.SetPositionAndRotation(camera,Quaternion.LookRotation(focus-camera,visual.up));
                if(render)SaveFrame(cam,boot,rt,tex,dir,"100_upper_crew");
                controls.SetHudCapture(true,960,456);HudLayout layout=controls.Layout(960,456);
                // Keep the inspection camera with the complete helm station when its layout changes.
                focus=visual.TransformPoint(ShipCrew.HelmStation+new Vector3(0,1.28f,.06f));
                camera=visual.TransformPoint(ShipCrew.HelmStation+new Vector3(-1.8f,2.1f,1.0f));
                cam.transform.SetPositionAndRotation(camera,Quaternion.LookRotation(focus-camera,visual.up));
                float[] inputs={-1f,0f,1f};string[] names={"101_helm_left","102_helm_center","103_helm_right"};
                Vector3[][] handCenters=new Vector3[inputs.Length][];
                float maxError=0f;
                for(int i=0;i<inputs.Length;i++)
                {
                    controls.ResetInteraction();
                    if(inputs[i]!=0f)
                    {
                        controls.PointerCapture(141,layout.wheel,true,false);
                        controls.PointerCapture(141,layout.wheel+Vector2.right*(layout.radius*inputs[i]),false,false);
                    }
                    controls.ApplyStickCapture();crew.Pose(2.15f);
                    float turn=boot.Ship.turn;
                    float rimX=(crew.Wheel.localRotation*Vector3.up).x;
                    bool wrong=inputs[i]==0f?Mathf.Abs(crew.WheelAngle)>.001f:Mathf.Sign(turn)!=Mathf.Sign(inputs[i])||Mathf.Sign(rimX)!=Mathf.Sign(turn);
                    if(wrong)
                        throw new System.InvalidOperationException("Helm direction does not match joystick input");
                    if(Mathf.Abs(crew.WheelAngle+turn*50f)>.001f)throw new System.InvalidOperationException("Helm/joystick angle differs");
                    maxError=Mathf.Max(maxError,crew.Helmsman.HandError);
                    if(maxError>.035f)throw new System.InvalidOperationException("Helmsman hands left wheel: "+maxError);
                    handCenters[i]=CrewHandLandmarks(boot.CurrentShip.crew,crew.Helmsman);
                    CheckHelmSurface(boot.CurrentShip.crew,crew);
                    if(render)SaveFrame(cam,boot,rt,tex,dir,names[i]);
                }
                float leftTravel=Vector3.Distance(handCenters[0][0],handCenters[2][0]);
                float rightTravel=Vector3.Distance(handCenters[0][1],handCenters[2][1]);
                if(Mathf.Min(leftTravel,rightTravel)<.10f)throw new System.InvalidOperationException("Helm bones move but hand skin does not follow");
                Debug.Log("SkyTide: helm skin verified — hand vertex travel left="+leftTravel.ToString("F4")
                    +" m, right="+rightTravel.ToString("F4")+" m; per-render skin matrices refreshed");
                controls.PointerCapture(141,layout.wheel+Vector2.right*layout.radius,false,true);
                controls.ApplyStickCapture();crew.Pose(2.15f);
                if(crew.WheelAngle!=0f)throw new System.InvalidOperationException("Helm did not return to neutral after joystick release");
                controls.ResetInteraction();controls.SetHudCapture(false);crew.Pose(0f);
                if(crew.WheelAngle!=0f)throw new System.InvalidOperationException("Helm did not return to neutral with input reset");
                guns.SetReadinessCapture(1f,1f);guns.SetRecoilCapture(true);guns.ripple=true;Random.InitState(51411);
                if(!guns.Fire(1))throw new System.InvalidOperationException("Upper gun salvo rejected");
                float time=0f;StepSalvo(guns,boot.Fx,ref time,.34f);crew.Pose(time);
                if(crew.BarrelOffsetCapture(1).magnitude<.05f||crew.BarrelOffsetCapture(-1).sqrMagnitude!=0f)
                    throw new System.InvalidOperationException("Upper gun recoil not tied to its shot");
                boot.Fx.Clear(); // Keep the inspection view readable after verifying the actual shot/recoil.
                focus=visual.TransformPoint(new Vector3(5.65f,8f,1.9f));camera=visual.TransformPoint(new Vector3(9.1f,10.4f,5.8f));
                cam.transform.SetPositionAndRotation(camera,Quaternion.LookRotation(focus-camera,visual.up));
                if(render)SaveFrame(cam,boot,rt,tex,dir,"104_upper_gun_crew");
                StepSalvo(guns,boot.Fx,ref time,1.8f);crew.Pose(time);
                if(crew.BarrelOffsetCapture(1).sqrMagnitude!=0f)throw new System.InvalidOperationException("Upper barrel did not return home");
                boot.Fx.Clear();crew.Pose(2.15f);
                CrewActor captain=CheckCaptainHat(boot.CurrentShip.crew,crew);
                focus=captain.root.TransformPoint(new Vector3(0,1.69f,0));
                camera=captain.root.TransformPoint(new Vector3(-.45f,1.90f,.95f));
                cam.transform.SetPositionAndRotation(camera,Quaternion.LookRotation(focus-camera,visual.up));
                if(render)SaveFrame(cam,boot,rt,tex,dir,"105_captain_hat");
                Debug.Log("SkyTide: upper crew verified — 15 skinned sailors, 21 joints each, all on upper decks; animated="+moving
                    +"; real joystick full left/neutral/right, release and focus reset OK; helm hand error="+maxError.ToString("F4")
                    +" m; closed finger surfaces/wood contact and fitted captain hat OK; four upper gun mounts, firing/recoil/run-out OK; "+(render?"six frames rendered":"checks without extra frames"));
            }
            finally
            {
                rendererIndex=0;foreach(CrewActor actor in crew.ActorsCapture)actor.renderer.forceMatrixRecalculationPerRender=recalculate[rendererIndex++];
                controls.SetHudCapture(false);controls.ResetInteraction();crew.SetVisible(false);guns.SetReadinessCapture(1f,1f);guns.SetRecoilCapture(false);guns.ripple=ripple;
                boot.Fx.Clear();Random.state=random;cam.transform.SetPositionAndRotation(oldPos,oldRot);
                cam.targetTexture=null;RenderTexture.active=null;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(tex);
            }
        }

        static void CheckCargoContact(CrewModelData model,ShipCrew crew,float time)
        {
            crew.Pose(time);CrewActor actor=crew.CarrierCapture;
            if(!actor.activity.cargo||actor.HandError>.002f)throw new System.InvalidOperationException("Cargo hands cannot reach the box: "+actor.HandError);
            Mesh baked=new Mesh();
            try
            {
                actor.renderer.BakeMesh(baked);Vector3[] vertices=baked.vertices;
                for(int side=0;side<2;side++)
                {
                    int[] tri=side==0?model.Grip.leftTriangles:model.Grip.rightTriangles;
                    Vector3 axis=actor.cargo.localPosition+new Vector3(side==0?-.32f:.32f,.09f,0);float clearance=float.PositiveInfinity;
                    for(int i=0;i<tri.Length;i+=3)
                    {
                        Vector3 a=vertices[tri[i]]-axis,b=vertices[tri[i+1]]-axis,c=vertices[tri[i+2]]-axis;
                        clearance=Mathf.Min(clearance,TriangleRadius(new Vector2(a.x,a.y),new Vector2(b.x,b.y),new Vector2(c.x,c.y))-.025f);
                    }
                    if(clearance<-.0005f||clearance>.004f)throw new System.InvalidOperationException("Cargo fingers/wood intersection: "+clearance);
                }
            }
            finally{Object.DestroyImmediate(baked);}
        }

        static void CheckGunCrew(ShipCrew crew,CrewModelData model,float time)
        {
            CheckCrewPose(crew,time);Mesh baked=new Mesh();
            byte[] source=model.geometry.bytes;int count=System.BitConverter.ToInt32(source,4),faces=System.BitConverter.ToInt32(source,8);
            int ids=16+count*32+faces*12,weights=ids+count*4;bool[] head=new bool[count];
            for(int i=0;i<count;i++)for(int slot=0;slot<4;slot++)
                if(source[ids+i*4+slot]==4&&System.BitConverter.ToSingle(source,weights+(i*4+slot)*4)>.5f)head[i]=true;
            try
            {
                foreach(CrewActor actor in crew.ActorsCapture)
                {
                    if(actor.duty!=CrewActor.Duty.Gunner&&actor.duty!=CrewActor.Duty.Loader)continue;
                    if(actor.HandError>.002f)throw new System.InvalidOperationException("Ordnance wrist target unreachable: "+actor.duty+" "+actor.HandError);
                    actor.renderer.BakeMesh(baked);Vector3[] vertices=baked.vertices;
                    Transform frame=actor.duty==CrewActor.Duty.Gunner?actor.gunBarrel:actor.tool;
                    for(int i=0;i<vertices.Length;i++)vertices[i]=frame.InverseTransformPoint(actor.root.TransformPoint(vertices[i]));
                    if(actor.duty==CrewActor.Duty.Loader)for(int i=0;i<count;i++)
                    {
                        Vector3 p=vertices[i];
                        if(head[i]&&p.z>=-.25f&&p.z<=1.02f&&new Vector2(p.x,p.y).magnitude<.030f)
                            throw new System.InvalidOperationException("Rammer touches loader face");
                    }
                    for(int side=0;side<2;side++)
                    {
                        int[] tri=side==0?model.Grip.leftTriangles:model.Grip.rightTriangles;float clearance=float.PositiveInfinity;
                        for(int i=0;i<tri.Length;i+=3)
                        {
                            Vector3 a=vertices[tri[i]],b=vertices[tri[i+1]],c=vertices[tri[i+2]];Vector2 pa,pb,pc;
                            if(actor.duty==CrewActor.Duty.Gunner)
                            {
                                Vector2 center=new Vector2(-.28f,side==0?.24f:-.24f);
                                pa=new Vector2(a.y,a.z)-center;pb=new Vector2(b.y,b.z)-center;pc=new Vector2(c.y,c.z)-center;
                            }
                            else{pa=new Vector2(a.x,a.y);pb=new Vector2(b.x,b.y);pc=new Vector2(c.x,c.y);}
                            clearance=Mathf.Min(clearance,TriangleRadius(pa,pb,pc)-CrewParts.GripWoodRadius);
                        }
                        if(clearance<-.0005f||clearance>.004f)throw new System.InvalidOperationException("Ordnance hand/wood contact failed: "+actor.duty+" "+clearance);
                    }
                }
            }
            finally{Object.DestroyImmediate(baked);}
        }

        static void CaptureLife(OrbitCamera oc,Camera cam,SkyTideBootstrap boot,string dir,bool workReview=false,bool render=true)
        {
            // Keep the existing helm, hat and all-sailor skin checks, without rendering another gallery.
            CaptureCrew(oc,cam,boot,dir,false);
            ShipCrew crew=boot.Ship.GetComponent<ShipCrew>();ShipSteeringSail sail=boot.Ship.GetComponent<ShipSteeringSail>();
            TouchControls controls=boot.GetComponent<TouchControls>();Broadside guns=boot.Ship.GetComponent<Broadside>();Transform visual=boot.Ship.visual;
            if(sail==null||sail.Sail==null)throw new System.InvalidOperationException("Working direction sail missing");
            RenderTexture rt=new RenderTexture(960,456,24,RenderTextureFormat.ARGB32);Texture2D tex=new Texture2D(960,456,TextureFormat.RGB24,false);
            Random.State random=Random.state;Vector3 oldPos=cam.transform.position;Quaternion oldRot=cam.transform.rotation;bool ripple=guns.ripple;
            bool[] recalculate=new bool[crew.Count];int index=0;
            foreach(CrewActor actor in crew.ActorsCapture){recalculate[index++]=actor.renderer.forceMatrixRecalculationPerRender;actor.renderer.forceMatrixRecalculationPerRender=true;}
            try
            {
                crew.SetVisible(true);boot.Fx.Clear();boot.SetWindCapture(0,0);
                for(int side=-1;side<=1;side+=2)for(int gun=0;gun<2;gun++)crew.NotifyShot(crew.UpperMount(side,gun),0);
                foreach(float age in new[]{0f,.06f,.45f,.9f,1.5f,2.2f})CheckGunCrew(crew,boot.CurrentShip.crew,age);
                foreach(CrewActor actor in crew.ActorsCapture)if(actor.duty==CrewActor.Duty.Loader&&actor.ReloadAmount!=0)
                    throw new System.InvalidOperationException("Reload did not settle before readiness returned");
                Debug.Log("SkyTide: ordnance grip verified — 4 gunners + 4 loaders, actual hand/wood surfaces through rest/kick/return/reload/ready; feet planted");
                DeckRoutine cargo=crew.CargoRoutineCapture,patrol=crew.PatrolRoutineCapture;
                foreach(float t in new[]{0f,.8f,2.15f,8f,cargo.Arrival+DeckRoutine.DropTime*.5f,cargo.Arrival+DeckRoutine.DropTime*.98f})
                {
                    CheckCrewPose(crew,t);CheckCargoContact(boot.CurrentShip.crew,crew,t);
                }
                DeckRoutine.Frame loaded=cargo.Sample(6f),returned=cargo.Sample(cargo.Arrival+DeckRoutine.DropTime+DeckRoutine.RecoveryTime+2f),walking=patrol.Sample(6f);
                if(Mathf.Abs(loaded.speed-DeckRoutine.LoadedSpeed)>.001f||Mathf.Abs(returned.speed-DeckRoutine.EmptySpeed)>.001f
                    ||Mathf.Abs(walking.speed-DeckRoutine.PatrolSpeed)>.001f||Vector3.Distance(loaded.position,walking.position)<1f||returned.cargo)
                    throw new System.InvalidOperationException("Deck jobs share a route/speed or cargo was not released");
                crew.Pose(6f);Vector3 focus=crew.CarrierCapture.root.position+visual.up*1.05f;
                Vector3 camera=crew.CarrierCapture.root.TransformPoint(new Vector3(-2.4f,2.0f,2.4f));
                cam.transform.SetPositionAndRotation(camera,Quaternion.LookRotation(focus-camera,visual.up));if(render)SaveFrame(cam,boot,rt,tex,dir,"110_cargo_carry");
                crew.Pose(cargo.Arrival+DeckRoutine.DropTime*.85f);
                focus=visual.TransformPoint(DeckRoutine.CargoDestination+new Vector3(0,.8f,-.4f));camera=visual.TransformPoint(DeckRoutine.CargoDestination+new Vector3(-2.4f,1.8f,2f));
                cam.transform.SetPositionAndRotation(camera,Quaternion.LookRotation(focus-camera,visual.up));if(render&&!workReview)SaveFrame(cam,boot,rt,tex,dir,"111_cargo_stores");
                crew.Pose(cargo.Arrival+DeckRoutine.DropTime+DeckRoutine.RecoveryTime+2f);
                if(crew.CarrierCapture.cargo.gameObject.activeSelf||!crew.CarrierCapture.activity.stored)throw new System.InvalidOperationException("Box not deposited in stores");
                crew.Pose(patrol.Arrival+2.1f);
                if(crew.PatrolCapture.activity.talk<.95f||crew.LookoutCapture.conversation<.95f||crew.PatrolCapture.activity.speed!=0)
                    throw new System.InvalidOperationException("Deck conversation not synchronized");
                focus=(crew.PatrolCapture.root.position+crew.LookoutCapture.root.position)*.5f+visual.up*1.15f;
                camera=focus+visual.TransformDirection(new Vector3(-2.4f,1.0f,-1.2f));
                cam.transform.SetPositionAndRotation(camera,Quaternion.LookRotation(focus-camera,visual.up));if(render)SaveFrame(cam,boot,rt,tex,dir,"112_deck_conversation");
                guns.SetReadinessCapture(1,1);guns.SetRecoilCapture(true);guns.ripple=true;Random.InitState(56041);
                if(!guns.Fire(-1)||!guns.Fire(1))throw new System.InvalidOperationException("Four upper gun salvo rejected");
                float shotTime=0;StepSalvo(guns,boot.Fx,ref shotTime,.65f);crew.Pose(shotTime);
                for(int side=-1;side<=1;side+=2)for(int gun=0;gun<2;gun++)
                    if(crew.UpperMount(side,gun)==null||crew.BarrelOffsetCapture(side,gun).magnitude<.015f)
                        throw new System.InvalidOperationException("Upper gun did not fire/recoil: "+side+"/"+gun);
                StepSalvo(guns,boot.Fx,ref shotTime,1.8f);crew.Pose(shotTime);boot.Fx.Clear();
                for(int side=-1;side<=1;side+=2)for(int gun=0;gun<2;gun++)if(crew.BarrelOffsetCapture(side,gun).sqrMagnitude!=0)
                    throw new System.InvalidOperationException("Upper gun did not return home");
                controls.SetHudCapture(true,960,456);HudLayout layout=controls.Layout(960,456);
                focus=visual.TransformPoint(new Vector3(0,12f,-13.3f));camera=visual.TransformPoint(new Vector3(-14f,15.5f,-11f));
                cam.transform.SetPositionAndRotation(camera,Quaternion.LookRotation(focus-camera,visual.up));
                float[] inputs={-1f,1f};string[] names={"113_direction_left","114_direction_right"};float[] edges=new float[2];
                for(int i=0;i<2;i++)
                {
                    controls.ResetInteraction();controls.PointerCapture(241,layout.wheel,true,false);
                    controls.PointerCapture(241,layout.wheel+Vector2.right*(layout.radius*inputs[i]),false,false);controls.ApplyStickCapture();
                    sail.SetCapture(boot.Ship.turn);crew.Pose(2.15f);
                    if(Mathf.Abs(sail.Angle+boot.Ship.turn*ShipSteeringSail.MaxAngle)>.001f)throw new System.InvalidOperationException("Sail does not follow joystick");
                    edges[i]=visual.InverseTransformPoint(sail.Sail.TransformPoint(new Vector3(.2f,10,-16.99f))).x;
                    if(render&&!workReview)SaveFrame(cam,boot,rt,tex,dir,names[i]);
                }
                if(Mathf.Abs(edges[1]-edges[0])<1.1f)throw new System.InvalidOperationException("Direction sail does not visibly deflect");
                controls.ResetInteraction();sail.SetCapture(0);crew.Pose(6f);
                Vector2 a=new Vector2(650,240),b=new Vector2(800,240);controls.PointerCapture(251,a,true,false);controls.PointerCapture(252,b,true,false);controls.PinchCapture();
                controls.PointerCapture(252,b+Vector2.right*2000f,false,false);controls.PinchCapture();oc.Apply(0);
                if(Mathf.Abs(oc.distance-27f)>.001f)throw new System.InvalidOperationException("Two-finger zoom did not reach the new close limit");
                controls.ResetInteraction();if(render&&!workReview)Render(oc,cam,boot,rt,tex,dir,new Shot("115_camera_close",215,20,oc.minDistance));
                if(workReview)
                {
                    controls.SetHudCapture(false);CrewActor gunner=null,loader=null,captain=null;
                    foreach(CrewActor actor in crew.ActorsCapture)
                    {
                        if(actor.gunIndex==2&&actor.duty==CrewActor.Duty.Gunner)gunner=actor;
                        if(actor.gunIndex==2&&actor.duty==CrewActor.Duty.Loader)loader=actor;
                        if(actor.duty==CrewActor.Duty.Captain)captain=actor;
                    }
                    crew.Pose(3f);focus=gunner.root.TransformPoint(new Vector3(0,1.45f,.34f));camera=gunner.root.TransformPoint(new Vector3(-2.2f,1.8f,1.2f));
                    cam.transform.SetPositionAndRotation(camera,Quaternion.LookRotation(focus-camera,visual.up));if(render)SaveFrame(cam,boot,rt,tex,dir,"116_gunner_grip");
                    crew.NotifyShot(crew.UpperMount(1,0),3f);CheckGunCrew(crew,boot.CurrentShip.crew,3.06f);
                    if(render)SaveFrame(cam,boot,rt,tex,dir,"117_gunner_recoil");
                    CheckGunCrew(crew,boot.CurrentShip.crew,4.35f);
                    if(loader.ReloadAmount<.8f)throw new System.InvalidOperationException("Loader did not work after barrel return");
                    focus=loader.root.TransformPoint(new Vector3(0,1.45f,.35f));camera=loader.root.TransformPoint(new Vector3(-1.8f,2f,1.5f));
                    cam.transform.SetPositionAndRotation(camera,Quaternion.LookRotation(focus-camera,visual.up));if(render)SaveFrame(cam,boot,rt,tex,dir,"118_loader_reload");
                    for(int side=-1;side<=1;side+=2)
                    {
                        crew.NotifySalvo(5f,side);crew.Pose(5.6f);
                        if(Mathf.Abs(Mathf.DeltaAngle(captain.yaw,captain.root.localEulerAngles.y)-side*28f)>.1f)
                            throw new System.InvalidOperationException("Captain order points at the wrong broadside");
                    }
                    focus=captain.root.TransformPoint(new Vector3(0,1.5f,.12f));camera=captain.root.TransformPoint(new Vector3(-.65f,1.95f,1.25f));
                    cam.transform.SetPositionAndRotation(camera,Quaternion.LookRotation(focus-camera,visual.up));if(render)SaveFrame(cam,boot,rt,tex,dir,"119_captain_order");
                    Debug.Log("SkyTide: gun crew verified — grips follow the real recoiling breech, loader starts after 0.9s return and settles before 2.2s readiness; captain points to both firing sides; "+(render?"six work frames rendered":"checks without extra frames"));
                }
                Debug.Log("SkyTide: deck life verified — 15 moving sailors; loaded/empty cargo speeds="+loaded.speed+"/"+returned.speed
                    +", patrol="+walking.speed+" m/s; pickup/wood contact/deposit/empty return OK; synchronized conversation OK; 4 upper guns fire/recoil/return; sail +/-12 deg, tail travel="
                    +Mathf.Abs(edges[1]-edges[0]).ToString("F3")+" m, joystick/release OK; real pinch limit="+oc.distance+" m; "+(render?"six frames rendered":"checks without extra frames"));
            }
            finally
            {
                index=0;foreach(CrewActor actor in crew.ActorsCapture)actor.renderer.forceMatrixRecalculationPerRender=recalculate[index++];
                controls.SetHudCapture(false);controls.ResetInteraction();sail.SetCapture(0);crew.SetVisible(false);guns.SetReadinessCapture(1,1);guns.SetRecoilCapture(false);guns.ripple=ripple;
                boot.Fx.Clear();Random.state=random;cam.transform.SetPositionAndRotation(oldPos,oldRot);
                cam.targetTexture=null;RenderTexture.active=null;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(tex);
            }
        }

        static void CheckRigging(ShipCrew crew,CrewModelData model)
        {
            Mesh baked=new Mesh();
            try
            {
                foreach(CrewActor actor in crew.ActorsCapture)
                {
                    if(actor.rope==null)continue;
                    if(actor.HandError>.002f)throw new System.InvalidOperationException("Rigger wrist target unreachable: "+actor.HandError);
                    if(Vector3.Distance(actor.ropePoints[0],actor.root.parent.parent.TransformPoint(ShipCrew.RiggingAnchor))>.0001f
                        ||Vector3.Distance(actor.ropePoints[5],actor.root.TransformPoint(ShipCrew.RiggingCleat))>.0001f)
                        throw new System.InvalidOperationException("Running rope detached from mast/cleat");
                    actor.renderer.BakeMesh(baked);Vector3[] vertices=baked.vertices;
                    Quaternion frame=Quaternion.Inverse(Quaternion.LookRotation(actor.ropeAxis,-actor.root.forward));
                    for(int side=0;side<2;side++)
                    {
                        Vector3 center=side==0?actor.ropeUpper:actor.ropeLower;
                        int[] tri=side==0?model.Grip.leftTriangles:model.Grip.rightTriangles;float clearance=float.PositiveInfinity;
                        for(int i=0;i<tri.Length;i+=3)
                        {
                            Vector3 a=frame*(actor.root.TransformPoint(vertices[tri[i]])-center);
                            Vector3 b=frame*(actor.root.TransformPoint(vertices[tri[i+1]])-center);
                            Vector3 c=frame*(actor.root.TransformPoint(vertices[tri[i+2]])-center);
                            clearance=Mathf.Min(clearance,TriangleRadius(new Vector2(a.x,a.y),new Vector2(b.x,b.y),new Vector2(c.x,c.y))-CrewRope.Radius);
                        }
                        if(clearance<-.0005f||clearance>.004f)throw new System.InvalidOperationException("Rigger fingers/rope contact failed: "+clearance);
                    }
                    Mesh tube=actor.rope.MeshCapture;
                    if(tube.vertexCount!=106||tube.triangles.Length!=432)throw new System.InvalidOperationException("Round rope mesh incomplete");
                    int spanVertices=0;Vector3 middle=(actor.ropeUpper+actor.ropeLower)*.5f;
                    foreach(Vector3 v in tube.vertices)
                    {
                        Vector3 local=frame*(actor.rope.TransformCapture.TransformPoint(v)-middle);
                        if(float.IsNaN(local.sqrMagnitude)||float.IsInfinity(local.sqrMagnitude))throw new System.InvalidOperationException("Non-finite rope tube");
                        if(Mathf.Abs(local.z)<.22f)
                        {
                            spanVertices++;
                            if(Mathf.Abs(new Vector2(local.x,local.y).magnitude-CrewRope.Radius)>.0001f)
                                throw new System.InvalidOperationException("Rope tube left the actual hand axis");
                        }
                    }
                    if(spanVertices<26)throw new System.InvalidOperationException("Rope has no round hand span");
                }
            }
            finally{Object.DestroyImmediate(baked);}
        }

        static void CaptureDeckMotion(Camera cam,SkyTideBootstrap boot,string dir)
        {
            ShipCrew crew=boot.Ship.GetComponent<ShipCrew>();CrewModelData model=boot.CurrentShip.crew;Transform visual=boot.Ship.visual;
            Vector3 oldPosition=cam.transform.position;Quaternion oldCamera=cam.transform.rotation,oldVisual=visual.localRotation;
            RenderTexture rt=new RenderTexture(960,456,24,RenderTextureFormat.ARGB32);Texture2D tex=new Texture2D(960,456,TextureFormat.RGB24,false);
            bool[] recalculate=new bool[crew.Count];int index=0;
            foreach(CrewActor actor in crew.ActorsCapture){recalculate[index++]=actor.renderer.forceMatrixRecalculationPerRender;actor.renderer.forceMatrixRecalculationPerRender=true;}
            try
            {
                crew.SetVisible(true);boot.Fx.Clear();boot.SetWindCapture(0,0);
                CrewActor rigger=null,other=null;
                foreach(CrewActor actor in crew.ActorsCapture)if(actor.rope!=null){if(rigger==null)rigger=actor;else other=actor;}
                if(rigger==null||other==null)throw new System.InvalidOperationException("Two running-rigging stations required");
                float reach=Mathf.Repeat(-rigger.phase*.13f,1f)*5.6f,pull=reach+5.6f*.49f;
                Vector3 upper=Vector3.zero;float independent=0;
                foreach(float t in new[]{reach,reach+.5f,pull,pull+.8f,reach+5.6f-.001f,reach+5.6f+.001f})
                {
                    CheckCrewPose(crew,t);CheckRigging(crew,model);
                    independent=Mathf.Max(independent,Mathf.Abs(rigger.RiggingPull-other.RiggingPull));
                    if(t==reach)upper=rigger.ropeUpper;
                    if(t==pull&&Vector3.Distance(upper,rigger.ropeUpper)<.16f)throw new System.InvalidOperationException("Rope pull is not visible");
                }
                if(independent<.25f)throw new System.InvalidOperationException("Riggers share the same work phase");
                foreach(Vector2 tilt in new[]{new Vector2(-4,-8),new Vector2(4,-8),new Vector2(-4,8),new Vector2(4,8)})
                {
                    visual.localRotation=oldVisual*Quaternion.Euler(tilt.x,0,tilt.y);
                    foreach(float age in new[]{0f,.06f,.45f,.9f,1.5f,2.2f})
                    {
                        for(int side=-1;side<=1;side+=2)for(int gun=0;gun<2;gun++)crew.NotifyShot(crew.UpperMount(side,gun),0);
                        CheckGunCrew(crew,model,age);CheckRigging(crew,model);
                        foreach(CrewActor actor in crew.ActorsCapture)
                        {
                            float deck=Vector3.Angle(actor.root.up,Vector3.up),body=Vector3.Angle(actor.root.TransformDirection(actor.Balance*Vector3.up),Vector3.up);
                            if(body>=deck*.91f)throw new System.InvalidOperationException("Sailor balance leans with the deck: "+actor.duty);
                            if(actor.duty==CrewActor.Duty.Helmsman&&actor.HandError>.002f)throw new System.InvalidOperationException("Balance pulled hands off helm");
                            if(actor.routine!=null)continue;
                            foreach(int foot in new[]{15,19})if(Vector3.Distance(actor.bones[foot].position,actor.root.TransformPoint(model.Rig.Point(foot)))>.002f)
                                throw new System.InvalidOperationException("Balance moved a planted foot: "+actor.duty);
                        }
                        CheckHelmSurface(model,crew);
                    }
                    DeckRoutine cargo=crew.CargoRoutineCapture;
                    foreach(float t in new[]{0f,.8f,2.15f,8f,cargo.Arrival+DeckRoutine.DropTime*.5f,cargo.Arrival+DeckRoutine.DropTime*.98f})
                    {CheckCrewPose(crew,t);CheckCargoContact(model,crew,t);}
                }
                visual.localRotation=oldVisual;crew.Pose(reach);
                Vector3 camera=rigger.root.TransformPoint(new Vector3(-2.2f,2.0f,2.6f));
                Vector3 focus=rigger.root.TransformPoint(new Vector3(0,1.02f,.25f));
                cam.transform.SetPositionAndRotation(camera,Quaternion.LookRotation(focus-camera,Vector3.up));SaveFrame(cam,boot,rt,tex,dir,"120_rigging_reach");
                crew.Pose(pull);SaveFrame(cam,boot,rt,tex,dir,"121_rigging_pull");
                foreach(int side in new[]{-1,1})
                {
                    visual.localRotation=oldVisual*Quaternion.Euler(3,0,side*8);CheckCrewPose(crew,pull);CheckRigging(crew,model);
                    focus=rigger.root.TransformPoint(new Vector3(0,1.02f,.25f));
                    cam.transform.SetPositionAndRotation(camera,Quaternion.LookRotation(focus-camera,Vector3.up));
                    SaveFrame(cam,boot,rt,tex,dir,side<0?"122_crew_bank_left":"123_crew_bank_right");
                }
                Debug.Log("SkyTide: deck motion verified — two independent 5.6s rope work cycles, closed finger/round rope contact, mast/cleat fixed; all 15 counter actual deck tilt, feet planted; helm/gun/rammer/cargo contacts at pitch +/-4 roll +/-8; four frames rendered");
            }
            finally
            {
                visual.localRotation=oldVisual;crew.Pose(0);crew.SetVisible(false);int at=0;
                foreach(CrewActor actor in crew.ActorsCapture)actor.renderer.forceMatrixRecalculationPerRender=recalculate[at++];
                cam.transform.SetPositionAndRotation(oldPosition,oldCamera);cam.targetTexture=null;RenderTexture.active=null;
                rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(tex);
            }
        }

        static void CaptureRecoil(OrbitCamera oc, Camera cam, SkyTideBootstrap boot, string dir)
        {
            Broadside guns = boot.Ship.GetComponent<Broadside>();
            Transform model = boot.Ship.visual.Find("Model");
            if (model == null || boot.CurrentShip.recoil == null) throw new System.InvalidOperationException("Recoil model/map missing");
            ShipGunWeights.Map map = JsonUtility.FromJson<ShipGunWeights.Map>(boot.CurrentShip.recoil.text);
            if (map.names.Length != 16 || map.indices.Length != 3072) throw new System.InvalidOperationException("Revalidate recoil geometry");
            Vector3 oldPosition = cam.transform.position;
            Quaternion oldRotation = cam.transform.rotation;
            Random.State random = Random.state;
            bool ripple = guns.ripple;
            RenderTexture rt = new RenderTexture(960,456,24,RenderTextureFormat.ARGB32);
            Texture2D tex = new Texture2D(960,456,TextureFormat.RGB24,false);
            try
            {
                Random.InitState(52711);
                boot.Fx.Clear(); boot.SetWindCapture(0f,0f);
                guns.SetReadinessCapture(1f,1f); guns.SetRecoilCapture(true); guns.ripple = true;
                Vector3 focus = model.TransformPoint(new Vector3(6.8f,4.55f,2.5f));
                Vector3 position = model.TransformPoint(new Vector3(19f,8f,6.5f));
                cam.transform.SetPositionAndRotation(position,Quaternion.LookRotation(focus-position,model.up));
                SaveFrame(cam,boot,rt,tex,dir,"90_gun_rest");
                if (!guns.Fire(1)) throw new System.InvalidOperationException("Recoil capture salvo rejected");
                float time = 0f;
                StepSalvo(guns,boot.Fx,ref time,0.1f); boot.Fx.Clear();
                float first = ((Vector3)guns.RecoilCapture(8)).magnitude;
                if (first < 0.30f || ((Vector3)guns.RecoilCapture(0)).sqrMagnitude != 0f
                    || ((Vector3)guns.RecoilCapture(15)).sqrMagnitude != 0f)
                    throw new System.InvalidOperationException("Recoil must follow actual ripple shot times, leaving unfired barrels fixed");
                MaterialPropertyBlock block = new MaterialPropertyBlock(); model.GetComponent<MeshRenderer>().GetPropertyBlock(block);
                Vector4[] offsets = block.GetVectorArray("_ShipGunOffsets");
                if (offsets.Length != 16 || (offsets[8]-guns.RecoilCapture(8)).sqrMagnitude > 1e-8f)
                    throw new System.InvalidOperationException("Recoil offsets not bound to renderer");
                SaveFrame(cam,boot,rt,tex,dir,"91_gun_recoil");
                StepSalvo(guns,boot.Fx,ref time,0.44f); boot.Fx.Clear();
                float returning = ((Vector3)guns.RecoilCapture(8)).magnitude;
                if (returning <= 0f || returning >= first) throw new System.InvalidOperationException("Barrel must run out smoothly");
                SaveFrame(cam,boot,rt,tex,dir,"92_gun_return");
                StepSalvo(guns,boot.Fx,ref time,1.7f); boot.Fx.Clear();
                for (int i=0;i<16;i++) if (guns.RecoilCapture(i).sqrMagnitude != 0f) throw new System.InvalidOperationException("Barrel did not return home");
                SaveFrame(cam,boot,rt,tex,dir,"93_gun_restored");
                guns.SetReadinessCapture(1f,1f); guns.SetRecoilCapture(true); guns.ripple = false;
                guns.Fire(-1); guns.Pump(0.03f); guns.Pump(0.09f);
                for (int i=0;i<16;i++)
                {
                    float amount=((Vector3)guns.RecoilCapture(i)).magnitude;
                    if (i<8 ? Mathf.Abs(amount-0.32f)>0.0001f : amount!=0f)
                        throw new System.InvalidOperationException("Simultaneous salvo recoil differs between barrels/sides");
                }
                Debug.Log("SkyTide: cannon recoil — 16 barrels/3072 vertices, renderer offsets OK; ripple and simultaneous fire OK; kick="
                    +first.ToString("F4")+", return="+returning.ToString("F4")+", restored=0; four GPU phases rendered");
            }
            finally
            {
                guns.SetRecoilCapture(false); guns.SetReadinessCapture(1f,1f); guns.ripple=ripple;
                boot.Fx.Clear(); Random.state=random; cam.transform.SetPositionAndRotation(oldPosition,oldRotation);
                cam.targetTexture=null; RenderTexture.active=null; rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
            }
        }

        static void CaptureWind(OrbitCamera oc,Camera cam,SkyTideBootstrap boot,string dir)
        {
            TextAsset mapAsset=boot.CurrentShip!=null?boot.CurrentShip.wind:null;
            if(mapAsset==null)throw new System.InvalidOperationException("Wind map missing from active ship");
            ShipWindWeights.Map map=JsonUtility.FromJson<ShipWindWeights.Map>(mapAsset.text);
            if(map.sails!=3||map.flags!=1||map.indices.Length!=906||map.pinnedVertices!=350)
                throw new System.InvalidOperationException("Wind mask counts changed: revalidate attachments");
            RenderTexture rt=new RenderTexture(960,456,24,RenderTextureFormat.ARGB32);
            Texture2D tex=new Texture2D(960,456,TextureFormat.RGB24,false);
            try
            {
                boot.SetWindCapture(0f,0f);
                Render(oc,cam,boot,rt,tex,dir,new Shot("83_cloth_rest",215f,12f,40f));
                float[] times={0f,0.35f,0.70f};
                string[] names={"84_wind_phase_a","85_wind_phase_b","86_wind_phase_c"};
                for(int i=0;i<times.Length;i++)
                {
                    boot.SetWindCapture(times[i],1f);
                    if(Shader.GetGlobalFloat("_ShipClothAmount")!=1f||Mathf.Abs(Shader.GetGlobalVector("_ShipClothWind").w-times[i])>0.0001f)
                        throw new System.InvalidOperationException("Cloth capture uniforms failed");
                    Render(oc,cam,boot,rt,tex,dir,new Shot(names[i],215f,12f,40f));
                }
                Debug.Log("SkyTide: cloth wind — 906 moving, 350 pinned, 3 sails/1 pennant; original mesh hash OK; rest + 3 GPU phases rendered");
            }
            finally {boot.SetWindCapture(0f,0f);cam.targetTexture=null;RenderTexture.active=null;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(tex);}
        }

        static void CheckHudLayout(HudLayout l)
        {
            Rect[] buttons = { l.port, l.starboard, l.mode, l.clouds, l.ship };
            for (int i=0;i<buttons.Length;i++)
            {
                Rect r=buttons[i];
                if(r.xMin<l.safe.xMin||r.yMin<l.safe.yMin||r.xMax>l.safe.xMax||r.yMax>l.safe.yMax)
                    throw new System.InvalidOperationException("HUD button outside safe area");
                for(int j=i+1;j<buttons.Length;j++)if(r.Overlaps(buttons[j]))
                    throw new System.InvalidOperationException("HUD buttons overlap");
            }
        }

        static void HudFrame(OrbitCamera oc,Camera cam,SkyTideBootstrap boot,TouchControls controls,string dir,string name,int width,int height)
        {
            RenderTexture rt=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32);
            Texture2D tex=new Texture2D(width,height,TextureFormat.RGB24,false);
            int before=controls.HudDrawCount;
            try
            {
                Render(oc,cam,boot,rt,tex,dir,new Shot(name,215f,12f,55f));
                if(controls.HudDrawCount!=before+1||controls.HudGlyphsDrawn<70||controls.HudMissingGlyphs!=0)
                    throw new System.InvalidOperationException("HUD text/render failed: glyphs="+controls.HudGlyphsDrawn+", missing="+controls.HudMissingGlyphs+", character="+controls.HudMissingCharacter);
            }
            finally {cam.targetTexture=null;RenderTexture.active=null;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(tex);}
        }

        static void CaptureHud(OrbitCamera oc,Camera cam,SkyTideBootstrap boot,string dir)
        {
            TouchControls controls=boot.GetComponent<TouchControls>();
            Broadside guns=controls.Guns();
            if(guns==null)throw new System.InvalidOperationException("HUD guns missing");
            bool ripple=guns.ripple;
            try
            {
                boot.Fx.Clear();guns.SetReadinessCapture(1f,1f);
                controls.SetHudCapture(true,960,432);
                HudLayout l=controls.Layout(960,432);CheckHudLayout(l);
                HudFrame(oc,cam,boot,controls,dir,"80_hud_ready",960,432);
                controls.PointerCapture(50,l.port.center,true,false);
                if(guns.Readiness(-1)>0.001f||controls.CapturedAsCamera(50))throw new System.InvalidOperationException("Port button did not fire or captured camera");
                controls.PointerCapture(60,l.mode.center,true,false);controls.PointerCapture(60,l.mode.center,false,true);
                if(guns.ripple==ripple)throw new System.InvalidOperationException("Volley mode button failed");
                controls.PointerCapture(51,l.wheel,true,false);
                controls.PointerCapture(51,l.wheel+new Vector2(l.radius*0.7f,-l.radius*0.5f),false,false);
                if(!controls.StickActive||controls.CapturedAsCamera(51))throw new System.InvalidOperationException("Joystick owner failed");
                Vector2 a=new Vector2(960f*0.7f,432f*0.45f),b=new Vector2(960f*0.85f,432f*0.4f);
                controls.PointerCapture(52,a,true,false);controls.PointerCapture(53,b,true,false);
                if(!controls.CapturedAsCamera(52)||!controls.CapturedAsCamera(53))throw new System.InvalidOperationException("Camera pointers captured by HUD");
                controls.PinchCapture();float distance=oc.distance;
                controls.PointerCapture(53,b+Vector2.right*35f,false,false);controls.PinchCapture();
                if(oc.distance>=distance)throw new System.InvalidOperationException("Pinch zoom failed");
                guns.SetReadinessCapture(0.4f,1f);
                HudFrame(oc,cam,boot,controls,dir,"81_hud_reload",960,432);
                controls.ResetInteraction();
                if(controls.ActivePointersCapture!=0||controls.StickActive||boot.Ship.throttle!=0f||boot.Ship.turn!=0f)
                    throw new System.InvalidOperationException("Pointer state survived reset");
                Rect cutout=new Rect(40f,8f,920f,410f);
                controls.SetHudCapture(true,1008,432,cutout);guns.SetReadinessCapture(1f,1f);
                CheckHudLayout(controls.Layout(1008,432));
                HudFrame(oc,cam,boot,controls,dir,"82_hud_cutout",1008,432);
                CheckHudLayout(HudLayout.Build(800,450,new Rect(0,0,800,450),controls.stickRadius));
                Debug.Log("SkyTide: HUD — Cyrillic/glyphs OK, ready/reload, shared hitboxes, mode/stick/two-finger pinch/reset OK; 16:9 and 21:9 safe areas OK");
            }
            finally {controls.SetHudCapture(false);guns.SetReadinessCapture(1f,1f);guns.ripple=ripple;boot.Fx.Clear();}
        }

        static void StepDamage(ShipDamageVisual damage, FxSystem fx, float duration)
        {
            while (duration > 1e-5f)
            {
                float dt = Mathf.Min(1f / 60f, duration);
                damage.Simulate(dt, fx);
                fx.Simulate(dt);
                duration -= dt;
            }
        }

        static void CaptureDamage(OrbitCamera oc, Camera cam, SkyTideBootstrap boot, RenderTexture rt, Texture2D tex, string dir)
        {
            TargetShip target = boot.Target;
            if (target == null || target.DamageVisual == null || target.DamageVisual.Model == null)
                throw new System.InvalidOperationException("Target damage visual is missing");
            ShipDamageVisual damage = target.DamageVisual;
            Transform oldTarget = oc.target;
            Vector3 oldPosition = target.transform.position;
            Quaternion oldRotation = target.transform.rotation;
            float oldLookHeight = oc.lookHeight;
            Random.State random = Random.state;
            Random.InitState(47071);
            try
            {
                target.ResetDamageCapture();
                boot.Fx.Clear();
                target.transform.SetPositionAndRotation(boot.Ship.transform.position + boot.Ship.transform.right * 90f, boot.Ship.transform.rotation);
                oc.target = target.transform;
                MeshFilter mf = damage.Model.GetComponent<MeshFilter>();
                Bounds bounds = mf.sharedMesh.bounds;
                oc.lookHeight = bounds.center.y;
                Vector3 restPosition = damage.Model.localPosition;
                Quaternion restRotation = damage.Model.localRotation;
                Render(oc, cam, boot, rt, tex, dir, new Shot("70_target_intact", 215f, 13f, 68f));
                Vector3 point = damage.Model.TransformPoint(bounds.center + new Vector3(bounds.extents.x * 0.8f, -bounds.extents.y * 0.5f, bounds.extents.z * 0.15f));
                target.OnHit(point);
                StepDamage(damage, boot.Fx, 0.12f);
                if (target.HitCount != 1 || damage.DebrisCount == 0 || !target.Alive || damage.Sinking)
                    throw new System.InvalidOperationException("Impact did not preserve target life or emit 3D fragments");
                Render(oc, cam, boot, rt, tex, dir, new Shot("71_target_impact", 215f, 13f, 68f));
                while (target.HitCount < 12)
                {
                    Vector3 hit = point + damage.Model.forward * ((target.HitCount % 3 - 1) * 2f);
                    target.OnHit(hit);
                }
                boot.Fx.Clear();
                StepDamage(damage, boot.Fx, 1.6f);
                if (damage.VentCount != 3 || damage.Sinking || !target.Alive || damage.DebrisCount > 32)
                    throw new System.InvalidOperationException("Persistent damage or debris bounds failed");
                Render(oc, cam, boot, rt, tex, dir, new Shot("72_target_damaged", 215f, 13f, 68f));
                while (target.HitCount < target.hitsToSink - 1) target.OnHit(point);
                boot.Fx.Clear();
                target.OnHit(point);
                StepDamage(damage, boot.Fx, 0.8f);
                if (target.Alive || !damage.Sinking || damage.Hidden || !damage.Model.gameObject.activeSelf || damage.DebrisCount == 0)
                    throw new System.InvalidOperationException("Sinking target disappeared before falling into clouds");
                Render(oc, cam, boot, rt, tex, dir, new Shot("73_target_foundering", 215f, 13f, 68f));
                StepDamage(damage, boot.Fx, 1.9f);
                // Только съёмка: кадр удерживает падающий корабль. Игровая камера не меняется.
                oc.lookHeight = damage.Model.TransformPoint(bounds.center).y - target.transform.position.y;
                Render(oc, cam, boot, rt, tex, dir, new Shot("74_target_cloud_entry", 215f, 20f, 80f));
                StepDamage(damage, boot.Fx, 2.5f);
                if (!damage.Hidden || damage.Model.gameObject.activeSelf)
                    throw new System.InvalidOperationException("Wreck did not disappear entirely below the cloud deck");
                int splashes = boot.Fx.CloudSplashCount;
                if (damage.HullCloudContacts!=1||splashes<1) throw new System.InvalidOperationException("Wreck must produce one hull/cloud contact; fragment splashes remain independent");
                target.ResetDamageCapture(); boot.Fx.Clear(); oc.lookHeight = bounds.center.y;
                if (damage.Sinking || damage.Hidden || damage.VentCount != 0 || damage.DebrisCount != 0
                    || damage.Model.localPosition != restPosition || Quaternion.Angle(damage.Model.localRotation, restRotation) > 0.001f || !target.Alive)
                    throw new System.InvalidOperationException("Damage state leaked into a restored target");
                Render(oc, cam, boot, rt, tex, dir, new Shot("75_target_restored", 215f, 13f, 68f));
                Debug.Log("SkyTide: damage — impact/debris OK, 3 vents, 30 hits, visible sinking, cloud contact=" + splashes + ", hidden below deck, restore clean");
            }
            finally
            {
                target.ResetDamageCapture(); boot.Fx.Clear(); Random.state = random;
                target.transform.SetPositionAndRotation(oldPosition, oldRotation);
                oc.target = oldTarget; oc.lookHeight = oldLookHeight;
            }
        }

        // Обычная сборка проверяет только текущую задачу. Полная галерея на llvmpipe занимала ~69 минут.
        // Разрешение/число кадров меняются только у съёмки в редакторе, качество Android-игры прежнее.
        static void CaptureTemporal(OrbitCamera oc, Camera cam, SkyTideBootstrap boot, string dir)
        {
            VolumetricClouds vol = boot.Volumetric;
            if (vol == null) return;
            RenderTexture rt = new RenderTexture(400, 190, 24, RenderTextureFormat.ARGB32);
            Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            try
            {
                vol.SetTemporalCapture(true);
                Render(oc, cam, boot, rt, tex, dir, new Shot("50_temporal_seed", 90f, 8f, 70f));
                if (vol.LastFrameUsedHistory) throw new System.InvalidOperationException("Cloud history was not reset before capture");
                Render(oc, cam, boot, rt, tex, dir, new Shot("51_temporal_turn", 92f, 8f, 70f));
                if (!vol.LastFrameUsedHistory) throw new System.InvalidOperationException("Cloud history was not used for a small camera turn");
                Render(oc, cam, boot, rt, tex, dir, new Shot("52_temporal_cut", 215f, 12f, 70f));
                if (vol.LastFrameUsedHistory) throw new System.InvalidOperationException("Cloud history survived a camera cut");
                vol.SetTemporalCapture(true);
                Render(oc, cam, boot, rt, tex, dir, new Shot("53_temporal_cut_reference", 215f, 12f, 70f));
                Debug.Log("SkyTide: temporal — first/reset OK, small turn uses history, camera cut resets; actual cloud distance=" + vol.LastFrameUsedCloudDistance);
            }
            finally
            {
                vol.SetTemporalCapture(false);
                cam.targetTexture = null;
                RenderTexture.active = null;
                rt.Release();
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(tex);
            }
        }

        static Vector3 BattleHit(TargetShip target,float z)
        {
            Transform model=target.DamageVisual.Model;Bounds box=target.Surface.Bounds;
            Vector3 a=model.TransformPoint(new Vector3(box.max.x+5f,3f,z)),b=model.TransformPoint(new Vector3(box.min.x-5f,3f,z)),hit;
            if(!target.TryHit(a,b,out hit))throw new System.InvalidOperationException("Surface shot missed the known hull section");
            Vector3 local=model.InverseTransformPoint(hit);
            if(z==0f&&(local.x<6f||local.x>7f))throw new System.InvalidOperationException("Hull impact is still on the enclosing box: "+local);
            target.OnHit(hit);return hit;
        }

        static void CaptureBattleLife(Camera cam,SkyTideBootstrap boot,string dir)
        {
            TargetShip target=boot.Target;ShipDamageVisual damage=target.DamageVisual;ShipCrew crew=boot.Ship.GetComponent<ShipCrew>();Transform visual=boot.Ship.visual;
            Vector3 oldTarget=target.transform.position,oldCamera=cam.transform.position;Quaternion oldTargetRotation=target.transform.rotation,oldCameraRotation=cam.transform.rotation;
            Random.State random=Random.state;RenderTexture rt=new RenderTexture(960,456,24,RenderTextureFormat.ARGB32);Texture2D tex=new Texture2D(960,456,TextureFormat.RGB24,false);
            bool[] recalculate=new bool[crew.Count];int at=0;foreach(CrewActor actor in crew.ActorsCapture){recalculate[at++]=actor.renderer.forceMatrixRecalculationPerRender;actor.renderer.forceMatrixRecalculationPerRender=true;}
            try
            {
                Random.InitState(62131);boot.Fx.Clear();target.ResetDamageCapture();crew.SetVisible(true);crew.Pose(8f);
                Transform supplies=visual.Find("Deck supplies");
                if(supplies==null||supplies.childCount!=6)throw new System.InvalidOperationException("Deck barrel/rope fittings missing");
                Vector3 focus=visual.TransformPoint(new Vector3(-4.1f,ShipCrew.MainDeck+.6f,-3.6f));
                Vector3 camera=visual.TransformPoint(new Vector3(-.4f,ShipCrew.MainDeck+3.3f,-1.1f));
                cam.transform.SetPositionAndRotation(camera,Quaternion.LookRotation(focus-camera,visual.up));SaveFrame(cam,boot,rt,tex,dir,"130_deck_supplies");
                target.transform.position=boot.Ship.transform.position+boot.Ship.transform.forward*240f+boot.Ship.transform.right*120f;
                CheckCrewPose(crew,1.9f);CrewActor lookout=crew.LookoutCapture,captain=null;
                foreach(CrewActor actor in crew.ActorsCapture)if(actor.duty==CrewActor.Duty.Captain)captain=actor;
                if(lookout.report<.95f||captain.report<.95f||lookout.HandError>.002f)throw new System.InvalidOperationException("Lookout/captain report not synchronized");
                focus=lookout.root.TransformPoint(new Vector3(0,1.4f,.12f));camera=lookout.root.TransformPoint(new Vector3(-1.65f,2f,2.1f));
                cam.transform.SetPositionAndRotation(camera,Quaternion.LookRotation(focus-camera,visual.up));SaveFrame(cam,boot,rt,tex,dir,"131_target_report");
                crew.SetVisible(false);target.transform.SetPositionAndRotation(boot.Ship.transform.position+boot.Ship.transform.right*90f,Quaternion.Euler(7,37,-4));
                Transform model=damage.Model;Bounds box=target.Surface.Bounds;Vector3 miss;
                Vector3 corner=new Vector3(box.max.x-.05f,box.max.y-.05f,box.max.z-.05f);
                if(target.TryHit(model.TransformPoint(corner),model.TransformPoint(corner+Vector3.left*.08f),out miss))
                    throw new System.InvalidOperationException("Empty space inside target bounds still reports a hit");
                Vector3 hit=BattleHit(target,0f);
                if(damage.ScarCount!=1||damage.ScarVertices<29||damage.DebrisCount==0)throw new System.InvalidOperationException("Projected scar or physical splinters missing");
                Vector3 localFocus=model.InverseTransformPoint(hit),localNormal=model.InverseTransformDirection(target.ImpactNormal);
                StepDamage(damage,boot.Fx,.09f);
                focus=model.TransformPoint(localFocus);camera=model.TransformPoint(localFocus+localNormal*16f+Vector3.up*5f+Vector3.forward*4f);
                cam.transform.SetPositionAndRotation(camera,Quaternion.LookRotation(focus-camera,Vector3.up));SaveFrame(cam,boot,rt,tex,dir,"132_hull_impact");
                BattleHit(target,-5f);BattleHit(target,5f);StepDamage(damage,boot.Fx,1f);boot.Fx.Clear();
                focus=model.TransformPoint(new Vector3(6,3,0));camera=model.TransformPoint(new Vector3(24,7,8));
                cam.transform.SetPositionAndRotation(camera,Quaternion.LookRotation(focus-camera,Vector3.up));SaveFrame(cam,boot,rt,tex,dir,"133_battle_scars");
                while(target.HitCount<15)BattleHit(target,(target.HitCount%3-1)*5f);
                boot.Fx.Clear();StepDamage(damage,boot.Fx,2f);
                if(!damage.Burning||boot.Fx.ActiveFlames==0||damage.VentCount!=3||damage.ScarCount>16)
                    throw new System.InvalidOperationException("Persistent hull fire/scar limits failed");
                cam.transform.SetPositionAndRotation(model.TransformPoint(new Vector3(27,9,10)),Quaternion.LookRotation(model.TransformPoint(new Vector3(5,4,0))-model.TransformPoint(new Vector3(27,9,10)),Vector3.up));
                SaveFrame(cam,boot,rt,tex,dir,"134_hull_fire");
                int flames=boot.Fx.ActiveFlames;
                while(target.HitCount<target.hitsToSink)BattleHit(target,0f);
                if(target.Alive||!damage.Sinking||damage.Hidden)throw new System.InvalidOperationException("Thirty hits no longer start visible foundering");
                boot.Fx.Clear();StepDamage(damage,boot.Fx,5.4f);
                if(!damage.Hidden||damage.HullCloudContacts!=1)throw new System.InvalidOperationException("Wreck/cloud entry must happen once before hiding");
                target.ResetDamageCapture();boot.Fx.Clear();
                if(damage.ScarCount!=0||damage.ScarVertices!=0||damage.Burning||damage.DebrisCount!=0||!target.Alive)
                    throw new System.InvalidOperationException("Battle damage leaked into respawn");
                OrbitCamera orbit=boot.Cam;orbit.SalvoImpulse(1);float peak=0;
                for(int frame=0;frame<120;frame++){orbit.Apply(1f/60f);peak=Mathf.Max(peak,orbit.RecoilAngle);}
                if(peak<.03f||peak>.3f||orbit.RecoilAngle>.001f)throw new System.InvalidOperationException("Salvo camera impulse failed to settle");
                Debug.Log("SkyTide: battle-life verified — exact source triangle hit under rotation, empty bounding-box corner rejected; projected scars/wood fragments; 3 attached fire vents, flames="+flames+"; thirty hits, one wreck/cloud entry, clean respawn; 4 staved barrels/2 rope coils; lookout/captain report; camera impulse peak="+peak.ToString("F3")+" deg and settled; five detail frames rendered");
            }
            finally
            {
                target.ResetDamageCapture();boot.Fx.Clear();target.transform.SetPositionAndRotation(oldTarget,oldTargetRotation);Random.state=random;
                crew.SetVisible(false);at=0;foreach(CrewActor actor in crew.ActorsCapture)actor.renderer.forceMatrixRecalculationPerRender=recalculate[at++];
                cam.transform.SetPositionAndRotation(oldCamera,oldCameraRotation);cam.targetTexture=null;RenderTexture.active=null;
                rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(tex);
            }
        }

        /// <summary>
        /// Обзор (проверки Claude): 4 кадра 960×456 с HUD — общий вид, против солнца, сверху на море облаков, взрыв на мишени.
        /// Каждый кадр проверяется автоматически: розовая заливка (шейдер не скомпилировался), чёрный/пустой кадр,
        /// пересвет (почти белый кадр). Провал — исключение, сборка падает сразу, а не на телефоне.
        /// </summary>
        static void CaptureOverview(OrbitCamera oc, Camera cam, SkyTideBootstrap boot, string dir)
        {
            float start = Time.realtimeSinceStartup;
            TouchControls controls = Object.FindFirstObjectByType<TouchControls>();
            RenderTexture rt = new RenderTexture(960, 456, 24, RenderTextureFormat.ARGB32);
            Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            Broadside guns = boot.Ship != null ? boot.Ship.GetComponent<Broadside>() : null;
            TargetShip target = boot.Target;
            Vector3 oldTarget = target != null ? target.transform.position : Vector3.zero;
            Quaternion oldRot = target != null ? target.transform.rotation : Quaternion.identity;
            try
            {
                boot.SetVolumetric(true);
                boot.Fx.Clear();
                if (controls != null) controls.SetHudCapture(true, rt.width, rt.height);
                if (target != null)
                {
                    // Мишень на правом траверзе в 260 м — видно радар, дугу прицела и корпус цели.
                    Transform s = boot.Ship.transform;
                    target.transform.SetPositionAndRotation(s.position + s.right * 260f + s.forward * 40f, Quaternion.LookRotation(s.forward));
                }
                if (guns != null) { guns.SetReadinessCapture(1, 1); guns.AimPreview(); }
                OverviewFrame(oc, cam, boot, rt, tex, dir, new Shot("200_overview_chase", 0f, 14f, 62f));
                if (guns != null) guns.AimPreview();
                OverviewFrame(oc, cam, boot, rt, tex, dir, new Shot("201_overview_sun", 35f, 8f, 75f));
                if (guns != null) guns.AimPreview();
                OverviewFrame(oc, cam, boot, rt, tex, dir, new Shot("202_overview_down", 140f, 45f, 110f));
                // Взрыв — точно в кадре (перед камерой, сбоку от корабля), на 0.2 с: видно нарисованный огненный шар.
                Shot hitShot = new Shot("203_overview_hit", 80f, 8f, 90f);
                boot.Fx.ClearPreview();
                boot.Fx.Explosion(Ahead(oc, cam, hitShot, 60f, 18f), 1f);
                for (int k = 0; k < 4; k++) boot.Fx.Simulate(0.05f);
                OverviewFrame(oc, cam, boot, rt, tex, dir, hitShot);
                // Мои изменения отдельными кадрами: залп (вспышка у дула, пороховой клуб), всплеск облака, баннеры и виньетка.
                foreach (string res in new[] { "FX/fx_flipbooks", "FX/hull_decals", "UI/banner_victory", "UI/banner_defeat", "UI/plate_notice", "UI/vignette_damage", "UI/icons" })
                    if (Resources.Load<Texture2D>(res) == null) throw new System.InvalidOperationException("Нет картинки Resources/" + res);
                Shader decal = Shader.Find("Hidden/SkyTide/HullDecal");
                if (decal == null || !decal.isSupported) throw new System.InvalidOperationException("Шейдер пробоин HullDecal не собран");
                boot.Fx.Clear();
                Transform sh = boot.Ship.transform;
                // Залп бортом, обращённым к камере (иначе вспышки за корпусом).
                Shot volleyShot = new Shot("204_overview_volley", 70f, 6f, 55f);
                Ahead(oc, cam, volleyShot, 0f, 0f);
                Vector3 toCam = cam.transform.position - sh.position;
                Vector3 broadside = Vector3.Dot(toCam, sh.right) > 0f ? sh.right : -sh.right;
                for (int k = 0; k < 4; k++)
                    boot.Fx.Fire(sh.position + broadside * 9f + sh.up * 4f + sh.forward * (k * 5f - 7.5f), broadside, Vector3.zero, 160f);
                for (int k = 0; k < 3; k++) boot.Fx.Simulate(0.04f);
                OverviewFrame(oc, cam, boot, rt, tex, dir, volleyShot);
                boot.Fx.Clear();
                Shot splashShot = new Shot("205_overview_splash", 0f, 10f, 70f);
                Vector3 splashAt = Ahead(oc, cam, splashShot, 160f, -30f); splashAt.y = boot.Fx.deckTop;
                boot.Fx.CloudSplash(splashAt, 1f);
                for (int k = 0; k < 8; k++) boot.Fx.Simulate(0.05f);
                OverviewFrame(oc, cam, boot, rt, tex, dir, splashShot);
                boot.Fx.Clear();
                ShipHud.CaptureBanner = 1;
                OverviewFrame(oc, cam, boot, rt, tex, dir, new Shot("206_banner_victory", 20f, 12f, 70f));
                ShipHud.CaptureBanner = 2;
                OverviewFrame(oc, cam, boot, rt, tex, dir, new Shot("207_banner_defeat", 20f, 12f, 70f));
                ShipHud.CaptureBanner = 0;
                Debug.Log("SkyTide: обзор и проверки кадров — успешно, секунд=" + (Time.realtimeSinceStartup - start).ToString("F1"));
            }
            finally
            {
                ShipHud.CaptureBanner = 0;
                if (controls != null) controls.SetHudCapture(false);
                if (target != null) target.transform.SetPositionAndRotation(oldTarget, oldRot);
                boot.Fx.ClearPreview(); boot.Fx.Clear();
                cam.targetTexture = null; RenderTexture.active = null; rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
            }
        }

        /// <summary>Поставить камеру обзора как в кадре s и вернуть точку перед ней (дальше на ahead, правее на right).</summary>
        static Vector3 Ahead(OrbitCamera oc, Camera cam, Shot s, float ahead, float right)
        {
            oc.yaw = s.yaw; oc.pitch = s.pitch; oc.distance = s.dist;
            oc.Snap(); oc.Apply(0f);
            Vector3 f = cam.transform.forward; f.y = 0f; f.Normalize();
            Vector3 r = Vector3.Cross(Vector3.up, f);
            return cam.transform.position + f * ahead + r * right;
        }

        static void OverviewFrame(OrbitCamera oc, Camera cam, SkyTideBootstrap boot, RenderTexture rt, Texture2D tex, string dir, Shot s)
        {
            Render(oc, cam, boot, rt, tex, dir, s);
            // Текст HUD: ни одной надписи мельче читаемого или за краем экрана (в плашках — только во внутреннем поле).
            TouchControls hudOwner = Object.FindFirstObjectByType<TouchControls>();
            if (hudOwner != null && hudOwner.HudTextIssues > 0)
                throw new System.InvalidOperationException("Кадр " + s.name + ": текст HUD — " + hudOwner.HudTextIssues + " нарушений, первое: " + hudOwner.HudFirstTextIssue);
            Color32[] px = tex.GetPixels32();
            int magenta = 0; double lum = 0; int white = 0;
            for (int i = 0; i < px.Length; i += 3)
            {
                Color32 c = px[i];
                if (c.r > 200 && c.b > 200 && c.g < 70) magenta++;
                float l = (0.3f * c.r + 0.59f * c.g + 0.11f * c.b) / 255f;
                lum += l;
                if (l > 0.97f) white++;
            }
            int n = (px.Length + 2) / 3;
            float mf = magenta / (float)n, ml = (float)(lum / n), wf = white / (float)n;
            Debug.Log("SkyTide: проверка " + s.name + " — розовое " + (mf * 100f).ToString("F2") + "%, яркость " + ml.ToString("F2") + ", пересвет " + (wf * 100f).ToString("F1") + "%");
            if (mf > 0.005f) throw new System.InvalidOperationException("Кадр " + s.name + ": розовая заливка " + (mf * 100f).ToString("F1") + "% — шейдер не скомпилировался");
            if (ml < 0.03f) throw new System.InvalidOperationException("Кадр " + s.name + ": почти чёрный (яркость " + ml.ToString("F3") + ")");
            if (wf > 0.35f) throw new System.InvalidOperationException("Кадр " + s.name + ": пересвет, белого " + (wf * 100f).ToString("F0") + "%");
        }

        static void CaptureQuick(OrbitCamera oc, Camera cam, SkyTideBootstrap boot, string dir)
        {
            float start = Time.realtimeSinceStartup;
            RenderTexture rt = new RenderTexture(800, 380, 24, RenderTextureFormat.ARGB32);
            Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            try
            {
                boot.SetVolumetric(true);
                if (boot.Volumetric != null) boot.Volumetric.ResetTowers(boot.Ship.transform.position);
                // Каждый пакет снимает свои состояния. Предыдущие профили остаются доступны отдельно.
                string profile = System.Environment.GetEnvironmentVariable("SKYTIDE_SHOT_PROFILE") ?? "battle-life";
                if(profile=="battle-life")
                {
                    CaptureLife(oc,cam,boot,dir,true,false);boot.Fx.Clear();
                    Render(oc,cam,boot,rt,tex,dir,new Shot("30_vol_toward_sun",35,8,75));
                    Render(oc,cam,boot,rt,tex,dir,new Shot("32_vol_side",90,8,70));
                    CaptureBattleLife(cam,boot,dir);
                    Broadside reviewGuns=boot.Ship.GetComponent<Broadside>();bool ripple=reviewGuns.ripple;Random.State random=Random.state;
                    try
                    {
                        reviewGuns.ripple=true;reviewGuns.SetReadinessCapture(1,1);Random.InitState(74131);reviewGuns.Fire(-1);reviewGuns.Fire(1);float time=0;
                        StepSalvo(reviewGuns,boot.Fx,ref time,.06f);Render(oc,cam,boot,rt,tex,dir,new Shot("45_vol_muzzle_flash",90,6,58));
                    }
                    finally{reviewGuns.ripple=ripple;reviewGuns.SetReadinessCapture(1,1);boot.Fx.Clear();Random.state=random;}
                    CaptureOverview(oc,cam,boot,dir);
                    Debug.Log("SkyTide: быстрая проверка battle-life (3x800x380 + 5x960x456 + обзор 8x960x456), кадров="+Directory.GetFiles(dir,"*.jpg").Length+", секунд="+(Time.realtimeSinceStartup-start).ToString("F1"));
                    return;
                }
                if(profile=="deck-motion")
                {
                    CaptureLife(oc,cam,boot,dir,true,false);CaptureDeckMotion(cam,boot,dir);
                    Debug.Log("SkyTide: быстрая проверка deck-motion (4x960x456), кадров="
                        +Directory.GetFiles(dir,"*.jpg").Length+", секунд="+(Time.realtimeSinceStartup-start).ToString("F1"));
                    return;
                }
                if(profile=="life-review"||profile=="crew-work")
                {
                    CaptureLife(oc,cam,boot,dir,profile=="crew-work");
                    Debug.Log("SkyTide: быстрая проверка "+profile+" (6x960x456), кадров="
                        +Directory.GetFiles(dir,"*.jpg").Length+", секунд="+(Time.realtimeSinceStartup-start).ToString("F1"));
                    return;
                }
                // Camera-only follow-up: keep the full seven-view comparison in the "crew" profile.
                if(profile=="crew-review")
                {
                    CaptureCrew(oc,cam,boot,dir);
                    Debug.Log("SkyTide: быстрая проверка crew-review (6x960x456), кадров="
                        +Directory.GetFiles(dir,"*.jpg").Length+", секунд="+(Time.realtimeSinceStartup-start).ToString("F1"));
                    return;
                }
                bool environment = profile != "fx";
                bool appearance = profile == "appearance";
                if (environment) Render(oc, cam, boot, rt, tex, dir, new Shot("30_vol_toward_sun", 35f, 8f, 75f));
                Render(oc, cam, boot, rt, tex, dir, new Shot("32_vol_side", 90f, 8f, 70f));
                if (boot.Volumetric != null && boot.Volumetric.LastFrameUsedCloudDistance)
                    throw new System.InvalidOperationException("Static capture unnecessarily used cloud-distance MRT");
                Debug.Log("SkyTide: статический кадр — без MRT/истории, цвет через общий March");
                if (environment)
                {
                    Render(oc, cam, boot, rt, tex, dir, new Shot("36_vol_away_from_sun", 215f, 12f, 70f));
                    Render(oc, cam, boot, rt, tex, dir, new Shot("37_vol_high", 140f, 50f, 115f));
                }
                Broadside guns = boot.Ship != null ? boot.Ship.GetComponent<Broadside>() : null;
                if (guns != null && guns.HasCannons && boot.Fx != null)
                    CaptureBroadside(oc, cam, boot, rt, tex, dir, guns, false, environment, appearance);
                else Debug.LogWarning("SkyTide: пушек нет — залп не снят");
                if (appearance)
                {
                    Render(oc, cam, boot, rt, tex, dir, new Shot("60_ship_materials", 215f, 12f, 40f));
                    Render(oc, cam, boot, rt, tex, dir, new Shot("61_ship_backlight", 90f, 10f, 42f));
                }
                // Четыре маленьких кадра, по одному Render на состояние: без дорогого многокадрового прогрева.
                if (appearance) CaptureTemporal(oc, cam, boot, dir);
                else if (profile == "damage") CaptureDamage(oc, cam, boot, rt, tex, dir);
                else if (profile == "hud") CaptureHud(oc, cam, boot, dir);
                else if (profile == "wind") CaptureWind(oc, cam, boot, dir);
                else if (profile == "life") CaptureLife(oc,cam,boot,dir);
                else if (profile == "crew") CaptureCrew(oc, cam, boot, dir);
                else if (environment) CaptureRecoil(oc, cam, boot, dir);
                string sizes = profile == "life" ? "7x800x380 + 6x960x456" : profile == "crew" ? "7x800x380 + 6x960x456"
                    : profile == "wind" || profile == "recoil" ? "7x800x380 + 4x960x456"
                    : profile == "hud" ? "7x800x380 + 2x960x432 + 1x1008x432"
                    : appearance ? "12x800x380 + 4x400x190" : "800x380";
                Debug.Log("SkyTide: быстрая проверка " + profile + " (" + sizes + "), кадров="
                    + Directory.GetFiles(dir, "*.jpg").Length + ", секунд=" + (Time.realtimeSinceStartup - start).ToString("F1"));
            }
            finally
            {
                cam.targetTexture = null;
                RenderTexture.active = null;
                rt.Release();
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(tex);
            }
        }

        public static void Capture(string scenePath)
        {
            Debug.Log("SkyTide: кадры — графика " + SystemInfo.graphicsDeviceType + " / " + SystemInfo.graphicsDeviceName);
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) { Debug.Log("SkyTide: нет графического устройства — кадров не будет"); return; }

            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../build/shots"));
            Directory.CreateDirectory(dir);
            try
            {
                EditorSceneManager.OpenScene(scenePath);
                SkyTideBootstrap boot = Object.FindFirstObjectByType<SkyTideBootstrap>();
                if (boot == null) { Debug.LogWarning("SkyTide: нет SkyTideBootstrap в сцене"); return; }
                boot.Init();
                OrbitCamera oc = boot.Cam;
                Camera cam = oc.GetComponent<Camera>();
                QualitySettings.shadowDistance = 160f;

                // Полная галерея — только по явному запросу, не тормозит каждый выпуск APK.
                if (System.Environment.GetEnvironmentVariable("SKYTIDE_FULL_SHOTS") != "1")
                {
                    CaptureQuick(oc, cam, boot, dir);
                    return;
                }

                // Курс корабля на старте 55°, солнце — курс 90°. yaw камеры считается от курса корабля.
                Shot[] shots =
                {
                    new Shot("01_chase", 0f, 14f, 62f),
                    new Shot("02_toward_sun", 35f, 8f, 75f),
                    new Shot("03_side", 90f, 10f, 60f),
                    new Shot("04_away_from_sun", 215f, 12f, 70f),
                    new Shot("05_high", 140f, 50f, 115f),
                    new Shot("06_low", -40f, -14f, 70f),
                    new Shot("07_close_stern", 160f, 6f, 38f),
                    new Shot("08_wide", 60f, 22f, 140f),
                };

                VolumetricClouds vol = cam.GetComponent<VolumetricClouds>();
                if (vol != null) Debug.Log("SkyTide: облака — " + vol.Probe());

                RenderTexture rt = new RenderTexture(1600, 760, 24, RenderTextureFormat.ARGB32);
                rt.antiAliasing = 4;
                Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                Shot[] debugShots =
                {
                    new Shot("90_dbg_fields", 35f, 8f, 75f),
                    new Shot("91_dbg_clouds", 35f, 8f, 75f),
                    new Shot("92_dbg_trans", 35f, 8f, 75f),
                };
                Shot[] shipShots =
                {
                    new Shot("20_ship2_chase", 20f, 12f, 62f),
                    new Shot("21_ship2_side", 90f, 8f, 58f),
                    new Shot("22_ship2_stern_close", 165f, 6f, 40f),
                };
                int idx = 0;
                Shot[] all = new Shot[shots.Length + debugShots.Length];
                shots.CopyTo(all, 0);
                debugShots.CopyTo(all, shots.Length);
                foreach (Shot s in all)
                {
                    if (vol != null) vol.debugView = idx >= shots.Length ? idx - shots.Length + 1 : 0;
                    boot.SetVolumetric(idx >= shots.Length);   // обычные кадры — как в игре по умолчанию, отладка — с объёмом
                    idx++;
                    oc.yaw = s.yaw; oc.pitch = s.pitch; oc.distance = s.dist;
                    oc.Snap();
                    oc.Apply(0f);
                    boot.World.Step(cam.transform.position, 0f);
                    cam.aspect = (float)rt.width / rt.height;
                    cam.targetTexture = rt;
                    cam.Render();
                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                    tex.Apply();
                    RenderTexture.active = null;
                    cam.targetTexture = null;
                    File.WriteAllBytes(Path.Combine(dir, s.name + ".jpg"), tex.EncodeToJPG(88));
                }
                if (vol != null) vol.debugView = 0;
                // Объёмные облака (тестовый режим) — кадры для настройки.
                boot.SetVolumetric(true);
                if (vol != null) vol.ResetTowers(boot.Ship.transform.position);
                Render(oc, cam, boot, rt, tex, dir, new Shot("30_vol_toward_sun", 35f, 8f, 75f));
                Render(oc, cam, boot, rt, tex, dir, new Shot("31_vol_wide", 60f, 22f, 140f));
                Render(oc, cam, boot, rt, tex, dir, new Shot("32_vol_side", 90f, 10f, 60f));
                Render(oc, cam, boot, rt, tex, dir, new Shot("33_vol_low", -40f, -14f, 70f));
                Render(oc, cam, boot, rt, tex, dir, new Shot("36_vol_away_from_sun", 215f, 12f, 70f));
                Render(oc, cam, boot, rt, tex, dir, new Shot("37_vol_high", 140f, 50f, 115f));
                // Залп прокручивается по времени: факел → волна вспышек → дым → контакт с облаками.
                Broadside guns = boot.Ship != null ? boot.Ship.GetComponent<Broadside>() : null;
                if (guns != null && guns.HasCannons && boot.Fx != null)
                {
                    CaptureBroadside(oc, cam, boot, rt, tex, dir, guns);
                }
                else Debug.LogWarning("SkyTide: пушек нет — залп не снят");
                if (vol != null)
                {
                    Texture2D sheet = vol.ImpostorSheet();
                    if (sheet != null) File.WriteAllBytes(Path.Combine(dir, "93_dbg_impostors.jpg"), sheet.EncodeToJPG(90));
                    else Debug.LogWarning("SkyTide: листа импосторов нет");
                }
                // Те же облака без постобработки (настройка формы и света до ACES/блума).
                SkyTidePost post = cam.GetComponent<SkyTidePost>();
                if (post != null) post.enabled = false;
                Render(oc, cam, boot, rt, tex, dir, new Shot("34_vol_nopost_toward_sun", 35f, 8f, 75f));
                Render(oc, cam, boot, rt, tex, dir, new Shot("35_vol_nopost_wide", 60f, 22f, 140f));
                if (post != null) post.enabled = true;
                boot.SetVolumetric(false);

                int shipCount = boot.ships != null ? boot.ships.Length : 0;
                if (shipCount > 1)
                {
                    boot.NextShip();
                    foreach (Shot s in shipShots) Render(oc, cam, boot, rt, tex, dir, s);
                }
                Debug.Log("SkyTide: кадров снято: " + Directory.GetFiles(dir, "*.jpg").Length + " → " + dir + ", моделей: " + shipCount);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("SkyTide: кадры не сняты: " + e);
                if (Application.isBatchMode) throw;
            }
            finally
            {
                // Сцену с диска — без объектов, созданных для съёмки.
                EditorSceneManager.OpenScene(scenePath);
            }
        }
    }
}
#endif
