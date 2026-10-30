"""Find holes in pixel art / decal images (copy of claude/tools/holes.py)."""
import sys, numpy as np
from PIL import Image
from scipy import ndimage as nd
def disk(r):
    g = np.arange(-r, r + 1) ** 2
    return np.add.outer(g, g) <= r * r
def check(path):
    im = Image.open(path).convert('RGBA'); a = np.array(im).astype(int); A = a[:, :, 3]
    m = A >= 128
    filled = nd.binary_fill_holes(m)
    holes = filled & ~m
    din = nd.distance_transform_edt(filled); dout = nd.distance_transform_edt(~filled)
    faint = (din > 1.5) & (A < 255) & ~holes
    notch = nd.binary_closing(filled, disk(2)) & ~filled
    stray = (dout > 2.5) & (A > 0)
    hl, hn = nd.label(holes); nl, nn = nd.label(notch)
    print(f'{path}: {im.size[0]}x{im.size[1]}  shape {filled.sum()} px')
    print(f'  enclosed holes: {hn} ({holes.sum()} px)')
    print(f'  faint inside:   {faint.sum()} px (alpha<200: {(faint & (A < 200)).sum()})')
    print(f'  notches:        {nn} ({notch.sum()} px) - inner corners of the design also show here, look at the map')
    print(f'  stray outside:  {stray.sum()} px')
    for i in range(1, nn + 1):
        ys, xs = np.nonzero(nl == i); print(f'    notch at x{xs.min()}-{xs.max()} y{ys.min()}-{ys.max()} ({len(xs)} px)')
    s = max(1, 512 // max(im.size))
    vis = np.zeros(a.shape, np.uint8); vis[..., :3] = (60, 60, 60); vis[..., 3] = 255
    vis[m] = (200, 200, 200, 255)
    vis[faint] = (255, 220, 0, 255); vis[holes] = (255, 0, 0, 255); vis[notch] = (0, 220, 255, 255); vis[stray] = (255, 0, 255, 255)
    out = path.rsplit('.', 1)[0] + '_holes.png'
    Image.fromarray(vis).resize((im.size[0] * s, im.size[1] * s), Image.NEAREST).save(out)
    return dict(holes=hn, faint=int(faint.sum()), notches=nn, stray=int(stray.sum()))
if __name__ == '__main__':
    for p in sys.argv[1:]: check(p)
