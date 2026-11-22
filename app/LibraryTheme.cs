using System;
using System.IO;
using System.Text.RegularExpressions;

// Original bundled atmospheres; game cover art remains the user's USB content.
internal sealed class LibraryTheme
{
    internal readonly string Asset, Title, Category, Accent;
    internal readonly int R,G,B;
    LibraryTheme(string asset,string title,string category,string accent,int r,int g,int b) {
        Asset=asset;Title=title;Category=category;Accent=accent;R=r;G=g;B=b;
    }
    internal static LibraryTheme ForGame(string file) {
        string name=Path.GetFileNameWithoutExtension(file??"").Replace('_',' ');
        string key=name.ToLowerInvariant();
        if(key.Contains("vice city"))return new LibraryTheme("vice-city","Grand Theft Auto\nVice City Stories","COASTAL NIGHTS", "#ff849f",255,132,159);
        if(key.Contains("liberty city"))return new LibraryTheme("liberty-city","Grand Theft Auto\nLiberty City Stories","CITY AFTER DARK", "#edba79",237,186,121);
        if(key.Contains("god of war")&&key.Contains("chains of olympus"))return new LibraryTheme("olympus","God of War\nChains of Olympus","MYTHS & LEGENDS", "#e8b26b",232,178,107);
        if(key.Contains("mortal kombat")&&key.Contains("unchained"))return new LibraryTheme("kombat","Mortal Kombat\nUnchained","ENTER THE ARENA", "#f58a67",245,138,103);
        if(key.Contains("tekken 6"))return new LibraryTheme("tekken","Tekken 6","THE NEXT CHALLENGE", "#e989c7",233,137,199);
        if(key.Contains("gran turismo"))return new LibraryTheme("grand-tour","Gran Turismo","PRECISION IN MOTION", "#94d3ed",148,211,237);
        if(key.Contains("most wanted"))return new LibraryTheme("most-wanted","Need for Speed\nMost Wanted 5-1-0","AFTER HOURS", "#d3da83",211,218,131);
        if(key.Contains("shrek the third"))return new LibraryTheme("far-far-away","Shrek the Third","FAR, FAR AWAY", "#a5d996",165,217,150);
        string title=Regex.Replace(name,@"\s*[\(\[].*?[\)\]]","").Trim();
        return new LibraryTheme("vice-city",title.Length==0?"Your PSP collection":title,"READY TO PLAY", "#8ce0e6",140,224,230);
    }
    internal string Bundled(string resources)=>string.IsNullOrEmpty(resources)?null:Path.Combine(resources,"themes",Asset+".png");
    internal static bool UsableFile(string path) {
        try{return File.Exists(path)&&new FileInfo(path).Length>0&&new FileInfo(path).Length<=8*1024*1024;}
        catch(IOException){return false;}catch(UnauthorizedAccessException){return false;}
    }
    internal string Background(string file,string resources) {
        if(!string.IsNullOrEmpty(file)) {
            string stem=Path.Combine(Path.GetDirectoryName(file)??"",Path.GetFileNameWithoutExtension(file));
            foreach(string extension in new[]{".background.jpg",".background.png",".background.jpeg"})
                if(UsableFile(stem+extension))return stem+extension;
        }
        string fallback=Bundled(resources);return UsableFile(fallback)?fallback:null;
    }
}
