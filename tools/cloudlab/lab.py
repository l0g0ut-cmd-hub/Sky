import numpy as np, sys, time
from PIL import Image
NZ=np.fromfile('' + __import__("os").path.join(__import__("os").path.dirname(__import__("os").path.abspath(__file__)), "..", "..", "Assets", "_Project", "Art", "CloudNoise3D.bytes") + '',np.uint8).reshape(64,64,64,4).astype(np.float32)/255  # [z][y][x][c]

def tex3(p, c):
    """трилинейная выборка с повтором; p в текстурных координатах (N,3)"""
    q=p*64-0.5; i0=np.floor(q).astype(np.int64); f=q-i0
    out=0
    for dx in (0,1):
        for dy in (0,1):
            for dz in (0,1):
                w=(f[:,0] if dx else 1-f[:,0])*(f[:,1] if dy else 1-f[:,1])*(f[:,2] if dz else 1-f[:,2])
                out=out+w*NZ[(i0[:,2]+dz)%64,(i0[:,1]+dy)%64,(i0[:,0]+dx)%64,c]
    return out

P=dict(R=190., H=520., base=-10., noiseScale=620., billow=0.95, thresh=0.42, sharp=3.2, erode=0.45, detailScale=120.,
       dens=0.03, absorb=1.5, sunInt=8.0, ambInt=0.75, g1=0.6, g2=-0.2, mix=0.65, rim=1.5)
import sys; sys.path.insert(0, __import__("os").path.dirname(__import__("os").path.abspath(__file__))); from puffs import make_tower
SPH=make_tower(7)
P.update(dens=0.035, noiseScale=300., detailScale=90., billow=0.55, erode=0.5, ambInt=0.8, sunInt=9.0)
def tower_density(p, P, detail=True):
    q=p.copy(); q[:,1]-=P['base']
    # поле «пухлости»: максимум по сферам (1 у центра, 0 на поверхности)
    f=np.full(len(p),-1.0)
    for x,y,z,r in SPH:
        d=np.sqrt((q[:,0]-x)**2+(q[:,1]-y)**2+(q[:,2]-z)**2)
        f=np.maximum(f,1-d/r)
    near=f>-0.35
    out=np.zeros(len(p))
    if not near.any(): return out
    pn=p[near]; fn=f[near]; hq=q[near,1]
    n=tex3(pn/P['noiseScale'],0)
    bump=tex3(pn/(P['noiseScale']*0.32),0)              # мелкие клубы «цветной капусты» по поверхности
    m=np.clip((fn+(n-0.5)*P['billow']+(bump-0.5)*0.2)*4.0,0,1)
    m*=np.clip(hq/(P['H']*0.06),0,1)            # плоское дно
    if detail:
        dn=tex3(pn/P['detailScale'],1)*0.7+tex3(pn/(P['detailScale']*0.45),2)*0.3
        edge=1-np.clip((m-0.4)/0.45,0,1)
        m=np.clip(m-dn*P['erode']*edge,0,1)
    m=m*m*(3-2*m)
    out[near]=m*P['dens']
    return out

def HG(c,g): return (1-g*g)/(12.566*np.power(np.maximum(1+g*g-2*g*c,1e-3),1.5))

