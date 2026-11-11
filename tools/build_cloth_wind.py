"""Bake sparse cloth weights without changing the source mesh, UVs or triangles."""
from pathlib import Path
import hashlib, json, struct
import numpy as np
from scipy.sparse import coo_matrix
from scipy.sparse.csgraph import connected_components
from scipy.spatial import ConvexHull

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'Assets/_Project/Models/CorsairRepaired.bytes'
OUTPUT = SOURCE.with_name('CorsairRepaired_wind.json')

def components(vertices, triangles):
    used = np.unique(triangles)
    welded, inverse = np.unique(np.round(vertices[used], 5), axis=0, return_inverse=True)
    remap = np.zeros(len(vertices), dtype=int)
    remap[used] = inverse
    t = remap[triangles]
    graph = coo_matrix((np.ones(t.size), (t.ravel(), np.roll(t, -1, axis=1).ravel())), shape=(len(welded), len(welded)))
    _, labels = connected_components(graph, directed=False)
    return [(used[labels[inverse] == c], welded[labels == c]) for c in np.unique(labels)]

def smooth(x):
    q = np.clip(x, 0, 1)
    return q*q*(3-2*q), 6*q*(1-q)*((x > 0) & (x < 1))

def sail_weights(points):
    yz = points[:, 1:]
    # The three farthest extremal corners define each sewn triangular sail.
    hull = yz[ConvexHull(yz).vertices]
    best = None
    for i in range(len(hull)):
        for j in range(i+1, len(hull)):
            a, b = hull[j]-hull[i], hull[j+1:]-hull[i]
            area = np.abs(a[0]*b[:,1]-a[1]*b[:,0])
            if len(area) and (best is None or area.max() > best[0]):
                k = j+1+int(area.argmax())
                best = (float(area.max()), np.array([hull[i], hull[j], hull[k]]))
    corners = best[1]
    inverse = np.linalg.inv(np.vstack([corners.T, np.ones(3)]))
    bary = np.c_[yz, np.ones(len(yz))] @ inverse.T
    a, da = smooth((bary-0.025)/0.275)
    da /= 0.275
    w = np.prod(a, axis=1)
    gradient = np.zeros((len(points), 2))
    for i in range(3):
        others = np.prod(a[:, [j for j in range(3) if j != i]], axis=1)
        gradient += (da[:,i]*others)[:,None]*inverse[i,:2]
    # The entire geometric perimeter must stay fixed, including the thickness shell.
    perimeter = ConvexHull(yz).vertices
    assert np.all(w[perimeter] == 0), 'moving sail perimeter'
    return w, gradient

def main():
    data = SOURCE.read_bytes()
    assert data[:4] == b'STM2'
    count, slots = struct.unpack_from('<ii', data, 4)
    vertices = np.frombuffer(data, '<f4', count*3, 12).reshape(-1,3)
    off = 12+count*32
    masks = np.zeros((count,4), dtype=np.uint8)
    masks[:,1:3] = 128
    sails = flags = pins = 0
    for slot in range(slots):
        _, n = struct.unpack_from('<ii',data,off); off += 8
        triangles = np.frombuffer(data,'<i4',n,off).reshape(-1,3);off += n*4
        if slot not in (3,7): continue
        for indices, unique in components(vertices,triangles):
            if slot == 3:
                if len(unique) != 380: continue  # the three balloon shells stay rigid
                w, g = sail_weights(vertices[indices]); role = 128; sails += 1
            else:
                # Forward edge is sewn to the pole; tail points towards local -Z.
                z = vertices[indices,2]
                length = float(z.max()-z.min())
                q = (z.max()-z)/length
                w, d = smooth((q-0.04)/0.96)
                g = np.c_[np.zeros(len(z)), -d/(length*0.96)]
                role = 255; flags += 1
                assert np.all(w[z > z.max()-0.03] == 0), 'moving flag attachment'
            weights = np.rint(w*255).astype(np.uint8)
            gradient = np.rint(g*64+128)
            assert gradient.min() >= 0 and gradient.max() <= 255
            masks[indices,0] = weights
            masks[indices,1:3] = gradient.astype(np.uint8)
            masks[indices,3] = role
            pins += int((weights == 0).sum())
    assert sails == 3 and flags == 1
    active = np.flatnonzero(masks[:,0])
    packed = masks[active].copy().view('<i4').reshape(-1)
    result = dict(vertexCount=count, meshSha256=hashlib.sha256(data).hexdigest(),
                  sails=sails, flags=flags, pinnedVertices=pins,
                  indices=active.tolist(), packed=packed.tolist())
    OUTPUT.write_text(json.dumps(result,separators=(',',':'))+'\n')
    print(f'{OUTPUT.name}: {len(active)} moving vertices, {pins} pinned cloth vertices; {sails} sails, {flags} pennant; {OUTPUT.stat().st_size} bytes')

if __name__ == '__main__': main()
