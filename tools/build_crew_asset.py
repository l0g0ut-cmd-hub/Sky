"""Import the generated deckhand without decimation; bake a 21-joint skin in Blender.

python tools/build_crew_asset.py tools/source/Sailor.glb
"""
from pathlib import Path
import io, json, struct, subprocess, tempfile, hashlib, shutil
import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT/'Assets/_Project/Models/Crew'

def load(path):
    raw = Path(path).read_bytes(); offset = 12; doc = data = None
    while offset < len(raw):
        size, kind = struct.unpack_from('<II', raw, offset); offset += 8
        block = raw[offset:offset+size]; offset += size
        if kind == 0x4e4f534a: doc = json.loads(block)
        elif kind == 0x004e4942: data = block
    return doc, data

def accessor(doc, data, index):
    a=doc['accessors'][index];v=doc['bufferViews'][a['bufferView']]
    types={5126:'<f4',5125:'<u4',5123:'<u2',5121:'u1'}
    sizes={'SCALAR':1,'VEC2':2,'VEC3':3,'VEC4':4,'MAT4':16};n=sizes[a['type']]
    dt=np.dtype(types[a['componentType']]);off=v.get('byteOffset',0)+a.get('byteOffset',0)
    stride=v.get('byteStride',dt.itemsize*n)
    return np.ndarray((a['count'],n),dt,buffer=data,offset=off,strides=(stride,dt.itemsize)).copy()

def skeleton():
    names=['Hips','Spine','Chest','Neck','Head','LShoulder','LUpperArm','LForeArm','LHand',
           'RShoulder','RUpperArm','RForeArm','RHand','LThigh','LShin','LFoot','LToe','RThigh','RShin','RFoot','RToe']
    parents=[-1,0,1,2,3,2,5,6,7,2,9,10,11,0,13,14,15,0,17,18,19]
    p=[[0,.94,0],[0,1.12,0],[0,1.36,-.015],[0,1.56,.015],[0,1.68,.025],
       [-.10,1.45,-.05],[-.20,1.44,-.065],[-.29,1.215,-.075],[-.323,1.015,-.07],
       [.10,1.45,-.05],[.20,1.44,-.065],[.29,1.215,-.075],[.323,1.015,-.07],
       [-.125,.93,0],[-.235,.49,-.035],[-.30,.13,-.055],[-.345,.055,.09],
       [.125,.93,0],[.235,.49,-.035],[.30,.13,-.055],[.345,.055,.09]]
    # Tail positions guide heat weighting; transforms are later rebuilt in Unity's coordinates.
    tails=[]
    for i in range(len(names)):
        kids=[j for j,pid in enumerate(parents) if pid==i]
        if i==4: tail=[0,1.80,.025]
        elif i in (8,12): tail=[p[i][0]*1.05,.90,-.045]
        elif i in (16,20): tail=[p[i][0],.055,.17]
        elif i==0: tail=p[1]
        else: tail=p[kids[0]]
        tails.append(tail)
    return names,np.array(parents),np.array(p),np.array(tails)

def main(source):
    doc,data=load(source);assert len(doc['nodes'])==1 and len(doc['meshes'])==1
    assert np.allclose(np.array(doc['nodes'][0].get('matrix',np.eye(4).ravel())).reshape(4,4),np.eye(4))
    assert len(doc['meshes'][0]['primitives'])==1
    prim=doc['meshes'][0]['primitives'][0];at=prim['attributes']
    p=accessor(doc,data,at['POSITION']);n=accessor(doc,data,at['NORMAL']);uv=accessor(doc,data,at['TEXCOORD_0'])
    tri=accessor(doc,data,prim['indices']).reshape(-1,3).astype(np.int32)
    center=np.array([(p[:,0].min()+p[:,0].max())/2,p[:,1].min(),(p[:,2].min()+p[:,2].max())/2])
    p=(p-center)*(1.82/np.ptp(p[:,1]));p[:,0]*=-1;n[:,0]*=-1
    face=np.cross(p[tri[:,1]]-p[tri[:,0]],p[tri[:,2]]-p[tri[:,0]])
    flip=np.sum(face*n[tri].sum(1),axis=1)<0;tri[flip]=tri[flip][:,[0,2,1]]
    uv[:,1]=1-uv[:,1]
    names,parents,joints,tails=skeleton();OUT.mkdir(parents=True,exist_ok=True)
    with tempfile.TemporaryDirectory(prefix='sky-crew-') as tmp:
        tmp=Path(tmp);inp=tmp/'input.npz';weights=tmp/'weights.npz'
        np.savez(inp,positions=p,triangles=tri,joints=joints,tails=tails,parents=parents,names=names)
        run=subprocess.run(['blender','-b','--factory-startup','--python',str(ROOT/'tools/bake_crew_weights.py'),'--',str(inp),str(weights)],capture_output=True,text=True,timeout=180)
        if run.returncode or not weights.exists(): raise RuntimeError(run.stdout[-3000:]+run.stderr[-1500:])
        w=np.load(weights);ids=w['ids'];values=w['weights']
    assert ids.shape==values.shape==(len(p),4) and np.isfinite(values).all()
    assert np.allclose(values.sum(1),1) and values.min()>=0 and ids.min()>=0 and ids.max()<len(names)
    output=OUT/'Deckhand.bytes'
    with output.open('wb') as f:
        f.write(b'STC1');f.write(struct.pack('<iii',len(p),len(tri),len(names)))
        for a in (p,n,uv):f.write(a.astype('<f4').tobytes())
        f.write(tri.astype('<i4').tobytes());f.write(ids.astype('u1').tobytes());f.write(values.astype('<f4').tobytes())
    rig={'names':names,'parents':parents.tolist(),'positions':joints.ravel().tolist(),'height':1.82,
         'sourceSha256':hashlib.sha256(Path(source).read_bytes()).hexdigest(),'vertices':len(p),'triangles':len(tri)}
    (OUT/'Deckhand_rig.json').write_text(json.dumps(rig,separators=(',',':'))+'\n')
    def image(index):
        im=doc['images'][doc['textures'][index]['source']];v=doc['bufferViews'][im['bufferView']]
        raw=data[v.get('byteOffset',0):v.get('byteOffset',0)+v['byteLength']]
        return raw,Image.open(io.BytesIO(raw))
    m=doc['materials'][prim.get('material',0)];pbr=m['pbrMetallicRoughness']
    raw,im=image(pbr['baseColorTexture']['index']);assert im.size==(2048,2048)
    (OUT/'Deckhand_Albedo.jpg').write_bytes(raw)
    raw,im=image(m['normalTexture']['index']);(OUT/'Deckhand_Normal.jpg').write_bytes(raw)
    raw,im=image(pbr['metallicRoughnessTexture']['index']);a=np.asarray(im.convert('RGB'))
    packed=np.zeros((a.shape[0],a.shape[1],4),dtype=np.uint8);packed[:,:,0]=a[:,:,2];packed[:,:,3]=255-a[:,:,1]
    Image.fromarray(packed).save(OUT/'Deckhand_MetalSmooth.png')
    print('CREW:',len(p),'vertices,',len(tri),'triangles,',len(names),'heat-weighted joints; full 2K PBR textures preserved')

if __name__=='__main__':
    import sys
    main(Path(sys.argv[1]))
