using System;
internal static class CoreImportChecks
{
    internal static void Run() {
        string[] lines={"# fixture","malloc\tGLIBC_2.4\tlibc.so.6\t0","optional\t\t\t1"};
        int logs=0;
        string ok=CoreImportCheck.Run(lines,(n,v,l)=>n=="malloc"&&v=="GLIBC_2.4"&&l=="libc.so.6",s=>logs++);
        if(!ok.Contains("1/1 PASS")||logs!=2)throw new Exception("Optional import handling");
        string fail=CoreImportCheck.Run(lines,(n,v,l)=>false,s=>{});
        if(!fail.Contains("0/1 MISSING"))throw new Exception("Required import missing was accepted");
        bool rejected=false;try{CoreImportCheck.Run(new[]{"bad"},(n,v,l)=>true,s=>{});}catch(FormatException){rejected=true;}
        if(!rejected)throw new Exception("Malformed manifest accepted");
        Console.WriteLine("PASS: core import requirements, version/library forwarding, optional symbols and malformed manifest rejection.");
    }
}
