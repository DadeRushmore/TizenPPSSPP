using System;
using System.Collections.Generic;
internal static class InitializerChecks
{
    static int W(byte[] b,int at)=>(int)DynamicElf.U32(b,at);
    static int FileAt(byte[] b,int address) {
        int ph=W(b,28),count=BitConverter.ToUInt16(b,44);
        for(int i=0;i<count;i++) {
            int h=ph+i*32;
            if(W(b,h)==1 && address>=W(b,h+8) && address<W(b,h+8)+W(b,h+16))
                return W(b,h+4)+address-W(b,h+8);
        }
        throw new Exception("Fixture address not file backed");
    }
    static int Tag(byte[] b,uint wanted) {
        int ph=W(b,28),count=BitConverter.ToUInt16(b,44);
        for(int i=0;i<count;i++) {
            int h=ph+i*32;if(W(b,h)!=2)continue;
            for(int at=W(b,h+4);at<W(b,h+4)+W(b,h+16);at+=8)
                if(DynamicElf.U32(b,at)==wanted)return at;
        }
        throw new Exception("Missing fixture tag "+wanted);
    }
    static void Reject(byte[] bytes,string reason,bool allow=true) {
        try {DynamicElf.Parse(bytes,4096,"tizenpsp_glview_clear",allow);}
        catch(Exception e) when(e.Message.StartsWith("Dynamic ELF:")) {return;}
        throw new Exception("Accepted invalid initializer: "+reason);
    }
    static byte[] Change(byte[] original,int offset,uint value) {
        var copy=(byte[])original.Clone();DynamicElf.Put(copy,offset,value);return copy;
    }
    internal static void Run(byte[] bytes) {
        var plan=DynamicElf.Parse(bytes,4096,"tizenpsp_glview_clear",true);
        if(plan.Initializers.Count!=1)throw new Exception("Expected one compiler-generated startup routine");
        Reject(bytes,"default policy must reject constructors",false);
        int arrayTag=Tag(bytes,25),sizeTag=Tag(bytes,27),array=W(bytes,arrayTag+4);
        Reject(Change(bytes,sizeTag+4,3),"unaligned size");
        Reject(Change(bytes,sizeTag+4,1028),"excessive count");
        Reject(Change(bytes,arrayTag+4,0x100000),"out-of-image array");
        Reject(Change(bytes,sizeTag,0x70000001),"missing array-size tag");
        Reject(Change(bytes,arrayTag,12),"DT_INIT still unsupported");
        Reject(Change(bytes,arrayTag,26),"finalizers still unsupported");
        Reject(Change(bytes,FileAt(bytes,array),(uint)array),"non-executable target");
        Reject(Change(bytes,FileAt(bytes,array),0),"null target");
        int rel=W(bytes,Tag(bytes,17)+4),relBytes=W(bytes,Tag(bytes,18)+4),slot=-1;
        for(int at=FileAt(bytes,rel),end=at+relBytes;at<end;at+=8)
            if(W(bytes,at)==array)slot=at;
        if(slot<0)throw new Exception("Initializer relocation missing");
        Reject(Change(bytes,slot+4,21),"initializer must use local RELATIVE");
        foreach(uint bias in new[]{0x10000000u,0x70000000u}) {
            byte[] relocated=plan.Relocate(bias,n=>0x12340000);
            if(DynamicElf.U32(relocated,array)!=bias+(uint)plan.Initializers[0])
                throw new Exception("Initializer relocation mismatch");
        }
        var calls=new List<int>();var run=new ModuleInitializers();
        run.Run(new[]{4,8},calls.Add);
        if(calls.Count!=2||calls[0]!=4||calls[1]!=8)throw new Exception("Initializer order");
        bool rejected=false;try {run.Run(new[]{4},calls.Add);}catch(InvalidOperationException){rejected=true;}
        if(!rejected||calls.Count!=2)throw new Exception("Initializer repeated");
        var failed=new ModuleInitializers();
        try {failed.Run(new[]{4,8},i=>throw new Exception("injected constructor failure"));}catch(Exception){}
        rejected=false;try {failed.Run(new[]{4},calls.Add);}catch(InvalidOperationException){rejected=true;}
        if(!rejected)throw new Exception("Partial initialization retried");
        Console.WriteLine("PASS: opt-in initializers, bounds/target/relocation rejection, two bases, ordered once-only execution and failure retry rejection.");
    }
}
