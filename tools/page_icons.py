# The page icons (80 px, docs/images/icon-*.png): small models rendered with render_icons.py (copied from PotatoRider).
# A rune is laid on its board as raised cells of paint, one per cell of its drawing (assets/runes/<art>.png) on a grid,
# so it's lit and shadowed like the rest of the model. The Rúnakefli is its own game model.
#   python3 -I tools/page_icons.py OUT_DIR
import os, sys
import numpy as np
from PIL import Image, ImageFilter
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import render_icons as ri

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
RED_OCHRE = np.array([0.62, 0.12, 0.06]); EMBER = np.array([1.0, 0.267, 0.0])

def mask(art, grid):
    a = np.asarray(Image.open(os.path.join(HERE, "assets", "runes", art + ".png")).convert("RGBA"))[..., 3]
    # Cropped to the drawing (a square around it, 6% margin), so it fills the board.
    ys, xs = np.nonzero(a > 40); cy, cx = (ys.min() + ys.max()) / 2, (xs.min() + xs.max()) / 2
    half = max(ys.max() - ys.min(), xs.max() - xs.min()) / 2 * 1.06
    a = np.asarray(Image.fromarray(a).crop((int(cx - half), int(cy - half), int(cx + half), int(cy + half))).resize((256, 256), Image.LANCZOS))
    small = np.asarray(Image.fromarray(a).resize((grid, grid), Image.BOX), float) / 255.0
    return small > 0.45, np.asarray(Image.fromarray(a).filter(ImageFilter.GaussianBlur(14)), float) / 255.0

def board(art, surface, paint, glow=False, grid=30, size=0.82, depth=0.13, halo=0.0, yaw=28, pitch=18):
    on, blur = mask(art, grid)
    cell = size / grid
    if halo > 0:   # the etched glow's halo, on the board around the strokes
        base = surface
        def surface(p, n, base=base):
            col = base(p, n)
            u = np.clip(((p[:, 0] / size) + 0.5) * 255, 0, 255).astype(int); v = np.clip((0.5 - p[:, 1] / size) * 255, 0, 255).astype(int)
            h = blur[v, u] * (n[:, 2] > 0.5) * halo
            return np.clip(col + EMBER[None, :] * h[:, None], 0, 1)
    parts = [(ri.box((1.0, 1.0, depth), (0, 0, -depth / 2)), surface)]
    mat = ri.glow(paint) if glow else ri.flat(paint, 0.12)
    import trimesh
    cells = [ri.box((cell * 1.02, cell * 1.02, 0.012), ((x + 0.5) * cell - size / 2, size / 2 - (y + 0.5) * cell, 0.006))
             for y in range(grid) for x in range(grid) if on[y, x]]
    parts.append((trimesh.util.concatenate(cells), mat))
    return ri.render(parts, yaw=yaw, pitch=pitch, fov=26, shadows=SHADOWS)

ICONS = {
    "painted": lambda: board("elder_ansuz", ri.wood(ri.WOOD, axis=0, planks=0.28, plank_axis=1), RED_OCHRE),
    "etched": lambda: board("elder_othala", ri.flat(ri.STONE, 0.14, 5), EMBER, glow=True, halo=0.6),
    "symbols": lambda: board("symbol_valknut", ri.flat(ri.STONE * 0.95, 0.14, 5), RED_OCHRE, grid=36),
    "hand": lambda: board("symbol_hand_left", ri.wood(ri.WOOD_D, axis=1, planks=0.3, plank_axis=0), RED_OCHRE, grid=36),
    "kefli": lambda: ri.render_lowpoly(os.path.join(HERE, "assets", "runakefli", "runakefli.mesh") + "," + os.path.join(OUT_TMP, "runakefli_runes.mesh"), yaw=35, pitch=25),
}

SHADOWS = os.environ.get("SHADOWS", "1") == "1"
OUT_TMP = "/tmp/pp_icons_src"
if __name__ == "__main__":
    out = sys.argv[1]; names = sys.argv[2:] or list(ICONS)
    os.makedirs(out, exist_ok=True); os.makedirs(OUT_TMP, exist_ok=True)
    k = os.path.join(HERE, "assets", "runakefli")
    # render_lowpoly reads <name>_d.png beside each mesh: the rune sheet's texture is runakefli_runes.png.
    import shutil
    shutil.copy(os.path.join(k, "runakefli_runes.mesh"), OUT_TMP); shutil.copy(os.path.join(k, "runakefli_runes.png"), os.path.join(OUT_TMP, "runakefli_runes_d.png"))
    done = []
    for n in names:
        im = ICONS[n](); im.save(os.path.join(out, f"icon-{n}.png")); done.append(im)
    sheet = Image.new("RGBA", (len(done) * 88, 88), (58, 50, 44, 255))
    for i, im in enumerate(done): sheet.paste(im, (i * 88 + 4, 4), im)
    sheet.resize((sheet.width * 3, sheet.height * 3), Image.NEAREST).save(os.path.join(out, "_sheet.png"))
