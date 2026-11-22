using System;
internal static class CoreImportCheck
{
    internal static string Run(string[] lines,Func<string,string,string,bool> lookup,Action<string> log)
    {
        int required=0,found=0,optionalMissing=0;
        foreach(string line in lines) {
            if(line.Length==0||line.StartsWith("#"))continue;
            string[] fields=line.Split('\t');
            if(fields.Length!=4||(fields[3]!="0"&&fields[3]!="1"))throw new FormatException("Invalid core import manifest");
            bool weak=fields[3]=="1",present=lookup(fields[0],fields[1],fields[2]);
            if(!weak) {required++;if(present)found++;}
            if(!present) {
                if(weak)optionalMissing++;
                log((weak?"CORE_OPTIONAL_MISSING ":"CORE_IMPORT_MISSING ")+fields[0]+" version="+fields[1]+" library="+fields[2]);
            }
        }
        if(required==0)throw new FormatException("Empty core import manifest");
        string result="PPSSPP imports: "+found+"/"+required+(found==required?" PASS":" MISSING")+"; core not executed";
        log("CORE_IMPORT_RESULT "+result+"; optional_missing="+optionalMissing);
        return result;
    }
}
