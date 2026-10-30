# Makes a game-ready model from a high-detail .glb (Meshy's full model):
#  1. reduces the shape to the given triangle count,
#  2. gives the reduced model a fresh, even texture layout (xatlas), so no part of the texture is stretched,
#  3. paints ("bakes") the color and metal maps onto that layout by sampling the detailed model at every texel.
# The detailed model's normal map is left out: it belongs to the detailed shape and would look wrong on the reduced
# one, and at Valheim's pixel size the bumps barely show. Writes a new .glb for convert.py.
#   python3 -I bake.py full.glb game.glb [triangles] [texture_px] [padding_px]
# Needs: pip install pymeshlab xatlas scipy (and the libopengl0 system library on Linux).
import io, json, struct, sys
import numpy as np
from PIL import Image
from scipy import ndimage
from scipy.spatial import cKDTree

src, dst = sys.argv[1], sys.argv[2]
target = int(sys.argv[3]) if len(sys.argv) > 3 else 20000
tex_px = int(sys.argv[4]) if len(sys.argv) > 4 else 1024
# Space between the layout's pieces, in texels. The game blends smaller copies of the texture at a distance and at
# glancing angles; too little space and neighbouring pieces' colours bleed into each other's edges there.
padding = int(sys.argv[5]) if len(sys.argv) > 5 else 4

data = open(src, "rb").read()
jlen = struct.unpack("<I", data[12:16])[0]
gltf = json.loads(data[20:20 + jlen])
blob = data[20 + jlen + 8:]

def accessor(i):
    a = gltf["accessors"][i]; v = gltf["bufferViews"][a["bufferView"]]
    comps = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4}[a["type"]]
    dtype = {5126: np.float32, 5125: np.uint32, 5123: np.uint16, 5121: np.uint8}[a["componentType"]]
    arr = np.frombuffer(blob, dtype, a["count"] * comps, v.get("byteOffset", 0) + a.get("byteOffset", 0)).copy()
    return arr.reshape(a["count"], comps) if comps > 1 else arr

def image(i):
    v = gltf["bufferViews"][gltf["images"][i]["bufferView"]]
    return Image.open(io.BytesIO(blob[v.get("byteOffset", 0):v.get("byteOffset", 0) + v["byteLength"]]))

prim = gltf["meshes"][0]["primitives"][0]
pos = accessor(prim["attributes"]["POSITION"]).astype(np.float64)
nrm = accessor(prim["attributes"]["NORMAL"]).astype(np.float64)
uv = accessor(prim["attributes"]["TEXCOORD_0"]).astype(np.float64)
idx = accessor(prim["indices"]).astype(np.int64).reshape(-1, 3)
mat = gltf["materials"][prim["material"]]
pbr = mat["pbrMetallicRoughness"]
color_src = image(gltf["textures"][pbr["baseColorTexture"]["index"]]["source"]).convert("RGB")
metal_src = image(gltf["textures"][pbr["metallicRoughnessTexture"]["index"]]["source"]).convert("RGB")
size = min(2048, color_src.size[0])
color_src = np.asarray(color_src.resize((size, size), Image.LANCZOS)).astype(np.float64)
metal_src = np.asarray(metal_src.resize((size, size), Image.LANCZOS)).astype(np.float64)
print(f"in: {len(idx)} triangles, {len(pos)} vertices")

# 1. Reduce the shape.
import pymeshlab
ms = pymeshlab.MeshSet()
ms.add_mesh(pymeshlab.Mesh(vertex_matrix=pos, face_matrix=idx.astype(np.int32)))
ms.meshing_merge_close_vertices(threshold=pymeshlab.PercentageValue(0.0001))
ms.meshing_decimation_quadric_edge_collapse(targetfacenum=target, qualitythr=0.5, preserveboundary=True, boundaryweight=1.0,
                                            optimalplacement=True, preservenormal=True, planarquadric=True)
ms.compute_normal_per_vertex(weightmode="By Angle")
low = ms.current_mesh()
LV = low.vertex_matrix(); LF = low.face_matrix().astype(np.int64); LN = low.vertex_normal_matrix()
print(f"reduced: {len(LF)} triangles")

# 2. Fresh texture layout.
import xatlas
atlas = xatlas.Atlas()
atlas.add_mesh(LV.astype(np.float32), LF.astype(np.uint32), LN.astype(np.float32))
pack = xatlas.PackOptions(); pack.resolution = tex_px; pack.padding = padding; pack.bilinear = True
atlas.generate(pack_options=pack)
vmap, F, U = atlas[0]
V = LV[vmap]; N = LN[vmap]; F = F.astype(np.int64); U = U.astype(np.float64)
print(f"layout: {len(V)} vertices, {atlas.chart_count} charts")

