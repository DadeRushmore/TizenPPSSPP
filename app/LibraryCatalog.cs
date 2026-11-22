using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

internal enum LibraryFilter { All, Favorites, Recent }
internal enum LibrarySort { Title, TitleDescending, LastPlayed }
internal enum GameCpuChoice { Default, ArmJit, Interpreter }

internal sealed class LibraryItem
{
    internal readonly string Path, Key, Title;
    internal readonly long Bytes;
    internal LibraryItem(string path,long bytes) {
        Path=path;Bytes=bytes;Title=System.IO.Path.GetFileNameWithoutExtension(path).Replace('_',' ');
        // USB mount points change. Filename + length identifies a local library entry
        // without reading a large ISO. Identical names/sizes intentionally share preferences.
        string identity=System.IO.Path.GetFileName(path)+"\n"+bytes.ToString(CultureInfo.InvariantCulture);
        if(bytes<0)identity=path; // Do not merge files whose metadata could not be read.
        using(var sha=SHA256.Create())Key=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(identity))).Replace("-","");
    }
}

internal sealed class GamePreference
{
    internal bool Favorite,SkipBufferEffects;
    internal long LastPlayedUtcTicks;
    internal int AudioBuffer=-1; // Inherit the existing global setting.
    internal GameCpuChoice Cpu=GameCpuChoice.Default;
    internal int ResolutionScale=1,FrameSkip;
    internal bool UseArmJit=>Cpu!=GameCpuChoice.Interpreter;
}

