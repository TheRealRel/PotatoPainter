# Makes the Rúnakefli's game assets from convert.py's output (the stick laid along X, antler tip at +X, standing on y = 0):
#  - runakefli.mesh: the stick in the hammer's "attach" space (the hand): leather grip at the origin, the stick along +Z with
#    the antler tip up out of the fist (where the hammer's head is), so the game holds it like the hammer.
#  - runakefli_runes.mesh + runakefli_runes.png: the word painted along the plain shaft on two opposite sides, as its own thin
#    sheet a little above the wood (the stick's own 128 px texture would make 4 cm runes 4 pixels tall).
#  - runakefli_halo.png: the word's glow halo (the strokes' outline blurred, strokes left out), drawn on the same sheet.
# The runes read from the grip toward the tip, each standing across the stick with its top to the reader's left (the
# text turned a quarter left, like a book spine), as on the old rune sticks.
#   python3 -I kefli.py conv_dir out_dir rune_png_dir "raido uruz naudiz ansuz kauna ehwaz fehu laguz isaz"
import struct, sys
import numpy as np
from PIL import Image
import trimesh

conv, out, runes_dir, word = sys.argv[1], sys.argv[2], sys.argv[3], sys.argv[4].split()
GRIP_X = -0.17                    # centre of the leather wrap along convert.py's X (from the stick's radius profile)
AXIS_Y = 0.029                    # the stick's axis height in convert.py's space
PLAIN = (-0.02, 0.195)            # plain wood between the knot band (its bulge ends at -0.025) and the antler ferrule
CLEAR = 0.01                      # kept clear of both
WRAP = np.radians(136)            # most a rune may wrap round the stick on each side (0.1.1 had 136 at its tip end)
GAP = 20                          # px between runes
TOP, BOTTOM = 8, 248              # rows of the 256 px drawings kept: strokes are rows 29 to 226, the halo needs ~20 more
LIFT = 0.0006                     # paint 0.6 mm above the wood

def load(p):
    b = open(p, 'rb').read(); assert b[:4] == b'PTM1'
    nv, ni = struct.unpack('<ii', b[4:12]); o = 12
    P = np.frombuffer(b, '<f4', nv * 3, o).reshape(-1, 3).astype(float); o += nv * 12
    N = np.frombuffer(b, '<f4', nv * 3, o).reshape(-1, 3).astype(float); o += nv * 12
    U = np.frombuffer(b, '<f4', nv * 2, o).reshape(-1, 2).astype(float); o += nv * 8
    I = np.frombuffer(b, '<i4', ni, o).reshape(-1, 3)
    return P, N, U, I

def save(p, P, N, U, I):
    with open(p, 'wb') as f:
        f.write(b'PTM1'); f.write(struct.pack('<ii', len(P), I.size))
        for a in (P, N, U, I): f.write(np.ascontiguousarray(a).astype('<i4' if a is I else '<f4').tobytes())

# convert.py space -> the hand's space: a rotation (no mirror, so the winding stays): x' = -Z, y' = Y - axis, z' = X - grip.
def to_hand(v, point=True):
    v = np.atleast_2d(v)
    return np.stack([-v[:, 2], v[:, 1] - (AXIS_Y if point else 0), v[:, 0] - (GRIP_X if point else 0)], 1)

P, N, U, I = load(f'{conv}/runakefli.mesh')
P2, N2 = to_hand(P), to_hand(N, False)
save(f'{out}/runakefli.mesh', P2, N2, U, I)
Image.open(f'{conv}/runakefli_d.png').save(f'{out}/runakefli_d.png')
print(f'stick: {len(I)} triangles, from z {P2[:, 2].min():.3f} to {P2[:, 2].max():.3f} m (hand at 0), '
      f'width {np.ptp(P2[:, 0]) * 100:.1f} x {np.ptp(P2[:, 1]) * 100:.1f} cm')

# The word as one texture: each rune's drawing cut to its strokes, side by side, GAP px apart, rows TOP to BOTTOM.
tiles = []
for name in word:
    a = np.asarray(Image.open(f'{runes_dir}/elder_{name}.png').convert('RGBA'))[TOP:BOTTOM]
    cols = np.where(a[..., 3].max(0) > 0)[0]
    tiles.append(a[:, cols.min():cols.max() + 1])
W = sum(t.shape[1] for t in tiles) + GAP * (len(tiles) + 1); H = BOTTOM - TOP
sheet = np.zeros((H, W, 4), np.uint8); sheet[..., :3] = 255
x = GAP
for t in tiles:
    sheet[:, x:x + t.shape[1]] = t; x += t.shape[1] + GAP
