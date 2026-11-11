"""Geometry/attachment checks for the sparse GPU wind stream (no Unity required)."""
import hashlib, json, struct
from pathlib import Path
import numpy as np

root=Path(__file__).resolve().parents[1]
b=(root/'Assets/_Project/Models/CorsairRepaired.bytes').read_bytes()
data=json.loads((root/'Assets/_Project/Models/CorsairRepaired_wind.json').read_text())
count,slots=struct.unpack_from('<ii',b,4)
assert data['meshSha256']==hashlib.sha256(b).hexdigest() and data['vertexCount']==count
indices=np.array(data['indices']); packed=np.array(data['packed'],dtype='<i4').view('u1').reshape(-1,4)
assert len(indices)==906 and len(np.unique(indices))==906 and packed[:,0].min()>0
assert data['pinnedVertices']==350 and data['sails']==3 and data['flags']==1
v=np.frombuffer(b,'<f4',count*3,12).reshape(-1,3)
off=12+count*32
allowed=set(); wood=None; flag_vertices=None
for slot in range(slots):
    _,n=struct.unpack_from('<ii',b,off);off+=8
    t=np.frombuffer(b,'<i4',n,off);off+=n*4
    if slot in (3,7): allowed.update(t.tolist())
    else: assert not np.isin(indices,t).any(), f'non-cloth vertices move in slot {slot}'
    if slot==5: wood=v[np.unique(t)]
    if slot==7: flag_vertices=v[np.unique(t)]
assert set(indices).issubset(allowed)
tip=flag_vertices[flag_vertices[:,2]>flag_vertices[:,2].max()-0.03]
tail=flag_vertices[flag_vertices[:,2]<flag_vertices[:,2].min()+0.03]
pole=wood[(abs(wood[:,0])<0.2)&(abs(wood[:,2]-tip[:,2].mean())<0.15)&(wood[:,1]>17)]
assert len(pole)>3 and pole[:,1].max()>tip[:,1].max(), 'pennant pole not found'
pole_distance=lambda p:np.linalg.norm(p[:,None,[0,2]]-pole[None,:,[0,2]],axis=2).min()
assert pole_distance(tip)<0.15, 'pennant is not pinned at its wooden pole'
assert pole_distance(tail)>pole_distance(tip), 'wrong pennant attachment end'

positions=v[indices].astype(float); freedom=packed[:,0]/255.;gradient=(packed[:,1:3].astype(float)-128)/64
flag=packed[:,3]>192;k1=np.where(flag[:,None],[0,1.55],[0.22,0.45]);k2=np.where(flag[:,None],[0.7,2.7],[-0.28,0.8])
amplitude=np.where(flag,.65,.30)*1.25
max_delta=0
rng=np.random.default_rng(49)
for time in np.linspace(0,10,121):
    p1=(positions[:,1:]*k1).sum(1)-time*np.where(flag,5.3,1.7)
    p2=(positions[:,1:]*k2).sum(1)-time*np.where(flag,7.8,2.9)
    wave=.75*np.sin(p1)+.2*np.sin(p2)
    delta=amplitude*freedom*wave
    changed=positions.copy();changed[:,0]+=delta
    assert (changed>=v.min(0)-1e-5).all() and (changed<=v.max(0)+1e-5).all(), 'motion leaves original culling bounds'
    max_delta=max(max_delta,float(abs(delta).max()))
    slope=amplitude[:,None]*(gradient*wave[:,None]+freedom[:,None]*(.75*np.cos(p1)[:,None]*k1+.2*np.cos(p2)[:,None]*k2))
    n=rng.normal(size=positions.shape);n/=np.linalg.norm(n,axis=1)[:,None]
    t=rng.normal(size=positions.shape);t-=n*(n*t).sum(1)[:,None];t/=np.linalg.norm(t,axis=1)[:,None]
    nn=n.copy();nn[:,1:]-=n[:,:1]*slope
    tt=t.copy();tt[:,0]+=(t[:,1:]*slope).sum(1)
    assert np.isfinite(nn).all() and np.isfinite(tt).all()
    assert abs((nn*tt).sum(1)).max()<1e-12, 'normal/tangent basis distorted'
print(f'Wind mask OK: 906 vertices, 350 pins, 3 sails and pennant; pole distance={pole_distance(tip):.4f}m.')
print(f'121 phases stay within original culling bounds; max conservative displacement={max_delta:.4f}m; normal/tangent orthogonality OK.')
