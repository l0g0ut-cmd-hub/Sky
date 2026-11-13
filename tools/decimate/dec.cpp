// Обёртка над Fast-Quadric-Mesh-Simplification (MIT, sp4cerat): сырой ввод/вывод для tools/decimate_ship.py
#include <cstdint>
#include <cstdlib>
#include "Simplify.h"
#include <cstdio>
int main(int argc,char**argv){
  if(argc<4){fprintf(stderr,"dec in.raw out.raw target [aggr]\n");return 1;}
  FILE*f=fopen(argv[1],"rb");int nv,nt;fread(&nv,4,1,f);fread(&nt,4,1,f);
  Simplify::vertices.resize(nv);Simplify::triangles.resize(nt);
  for(int i=0;i<nv;i++){float p[3];fread(p,4,3,f);Simplify::vertices[i].p=vec3f(p[0],p[1],p[2]);}
  for(int i=0;i<nt;i++){int v[3];float uv[6];int m;fread(v,4,3,f);fread(uv,4,6,f);fread(&m,4,1,f);
    Simplify::Triangle&t=Simplify::triangles[i];for(int k=0;k<3;k++){t.v[k]=v[k];t.uvs[k]=vec3f(uv[k*2],uv[k*2+1],0);} t.attr=Simplify::TEXCOORD;t.material=m;t.deleted=0;}
  fclose(f);
  double ag=argc>4?atof(argv[4]):7;
  Simplify::simplify_mesh(atoi(argv[3]),ag,true);
  f=fopen(argv[2],"wb");nv=Simplify::vertices.size();nt=Simplify::triangles.size();fwrite(&nv,4,1,f);fwrite(&nt,4,1,f);
  for(auto&v:Simplify::vertices){float p[3]={(float)v.p.x,(float)v.p.y,(float)v.p.z};fwrite(p,4,3,f);}
  for(auto&t:Simplify::triangles){float uv[6];for(int k=0;k<3;k++){uv[k*2]=t.uvs[k].x;uv[k*2+1]=t.uvs[k].y;}fwrite(t.v,4,3,f);fwrite(uv,4,6,f);fwrite(&t.material,4,1,f);}
  fclose(f);return 0;}
