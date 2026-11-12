"""Bake closed, non-intersecting helm hands without changing the other sailors.

Preserves the source UVs, body and 21-joint rig. Refines only hand triangles so
the bent finger surfaces follow the wooden handle rather than cutting through it.
"""
from pathlib import Path
import argparse
import hashlib
import json
import struct
import numpy as np

ROOT = Path(__file__).resolve().parents[1]
FOLDER = ROOT / 'Assets/_Project/Models/Crew'
RADIUS = .041
WOOD_RADIUS = .025
THICKNESS = .014


def load_mesh(path):
    b = path.read_bytes()
    assert b[:4] == b'STC1'
    vc, tc, bc = struct.unpack_from('<iii', b, 4)
    off = 16
    def read(dtype, count):
        nonlocal off
        a = np.frombuffer(b, dtype, count=count, offset=off).copy()
        off += a.nbytes
        return a
    p = read('<f4', vc*3).reshape(-1, 3).astype(float)
    n = read('<f4', vc*3).reshape(-1, 3).astype(float)
    uv = read('<f4', vc*2).reshape(-1, 2).astype(float)
    tri = read('<i4', tc*3).reshape(-1, 3)
    ids = read('u1', vc*4).reshape(-1, 4)
    w = read('<f4', vc*4).reshape(-1, 4)
    dense = np.zeros((vc, bc))
    for k in range(4):
        dense[np.arange(vc), ids[:, k]] += w[:, k]
    assert off == len(b)
    return b, p, n, uv, tri, dense, ids, w


def tags(p, dense, joints):
    out = np.zeros(len(p), dtype=int)
    for side, bone in enumerate((8, 12), 1):
        dominant = np.argmax(dense, axis=1)
        mask = (dense[:, bone] > .15) & np.isin(dominant, (bone, bone-1)) & (p[:, 1] < 1.045) & (abs(p[:, 0]-joints[bone, 0]) < .20)
        out[mask] = side
    return out


def refine(p, n, uv, tri, dense, joints):
    for _ in range(4):
        tag = tags(p, dense, joints)
        edges = set()
        for face in tri:
            for a, b in zip(face, np.roll(face, -1)):
                if tag[a] and tag[a] == tag[b] and np.linalg.norm(p[a]-p[b]) > .018:
                    edges.add(tuple(sorted((int(a), int(b)))))
        if not edges:
            break
        pp, nn, uu, ww = map(list, (p, n, uv, dense))
        mids = {}
        for a, b in sorted(edges):
            mids[(a, b)] = len(pp)
            pp.append((p[a]+p[b])*.5)
            normal = n[a]+n[b]
            nn.append(normal/max(np.linalg.norm(normal), 1e-12))
            uu.append((uv[a]+uv[b])*.5)
            ww.append((dense[a]+dense[b])*.5)
        new = []
        for face in tri:
            a, b, c = map(int, face)
            m = [mids.get(tuple(sorted(e))) for e in ((a, b), (b, c), (c, a))]
            count = sum(x is not None for x in m)
            if count == 0:
                new.append((a, b, c))
            elif count == 1:
                i = next(i for i, x in enumerate(m) if x is not None)
                a, b, c = [int(face[(i+j) % 3]) for j in range(3)]
                new.extend(((a, m[i], c), (m[i], b, c)))
            elif count == 2:
                i = next(i for i in range(3) if m[i] is not None and m[(i+1) % 3] is not None)
                a, b, c = [int(face[(i+j) % 3]) for j in range(3)]
                ab, bc = m[i], m[(i+1) % 3]
                new.extend(((b, bc, ab), (a, ab, bc), (a, bc, c)))
            else:
                ab, bc, ca = m
                new.extend(((a, ab, ca), (ab, b, bc), (ca, bc, c), (ab, bc, ca)))
        p, n, uv, dense = map(np.asarray, (pp, nn, uu, ww))
        tri = np.asarray(new, dtype=np.int32)
    return p, n, uv, tri, dense