# 3. Bake: for every texel inside a triangle, find the matching spot on the detailed model and copy its texture.
T = tex_px
texel_pos = np.zeros((T, T, 3)); texel_nrm = np.zeros((T, T, 3)); covered = np.zeros((T, T), bool)
ys, xs = np.mgrid[0:T, 0:T]
for tri in F:
    p = U[tri] * T  # xatlas UVs: x across, y down the image
    lo = np.floor(p.min(0)).astype(int).clip(0, T - 1); hi = np.ceil(p.max(0)).astype(int).clip(0, T - 1)
    sx = xs[lo[1]:hi[1] + 1, lo[0]:hi[0] + 1] + 0.5; sy = ys[lo[1]:hi[1] + 1, lo[0]:hi[0] + 1] + 0.5
    (x0, y0), (x1, y1), (x2, y2) = p
    den = (y1 - y2) * (x0 - x2) + (x2 - x1) * (y0 - y2)
    if abs(den) < 1e-12: continue
    w0 = ((y1 - y2) * (sx - x2) + (x2 - x1) * (sy - y2)) / den
    w1 = ((y2 - y0) * (sx - x2) + (x0 - x2) * (sy - y2)) / den
    w2 = 1 - w0 - w1
    inside = (w0 >= -0.02) & (w1 >= -0.02) & (w2 >= -0.02)
    if not inside.any(): continue
    rows, cols = np.nonzero(inside)
    rows = rows + lo[1]; cols = cols + lo[0]
    w = np.stack([w0[inside], w1[inside], w2[inside]], -1)
    texel_pos[rows, cols] = w @ V[tri]
    texel_nrm[rows, cols] = w @ N[tri]
    covered[rows, cols] = True
print(f"texels covered: {covered.mean():.0%}")

tree = cKDTree(pos)
points = texel_pos[covered]; normals = texel_nrm[covered]
normals /= np.linalg.norm(normals, axis=1, keepdims=True).clip(1e-9)
dist, near = tree.query(points, k=8, workers=-1)
# Prefer the nearest detailed point facing the same way (thin parts have surfaces on both sides).
facing = np.einsum("nkd,nd->nk", nrm[near], normals) > 0.2
pick = np.where(facing.any(1), facing.argmax(1), 0)
src_vertex = near[np.arange(len(near)), pick]

def sample(tex, at):
    h, w = tex.shape[:2]
    x = np.clip((at[:, 0] % 1.0) * w - 0.5, 0, w - 1); y = np.clip((at[:, 1] % 1.0) * h - 0.5, 0, h - 1)
    x0 = np.floor(x).astype(int); y0 = np.floor(y).astype(int); x1 = np.minimum(x0 + 1, w - 1); y1 = np.minimum(y0 + 1, h - 1)
    fx = (x - x0)[:, None]; fy = (y - y0)[:, None]
    return tex[y0, x0] * (1 - fx) * (1 - fy) + tex[y0, x1] * fx * (1 - fy) + tex[y1, x0] * (1 - fx) * fy + tex[y1, x1] * fx * fy

def finish(values):
    img = np.zeros((T, T, 3)); img[covered] = values
    # Fill the gaps between charts with the nearest painted colour, so edges never pick up black.
    near_painted = ndimage.distance_transform_edt(~covered, return_distances=False, return_indices=True)
    img = img[near_painted[0], near_painted[1]]
    out = io.BytesIO(); Image.fromarray(img.round().clip(0, 255).astype(np.uint8), "RGB").save(out, "PNG"); return out.getvalue()

# The texture spot: the closest point on the detailed surface, among the triangles around the picked vertex, with its
# texture coordinates blended from that triangle's corners. (Copying the nearest vertex's coordinates instead painted
# flat, sparsely-built areas in big blotches, one per detailed vertex, while busy carved areas came out fine.)
order = np.argsort(idx.ravel(), kind="stable")
starts = np.searchsorted(idx.ravel()[order], np.arange(len(pos)))
counts = np.bincount(idx.ravel(), minlength=len(pos))
FAN = 12

def closest_on_triangles(p, a, b, c):
    """Barycentric weights of the closest point to p on each triangle abc (all arrays (..., 3))."""
    ab, ac, ap = b - a, c - a, p - a
    d1 = (ab * ap).sum(-1); d2 = (ac * ap).sum(-1)
    bp = p - b; d3 = (ab * bp).sum(-1); d4 = (ac * bp).sum(-1)
    cp = p - c; d5 = (ab * cp).sum(-1); d6 = (ac * cp).sum(-1)
    va = d3 * d6 - d5 * d4; vb = d5 * d2 - d1 * d6; vc = d1 * d4 - d3 * d2
    denom = np.where(np.abs(va + vb + vc) < 1e-30, 1e-30, va + vb + vc)
    v = vb / denom; w = vc / denom; u = 1 - v - w
    out = np.stack([u, v, w], -1)
    def put(mask, uu, vv, ww):
        nonlocal out
        out = np.where(mask[..., None], np.stack([uu, vv, ww], -1), out)
    z = np.zeros_like(d1); one = np.ones_like(d1)
    # Edges (checked before corners so corners win where both apply).
    t = d1 / np.where(np.abs(d1 - d3) < 1e-30, 1e-30, d1 - d3); put((vc <= 0) & (d1 >= 0) & (d3 <= 0), 1 - t, t, z)
    t = d2 / np.where(np.abs(d2 - d6) < 1e-30, 1e-30, d2 - d6); put((vb <= 0) & (d2 >= 0) & (d6 <= 0), 1 - t, z, t)
    t = (d4 - d3) / np.where(np.abs((d4 - d3) + (d5 - d6)) < 1e-30, 1e-30, (d4 - d3) + (d5 - d6)); put((va <= 0) & ((d4 - d3) >= 0) & ((d5 - d6) >= 0), z, 1 - t, t)
    put((d1 <= 0) & (d2 <= 0), one, z, z)
    put((d3 >= 0) & (d4 <= d3), z, one, z)
    put((d6 >= 0) & (d5 <= d6), z, z, one)
    return out

