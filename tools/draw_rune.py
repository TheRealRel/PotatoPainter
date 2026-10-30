# Draws Elder Futhark runes (python3 -I draw_rune.py <PotatoRunes tools/futhark/make_font.py> <codepoint hex> <out.png>)
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
def render(cp, scale=0.25, size=256, ss=4, stroke=None):
    if stroke: ns['STROKE']=stroke
    g=ns['outline'](cp)
    minx,miny,maxx,maxy=g.bounds; cx=(minx+maxx)/2; cy=(miny+maxy)/2
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
