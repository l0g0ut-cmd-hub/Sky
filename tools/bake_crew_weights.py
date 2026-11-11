"""Blender helper: real bone-heat skinning, exported to Unity without DCC axis ambiguity."""
import bpy, numpy as np, sys
from mathutils.kdtree import KDTree

inp,out=sys.argv[sys.argv.index('--')+1:]
d=np.load(inp);p=d['positions'];tri=d['triangles'];joints=d['joints'];tails=d['tails'];parents=d['parents'];names=d['names']
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
mesh=bpy.data.meshes.new('Deckhand');mesh.from_pydata(p.tolist(),[],tri.tolist());mesh.update()
body=bpy.data.objects.new('Deckhand',mesh);bpy.context.collection.objects.link(body)
# Heat is solved on a watertight voxel proxy. UV seams and small disconnected facial details
# in the original visual mesh stay untouched; weights alone are transferred back afterwards.
bpy.context.view_layer.objects.active=body;body.select_set(True)
body.data.remesh_voxel_size=.022
bpy.ops.object.voxel_remesh()
mesh=body.data
body.select_set(False)
arm=bpy.data.armatures.new('CrewRig');rig=bpy.data.objects.new('CrewRig',arm);bpy.context.collection.objects.link(rig)
bpy.context.view_layer.objects.active=rig;rig.select_set(True);bpy.ops.object.mode_set(mode='EDIT')
for i,name in enumerate(names):
    bone=arm.edit_bones.new(str(name));bone.head=joints[i];bone.tail=tails[i]
    if parents[i]>=0:bone.parent=arm.edit_bones[int(parents[i])]
bpy.ops.object.mode_set(mode='OBJECT');body.select_set(True)
bpy.ops.object.parent_set(type='ARMATURE_AUTO')
ids=np.zeros((len(p),4),dtype=np.uint8);values=np.zeros((len(p),4),dtype=np.float32)
lookup={group.index:list(names).index(group.name) for group in body.vertex_groups if group.name in names}
tree=KDTree(len(mesh.vertices))
for v in mesh.vertices:tree.insert(v.co,v.index)
tree.balance()
for i,point in enumerate(p):
    accumulated=np.zeros(len(names),dtype=float)
    for _,vertex_id,distance in tree.find_n(point,4):
        for group in mesh.vertices[vertex_id].groups:
            if group.group in lookup:accumulated[lookup[group.group]]+=group.weight/max(distance,.002)**2
    best=np.argsort(accumulated)[-4:][::-1];total=accumulated[best].sum()
    if total<=0:raise RuntimeError('Bone heat left an unweighted vertex: '+str(i))
    ids[i]=best;values[i]=accumulated[best]/total
np.savez(out,ids=ids,weights=values)
print('CREW_HEAT_OK',len(p),'original vertices,',len(mesh.vertices),'proxy vertices,',len(names),'bones, no missing weights')
