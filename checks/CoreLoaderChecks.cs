using System;
using System.IO;
using System.Linq;
using System.Text;
internal static class CoreLoaderChecks
{
    static void Reject(byte[] bytes,bool core=true) {
        try {DynamicElf.Parse(bytes,4096,"retro_init",true,core);}
        catch(Exception e) when(e.Message.StartsWith("Dynamic ELF:")){return;}
        throw new Exception("Malformed/unsupported core accepted");
    }
    static int FileAt(byte[] b,int address) {
        int ph=(int)DynamicElf.U32(b,28),n=BitConverter.ToUInt16(b,44);
        for(int i=0;i<n;i++) {int h=ph+32*i,a=(int)DynamicElf.U32(b,h+8),sz=(int)DynamicElf.U32(b,h+16);
            if(DynamicElf.U32(b,h)==1&&address>=a&&address<a+sz)return (int)DynamicElf.U32(b,h+4)+address-a;}
        throw new Exception("Not file-backed");
    }
    internal static void Run(byte[] bytes,bool armJit=false) {
        Reject(bytes,false);
        var p=DynamicElf.Parse(bytes,4096,"retro_init",true,true);
        if(p.Initializers.Count!=(armJit?119:117)||p.Dependencies.Count!=5||p.InitFunction==0||p.FiniFunction==0)throw new Exception("Core startup metadata mismatch");
        foreach(string name in new[]{"retro_init","retro_deinit","retro_api_version","retro_set_environment","retro_get_system_info"})p.Export(name);
        if(armJit){
            p.Export("tizenpsp_jit_init");p.Export("tizenpsp_jit_snapshot");
            p.Export("tizenpsp_set_command_queue");p.Export("tizenpsp_command_queue_status");p.Export("tizenpsp_set_vertex_binding");p.Export("tizenpsp_vertex_binding_status");
            p.Export("retro_tizenpsp_guest_millis");p.Export("retro_tizenpsp_actual_flips");p.Export("retro_tizenpsp_folded_flips");
        }
        var manifest=File.ReadAllLines(armJit?"app/res/core-jit-runtime-imports.tsv":"app/res/core-runtime-imports.tsv").Where(s=>!s.StartsWith("#")&&s.Length>0).ToDictionary(s=>s.Split('\t')[0]);
        foreach(var s in p.Symbols.Where(s=>s.Section==0&&s.Name.Length>0)) {
            var f=manifest[s.Name].Split('\t');
            if(s.Version!=f[1]||s.Library!=f[2]||(s.Binding==2)!=(f[3]=="1"))throw new Exception("Symbol version/binding differs from TV-tested manifest");
        }
        foreach(uint bias in new[]{0x10000000u,0x70000000u}) {
            var image=p.RelocateSymbols(bias,s=>s.Binding==2?0:0x23450000u);
            foreach(var r in p.Relocations) {
                var s=p.Symbols[r.Symbol];uint value;
                if(r.Type==23)value=bias+DynamicElf.U32(p.Image,r.Offset);
                else {value=s.Section==0?(s.Binding==2?0:0x23450000u):bias+s.Value;if(r.Type==21||r.Type==2)value=unchecked(value+DynamicElf.U32(p.Image,r.Offset));}
                if(DynamicElf.U32(image,r.Offset)!=value)throw new Exception("Core relocation mismatch");
            }
        }
        bool missing=false;try{p.RelocateSymbols(0x10000000u,s=>0);}catch(Exception){missing=true;}if(!missing)throw new Exception("Required imports accepted as null");
        var bad=(byte[])bytes.Clone();DynamicElf.Put(bad,36,DynamicElf.U32(bad,36)|0x400);Reject(bad);
        byte[] needle=Encoding.ASCII.GetBytes("libc.so.6\0");int where=-1;
        for(int i=0;i<bytes.Length-needle.Length;i++)if(bytes.Skip(i).Take(needle.Length).SequenceEqual(needle)){where=i;break;}
        if(where<0)throw new Exception("Dependency fixture missing");
        bad=(byte[])bytes.Clone();Array.Copy(Encoding.ASCII.GetBytes("evil.so.6"),0,bad,where,9);Reject(bad);
        // Corrupt a version index without changing the symbol or its relocation.
        int ph=(int)DynamicElf.U32(bytes,28),count=BitConverter.ToUInt16(bytes,44);
        for(int i=0;i<count;i++) {int h=ph+32*i;if(DynamicElf.U32(bytes,h)!=2)continue;
            int start=(int)DynamicElf.U32(bytes,h+4),end=start+(int)DynamicElf.U32(bytes,h+16);
            for(int at=start;at<end;at+=8)if(DynamicElf.U32(bytes,at)==0x6ffffff0) {
                bad=(byte[])bytes.Clone();int table=FileAt(bytes,(int)DynamicElf.U32(bytes,at+4));bad[table+2]=0xfe;bad[table+3]=0x7f;Reject(bad);
            }
        }
        Console.WriteLine("PASS: actual core metadata, manifest versions, weak imports, "+p.Relocations.Count+" relocations at two bases, default-policy rejection and malformed ABI/dependency/version rejection.");
    }
}
