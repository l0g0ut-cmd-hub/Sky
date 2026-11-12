"""Identify the intact aft sail and its sewn rope perimeter; leave its vertical mast fixed."""
from pathlib import Path
import hashlib,json,struct
import numpy as np
from build_cloth_wind import components

root=Path(__file__).resolve().parents[1]
source=root/'Assets/_Project/Models/CorsairRepaired.bytes';data=source.read_bytes()
count,slots=struct.unpack_from('<ii',data,4);p=np.frombuffer(data,'<f4',count*3,12).reshape(-1,3)
offset=12+count*32;parts=[]
for slot in range(slots):
    _,n=struct.unpack_from('<ii',data,offset);offset+=8
    tri=np.frombuffer(data,'<i4',n,offset).reshape(-1,3);offset+=n*4
    if slot not in (2,3,4):continue
    chosen=set()
    for indices,points in components(p,tri):
        lo,hi=points.min(0),points.max(0)
        cloth=slot==3 and len(points)==380 and hi[2]<-9
        outline=slot==2 and len(indices)==42 and lo[1]>9.8 and hi[1]<15 and lo[2]<-16.9 and -9.8<hi[2]<-9.5
        patches=slot==4 and hi[2]<-9.5 and lo[1]>9.8 and abs(lo[0])<1.2 and abs(hi[0])<1.2
        if cloth or outline or patches:chosen.update(indices.tolist())
    mask=np.isin(tri,list(chosen)).all(1)
    if mask.any():parts.append({'slot':slot,'triangles':np.flatnonzero(mask).tolist()})
assert {part['slot'] for part in parts}>={2,3},parts
result={'vertexCount':count,'meshSha256':hashlib.sha256(data).hexdigest(),'pivot':[0,10.412,-13.6395],'parts':parts}
output=source.with_name('CorsairRepaired_steering.json');output.write_text(json.dumps(result,separators=(',',':'))+'\n')
print('Steering assembly:',[(part['slot'],len(part['triangles'])) for part in parts],'triangles; original source unchanged')
