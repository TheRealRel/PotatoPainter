Checks src/Tess.cs against Microsoft's D3D11 tessellator reference (tessellator.cpp/.hpp from
github.com/microsoft/DirectX-Specs, d3d/archive/images/d3d11/; rename its fmin/fmax/isNaN to avoid clashes with libc).
  g++ -O1 -w -fpermissive -o ref ref.cpp tessellator.cpp && ./ref > ref.txt     (4000 factor sets, fractional_odd, tri, cw)
  csc Program.cs ../../src/Tess.cs against the .NET 8 reference assemblies, then: dotnet cs.dll ref.txt
2026-10-30: 4000 cases, 0 mismatches (points bit-exact, indices identical).