internal sealed class LibraryCatalog
{
    const int MaxRecords=4096,MaxBytes=1024*1024;
    readonly Dictionary<string,GamePreference> entries=new Dictionary<string,GamePreference>(StringComparer.Ordinal);
    internal LibrarySort Sort;
    internal static bool ValidAudio(int value)=>value==-1||value==0||value==60||value==120||value==180;
    internal static bool ValidCpu(int value)=>value>=0&&value<=2;
    internal static bool ValidResolution(int value)=>value==1||value==2;
    internal static bool ValidFrameSkip(int value)=>value==0||value==1;
    internal GamePreference Get(LibraryItem item) {
        GamePreference value;return entries.TryGetValue(item.Key,out value)?value:new GamePreference();
    }
    GamePreference Edit(LibraryItem item) {
        GamePreference value;
        if(!entries.TryGetValue(item.Key,out value)) {
            if(entries.Count>=MaxRecords)throw new IOException("Library preference limit reached");
            entries.Add(item.Key,value=new GamePreference());
        }
        return value;
    }
    internal void ToggleFavorite(LibraryItem item){var value=Edit(item);value.Favorite=!value.Favorite;}
    internal void SetAudio(LibraryItem item,int value) {
        if(!ValidAudio(value))throw new ArgumentOutOfRangeException(nameof(value));
        Edit(item).AudioBuffer=value;
    }
    internal void SetCpu(LibraryItem item,GameCpuChoice value) {
        if(!ValidCpu((int)value))throw new ArgumentOutOfRangeException(nameof(value));
        Edit(item).Cpu=value;
    }
    internal void SetResolution(LibraryItem item,int value) {
        if(!ValidResolution(value))throw new ArgumentOutOfRangeException(nameof(value));
        Edit(item).ResolutionScale=value;
    }
    internal void SetFrameSkip(LibraryItem item,int value) {
        if(!ValidFrameSkip(value))throw new ArgumentOutOfRangeException(nameof(value));
        Edit(item).FrameSkip=value;
    }
    internal void SetSkipBufferEffects(LibraryItem item,bool value){Edit(item).SkipBufferEffects=value;}
    internal void RecordStarted(LibraryItem item,DateTime utc) {
        if(utc.Kind!=DateTimeKind.Utc)throw new ArgumentException("UTC required");
        Edit(item).LastPlayedUtcTicks=utc.Ticks;
    }
    internal LibraryItem[] Select(IEnumerable<LibraryItem> available,LibraryFilter filter) {
        var view=available.Where(x=>filter==LibraryFilter.All || (filter==LibraryFilter.Favorites?Get(x).Favorite:Get(x).LastPlayedUtcTicks>0));
        IOrderedEnumerable<LibraryItem> sorted;
        if(filter==LibraryFilter.Recent || Sort==LibrarySort.LastPlayed)
            sorted=view.OrderByDescending(x=>Get(x).LastPlayedUtcTicks).ThenBy(x=>x.Title,StringComparer.OrdinalIgnoreCase);
        else if(Sort==LibrarySort.TitleDescending)sorted=view.OrderByDescending(x=>x.Title,StringComparer.OrdinalIgnoreCase);
        else sorted=view.OrderBy(x=>x.Title,StringComparer.OrdinalIgnoreCase);
        return sorted.ThenBy(x=>x.Path,StringComparer.Ordinal).ToArray();
    }
    internal string Serialize() {
        var text=new StringBuilder("TizenPSP-library-v3\t"+((int)Sort).ToString(CultureInfo.InvariantCulture)+"\n");
        foreach(var entry in entries.OrderBy(x=>x.Key,StringComparer.Ordinal)) {
            var p=entry.Value;
            text.Append(entry.Key).Append('\t').Append(p.Favorite?"1":"0").Append('\t')
                .Append(p.LastPlayedUtcTicks.ToString(CultureInfo.InvariantCulture)).Append('\t')
                .Append(p.AudioBuffer.ToString(CultureInfo.InvariantCulture)).Append('\t')
                .Append(((int)p.Cpu).ToString(CultureInfo.InvariantCulture)).Append('\t')
                .Append(p.ResolutionScale.ToString(CultureInfo.InvariantCulture)).Append('\t')
                .Append(p.FrameSkip.ToString(CultureInfo.InvariantCulture)).Append('\t').Append(p.SkipBufferEffects?"1":"0").Append('\n');
        }
        return text.ToString();
    }
    internal static LibraryCatalog Parse(string text) {
        if(text.Length>MaxBytes)throw new InvalidDataException("Library settings too large");
        var lines=text.Replace("\r\n","\n").Split('\n');
        var header=lines[0].Split('\t');int sort;
        if(header.Length!=2 || (header[0]!="TizenPSP-library-v1"&&header[0]!="TizenPSP-library-v2"&&header[0]!="TizenPSP-library-v3") || !int.TryParse(header[1],out sort) || sort<0 || sort>2)
            throw new InvalidDataException("Unsupported library settings");
        bool legacy=header[0]=="TizenPSP-library-v1",effectsSchema=header[0]=="TizenPSP-library-v3";
        var result=new LibraryCatalog{Sort=(LibrarySort)sort};
        foreach(string line in lines.Skip(1)) {
            if(line.Length==0)continue;
            var fields=line.Split('\t');long ticks;int audio,cpu=0,resolution=1,frameSkip=0;
            if(fields.Length!=(legacy?4:effectsSchema?8:7) || fields[0].Length!=64 || fields[0].Any(c=>!"0123456789ABCDEF".Contains(c)) ||
                (fields[1]!="0" && fields[1]!="1") || !long.TryParse(fields[2],NumberStyles.None,CultureInfo.InvariantCulture,out ticks) || ticks>DateTime.MaxValue.Ticks ||
                !int.TryParse(fields[3],NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out audio) || !ValidAudio(audio) ||
                result.entries.ContainsKey(fields[0]) || result.entries.Count>=MaxRecords)
                throw new InvalidDataException("Invalid library settings record");
            if(!legacy && (!int.TryParse(fields[4],NumberStyles.None,CultureInfo.InvariantCulture,out cpu)||!ValidCpu(cpu)||
                !int.TryParse(fields[5],NumberStyles.None,CultureInfo.InvariantCulture,out resolution)||!ValidResolution(resolution)||
                !int.TryParse(fields[6],NumberStyles.None,CultureInfo.InvariantCulture,out frameSkip)||!ValidFrameSkip(frameSkip)))
                throw new InvalidDataException("Invalid game launch settings");
            if(effectsSchema && fields[7]!="0" && fields[7]!="1")throw new InvalidDataException("Invalid buffer effects setting");
            result.entries.Add(fields[0],new GamePreference{Favorite=fields[1]=="1",LastPlayedUtcTicks=ticks,AudioBuffer=audio,
                Cpu=(GameCpuChoice)cpu,ResolutionScale=resolution,FrameSkip=frameSkip,SkipBufferEffects=effectsSchema&&fields[7]=="1"});
        }
        return result;
    }
    internal static LibraryCatalog Load(string path,Action<string> log) {
        foreach(string candidate in new[]{path,path+".bak"}) {
            try {
                if(!File.Exists(candidate))continue;
                if(new FileInfo(candidate).Length>MaxBytes)throw new InvalidDataException("Library settings too large");
                var result=Parse(File.ReadAllText(candidate));
                if(candidate!=path)log("LIBRARY_RECOVERED backup; preferences will be restored on next save");
                return result;
            }catch(Exception e){log("LIBRARY_LOAD "+e.Message);}
        }
        return new LibraryCatalog();
    }
    internal void Save(string path) {
        // Validate before touching the last good file; keep one recoverable previous version.
        string value=Serialize();Parse(value);
        string temp=path+".tmp";
        using(var stream=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None)) {
            byte[] bytes=Encoding.UTF8.GetBytes(value);stream.Write(bytes,0,bytes.Length);stream.Flush(true);
        }
        if(File.Exists(path)) {
            bool valid=false;
            try {valid=new FileInfo(path).Length<=MaxBytes && Parse(File.ReadAllText(path))!=null;}catch(Exception){}
            File.Replace(temp,path,valid?path+".bak":null);
        } else File.Move(temp,path);
    }
}