Image.fromarray(sheet, 'RGBA').save(f'{out}/runakefli_runes.png')
# The halo as in probe test 22: alpha blurred (gaussian, sigma 7 px), x 1.6, strokes left out, white under it.
alpha = sheet[..., 3].astype(float) / 255
sigma, rad = 7.0, 21
k = np.exp(-np.arange(-rad, rad + 1) ** 2 / (2 * sigma * sigma)); k /= k.sum()
blur = np.apply_along_axis(lambda r: np.convolve(r, k, 'same'), 1, alpha)
blur = np.apply_along_axis(lambda c: np.convolve(c, k, 'same'), 0, blur)
halo = np.zeros_like(sheet); halo[..., :3] = 255
halo[..., 3] = np.round(np.clip(blur * 1.6, 0, 1) * (1 - alpha) * 255).astype(np.uint8)
Image.fromarray(halo, 'RGBA').save(f'{out}/runakefli_halo.png')
print(f'word texture {W}x{H} (strokes {29 - TOP} px from the top and {BOTTOM - 1 - 226} px from the bottom)')

# The sheet: cast onto the stick from outside, on two opposite sides (facing +x and -x in the hand's space).
mesh = trimesh.Trimesh(P2, I, process=False)
def radius(z):
    ring = mesh.vertices[np.abs(mesh.vertices[:, 2] - z) < 0.01]
    return np.sqrt(ring[:, 0] ** 2 + ring[:, 1] ** 2).max() if len(ring) else 0.02
# Centred in the plain wood, as large as it can be without wrapping more than WRAP round the stick anywhere along it (the
# stick narrows toward the tip). The texture keeps its proportions: length = arc x W / H.
lo, hi = PLAIN[0] + CLEAR - GRIP_X, PLAIN[1] - CLEAR - GRIP_X; mid = (lo + hi) / 2
arc = (hi - lo) * H / W
for _ in range(60):
    half = arc * W / H / 2
    rmin = min(radius(z) for z in np.linspace(mid - half, mid + half, 25))
    arc = min((hi - lo) * H / W, WRAP * rmin) * 0.5 + arc * 0.5
z0, z1 = mid - arc * W / H / 2, mid + arc * W / H / 2
nz, na = 64, 16
verts, uvs, nrms, tris = [], [], [], []
for d in (np.array([1.0, 0, 0]), np.array([-1.0, 0, 0])):
    t = np.cross([0, 0, 1.0], d)             # the reader's left when facing this side with the tip up: the rune's top
    start = len(verts); ok = []
    for i in range(nz + 1):
        z = z0 + (z1 - z0) * i / nz
        r = radius(z)
        for j in range(na + 1):
            s = -arc / 2 + arc * j / na
            th = s / r
            dirv = d * np.cos(th) + t * np.sin(th)
            origin = np.array([0, 0, z]) + dirv * 0.2
            loc, ray, tri = mesh.ray.intersects_location([origin], [-dirv])
            if len(loc):
                k = np.argmin(np.linalg.norm(loc - origin, axis=1))
                n = mesh.face_normals[tri[k]]
                if np.dot(n, dirv) < 0: n = -n
                verts.append(loc[k] + n * LIFT); nrms.append(n); ok.append(True)
            else:
                verts.append(origin - dirv * 0.2); nrms.append(dirv); ok.append(False)
            uvs.append([i / nz, j / na])
    for i in range(nz):
        for j in range(na):
            a = start + i * (na + 1) + j; b = a + 1; c = a + na + 1; e = c + 1
            if not (ok[a - start] and ok[b - start] and ok[c - start] and ok[e - start]): continue
            # Facing outward: Unity draws a triangle's front where Cross(b - a, c - a) points, so the corners are swapped
            # when that points into the stick.
            tri1, tri2 = [a, c, b], [b, c, e]
            for tr in (tri1, tri2):
                pa, pb, pc = (np.array(verts[q]) for q in tr)
                if np.dot(np.cross(pb - pa, pc - pa), np.array(nrms[tr[0]])) < 0: tr[1], tr[2] = tr[2], tr[1]
                tris.append(tr)
V = np.array(verts); Nn = np.array(nrms); Uv = np.array(uvs); T = np.array(tris, np.int64)
save(f'{out}/runakefli_runes.mesh', V, Nn, Uv, T)
print(f'rune sheet: {len(T)} triangles on two sides, from z {z0:.3f} to {z1:.3f} ({(z1 - z0) * 100:.1f} cm along the shaft, '
      f'plain wood z {PLAIN[0] - GRIP_X:.3f} to {PLAIN[1] - GRIP_X:.3f}), {arc * 100:.2f} cm across (runes about {arc * 198 / H * 100:.1f} cm tall), '
      f'wrapping at most {np.degrees(arc / min(radius(z) for z in np.linspace(z0, z1, 25))):.0f} degrees')
