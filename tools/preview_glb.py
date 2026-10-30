# Stick + rune sheet (PTM1, hand space) -> one glb laid along +X for page.html. Rotation (x,y,z)->(z,y,-x), no mirror.
import struct, sys, numpy as np, trimesh
from PIL import Image
src, out = sys.argv[1], sys.argv[2]
def load(p):
    b = open(p, 'rb').read(); nv, ni = struct.unpack('<ii', b[4:12]); o = 12
    P = np.frombuffer(b, '<f4', nv*3, o).reshape(-1, 3).astype(float); o += nv*12
    N = np.frombuffer(b, '<f4', nv*3, o).reshape(-1, 3).astype(float); o += nv*12
    U = np.frombuffer(b, '<f4', nv*2, o).reshape(-1, 2).astype(float); o += nv*8
    I = np.frombuffer(b, '<i4', ni, o).reshape(-1, 3)
    return P, N, U, I
# Unity is left-handed, glTF right-handed: x negated (and the winding turned) as Unity's own glTF importers do, then turned.
rot = lambda v: np.stack([v[:, 2], v[:, 1], v[:, 0]], 1)
scene = trimesh.Scene()
P, N, U, I = load(f'{src}/runakefli.mesh')
m = trimesh.Trimesh(rot(P), I[:, ::-1], vertex_normals=rot(N), process=False)
m.visual = trimesh.visual.TextureVisuals(uv=U, material=trimesh.visual.material.PBRMaterial(baseColorTexture=Image.open(f'{src}/runakefli_d.png'), metallicFactor=0, roughnessFactor=0.9))
scene.add_geometry(m, 'stick')
P, N, U, I = load(f'{src}/runakefli_runes.mesh')
tex = Image.open(f'{src}/runakefli_runes.png').convert('RGBA')
m = trimesh.Trimesh(rot(P), I[:, ::-1], vertex_normals=rot(N), process=False)
m.visual = trimesh.visual.TextureVisuals(uv=U, material=trimesh.visual.material.PBRMaterial(baseColorTexture=tex, baseColorFactor=[158, 31, 15, 255], emissiveFactor=[1.0, 0.267, 0.0], emissiveTexture=tex.convert('RGB'), alphaMode='MASK', alphaCutoff=0.5, metallicFactor=0, roughnessFactor=0.9))
scene.add_geometry(m, 'runes')
scene.export(out)