def render(cam, target, sun, W=300, Hh=240, fov=55, steps=150, P=P, bg=(0.32,0.30,0.34), mode='full'):
    fwd=target-cam; fwd/=np.linalg.norm(fwd); right=np.cross(fwd,[0,1,0]); right/=np.linalg.norm(right); up=np.cross(right,fwd)
    t=np.tan(np.radians(fov)/2); xs=(np.arange(W)+0.5)/W*2-1; ys=1-(np.arange(Hh)+0.5)/Hh*2
    XX,YY=np.meshgrid(xs*t*W/Hh,ys*t); rd=fwd+XX[...,None]*right+YY[...,None]*up; rd=(rd/np.linalg.norm(rd,axis=-1,keepdims=True)).reshape(-1,3)
    n=len(rd); trans=np.ones(n); light=np.zeros((n,3))
    dist=np.linalg.norm(target-cam); t0=dist-P['R']*1.8; t1=dist+P['R']*1.8; stepLen=(t1-t0)/steps
    sun=sun/np.linalg.norm(sun); cosT=rd@sun
    phase=np.minimum((HG(cosT,P['g2'])*(1-P['mix'])+HG(cosT,P['g1'])*P['mix'])*12.566,2.6)
    sunCol=np.array([1.0,0.36,0.08]); ambTop=np.array([0.36,0.22,0.55]); ambBot=np.array([0.16,0.10,0.34])
    tt=t0+stepLen*0.5
    for s in range(steps):
        alive=trans>0.02
        if not alive.any(): break
        idx=np.nonzero(alive)[0]
        p=cam+rd[idx]*tt
        d=tower_density(p,P)
        m=d>1e-5
        if m.any():
            ii=idx[m]; pp=p[m]; dd=d[m]
            od=np.zeros(len(pp)); seg=10.; acc=0.
            for k in range(6):
                od+=tower_density(pp+sun*(acc+seg*0.5),P,False)*seg; acc+=seg; seg*=1.9
            sunT=np.exp(-od*P['absorb'])
            # многократное рассеяние (приближение): несколько октав с ослабленным поглощением
            ms=np.exp(-od*P['absorb']*0.22)*0.2
            edge=(1-np.clip(dd/(P['dens']*0.7),0,1))**1.6
            rim=edge*np.clip(cosT[ii],0,1)**4
            sunL=(sunT*phase[ii]*(1+P['rim']*rim)+ms)
            hgt=np.clip((pp[:,1]-P['base'])/P['H'],0,1)
            ambVis=np.exp(-dd*35)
            amb=(ambBot+(ambTop-ambBot)*hgt[:,None])*(0.15+0.85*ambVis)[:,None]*np.clip(0.35+0.65*hgt,0,1)[:,None]
            if mode=='sun': amb=amb*0
            if mode=='amb': sunL=sunL*0
            # Цвет солнца по глубине: открытые вершины — золото, полутень — красно-оранжевый.
            gold=np.array([1.0,0.62,0.22]); ember=np.array([1.0,0.24,0.06])
            sc=ember+(gold-ember)*np.clip(sunT,0,1)[:,None]**0.6
            msc=np.array([0.95,0.38,0.42])                  # многократное рассеяние — розовое
            src=sc*P['sunInt']*(sunT*phase[ii]*(1+P['rim']*rim))[:,None]+msc*P['sunInt']*ms[:,None]*0.6+amb*P['ambInt']
            st=np.exp(-dd*stepLen)
            light[ii]+=trans[ii,None]*src*0.86*(1-st)[:,None]
            trans[ii]*=st
        tt+=stepLen
    col=np.array(bg)*trans[:,None]+light
    col=col/(1+col)*1.15  # простой тонмаппинг для оценки
    img=(np.clip(col,0,1)**(1/2.2)*255).reshape(Hh,W,3).astype(np.uint8)
    return img, trans.reshape(Hh,W)

if __name__=='__main__':
    t=time.time()
    sun=np.array([1.0,0.12,0.0])
    tgt=np.array([0,200.,0])
    tgt=np.array([0,240.,0])
    views=[('side_lit',np.array([-150,90.,-800]),sun),('toward_sun',np.array([-800,90.,-150]),sun),('away',np.array([800,110.,150]),sun)]
    tiles=[]
    for name,cam,s in views:
        img,tr=render(cam,tgt,s); tiles.append(img); print(name,'%.1fs'%(time.time()-t))
    Image.fromarray(np.hstack(tiles)).resize((900*2,240*2),Image.NEAREST).save('tower.png')
