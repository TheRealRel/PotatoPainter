# Draws the runes (Elder Futhark, Younger Futhark, futhorc) (python3 -I draw_rune.py <PotatoRunes tools/futhark/make_font.py> <codepoint hex> <out.png>)
# Draws them the way the painted Algiz looks: PotatoRunes' tools/futhark strokes (92 units wide, round
# ends, slight bend), 0.25 px per unit, centred in 256 px, white under the alpha, 4x supersampled edges.
import sys, math, random, importlib.util
import numpy as np
from PIL import Image, ImageDraw
from shapely.geometry import LineString
from shapely.ops import unary_union

src=open(sys.argv[1]).read()
cut=src.index("def draw(")   # strokes, wobble and outline only (no font building)
ns={}; exec(src[:cut], ns)
# PotatoPainter's own changes to PotatoRunes' strokes (PotatoRunes' font stays as it is):
# Jēra (ᛃ): the lower hook moved up 280 and right 80, so the two hooks sit nested at almost the same height with a clean
# gap between them (the user's pick "D level" of four layouts, 2026-10-30; the font's version has a big gap between them).
# Right 80 rather than the 66 shown: at 66 (a 33-unit gap) the hooks ran together into two pinholes at the 32 px size
# drawn far away; at 80 (41 units) it stays clean to 32 px like every other rune.
ns['R'][0x16C3]=[[(170,700),(0,560),(170,420)], [(210,560),(380,420),(210,280)]]
# PotatoPainter's own strokes for the Younger Futhark and the Anglo-Saxon futhorc (2026-10-31). Each one follows the
# layout of the Unicode reference glyph as drawn in Noto Sans Runic (where the branches and twigs join the stave, which
# way they run, how far they reach), in the Elder strokes' proportions: 700 tall, branches at about the slope of ansuz
# (220 across, 190 down), parallel strokes at least 41 units apart once 92 wide (clean down to the 32 px copy).
# Shapes the sources give as the same as an existing one are not drawn again: long-branch madr = algiz, futhorc ior and
# ger = long-branch hagall, calc = long-branch yr (Wikipedia's Algiz page; Noto draws ger and hagall alike).
NEW={
    # Younger Futhark, long-branch
    0x16AC: [[(180,0),(180,700)], [(0,640),(360,330)], [(0,430),(360,120)]],                 # oss: two strokes across
    0x16B4: [[(0,0),(0,700)], [(0,380),(260,700)]],                                          # kaun: one branch up
    0x16BC: [[(180,0),(180,700)], [(0,190),(360,510)], [(0,510),(360,190)]],                 # hagall: crossed
    0x16C5: [[(180,0),(180,700)], [(0,230),(360,500)]],                                      # ar: one stroke across
    0x16CB: [[(0,700),(0,260),(330,450),(330,0)]],                                           # sol: two half staves
    0x16E6: [[(200,0),(200,700)], [(0,0),(200,240),(400,0)]],                                # yr: algiz upside down
    # Younger Futhark, short-twig
    0x16AD: [[(0,0),(0,700)], [(0,480),(170,340)], [(0,290),(170,150)]],                     # oss: two twigs right
    0x16BD: [[(0,0),(0,700)]],                                                               # hagall: dot (BLOBS)
    0x16BF: [[(0,0),(0,700)], [(0,380),(170,240)]],                                          # naud: one twig right
    0x16C6: [[(170,0),(170,700)], [(170,360),(0,220)]],                                      # ar: one twig left
    0x16CC: [[(0,440),(0,700)]],                                                             # sol: upper half stave
    0x16D0: [[(170,0),(170,700),(0,560)]],                                                   # tyr: twig from the top
    0x16D3: [[(0,0),(0,700)], [(0,450),(170,590)], [(0,260),(170,400)]],                     # bjarkan: two twigs up
    0x16D9: [[(0,0),(0,600)]],                                                               # madr: dot on top (BLOBS)
    0x16E7: [[(0,0),(0,260)]],                                                               # yr: lower half stave
    # Anglo-Saxon futhorc
    0x16A9: [[(0,0),(0,700)], [(0,700),(190,560),(330,680)], [(0,490),(190,350),(330,470)]], # os: two hooked branches
    0x16AA: [[(0,0),(0,700)], [(0,700),(190,560),(330,680)], [(0,490),(190,350)]],           # ac: hooked, then plain
    0x16AB: [[(0,0),(0,700)], [(0,700),(330,416)], [(0,490),(330,206)]],                     # aesc: long branches
    0x16B3: [[(0,0),(0,700)], [(0,360),(240,0)]],                                            # cen: branch down
    0x16BB: [[(0,0),(0,700)], [(320,0),(320,700)], [(0,590),(320,350)], [(0,370),(320,130)]],  # haegl: two bars
    0x16DD: [[(0,350),(400,700)], [(400,350),(0,700)], [(0,350),(400,0)], [(400,350),(0,0)]],  # ing: open diamond
    0x16E0: [[(200,0),(200,700)], [(0,640),(100,530),(200,700),(300,530),(400,640)]],        # ear
    0x16A3: [[(0,0),(0,700),(420,0)], [(170,0),(170,140)]],                                  # yr (420/170: no pinhole in the sharp top corner at 128 px)
    0x16B8: [[(0,0),(400,700)], [(0,700),(400,0)], [(103,180),(0,350),(103,520)], [(297,180),(400,350),(297,520)]],  # gar
    0x16E2: [[(0,50),(100,160),(200,0),(200,700),(300,540),(400,650)]],                      # cweorth
    0x16E5: [[(0,0),(0,700)], [(440,0),(440,700)], [(0,700),(220,610),(440,700)], [(0,0),(220,90),(440,0)],
             [(0,350),(220,610),(440,350),(220,90),(0,350)]],                                # stan
    0x16E4: [[(0,0),(400,700)], [(0,700),(400,0)], [(103,180),(0,350),(103,520)], [(297,180),(400,350),(297,520)],
             [(200,0),(200,700)]],                                                           # Ruthwell rune
}
ns['R'].update(NEW)
# Round dots, as Unicode's reference glyphs draw short-twig hagall (mid stave) and madr (top): centre, radius.
BLOBS={0x16BD: ((0,350),95), 0x16D9: ((0,610),95)}
# Half staves that sit high or low in the rune's height: centred on the full 0-700 height, not on their own bounds,
# or short-twig sol and yr would both come out as the same dash in the middle.
FRAMED={0x16CC, 0x16E7}
def render(cp, scale=0.25, size=256, ss=4, stroke=None):
    if stroke: ns['STROKE']=stroke
    g=ns['outline'](cp)
    if cp in BLOBS:
        from shapely.geometry import Point
        (bx,by),br=BLOBS[cp]; g=unary_union([g, Point(bx,by).buffer(br, quad_segs=12)])
    minx,miny,maxx,maxy=g.bounds; cx=(minx+maxx)/2; cy=350 if cp in FRAMED else (miny+maxy)/2
    S=size*ss; im=Image.new('L',(S,S),0); d=ImageDraw.Draw(im)
    def tr(x,y): return ((x-cx)*scale*ss+S/2, S/2-(y-cy)*scale*ss)
    polys=list(g.geoms) if g.geom_type=="MultiPolygon" else [g]
    for p in polys:
        d.polygon([tr(x,y) for x,y in p.exterior.coords],fill=255)
        for r in p.interiors: d.polygon([tr(x,y) for x,y in r.coords],fill=0)
    a=np.asarray(im.resize((size,size),Image.BOX)).astype(np.uint8)
    out=np.zeros((size,size,4),np.uint8); out[...,:3]=255; out[...,3]=a
    return Image.fromarray(out,'RGBA')
if __name__=="__main__":
    render(int(sys.argv[2],16)).save(sys.argv[3])
