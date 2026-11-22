using System;
using System.IO;
using System.Linq;
using System.Text;

internal static class ReportExportChecks
{
    static void Need(bool value,string message){if(!value)throw new Exception(message);}
    static ReportExportException Fails(Action action) {
        try {action();}catch(ReportExportException error){return error;}
        throw new Exception("Export unexpectedly succeeded");
    }
    static void Case(string root,string name,out string data,out string psp) {
        data=Path.Combine(root,name,"data");psp=Path.Combine(root,name,"PSP");
        Directory.CreateDirectory(data);Directory.CreateDirectory(psp);
    }
    static string[] Backups(string data)=>Directory.GetFiles(Path.Combine(data,"diagnostic-exports"),"*.txt");
    static string[] Reports(string psp) {
        string folder=Path.Combine(psp,"TizenPSP-logs");
        return Directory.Exists(folder)?Directory.GetFiles(folder,"*.txt"):new string[0];
    }
    static ReportExportStore Store(Action<ReportWriteStage,string> checkpoint=null,Func<string,string> sync=null,Action<string,string> publish=null)
        =>new ReportExportStore(checkpoint,sync??(_=>null),publish);
    internal static void Run() {
        string root=Path.Combine(Directory.GetCurrentDirectory(),"build","report-export-check-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        byte[] report=Encoding.UTF8.GetBytes("game=Spider-Man 3; UTF8=\u00e9\n"+new string('x',516*1024));
        string data,psp;ReportExportSummary summary;

        Case(root,"write-order",out data,out psp);
        string observedData=data,observedPsp=psp;
        bool sawUsb=false;
        var ordered=Store((stage,path)=>{
            if(!path.StartsWith(observedPsp,StringComparison.Ordinal))return;
            sawUsb=true;
            Need(Backups(observedData).Length==1 && ReportExportStore.Matches(Backups(observedData)[0],report),"USB opened before complete internal backup");
            if(stage==ReportWriteStage.BeforePublish)Need(Reports(observedPsp).Length==0,"Final report appeared before verified publication");
        });
        string final=ordered.Export(data,psp,report,out summary);
        Need(sawUsb && ReportExportStore.Matches(final,report) && summary.Bytes==report.Length && summary.BackupCount==1,"Successful report lost bytes or backup metadata");
        Need(Directory.GetFiles(psp,"*.partial",SearchOption.AllDirectories).Length==0,"Successful export left a partial file");

        foreach(var stageToFail in new[]{ReportWriteStage.AfterWrite,ReportWriteStage.BeforeFlush,ReportWriteStage.BeforePublish}) {
            Case(root,"failure-"+stageToFail,out data,out psp);
            observedPsp=psp;
            var broken=Store((stage,path)=>{
                if(stage==stageToFail && path.StartsWith(observedPsp,StringComparison.Ordinal))throw new IOException("Injected USB write/flush/publication failure");
            });
            string exportData=data,exportPsp=psp;
            var error=Fails(()=>broken.Export(exportData,exportPsp,report,out summary));
            Need(error.BackupRetained && error.BackupCount==1 && !error.BackupLimitReached,"USB failure did not report retained backup");
            Need(Reports(psp).Length==0 && Directory.GetFiles(psp,"*.partial",SearchOption.AllDirectories).Length==0,"Failed USB write published a final report or left its temporary file");
            Need(ReportExportStore.Matches(Backups(data).Single(),report),"USB failure altered the internal snapshot");
        }

        Case(root,"corruption",out data,out psp);observedPsp=psp;
        var corrupt=Store((stage,path)=>{
            if(stage==ReportWriteStage.BeforeVerify && path.StartsWith(observedPsp,StringComparison.Ordinal))File.WriteAllBytes(path,new byte[0]);
        });
        string corruptData=data,corruptPsp=psp;
        var corruption=Fails(()=>corrupt.Export(corruptData,corruptPsp,report,out summary));
        Need(corruption.BackupRetained && Reports(psp).Length==0 && corruption.Message.Contains("read-back"),"Read-back corruption was accepted as success");

        Case(root,"internal-failure",out data,out psp);
        var internalFailure=Store((stage,path)=>{if(stage==ReportWriteStage.BeforeFlush)throw new IOException("Internal storage unavailable");});
        string badData=data,badPsp=psp;
        var internalError=Fails(()=>internalFailure.Export(badData,badPsp,report,out summary));
        Need(!internalError.BackupRetained && internalError.BackupCount==0 && Reports(psp).Length==0,"Internal failure claimed a retained snapshot or wrote USB");

        Case(root,"retry-without-game",out data,out psp);
        for(int game=0;game<3;game++) {
            byte[] gameReport=Encoding.UTF8.GetBytes("previous game "+game);
            string retryData=data;
            var missing=Fails(()=>Store().Export(retryData,null,gameReport,out summary));
            Need(missing.BackupRetained,"Disconnected USB prevented internal backup");
        }
        File.WriteAllText(Path.Combine(data,"session-context.txt"),"game=none");
        byte[] idle=DiagnosticFiles.ComposeReport(data,"No active game");
        final=Store().Export(data,psp,idle,out summary);
        Need(summary.RecoveredReports==3 && summary.BackupCount==4 && Reports(psp).Length==4,"Retry did not recover all earlier games without an active game");
        Need(Enumerable.Range(0,3).All(game=>Reports(psp).Any(path=>File.ReadAllText(path)=="previous game "+game)),"Retry changed an earlier game's snapshot");
        Need(File.ReadAllText(final).Contains("No active game"),"Retry confused current and recovered report paths");

        Case(root,"collision",out data,out psp);
        string original=Store().Export(data,psp,report,out summary);
        File.WriteAllBytes(original,new byte[0]);
        byte[] second=Encoding.UTF8.GetBytes("second report");
        final=Store().Export(data,psp,second,out summary);
        Need(new FileInfo(original).Length==0 && summary.RecoveredReports==1 && Reports(psp).Length==3,"Recovery overwrote the bad USB report or lost its replacement");
        string recovered=Reports(psp).Single(path=>path.Contains("-recovered-"));
        Need(ReportExportStore.Matches(recovered,report) && ReportExportStore.Matches(final,second),"Collision recovery contents are wrong");
        Store().Export(data,psp,Encoding.UTF8.GetBytes("third report"),out summary);
        Need(summary.RecoveredReports==0 && Reports(psp).Length==4,"Retry duplicated an already verified recovery");

        Case(root,"capacity",out data,out psp);
        for(int i=0;i<ReportExportStore.MaxBackups;i++) {
            string fullData=data;byte[] snapshot=Encoding.UTF8.GetBytes("retained "+i);
            var pending=Fails(()=>Store().Export(fullData,null,snapshot,out summary));
            Need(pending.BackupRetained,"Backup capacity filled too early");
        }
        string[] before=Backups(data);string capacityData=data;
        var full=Fails(()=>Store().Export(capacityData,null,report,out summary));
        Need(!full.BackupRetained && full.BackupLimitReached && full.BackupCount==ReportExportStore.MaxBackups && Backups(data).SequenceEqual(before),"Full storage silently evicted an unsent snapshot");
        Store().Export(data,psp,report,out summary);
        Need(summary.BackupCount==ReportExportStore.MaxBackups && summary.RecoveredReports==ReportExportStore.MaxBackups && Reports(psp).Length==ReportExportStore.MaxBackups+1,"Full queue was not recovered before bounded retention resumed");
        Need(before.All(path=>File.Exists(Path.Combine(psp,"TizenPSP-logs",Path.GetFileName(path)))),"Capacity pruning lost a report before recovery");

        Case(root,"directory-sync",out data,out psp);
        string usbDirectory=Path.Combine(psp,"TizenPSP-logs");
        var syncErrorStore=Store(sync:path=>{if(path==usbDirectory)throw new IOException("Injected directory fsync I/O error");return null;});
        string syncData=data,syncPsp=psp;
        var syncError=Fails(()=>syncErrorStore.Export(syncData,syncPsp,report,out summary));
        Need(syncError.BackupRetained && Reports(psp).Length==1 && ReportExportStore.Matches(Reports(psp)[0],report),"Directory sync failure was hidden or damaged a published report");
        Store().Export(data,psp,second,out summary);
        Need(Reports(psp).Length==2 && summary.RecoveredReports==0,"Retry duplicated a complete report after directory sync failure");
        var unsupported=Store(sync:path=>"Directory sync unsupported test warning");
        unsupported.Export(data,psp,second,out summary);
        Need(summary.Warnings=="Directory sync unsupported test warning","Unsupported directory sync did not surface one warning");

        Case(root,"retry-parent-sync",out data,out psp);
        string syncParent=psp;int parentSyncs=0;
        var parentSyncStore=Store(sync:path=>{
            if(path==syncParent && ++parentSyncs==1)throw new IOException("Injected parent-directory sync failure");
            return null;
        });
        string parentData=data,parentPsp=psp;
        var parentError=Fails(()=>parentSyncStore.Export(parentData,parentPsp,report,out summary));
        Need(parentError.BackupRetained && Directory.Exists(Path.Combine(psp,"TizenPSP-logs")),"Parent sync fixture did not leave an existing directory");
        parentSyncStore.Export(data,psp,second,out summary);
        Need(parentSyncs==2 && summary.RecoveredReports==1 && Reports(psp).Length==2,"Existing directory skipped its failed parent sync on retry");
        Case(root,"publish-denied",out data,out psp);
        string deniedData=data,deniedPsp=psp;
        var denial=new UnauthorizedAccessException("Injected publication permission failure");
        var deniedStore=Store(publish:(source,target)=>{
            if(source.StartsWith(deniedPsp,StringComparison.Ordinal))throw denial;
            File.Move(source,target);
        });
        var denied=Fails(()=>deniedStore.Export(deniedData,deniedPsp,report,out summary));
        Need(denied.BackupRetained && Reports(psp).Length==0 && denied.Summary.Operation=="publish-rename" &&
            denied.Summary.OperationPath.StartsWith(psp,StringComparison.Ordinal) &&
            denied.OriginalExceptionType==typeof(UnauthorizedAccessException).FullName && denied.OriginalHResult==denial.HResult &&
            ReferenceEquals(denied.InnerException,denial),"Publication denial lost backup, operation, path or original exception evidence");
        Store().Export(data,psp,second,out summary);
        Need(summary.RecoveredReports==1 && Reports(psp).Length==2,"Denied publication could not recover on retry");

        foreach(int badLength in new[]{0,5}) {
            Case(root,"publish-short-"+badLength,out data,out psp);
            string shortData=data,shortPsp=psp;
            var shortStore=Store(publish:(source,target)=>{
                if(source.StartsWith(shortPsp,StringComparison.Ordinal))File.WriteAllBytes(target,File.ReadAllBytes(source).Take(badLength).ToArray());
                else File.Move(source,target);
            });
            var shortError=Fails(()=>shortStore.Export(shortData,shortPsp,report,out summary));
            Need(shortError.BackupRetained && shortError.Summary.Operation=="verify-final" && new FileInfo(Reports(psp).Single()).Length==badLength,
                "Publisher-created empty/truncated final report was reported as successful");
            Store().Export(data,psp,second,out summary);
            Need(summary.RecoveredReports==1 && Reports(psp).Length==3 && Reports(psp).Any(path=>ReportExportStore.Matches(path,report)),
                "Retry lost a report after corrupt publication");
        }

        Case(root,"publish-full-then-error",out data,out psp);
        string fullErrorData=data,fullErrorPsp=psp;
        var fullErrorStore=Store(publish:(source,target)=>{
            if(source.StartsWith(fullErrorPsp,StringComparison.Ordinal)) {
                File.WriteAllBytes(target,File.ReadAllBytes(source));
                throw new UnauthorizedAccessException("Injected metadata failure after copying full bytes");
            }
            File.Move(source,target);
        });
        var fullError=Fails(()=>fullErrorStore.Export(fullErrorData,fullErrorPsp,report,out summary));
        Need(fullError.BackupRetained && fullError.Summary.Operation=="publish-rename" && ReportExportStore.Matches(Reports(psp).Single(),report),
            "Full bytes incorrectly hid a failed publication operation");
        int existingFlushes=0;
        Store((stage,path)=>{if(stage==ReportWriteStage.BeforeExistingFlush)existingFlushes++;}).Export(data,psp,second,out summary);
        Need(existingFlushes==1 && summary.RecoveredReports==0 && Reports(psp).Length==2,"Complete prior output was not flushed independently or was duplicated on retry");

        Case(root,"guard-existing-target",out data,out psp);
        string guardData=data,guardPsp=psp;bool calledUsbPublisher=false;string occupied=null;
        var guarded=Store((stage,path)=>{
            if(stage==ReportWriteStage.BeforePublish && path.StartsWith(guardPsp,StringComparison.Ordinal)) {
                occupied=Path.Combine(guardPsp,"TizenPSP-logs",Path.GetFileName(Backups(guardData).Single()));
                File.WriteAllText(occupied,"existing user report");
            }
        },publish:(source,target)=>{
            if(source.StartsWith(guardPsp,StringComparison.Ordinal))calledUsbPublisher=true;
            File.Move(source,target);
        });
        var guardError=Fails(()=>guarded.Export(guardData,guardPsp,report,out summary));
        Need(guardError.BackupRetained && guardError.Summary.Operation=="inspect-destination" && !calledUsbPublisher && File.ReadAllText(occupied)=="existing user report",
            "Publication guard overwrote an existing destination");

        Case(root,"post-sync-corruption",out data,out psp);
        string postSyncData=data,postSyncPsp=psp,postSyncDirectory=Path.Combine(psp,"TizenPSP-logs");
        var postSyncStore=Store(sync:path=>{
            if(path==postSyncDirectory)File.WriteAllBytes(Reports(postSyncPsp).Single(),new byte[0]);
            return null;
        });
        var postSyncError=Fails(()=>postSyncStore.Export(postSyncData,postSyncPsp,report,out summary));
        Need(postSyncError.BackupRetained && postSyncError.Summary.Operation=="verify-final-after-sync",
            "Final-file corruption during directory sync escaped verification");

        Case(root,"unreadable-old-usb",out data,out psp);
        string unreadable=Store().Export(data,psp,report,out summary);
        string oldRecovery=Path.Combine(Path.GetDirectoryName(unreadable),Path.GetFileNameWithoutExtension(unreadable)+"-recovered-old.txt");
        File.WriteAllBytes(oldRecovery,report);
        var unreadableStore=Store((stage,path)=>{
            if(stage==ReportWriteStage.BeforeExistingVerify && (path==unreadable || path==oldRecovery))
                throw new UnauthorizedAccessException("Injected corrupt old USB entry");
        });
        unreadableStore.Export(data,psp,second,out summary);
        Need(summary.RecoveredReports==1 && summary.Warnings.Contains("could not be read") && Reports(psp).Length==4 &&
            ReportExportStore.Matches(unreadable,report) && ReportExportStore.Matches(oldRecovery,report),
            "Unreadable old USB entries blocked recovery or were overwritten");
        Case(root,"reused-file-flush-failure",out data,out psp);
        string unsynced=Store().Export(data,psp,report,out summary);
        var failedExistingFlush=Store((stage,path)=>{
            if(stage==ReportWriteStage.BeforeExistingFlush && path==unsynced)throw new UnauthorizedAccessException("Injected old inode flush denial");
        });
        failedExistingFlush.Export(data,psp,second,out summary);
        Need(summary.RecoveredReports==1 && Reports(psp).Length==3 && summary.Warnings.Contains("or synced") &&
            ReportExportStore.Matches(unsynced,report) && Reports(psp).Any(path=>path!=unsynced && ReportExportStore.Matches(path,report)),
            "Unsynced matching old file was accepted without a fresh verified recovery");

        Case(root,"capacity-unsynced-recovery-failure",out data,out psp);
        for(int i=0;i<ReportExportStore.MaxBackups;i++)Store().Export(data,psp,Encoding.UTF8.GetBytes("saved "+i),out summary);
        string[] beforeFlushFailure=Backups(data);string capFlushData=data,capFlushPsp=psp;
        var noDurableCopy=Store((stage,path)=>{
            if(stage==ReportWriteStage.BeforeExistingFlush)throw new UnauthorizedAccessException("Injected old inode flush denial");
        },publish:(source,target)=>{
            if(source.StartsWith(capFlushPsp,StringComparison.Ordinal))throw new IOException("Injected replacement publication failure");
            File.Move(source,target);
        });
        var capFlushError=Fails(()=>noDurableCopy.Export(capFlushData,capFlushPsp,report,out summary));
        Need(capFlushError.BackupLimitReached && !capFlushError.BackupRetained && beforeFlushFailure.SequenceEqual(Backups(data)),
            "Capacity cleanup discarded retained reports without a synced old or newly verified USB copy");
        Case(root,"denied-new-target-probe",out data,out psp);
        string probeData=data,probePsp=psp;
        var probeStore=Store((stage,path)=>{
            if(stage==ReportWriteStage.BeforeDestinationProbe && path.StartsWith(probePsp,StringComparison.Ordinal))
                throw new UnauthorizedAccessException("Injected destination metadata denial");
        });
        var probeError=Fails(()=>probeStore.Export(probeData,probePsp,report,out summary));
        Need(probeError.BackupRetained && probeError.Summary.Operation=="inspect-destination" && Reports(psp).Length==0,
            "Failed destination lookup was treated as proof that the name is absent");

        Case(root,"unreadable-old-metadata",out data,out psp);
        string metadataDenied=Store().Export(data,psp,report,out summary);
        var metadataStore=Store((stage,path)=>{
            if(stage==ReportWriteStage.BeforeExistingProbe && path==metadataDenied)throw new IOException("Injected corrupt entry metadata");
        });
        metadataStore.Export(data,psp,second,out summary);
        Need(summary.RecoveredReports==1 && summary.Warnings.Contains("could not be inspected") && Reports(psp).Length==3 && ReportExportStore.Matches(metadataDenied,report),
            "Unreadable old metadata was treated as an absent file and overwritten");
        Console.WriteLine("PASS: report backup/retention, verified USB publication, denied/partial/late publication errors, strict collision probes, final verification before/after sync, reused-inode flushing/recovery and operation/exception evidence.");
    }
}
