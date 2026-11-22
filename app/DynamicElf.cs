using System;
using System.Collections.Generic;
using System.Text;

// Limited ELF32 ARM linker for hash-verified bundled binaries, not arbitrary ELF input.
internal sealed class DynamicElf {
 internal sealed class Segment { internal int Address, MemorySize, Flags; }
 internal sealed class Relocation { internal int Offset, Type, Symbol; }
 internal sealed class Symbol { internal string Name,Version="",Library=""; internal uint Value; internal int Section, Type,Binding; }
 internal byte[] Image;
 internal readonly List<Segment> Segments=new List<Segment>();
 internal readonly List<Relocation> Relocations=new List<Relocation>();
 internal readonly List<Symbol> Symbols=new List<Symbol>();
 internal int Entry, RelativeCount, GlobalCount, JumpCount;
 internal int[] Protections;
 internal readonly List<int> Initializers=new List<int>();
 internal readonly List<string> Dependencies=new List<string>();
 internal int InitFunction,FiniFunction;
 bool coreRuntime;
 readonly HashSet<int> relocationTargets=new HashSet<int>();
 static void Need(bool ok,string message){if(!ok)throw new Exception("Dynamic ELF: "+message);}
 internal static uint U32(byte[] b,int o){Need(o>=0&&o<=b.Length-4,"word bounds");return BitConverter.ToUInt32(b,o);}
 static int Number(uint v){Need(v<=int.MaxValue,"integer bounds");return (int)v;}
 static int Word(byte[] b,int o){return Number(U32(b,o));}
 static int Half(byte[] b,int o){Need(o>=0&&o<=b.Length-2,"halfword bounds");return BitConverter.ToUInt16(b,o);}
 internal static void Put(byte[] b,int o,uint v){Need(o>=0&&o<=b.Length-4,"write bounds");Array.Copy(BitConverter.GetBytes(v),0,b,o,4);}
 bool InSegment(int start,int size,int flags){foreach(var s in Segments)if(start>=s.Address&&(long)start+size<=s.Address+s.MemorySize&&(s.Flags&flags)==flags)return true;return false;}
 internal static DynamicElf Parse(byte[] file,int page,string entryName="reloc_probe",bool allowInitializers=false,bool coreRuntime=false){
  Need(page==4096,"requires 4096-byte pages");Need(file.Length>=52,"truncated header");
  Need(file[0]==127&&file[1]==69&&file[2]==76&&file[3]==70&&file[4]==1&&file[5]==1&&file[6]==1,"ELF32 LE required");
  Need(Half(file,16)==3&&Half(file,18)==40&&Word(file,20)==1,"ARM shared library required");
  Need((U32(file,36)&0x400)==0,"hard-float ABI unsupported");
  Need(Half(file,40)==52&&Half(file,42)==32,"header sizes");
  int ph=Word(file,28),count=Half(file,44);Need(count>0&&count<=64&&(long)ph+count*32<=file.Length,"program headers");
  var p=new DynamicElf{coreRuntime=coreRuntime};int dynamic=-1,ds=0,size=0;
  for(int i=0;i<count;i++){
   int h=ph+i*32,type=Word(file,h),off=Word(file,h+4),addr=Word(file,h+8),fs=Word(file,h+16),ms=Word(file,h+20),flags=Word(file,h+24);
   Need(type!=3&&type!=7,"interpreter/TLS unsupported");
   if(type==2){Need(dynamic<0,"multiple dynamic segments");dynamic=addr;ds=fs;}
   if(type!=1)continue;
   // NEON's static texture caches plus ARM dynarec tables occupy just over 8 MiB.
   Need(fs<=ms&&(long)off+fs<=file.Length&&(long)addr+ms<=(coreRuntime?32:12)*1024*1024,"load segment bounds");
   Need((addr-off)%page==0&&(flags&3)!=3,"alignment or W+X segment");
   foreach(var old in p.Segments)Need(addr>=old.Address+old.MemorySize||old.Address>=addr+ms,"overlap");
   p.Segments.Add(new Segment{Address=addr,MemorySize=ms,Flags=flags});size=Math.Max(size,(addr+ms+page-1)/page*page);
  }
  Need(size>0&&dynamic>=0&&ds>=8&&ds%8==0&&p.InSegment(dynamic,ds,4),"dynamic segment");
  p.Image=new byte[size];p.Protections=new int[size/page];
  for(int i=0;i<count;i++){int h=ph+i*32;if(Word(file,h)!=1)continue;Array.Copy(file,Word(file,h+4),p.Image,Word(file,h+8),Word(file,h+16));}
  foreach(var s in p.Segments)for(int i=s.Address/page;i<(s.Address+s.MemorySize+page-1)/page;i++)p.Protections[i]|=((s.Flags&4)!=0?1:0)|((s.Flags&2)!=0?2:0)|((s.Flags&1)!=0?4:0);
  foreach(int prot in p.Protections)Need((prot&6)!=6,"overlapping W+X pages");
  var tags=new Dictionary<uint,int>();var needed=new List<int>();bool terminated=false;
  for(int o=dynamic;o<dynamic+ds;o+=8){uint tag=U32(p.Image,o);int val=Word(p.Image,o+4);if(tag==0){terminated=true;break;}
   Need(tag!=7&&tag!=8&&tag!=9&&tag!=22&&tag!=26&&tag!=28&&tag!=32&&tag!=33&&tag!=0x6ffffffc&&((tag!=25&&tag!=27)||allowInitializers)&&
    (coreRuntime||(tag!=1&&tag!=12&&tag!=13&&tag!=0x6ffffff0&&tag!=0x6ffffffe&&tag!=0x6fffffff)),"dependencies/RELA/initializers/textrel/versioning unsupported");
   if(tag==1){Need(needed.Count<16,"dependency count");needed.Add(val);continue;}
   Need(!tags.ContainsKey(tag),"duplicate dynamic tag");tags.Add(tag,val);
  }
  Need(terminated,"unterminated dynamic table");
  Func<uint,int> get=t=>{Need(tags.ContainsKey(t),"missing dynamic tag "+t);return tags[t];};
  int str=get(5),strSize=get(10),sym=get(6),hash=get(4);Need(get(11)==16,"symbol entry size");
  Need(p.InSegment(str,strSize,4)&&strSize>0&&p.InSegment(hash,8,4),"string/hash range");
  Func<int,string> text=offset=> {Need(offset>=0&&offset<strSize,"string offset");int end=str+offset;while(end<str+strSize&&p.Image[end]!=0)end++;Need(end<str+strSize,"string terminator");return Encoding.ASCII.GetString(p.Image,str+offset,end-str-offset);};
  foreach(int offset in needed) {
   string library=text(offset);
   Need(library=="libc.so.6"||library=="libm.so.6"||library=="libpthread.so.0"||library=="libdl.so.2"||library=="librt.so.1","dependency not allowlisted");
   Need(!p.Dependencies.Contains(library),"duplicate dependency");p.Dependencies.Add(library);
  }
  int buckets=Word(p.Image,hash),symbols=Word(p.Image,hash+4);Need(symbols>0&&symbols<=4096&&buckets<=4096,"hash counts");
  Need(p.InSegment(hash,8+4*(buckets+symbols),4)&&p.InSegment(sym,16*symbols,4),"hash/symbol bounds");
  for(int i=0;i<symbols;i++){
   int at=sym+i*16,name=Word(p.Image,at);Need(name<strSize,"name offset");int end=str+name;while(end<str+strSize&&p.Image[end]!=0)end++;Need(end<str+strSize,"name terminator");
   var s=new Symbol{Name=Encoding.ASCII.GetString(p.Image,str+name,end-str-name),Value=U32(p.Image,at+4),Section=Half(p.Image,at+14),Type=p.Image[at+12]&15,Binding=p.Image[at+12]>>4};p.Symbols.Add(s);
   if(s.Name==entryName){Need(s.Section!=0&&s.Section<0xff00&&s.Type==2,"entry symbol");int entry=Number(s.Value&~1u);Need(p.InSegment(entry,4,1),"entry outside executable segment");p.Entry=Number(s.Value);}
  }
  Need(p.Entry!=0,"entry not found");
  if(coreRuntime) {
   p.ReadVersions(tags,text);
   if(tags.ContainsKey(12)) {p.InitFunction=get(12);p.CheckFunction(p.InitFunction);}
   if(tags.ContainsKey(13)) {p.FiniFunction=get(13);p.CheckFunction(p.FiniFunction);}
  }
  if(tags.ContainsKey(17)){Need(get(19)==8,"REL size");p.ReadRel(get(17),get(18),false);}
  if(tags.ContainsKey(23)){Need(get(20)==17,"PLT must be REL");p.ReadRel(get(23),get(2),true);}
  if(tags.ContainsKey(25)||tags.ContainsKey(27)) {
   int array=get(25),bytes=get(27);
   Need(bytes>0&&bytes<=1024&&bytes%4==0&&array%4==0&&p.InSegment(array,bytes,6),"initializer array bounds");
   for(int slot=array;slot<array+bytes;slot+=4) {
    Relocation found=null;
    foreach(var r in p.Relocations)if(r.Offset==slot)found=r;
    Need(found!=null&&found.Type==23,"initializer requires local RELATIVE relocation");
    uint pointer=U32(p.Image,slot); int target=Number(pointer&~1u);
    Need(pointer!=0&&((pointer&1)!=0||target%4==0)&&p.InSegment(target,(pointer&1)!=0?2:4,1),"initializer target must be executable");
    p.Initializers.Add(Number(pointer));
   }
  }
  if(entryName=="reloc_probe")Need(p.RelativeCount>0&&p.GlobalCount>0&&p.JumpCount>0,"fixture must exercise all three relocations");return p;
 }
 void ReadRel(int start,int size,bool plt){
  Need(size%8==0&&InSegment(start,size,4),"relocation table bounds");
  for(int o=start;o<start+size;o+=8){int target=Word(Image,o);uint info=U32(Image,o+4);int type=(int)(info&255),symbol=(int)(info>>8);
   Need(type==23||type==21||type==22||(coreRuntime&&type==2),"unsupported relocation "+type);Need(!plt||type==22,"unsupported PLT relocation");
   Need((target&3)==0&&InSegment(target,4,2),"relocation target must be aligned writable data");Need(symbol<Symbols.Count,"relocation symbol index");
   Need(relocationTargets.Add(target),"duplicate relocation target");
   if(type==23){Need(symbol==0,"symbol-relative variant unsupported");Need(InSegment(Number(U32(Image,target)),1,0),"relative addend outside image");RelativeCount++;}
   if(type==21)GlobalCount++;if(type==22)JumpCount++;
   Relocations.Add(new Relocation{Offset=target,Type=type,Symbol=symbol});
  }
 }
 internal byte[] Relocate(uint loadBias,Func<string,uint> resolve){
  return RelocateSymbols(loadBias,s=>resolve(s.Name));
 }
 internal byte[] RelocateSymbols(uint loadBias,Func<Symbol,uint> resolve){
  Need((ulong)loadBias+(uint)Image.Length<=0x100000000UL,"address wrap");var output=(byte[])Image.Clone();
  foreach(var r in Relocations){uint value;
   if(r.Type==23)value=checked(loadBias+U32(Image,r.Offset));
   else {var s=Symbols[r.Symbol];Need(s.Type!=6&&s.Type!=10,"TLS/IFUNC unsupported");
    if(s.Section==0){value=resolve(s);Need(value!=0||(coreRuntime&&s.Binding==2),"unresolved import "+s.Name);}
    else{Need(s.Section<0xff00&&InSegment(Number(s.Value&~1u),1,0),"symbol outside image");value=checked(loadBias+s.Value);}
    // ARM REL JUMP_SLOT addend is zero even when the slot contains a linker trampoline.
    if(r.Type==21||r.Type==2)value=unchecked(value+U32(Image,r.Offset));
   }
   Put(output,r.Offset,value);
  }
  return output;
 }
 void CheckFunction(int pointer) {
  if(pointer==0)return;
  int target=pointer&~1;Need(((pointer&1)!=0||target%4==0)&&InSegment(target,(pointer&1)!=0?2:4,1),"startup function outside executable image");
 }
 void ReadVersions(Dictionary<uint,int> tags,Func<int,string> text) {
  bool any=tags.ContainsKey(0x6ffffff0)||tags.ContainsKey(0x6ffffffe)||tags.ContainsKey(0x6fffffff);
  if(!any)return;
  Need(tags.ContainsKey(0x6ffffff0)&&tags.ContainsKey(0x6ffffffe)&&tags.ContainsKey(0x6fffffff),"incomplete version metadata");
  int count=tags[0x6fffffff],at=tags[0x6ffffffe],table=tags[0x6ffffff0];
  Need(count>0&&count<=16&&InSegment(table,Symbols.Count*2,4),"version bounds");
  var versions=new Dictionary<int,Tuple<string,string>>();var seen=new HashSet<int>();
  for(int i=0;i<count;i++) {
   Need(seen.Add(at)&&InSegment(at,16,4)&&Half(Image,at)==1,"version need record");
   int n=Half(Image,at+2);string library=text(Word(Image,at+4));
   Need(Dependencies.Contains(library)&&n>0&&n<=128,"version dependency/count");
   int delta=Word(Image,at+8);Need(delta>=16,"version auxiliary offset");int aux=checked(at+delta);
   for(int j=0;j<n;j++) {
    Need(seen.Add(aux)&&InSegment(aux,16,4),"version auxiliary bounds/cycle");
    int index=Half(Image,aux+6)&0x7fff;string version=text(Word(Image,aux+8));
    Need(index>=2&&!versions.ContainsKey(index)&&version.Length>0,"version index");
    versions.Add(index,Tuple.Create(library,version));int next=Word(Image,aux+12);
    Need(j==n-1?next==0:next>=16,"version auxiliary chain");aux=checked(aux+next);
   }
   int following=Word(Image,at+12);Need(i==count-1?following==0:following>=16,"version need chain");at=checked(at+following);
  }
  for(int i=0;i<Symbols.Count;i++) {
   int index=Half(Image,table+2*i)&0x7fff;if(index<=1)continue;
   Need(versions.ContainsKey(index)&&Symbols[i].Section==0,"unknown/local symbol version");
   Symbols[i].Library=versions[index].Item1;Symbols[i].Version=versions[index].Item2;
  }
 }
 internal int Export(string name) {
  foreach(var s in Symbols)if(s.Name==name&&s.Section!=0&&s.Section<0xff00&&s.Type==2){CheckFunction(Number(s.Value));return Number(s.Value);}
  throw new Exception("Dynamic ELF: missing export "+name);
 }
}
