using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

// Separate from library-v2.tsv so older installers retain all library preferences.
internal sealed class FastMemoryPreferences
{
    readonly HashSet<string> keys=new HashSet<string>(StringComparer.Ordinal);
    static bool ValidKey(string key)=>key!=null&&key.Length==64&&key.All(c=>"0123456789ABCDEF".Contains(c));
    internal bool Enabled(LibraryItem item)=>item!=null&&keys.Contains(item.Key);
    internal void Set(LibraryItem item,bool enabled) {
        if(item==null||!ValidKey(item.Key))throw new ArgumentException("Invalid game identity");
        if(enabled)keys.Add(item.Key);else keys.Remove(item.Key);
    }
    internal string Serialize()=>"TizenPSP-fast-memory-v1\n"+string.Join("\n",keys.OrderBy(x=>x,StringComparer.Ordinal))+"\n";
    internal static FastMemoryPreferences Parse(string text) {
        if(text.Length>300000)throw new InvalidDataException("Fast memory settings too large");
        var lines=text.Replace("\r\n","\n").Split('\n');
        if(lines[0]!="TizenPSP-fast-memory-v1")throw new InvalidDataException("Unsupported fast memory settings");
        var result=new FastMemoryPreferences();
        foreach(string key in lines.Skip(1).Where(x=>x.Length>0))
            if(!ValidKey(key)||result.keys.Count>=4096||!result.keys.Add(key))throw new InvalidDataException("Invalid fast memory game identity");
        return result;
    }
    internal static FastMemoryPreferences Load(string path,Action<string> log) {
        try {
            if(File.Exists(path)) {
                if(new FileInfo(path).Length>300000)throw new InvalidDataException("Fast memory settings too large");
                return Parse(File.ReadAllText(path));
            }
        }catch(Exception e){log("FAST_MEMORY_LOAD "+e.Message+"; using checked memory");}
        return new FastMemoryPreferences();
    }
    internal void Save(string path) {
        string value=Serialize();Parse(value);
        string temp=path+".tmp";
        using(var stream=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None)) {
            byte[] bytes=System.Text.Encoding.UTF8.GetBytes(value);stream.Write(bytes,0,bytes.Length);stream.Flush(true);
        }
        if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);
    }
}