def min_projected_radius(points, center):
    q = points[:, :, :2]-center[:2]
    a, b, c = q[:, 0], q[:, 1], q[:, 2]
    cross = lambda x, y: x[:, 0]*y[:, 1]-x[:, 1]*y[:, 0]
    turns = np.stack((cross(b-a, -a), cross(c-b, -b), cross(a-c, -c)), 1)
    area = abs(cross(b-a, c-a))
    inside = ((turns >= -1e-12).all(1) | (turns <= 1e-12).all(1)) & (area > 1e-12)
    distance = np.full(len(q), np.inf)
    for i in range(3):
        start, end = q[:, i], q[:, (i+1) % 3]
        edge = end-start
        t = np.clip(-(start*edge).sum(1)/np.maximum((edge*edge).sum(1), 1e-20), 0, 1)
        distance = np.minimum(distance, np.linalg.norm(start+edge*t[:, None], axis=1))
    distance[inside] = 0
    return distance.min()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--analysis', type=Path, help='Optional local preview data')
    args = parser.parse_args()
    source, p, n, uv, tri, dense, original_ids, original_w = load_mesh(FOLDER/'Deckhand.bytes')
    rig = json.loads((FOLDER/'Deckhand_rig.json').read_text())
    joints = np.asarray(rig['positions']).reshape(-1, 3)
    original_p, original_n = p.copy(), n.copy()
    original_tag = tags(p, dense, joints)
    # A duplicated thigh vertex accidentally has 25% hand weight in the source rig.
    # Keep that clothing on the thigh when the helmsman raises his hands.
    repaired_cloth = (np.argmax(dense, axis=1) == 13) & (dense[:, 8] > .15)
    dense[repaired_cloth, 13] += dense[repaired_cloth, 8]
    dense[repaired_cloth, 8] = 0
    p, n, uv, tri, dense = refine(p, n, uv, tri, dense, joints)
    original = p.copy()
    tag = tags(p, dense, joints)
    blend = np.clip((1.015-p[:, 1])/.07, 0, 1)
    blend = blend*blend*(3-2*blend)
    blend[tag == 0] = 0
    centers = []
    for side, bone in enumerate((8, 12), 1):
        sign = 1 if side == 1 else -1
        mask = tag == side
        ybase = float(p[mask, 1].min()+.125)
        distal = mask & (p[:, 1] < ybase+.05)
        xbase = float(np.median(p[mask & (p[:, 1] < ybase), 0]))
        zlo, zhi = p[distal, 2].min(), p[distal, 2].max()
        zcenter = float((zlo+zhi)*.5)
        zscale = min(1., .11/max(zhi-zlo, 1e-6))
        q = p[mask].copy()
        normal = THICKNESS*np.tanh(sign*(q[:, 0]-xbase)/THICKNESS)
        length = np.maximum(0, ybase-q[:, 1])
        angle = length/RADIUS
        curled = q.copy()
        curled[:, 0] = xbase+sign*(RADIUS*(1-np.cos(angle))+normal*np.cos(angle))
        curled[:, 1] = np.where(length > 0, ybase-(RADIUS-normal)*np.sin(angle), q[:, 1])
        curled[:, 2] = zcenter+(q[:, 2]-zcenter)*zscale
        p[mask] += (curled-q)*blend[mask, None]
        dense[mask] *= 1-blend[mask, None]
        dense[mask, bone] += blend[mask]
        centers.append(np.array([xbase+sign*RADIUS, ybase, zcenter]))
    affected = blend > 0
    face = np.cross(p[tri[:, 1]]-p[tri[:, 0]], p[tri[:, 2]]-p[tri[:, 0]])
    assert np.isfinite(p).all() and np.linalg.norm(face, axis=1).min() > 1e-12
    accum = np.zeros_like(p)
    for k in range(3):
        np.add.at(accum, tri[:, k], face)
    # Weld only hand normals across coincident UV seams; the rest of the body stays byte-exact.
    _, inverse = np.unique(np.round(p[affected], 5), axis=0, return_inverse=True)
    sums = np.zeros((inverse.max()+1, 3))
    np.add.at(sums, inverse, accum[affected])
    normals = sums[inverse]
    n[affected] = normals/np.maximum(np.linalg.norm(normals, axis=1)[:, None], 1e-12)
    ids = np.argsort(dense, axis=1)[:, -4:][:, ::-1].astype(np.uint8)
    weights = np.take_along_axis(dense, ids, axis=1)
    weights /= weights.sum(1)[:, None]
    untouched = np.where(original_tag == 0)[0]
    unchanged_weights = untouched[~repaired_cloth[untouched]]
    ids[unchanged_weights], weights[unchanged_weights] = original_ids[unchanged_weights], original_w[unchanged_weights]
    assert np.array_equal(p[untouched], original_p[untouched])
    assert np.array_equal(n[untouched], original_n[untouched])
    assert len(p) < 65535 and np.allclose(weights.sum(1), 1)
    meta = {'vertices': len(p), 'triangles': len(tri), 'bones': len(joints),
            'sourceSha256': hashlib.sha256(source).hexdigest(),
            'leftCenter': (centers[0]-joints[8]).tolist(),
            'rightCenter': (centers[1]-joints[12]).tolist()}
    for side, name in ((1, 'leftTriangles'), (2, 'rightTriangles')):
        rigid = (tag == side) & (blend > .9999)
        faces = tri[rigid[tri].all(1)]
        radius = min_projected_radius(p[faces], centers[side-1])
        assert radius > WOOD_RADIUS+.0005, (name, radius)
        assert radius < WOOD_RADIUS+.015, (name, radius)
        meta[name] = faces.ravel().tolist()
        print(name, 'triangles', len(faces), 'wood gap', round(radius-WOOD_RADIUS, 6))
    out = FOLDER/'Deckhand_HelmGrip.bytes'
    with out.open('wb') as f:
        f.write(b'STC1'+struct.pack('<iii', len(p), len(tri), len(joints)))
        for a in (p, n, uv):
            f.write(a.astype('<f4').tobytes())
        f.write(tri.astype('<i4').tobytes())
        f.write(ids.tobytes())
        f.write(weights.astype('<f4').tobytes())
    (FOLDER/'Deckhand_HelmGrip.json').write_text(json.dumps(meta, separators=(',', ':'))+'\n')
    if args.analysis:
        np.savez(args.analysis, positions=p, original=original,
                 normals=n, uv=uv, triangles=tri, tags=tag, centers=centers, blend=blend)
    print('HELM_GRIP_OK', len(p), 'vertices,', len(tri), 'triangles;', len(untouched), 'original body vertices untouched')
    print('Corrected clothing weights:', int(repaired_cloth.sum()))


if __name__ == '__main__':
    main()
