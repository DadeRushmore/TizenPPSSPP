using System;
using System.IO;
using System.Linq;

internal static class LibraryChecks
{
    static void Need(bool value,string message){if(!value)throw new Exception(message);}
    static void Reject(Action action) {
        try{action();}catch(InvalidDataException){return;}
        throw new Exception("Malformed library preferences accepted");
    }
    static void CheckCoreOptions() {
        string[] unaffected={"ppsspp_cpu_core","ppsspp_fast_memory","ppsspp_ignore_bad_memory_access","ppsspp_backend",
            "ppsspp_internal_resolution","ppsspp_inflight_frames","ppsspp_software_rendering","ppsspp_cropto16x9",
            "ppsspp_auto_frameskip","ppsspp_detect_vsync_swap_interval","ppsspp_frameskip","ppsspp_frameskiptype",
            "ppsspp_frame_duplication","ppsspp_skip_gpu_readbacks","SimulateBlockTransferEffects","unknown"};
        foreach(bool jit in new[]{false,true})foreach(bool fast in new[]{false,true})foreach(int resolution in new[]{1,2})foreach(int frameSkip in new[]{0,1}) {
            string normal,skip;
            Need(CoreOptionPolicy.TryOverride("ppsspp_skip_buffer_effects",jit,fast,resolution,frameSkip,false,out normal) && normal=="disabled" &&
                CoreOptionPolicy.TryOverride("ppsspp_skip_buffer_effects",jit,fast,resolution,frameSkip,true,out skip) && skip==(jit?"enabled":"disabled"),"Effects setting did not respect fixed ARM / preserved interpreter capabilities");
            foreach(string option in unaffected) {
                bool normalFound=CoreOptionPolicy.TryOverride(option,jit,fast,resolution,frameSkip,false,out normal);
                bool skipFound=CoreOptionPolicy.TryOverride(option,jit,fast,resolution,frameSkip,true,out skip);
                Need(normalFound==skipFound && normal==skip,"Effects toggle changed another core setting: "+option);
            }
        }
        string value;
        Need(CoreOptionPolicy.TryOverride("ppsspp_ignore_bad_memory_access",true,false,1,0,false,out value) && value=="disabled","Checked ARM memory containment changed");
        Need(!CoreOptionPolicy.TryOverride("ppsspp_ignore_bad_memory_access",true,true,1,0,false,out value),"Fast-memory error policy changed");
        Need(!CoreOptionPolicy.TryOverride("ppsspp_fast_memory",false,false,1,0,false,out value),"Interpreter fast-memory default changed");
        Need(!CoreOptionPolicy.TryOverride("ppsspp_skip_gpu_readbacks",true,false,1,0,true,out value),"Buffer experiment also changed readbacks");
        Need(!CoreOptionPolicy.TryOverride("SimulateBlockTransferEffects",true,false,1,0,true,out value),"Obsolete graphics option introduced");
    }
    internal static void Run() {
        var alpha=new LibraryItem("/usb/a/PSP/Alpha.iso",100);
        var alphaMoved=new LibraryItem("/usb/b/PSP/Alpha.iso",100);
        var beta=new LibraryItem("/usb/a/PSP/Beta.cso",200);
        var unicode=new LibraryItem("/usb/a/PSP/日本語_&_game.pbp",300);
        var all=new[]{beta,unicode,alpha};var catalog=new LibraryCatalog();
        Need(catalog.Get(alpha).UseArmJit && catalog.Get(alpha).Cpu==GameCpuChoice.Default,"New games must default to ARM JIT");
        Need(catalog.Get(alpha).ResolutionScale==1 && catalog.Get(alpha).FrameSkip==0 && !catalog.Get(alpha).SkipBufferEffects,"Default graphics must retain native size and full frames");
        Need(alpha.Key==alphaMoved.Key && alpha.Key!=new LibraryItem(alpha.Path,101).Key,"USB remount or different-file identity");
        Need(catalog.Select(all,LibraryFilter.All).First()==alpha,"Alphabetical library order");
        Need(catalog.Select(all,LibraryFilter.Favorites).Length==0 && catalog.Select(all,LibraryFilter.Recent).Length==0,"Fresh filters should be empty");
        catalog.ToggleFavorite(alpha);catalog.SetAudio(alpha,60);catalog.SetAudio(beta,0);
        catalog.SetCpu(alpha,GameCpuChoice.Interpreter);catalog.SetResolution(alpha,2);catalog.SetFrameSkip(alpha,1);
        catalog.SetCpu(beta,GameCpuChoice.ArmJit);catalog.SetSkipBufferEffects(alpha,true);
        var first=new DateTime(2026,11,9,12,0,0,DateTimeKind.Utc);
        catalog.RecordStarted(alpha,first);catalog.RecordStarted(beta,first.AddMinutes(1));
        Need(catalog.Get(alphaMoved).Favorite && catalog.Get(alphaMoved).AudioBuffer==60,"Remount lost preferences");
        Need(!catalog.Get(alphaMoved).UseArmJit && catalog.Get(alphaMoved).ResolutionScale==2 && catalog.Get(alphaMoved).FrameSkip==1 && catalog.Get(alphaMoved).SkipBufferEffects,"Remount lost CPU/graphics overrides");
        Need(catalog.Get(unicode).AudioBuffer==-1,"New game did not inherit default audio");
        Need(catalog.Select(all,LibraryFilter.Recent).Select(x=>x.Title).SequenceEqual(new[]{"Beta","Alpha"}),"Recent ordering or unplayed entry");
        Need(catalog.Select(new[]{beta},LibraryFilter.Favorites).Length==0,"Disconnected game was included");
        catalog.Sort=LibrarySort.TitleDescending;
        Need(catalog.Select(all,LibraryFilter.All).Last()==alpha,"Descending title order");
        Need(catalog.Select(all,LibraryFilter.Recent).First()==beta,"Recent filter must ignore title sorting");
        string original=catalog.Serialize();catalog=LibraryCatalog.Parse(original);
        Need(catalog.Serialize()==original,"Library persistence round trip");
        Need(!original.Contains(alpha.Path),"Preferences contain a remount-dependent USB path");
        Need(catalog.Get(alpha).Cpu==GameCpuChoice.Interpreter && catalog.Get(alpha).ResolutionScale==2 && catalog.Get(alpha).FrameSkip==1 && catalog.Get(beta).UseArmJit && catalog.Get(alpha).SkipBufferEffects && !catalog.Get(beta).SkipBufferEffects,"Saved CPU/graphics override round trip");
        string legacy="TizenPSP-library-v1\t2\n"+alpha.Key+"\t1\t"+first.Ticks+"\t60\n";
        var migrated=LibraryCatalog.Parse(legacy);var oldPreference=migrated.Get(alpha);
        Need(migrated.Sort==LibrarySort.LastPlayed && oldPreference.Favorite && oldPreference.AudioBuffer==60 && oldPreference.LastPlayedUtcTicks==first.Ticks,"v1 migration lost existing preferences");
        Need(oldPreference.UseArmJit && oldPreference.ResolutionScale==1 && oldPreference.FrameSkip==0 && !oldPreference.SkipBufferEffects,"v1 migration must use ARM/native/full/normal-effects defaults");
        Need(migrated.Serialize().StartsWith("TizenPSP-library-v3\t"),"Migration must write current schema");
        migrated.SetCpu(alpha,GameCpuChoice.Default);Need(migrated.Get(alpha).UseArmJit,"Reset CPU to default ARM JIT");
        string v2="TizenPSP-library-v2\t1\n"+alpha.Key+"\t1\t"+first.Ticks+"\t180\t2\t2\t1\n";
        var v2Migrated=LibraryCatalog.Parse(v2);var v2Preference=v2Migrated.Get(alphaMoved);
        Need(v2Migrated.Sort==LibrarySort.TitleDescending && v2Preference.Favorite && v2Preference.LastPlayedUtcTicks==first.Ticks &&
            v2Preference.AudioBuffer==180 && v2Preference.Cpu==GameCpuChoice.Interpreter && v2Preference.ResolutionScale==2 &&
            v2Preference.FrameSkip==1 && !v2Preference.SkipBufferEffects,"v2 migration changed saved settings or enabled effects skipping");
        v2Migrated.SetSkipBufferEffects(alpha,true);v2Migrated.SetSkipBufferEffects(alpha,false);
        Need(!LibraryCatalog.Parse(v2Migrated.Serialize()).Get(alpha).SkipBufferEffects,"Restoring normal effects did not persist");
        Reject(()=>LibraryCatalog.Parse("TizenPSP-library-v4\t0\n"));
        Reject(()=>LibraryCatalog.Parse(original+original.Split('\n')[1]+"\n"));
        Reject(()=>LibraryCatalog.Parse("TizenPSP-library-v1\t0\n"+alpha.Key+"\t1\t-1\t120\n"));
        Reject(()=>LibraryCatalog.Parse("TizenPSP-library-v1\t0\n"+alpha.Key+"\t1\t0\t999\n"));
        foreach(string settings in new[]{"3\t1\t0","0\t0\t0","0\t3\t0","0\t1\t2","-1\t1\t0","0\t1"})
            Reject(()=>LibraryCatalog.Parse("TizenPSP-library-v2\t0\n"+alpha.Key+"\t1\t0\t60\t"+settings+"\n"));
        foreach(string effects in new[]{"2","-1","true","","01"})
            Reject(()=>LibraryCatalog.Parse("TizenPSP-library-v3\t0\n"+alpha.Key+"\t1\t0\t60\t0\t1\t0\t"+effects+"\n"));
        Reject(()=>LibraryCatalog.Parse("TizenPSP-library-v3\t0\n"+alpha.Key+"\t1\t0\t60\t0\t1\t0\n"));
        Reject(()=>LibraryCatalog.Parse(new string('x',1024*1024+1)));
        string directory=Path.Combine(Directory.GetCurrentDirectory(),"build","library-check-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);string path=Path.Combine(directory,"library.tsv");
        catalog.Save(path);catalog.ToggleFavorite(beta);catalog.Save(path);
        Need(File.ReadAllText(path+".bak")==original,"Atomic replacement did not preserve previous version");
        File.WriteAllText(path,"partial write");
        var logs=new System.Collections.Generic.List<string>();catalog=LibraryCatalog.Load(path,logs.Add);
        Need(catalog.Serialize()==original && logs.Any(x=>x.Contains("RECOVERED")),"Backup recovery failed");
        catalog.Save(path);Need(File.ReadAllText(path)==original && File.ReadAllText(path+".bak")==original,"Repair destroyed good backup");
        File.WriteAllText(path,"invalid");File.WriteAllText(path+".bak","invalid");
        Need(LibraryCatalog.Load(path,_=>{}).Select(all,LibraryFilter.Favorites).Length==0,"Corrupt settings did not use defaults");
        var fast=new FastMemoryPreferences();Need(!fast.Enabled(alpha),"Fast memory must be opt-in");
        fast.Set(alpha,true);Need(fast.Enabled(alphaMoved),"Fast memory remount identity");
        var fastCopy=FastMemoryPreferences.Parse(fast.Serialize());Need(fastCopy.Enabled(alpha)&&!fastCopy.Enabled(beta),"Fast memory per-game round trip");
        string fastPath=Path.Combine(directory,"fast-memory.txt");fast.Save(fastPath);
        Need(FastMemoryPreferences.Load(fastPath,logs.Add).Enabled(alpha),"Fast memory save/load");
        fast.Set(alpha,false);fast.Save(fastPath);Need(!FastMemoryPreferences.Load(fastPath,logs.Add).Enabled(alpha),"Checked mode save/load");
        File.WriteAllText(fastPath,"partial write");Need(!FastMemoryPreferences.Load(fastPath,logs.Add).Enabled(alpha),"Corrupt fast settings must use checked memory");
        Need(catalog.Serialize()==original,"Separate fast-memory settings changed legacy library data");
        CheckCoreOptions();
        Console.WriteLine("PASS: v1/v2 migration, opt-in per-game buffer effects, production core-option mapping, default ARM JIT, saved CPU/resolution/frameskip overrides, favorites/audio/history, remount identity, malformed settings, atomic persistence, backup recovery and separate opt-in fast memory.");
    }
}
