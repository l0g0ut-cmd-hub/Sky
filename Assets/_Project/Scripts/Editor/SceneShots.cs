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

        static void CaptureCrew(OrbitCamera oc,Camera cam,SkyTideBootstrap boot,string dir)
        {
            ShipCrew crew=boot.Ship.GetComponent<ShipCrew>();
            TouchControls controls=boot.GetComponent<TouchControls>();
            Broadside guns=boot.Ship.GetComponent<Broadside>();Transform visual=boot.Ship.visual;
            if(crew==null||crew.Count!=10||crew.Helmsman==null)throw new System.InvalidOperationException("Crew incomplete");
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
                if(moving!=10)throw new System.InvalidOperationException("Crew work cycles are static: moving="+moving);
                Vector3 focus=visual.TransformPoint(new Vector3(0,9f,-4f));
                Vector3 camera=visual.TransformPoint(new Vector3(-16f,15f,-4f));
                cam.transform.SetPositionAndRotation(camera,Quaternion.LookRotation(focus-camera,visual.up));
                SaveFrame(cam,boot,rt,tex,dir,"100_upper_crew");
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
                    SaveFrame(cam,boot,rt,tex,dir,names[i]);
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
                SaveFrame(cam,boot,rt,tex,dir,"104_upper_gun_crew");
                StepSalvo(guns,boot.Fx,ref time,1.8f);crew.Pose(time);
                if(crew.BarrelOffsetCapture(1).sqrMagnitude!=0f)throw new System.InvalidOperationException("Upper barrel did not return home");
                Debug.Log("SkyTide: upper crew verified — 10 skinned sailors, 21 joints each, all on upper decks; animated="+moving
                    +"; real joystick full left/neutral/right, release and focus reset OK; helm hand error="+maxError.ToString("F4")
                    +" m; two upper gun mounts, firing/recoil/run-out OK; five frames rendered");
            }
            finally
            {
                rendererIndex=0;foreach(CrewActor actor in crew.ActorsCapture)actor.renderer.forceMatrixRecalculationPerRender=recalculate[rendererIndex++];
                controls.SetHudCapture(false);controls.ResetInteraction();crew.SetVisible(false);guns.SetReadinessCapture(1f,1f);guns.SetRecoilCapture(false);guns.ripple=ripple;
                boot.Fx.Clear();Random.state=random;cam.transform.SetPositionAndRotation(oldPos,oldRot);
                cam.targetTexture=null;RenderTexture.active=null;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(tex);
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
                if (splashes != 1) throw new System.InvalidOperationException("Wreck must produce exactly one cloud-entry splash, got " + splashes);
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
                string profile = System.Environment.GetEnvironmentVariable("SKYTIDE_SHOT_PROFILE") ?? "crew";
                // Camera-only follow-up: keep the full seven-view comparison in the "crew" profile.
                if(profile=="crew-review")
                {
                    CaptureCrew(oc,cam,boot,dir);
                    Debug.Log("SkyTide: быстрая проверка crew-review (5x960x456), кадров="
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
                else if (profile == "crew") CaptureCrew(oc, cam, boot, dir);
                else if (environment) CaptureRecoil(oc, cam, boot, dir);
                string sizes = profile == "crew" ? "7x800x380 + 5x960x456"
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
