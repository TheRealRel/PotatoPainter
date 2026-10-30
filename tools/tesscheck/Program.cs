using System; using System.IO; using System.Globalization; using System.Text;
namespace PotatoPainter {
static class P {
  static float H(string s){ return (float)double.Parse(HexToDec(s), CultureInfo.InvariantCulture); }
  static string HexToDec(string s){ // parse C %a
    bool neg=s.StartsWith("-"); if(neg) s=s.Substring(1);
    s=s.Substring(2); int pi=s.IndexOf('p'); string mant=s.Substring(0,pi); int exp=int.Parse(s.Substring(pi+1));
    string[] parts=mant.Split('.'); double v=Convert.ToInt64(parts[0],16);
    if(parts.Length>1){ double scale=1.0/16; foreach(char ch in parts[1]){ v+=Convert.ToInt32(ch.ToString(),16)*scale; scale/=16; } }
    v=v*Math.Pow(2,exp); if(neg) v=-v; return v.ToString("R",CultureInfo.InvariantCulture);
  }
  static void Main(string[] a){
    var lines=File.ReadAllLines(a[0]); var t=new Tess(); int i=0, cases=0, bad=0;
    while(i<lines.Length){
      var h=lines[i].Split(' '); float f0=H(h[0]),f1=H(h[1]),f2=H(h[2]),f3=H(h[3]); int np=int.Parse(h[4]), ni=int.Parse(h[5]); i++;
      t.TessellateTri(f0,f1,f2,f3); bool ok = t.NumPoints==np && t.NumIndices==ni;
      for(int k=0;k<np;k++){ var pp=lines[i+k].Split(' '); if(ok && (H(pp[0])!=t.U[k] || H(pp[1])!=t.V[k])) ok=false; }
      i+=np; var ix=lines[i].Trim().Split(' ',StringSplitOptions.RemoveEmptyEntries); i++;
      if(ok) for(int k=0;k<ni;k++) if(int.Parse(ix[k])!=t.Index[k]){ ok=false; break; }
      cases++; if(!ok){ bad++; if(bad<5) Console.WriteLine($"MISMATCH {f0} {f1} {f2} {f3}: ref {np}/{ni} ours {t.NumPoints}/{t.NumIndices}"); }
    }
    Console.WriteLine($"{cases} cases, {bad} mismatches");
  }
}}
