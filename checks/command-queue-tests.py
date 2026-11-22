"""Compile actual producer setter bodies; compare queued state with suppression off/on."""
import os,subprocess,json
from pathlib import Path
root=Path(__file__).resolve().parents[1]
s=(root/'vendor/ppsspp/Common/GPU/OpenGL/GLRenderManager.h').read_text(encoding='utf-8')
def method(name):
 start=s.index('\tvoid '+name+'('); opening=s.index('{',start);depth=1;end=opening+1
 while depth:
  depth+=(s[end]=='{')-(s[end]=='}');end+=1
 return s[start:end]
methods='\n'.join(method(n) for n in ('SetDepth','SetViewport','SetScissor','SetBlendAndMask','SetNoBlendAndMask','SetBlendFactor','SetRaster'))
code=r'''#undef NDEBUG
#include <cassert>
#include <vector>
#include <array>
#include <cstring>
#include <cstdio>
#include <random>
#include "../native/core-build/runtime/tizenpsp_command_cache.h"
using GLenum=unsigned;using GLboolean=unsigned char;
#define _dbg_assert_(x) assert(x)
#define _assert_(x) assert(x)
struct GLRViewport {float x,y,w,h,minZ,maxZ;};struct GLRect2D {int x,y,w,h;};
enum class GLRStepType{RENDER};enum class GLRRenderCommand{DEPTH,VIEWPORT,SCISSOR,BLEND,BLENDCOLOR,RASTER};
struct GLRRenderData {
 GLRRenderCommand cmd;
 struct {bool enabled,write;unsigned func;} depth{};
 struct {GLRViewport vp;} viewport{};struct {GLRect2D rc;} scissor{};
 struct {int mask;bool enabled;unsigned srcColor,dstColor,srcAlpha,dstAlpha,funcColor,funcAlpha;} blend{};
 struct {float color[4];} blendColor{};
 struct {unsigned cullEnable,frontFace,cullFace,ditherEnable,depthClampEnable;} raster{};
};
struct Commands:std::vector<GLRRenderData>{GLRRenderData &push_uninitialized(){emplace_back();return back();}};
struct Step{GLRStepType stepType=GLRStepType::RENDER;Commands commands;};
void CopyFloat4(float *a,const float*b){memcpy(a,b,16);}
struct Producer {Step step;Step *curRenderStep_=&step;TizenCommandCache commandCache_;bool enabled;
 Producer(bool e):enabled(e){}
 bool QueueState(unsigned slot,const void *data,unsigned size){if(!enabled)commandCache_.Clear();return !enabled||commandCache_.Update(slot,data,size);}
'''+methods+r'''
};
struct State {std::array<std::vector<unsigned char>,6> values;
 template<class T>void Put(int n,const T&v){const auto *p=(const unsigned char*)&v;values[n]={p,p+sizeof(v)};}
 void Apply(const GLRRenderData &d){switch(d.cmd){
 case GLRRenderCommand::DEPTH:Put(0,d.depth);break;
 case GLRRenderCommand::VIEWPORT:Put(1,d.viewport);break;
 case GLRRenderCommand::SCISSOR:Put(2,d.scissor);break;
 case GLRRenderCommand::BLEND:{auto b=d.blend;if(!b.enabled)b.srcColor=b.dstColor=b.srcAlpha=b.dstAlpha=b.funcColor=b.funcAlpha=0;Put(3,b);break;}
 case GLRRenderCommand::BLENDCOLOR:Put(4,d.blendColor);break;
 case GLRRenderCommand::RASTER:Put(5,d.raster);break;
 }}
};
int main(){Producer a(false),b(true);State x,y;std::mt19937 rng(243);unsigned before=0,after=0;
 for(int i=0;i<100000;i++){
  if(i%127==0){a.commandCache_.Clear();b.commandCache_.Clear();x=State{};y=State{};}
  unsigned kind=rng()%7;int value=(i/31)%4;
  for(auto *p:{&a,&b})switch(kind){
   case 0:p->SetDepth(true,value&1,value);break;
   case 1:p->SetViewport({float(value),0,480,272,0,1});break;
   case 2:p->SetScissor({value,0,480,272});break;
   case 3:p->SetBlendAndMask(15,true,value,1,2,3,4,5);break;
   case 4:{float color[]={float(value),0,-0.0f,1};p->SetBlendFactor(color);break;}
   case 5:p->SetRaster(value&1,1,2,0,0);break;
   case 6:p->SetNoBlendAndMask(value);break;
  }
  before+=a.step.commands.size();after+=b.step.commands.size();
  for(auto &d:a.step.commands)x.Apply(d);for(auto &d:b.step.commands)y.Apply(d);
  assert(x.values==y.values);a.step.commands.clear();b.step.commands.clear();
 }
 assert(after<before);printf("PASS: 100000 production state setter comparisons; queued %u -> %u\n",before,after);
 TizenCommandCache c;unsigned v=0;assert(c.Update(0,&v,4));assert(!c.Update(0,&v,4));v=0x80000000;assert(c.Update(0,&v,4));c.Clear();assert(c.Update(0,&v,4));
}
'''
(root/'build/command-queue-tests.cpp').write_text(code)
zig=Path('C:/path/to/user/Documents/Codex/2026-10-30/i-x20/work/native-build-tools/zig/zig-windows-x86_64-0.13.0/zig.exe')
env=dict(os.environ,ZIG_GLOBAL_CACHE_DIR=str(root/'.cache/zig-global'),ZIG_LOCAL_CACHE_DIR=str(root/'.cache/zig-local'),ZIG_LIB_DIR=str(zig.parent/'lib'))
exe=root/'build/command-queue-tests.exe'
subprocess.run([str(zig),'c++','-target','x86_64-windows-gnu','-std=c++17','-O2','-DTIZENPSP_ARM_JIT',str(root/'build/command-queue-tests.cpp'),'-o',str(exe)],env=env,check=True)
output=subprocess.check_output([str(exe)],text=True);print(output)
(root/'docs/command-queue-validation-v0243.json').write_text(json.dumps(dict(scope='Host modeled state; synthetic workload, not TV FPS',result=output.strip()),indent=2)+'\n')
