"""Tag complete iron barrel components, preserving the original STM2 geometry."""
from pathlib import Path
import hashlib, json, struct
import numpy as np
from build_cloth_wind import components

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'Assets/_Project/Models/CorsairRepaired.bytes'
OUTPUT = SOURCE.with_name('CorsairRepaired_recoil.json')

def main():
    data = SOURCE.read_bytes()
    assert data[:4] == b'STM2'
    count, slots = struct.unpack_from('<ii', data, 4)
    vertices = np.frombuffer(data, '<f4', count*3, 12).reshape(-1, 3)
    offset = 12+count*32
    iron = None
    other = set()
    for _ in range(slots):
        slot, size = struct.unpack_from('<ii', data, offset); offset += 8
        triangles = np.frombuffer(data, '<i4', size, offset).reshape(-1, 3); offset += size*4
        if slot == 1: iron = triangles
        else: other.update(np.unique(triangles).tolist())
    guns = json.loads(SOURCE.with_name('CorsairRepaired_cannons.json').read_text())['cannons']
    assert iron is not None and len(guns) == 16
    candidates = components(vertices, iron)
    indices, ids, assigned = [], [], set()
    for gun_id, gun in enumerate(guns, 1):
        muzzle, axis = np.array(gun['muzzle']), np.array(gun['dir'])
        axis /= np.linalg.norm(axis)
        matches = []
        for component_id, (idx, points) in enumerate(candidates):
            projection = (points-muzzle) @ axis
            if len(idx) != 192 or abs(np.ptp(projection)-gun['length']) > .002: continue
            tip = points[projection > projection.max()-.04*gun['length']].mean(0)
            if np.linalg.norm(tip-muzzle) < .002: matches.append((component_id, idx))
        assert len(matches) == 1, (gun['name'], len(matches))
        component_id, idx = matches[0]
        assert component_id not in assigned and not (set(idx) & other)
        assigned.add(component_id)
        indices.extend(idx.tolist()); ids.extend([gun_id]*len(idx))
        # Translation is inwards and remains inside the existing whole-ship bounds.
        moved = vertices[idx]-axis*.32
        assert np.all(moved >= vertices.min(0)) and np.all(moved <= vertices.max(0))
    assert len(indices) == len(set(indices)) == 3072
    wind = json.loads(SOURCE.with_name('CorsairRepaired_wind.json').read_text())
    assert not (set(indices) & set(wind['indices']))
    result = dict(vertexCount=count, meshSha256=hashlib.sha256(data).hexdigest(),
                  names=[g['name'] for g in guns], indices=indices, ids=ids)
    OUTPUT.write_text(json.dumps(result, separators=(',', ':'))+'\n')
    print(f'{OUTPUT.name}: {len(guns)} complete barrels / {len(indices)} vertices; axles and all other materials fixed')

if __name__ == '__main__': main()