src_uv = np.zeros((len(points), 2))
for s0 in range(0, len(points), 100000):
    s1 = min(len(points), s0 + 100000)
    vtx = src_vertex[s0:s1]
    k = np.arange(FAN)[None, :]
    slot = np.minimum(k, np.maximum(counts[vtx][:, None] - 1, 0))
    faces = order[starts[vtx][:, None] + slot] // 3                      # (n, FAN) triangles around the vertex
    tri = idx[faces]                                                    # (n, FAN, 3)
    p = points[s0:s1][:, None, :]
    bary = closest_on_triangles(p, pos[tri[..., 0]], pos[tri[..., 1]], pos[tri[..., 2]])
    q = np.einsum("nfk,nfkd->nfd", bary, pos[tri])
    dist = np.linalg.norm(q - p, axis=-1)
    best = dist.argmin(1); rows = np.arange(s1 - s0)
    src_uv[s0:s1] = np.einsum("nk,nkd->nd", bary[rows, best], uv[tri[rows, best]])
baked_color = finish(sample(color_src, src_uv))
baked_metal = finish(sample(metal_src, src_uv))
flat = io.BytesIO(); Image.new("RGB", (4, 4), (128, 128, 255)).save(flat, "PNG")
print("baked color and metal")

chunks, views = bytearray(), []
def add(raw, kind=None):
    while len(chunks) % 4: chunks.append(0)
    view = {"buffer": 0, "byteOffset": len(chunks), "byteLength": len(raw)}
    if kind: view["target"] = kind
    chunks.extend(raw); views.append(view); return len(views) - 1
P = V.astype(np.float32); Nn = (N / np.linalg.norm(N, axis=1, keepdims=True).clip(1e-9)).astype(np.float32)
UU = U.astype(np.float32); I = F.reshape(-1).astype(np.uint32)
vp = add(P.tobytes(), 34962); vn = add(Nn.tobytes(), 34962); vu = add(UU.tobytes(), 34962); vi = add(I.tobytes(), 34963)
ic = add(baked_color); im = add(baked_metal); iflat = add(flat.getvalue())
out = {
    "asset": {"version": "2.0", "generator": "PotatoFarmer bake.py"},
    "scene": 0, "scenes": [{"nodes": [0]}], "nodes": [{"mesh": 0}],
    "meshes": [{"primitives": [{"attributes": {"POSITION": 0, "NORMAL": 1, "TEXCOORD_0": 2}, "indices": 3, "material": 0}]}],
    "accessors": [
        {"bufferView": vp, "componentType": 5126, "count": len(P), "type": "VEC3", "min": P.min(0).tolist(), "max": P.max(0).tolist()},
        {"bufferView": vn, "componentType": 5126, "count": len(P), "type": "VEC3"},
        {"bufferView": vu, "componentType": 5126, "count": len(P), "type": "VEC2"},
        {"bufferView": vi, "componentType": 5125, "count": len(I), "type": "SCALAR"},
    ],
    "materials": [{"pbrMetallicRoughness": {"baseColorTexture": {"index": 0}, "metallicRoughnessTexture": {"index": 1},
                                            "metallicFactor": 1.0, "roughnessFactor": 1.0},
                   "normalTexture": {"index": 2}}],
    "textures": [{"source": 0}, {"source": 1}, {"source": 2}],
    "images": [{"bufferView": ic, "mimeType": "image/png"}, {"bufferView": im, "mimeType": "image/png"},
               {"bufferView": iflat, "mimeType": "image/png"}],
    "bufferViews": views, "buffers": [{"byteLength": len(chunks)}],
}
js = json.dumps(out).encode(); js += b" " * (-len(js) % 4)
while len(chunks) % 4: chunks.append(0)
with open(dst, "wb") as f:
    f.write(struct.pack("<III", 0x46546C67, 2, 12 + 8 + len(js) + 8 + len(chunks)))
    f.write(struct.pack("<II", len(js), 0x4E4F534A)); f.write(js)
    f.write(struct.pack("<II", len(chunks), 0x004E4942)); f.write(bytes(chunks))
print(f"wrote {dst}: {len(I) // 3} triangles, {len(P)} vertices, {tex_px}px textures")
