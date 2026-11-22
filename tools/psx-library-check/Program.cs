using System;using System.IO;using System.Linq;using System.Security.Cryptography;using System.Text.Json;
static class Native {internal static string Hash(byte[] bytes){using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}}
class Check {
 static void Main(string[] args){
  if(args.Length<1)throw new Exception("Pass the prepared PSX folder");
  var scan=GameLibrary.ScanDetailed(args[0],()=>false);if(!scan.Complete)throw new Exception("Incomplete library scan");
  var states=GameLibrary.InspectFiles(scan.Files,()=>false);
  var result=new {complete=scan.Complete,launchableDiscs=scan.Files.Count,suppressedTracks=scan.SuppressedFiles.Count,withCovers=states.Count(s=>s.Value.CoverPath.Length>0),missingCovers=states.Where(s=>s.Value.CoverPath.Length==0).Select(s=>s.Key).ToArray(),games=states.Select(s=>new{path=s.Key,cover=s.Value.CoverPath}).ToArray()};
  string json=JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true});
  if(args.Length>1)File.WriteAllText(args[1],json);
  Console.WriteLine("Actual TizenPSX scanner: "+scan.Files.Count+" launchable discs, "+scan.SuppressedFiles.Count+" tracks suppressed, "+result.withCovers+" covers");
 }
}
