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
                string profile = System.Environment.GetEnvironmentVariable("SKYTIDE_SHOT_PROFILE") ?? "recoil";
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
                else if (environment) CaptureRecoil(oc, cam, boot, dir);
                string sizes = profile == "wind" || profile == "recoil" ? "7x800x380 + 4x960x456"
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
