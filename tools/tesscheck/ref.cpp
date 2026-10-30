#include "tessellator.hpp"
#include <cstdio>
#include <cstdlib>
int main(){
  CHWTessellator t; t.Init(D3D11_TESSELLATOR_PARTITIONING_FRACTIONAL_ODD, D3D11_TESSELLATOR_OUTPUT_TRIANGLE_CW);
  srand(12345);
  for(int k=0;k<4000;k++){
    float f[4];
    for(int i=0;i<4;i++){ int r=rand()%1000; f[i]= (k<200)? (float)(1+ (k%40)*0.1f + i*0.37f) : (r/1000.0f)*5.0f; }
    if(k%7==0){ f[0]=f[1]=f[2]=4.0f; f[3]=4.0f; }
    t.TessellateTriDomain(f[0],f[1],f[2],f[3]);
    printf("%a %a %a %a %d %d\n",f[0],f[1],f[2],f[3],t.GetPointCount(),t.GetIndexCount());
    DOMAIN_POINT*p=t.GetPoints(); for(int i=0;i<t.GetPointCount();i++) printf("%a %a\n",p[i].u,p[i].v);
    int*ix=t.GetIndices(); for(int i=0;i<t.GetIndexCount();i++) printf("%d ",ix[i]); printf("\n");
  }
}
