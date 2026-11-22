using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

internal sealed class ReportExportSummary
{
    internal string Path,BackupPath;
    internal string Operation="capture",OperationPath="";
    internal long Bytes;
    internal int RecoveredReports,BackupCount;
    internal string Warnings="";
}

internal sealed class ReportExportException : IOException
{
    internal readonly bool BackupRetained,BackupLimitReached;
    internal readonly int BackupCount;
    internal readonly string OriginalExceptionType;
    internal readonly int OriginalHResult;
    internal readonly ReportExportSummary Summary;
    internal ReportExportException(Exception cause,ReportExportSummary summary,bool retained,bool full)
        :base(summary.Operation+" ["+summary.OperationPath+"]: "+cause.Message,cause) {
        OriginalExceptionType=cause.GetType().FullName;OriginalHResult=cause.HResult;
        Summary=summary;BackupRetained=retained;BackupCount=summary.BackupCount;BackupLimitReached=full;
    }
}

internal enum ReportWriteStage { AfterWrite, BeforeFlush, BeforeVerify, BeforePublish, AfterPublish, BeforeExistingVerify, BeforeExistingFlush, BeforeExistingProbe, BeforeDestinationProbe }

// Internal snapshots survive an unplugged/full USB drive and can be retried later.
internal sealed class ReportExportStore
{
    internal const int MaxBackups=8;
    readonly Action<ReportWriteStage,string> checkpoint;
    readonly Func<string,string> syncDirectory;
    readonly Action<string,string> publishFile;
    readonly string usbFolderName;
    static readonly object publishGate=new object();
    internal ReportExportStore(Action<ReportWriteStage,string> checkpoint=null,Func<string,string> syncDirectory=null,Action<string,string> publishFile=null,string usbFolderName="TizenPSP-logs") {
        if(string.IsNullOrWhiteSpace(usbFolderName) || usbFolderName=="." || usbFolderName==".." || usbFolderName.IndexOfAny(new[]{'/', '\\', ':'})>=0)
            throw new ArgumentException("Expected a single export folder name",nameof(usbFolderName));
        this.usbFolderName=usbFolderName;
        this.checkpoint=checkpoint;this.syncDirectory=syncDirectory??SyncDirectory;this.publishFile=publishFile??Rename;
    }
    [DllImport("libc.so.6",SetLastError=true)]static extern int open(string path,int flags);
    [DllImport("libc.so.6",SetLastError=true)]static extern int fsync(int fd);
    [DllImport("libc.so.6",SetLastError=true)]static extern int close(int fd);
    [DllImport("libc.so.6",SetLastError=true)]static extern int rename(string source,string destination);
    static void Rename(string source,string destination) {
        if(RuntimeInformation.IsOSPlatform(OSPlatform.Windows)){File.Move(source,destination);return;}
        // Older Unix File.Move can link/copy, including unsupported metadata changes.
        // Rename preserves the inode whose bytes were already flushed and verified.
        if(rename(source,destination)!=0)throw new IOException("Native export rename failed (errno "+Marshal.GetLastWin32Error()+")");
    }
    static bool PathExistsStrict(string path) {
        try {File.GetAttributes(path);return true;}
        catch(FileNotFoundException){return false;}
        catch(DirectoryNotFoundException){return false;}
    }
    void PublishStaged(string source,string destination,ReportExportSummary summary) {
        lock(publishGate) {
            if(System.IO.Path.GetDirectoryName(source)!=System.IO.Path.GetDirectoryName(destination))
                throw new IOException("Export publication must stay in the same directory");
            Operation(summary,"inspect-destination",destination);
            checkpoint?.Invoke(ReportWriteStage.BeforeDestinationProbe,destination);
            if(PathExistsStrict(destination))throw new IOException("Export destination already exists; refusing to replace it");
            // Names include a new GUID and this gate excludes competing app publishers.
            // POSIX rename itself cannot exclude an external writer racing this check.
            Operation(summary,"publish-rename",destination);
            publishFile(source,destination);
        }
    }
    static void Operation(ReportExportSummary summary,string operation,string path) {
        summary.Operation=operation;summary.OperationPath=path??"(none)";
    }
    void Sync(string directory,ReportExportSummary summary) {
        Operation(summary,"directory-sync",directory);Warning(summary,syncDirectory(directory));
    }
    static void VerifyFinal(string path,byte[] bytes,ReportExportSummary summary,string operation) {
        Operation(summary,operation,path);
        if(!Matches(path,bytes))throw new IOException("Published diagnostic report read-back did not match");
    }
    void SyncAndVerifyFinal(string path,byte[] bytes,ReportExportSummary summary) {
        Sync(System.IO.Path.GetDirectoryName(path),summary);
        VerifyFinal(path,bytes,summary,"verify-final-after-sync");
    }
    static string SyncDirectory(string path) {
        if(RuntimeInformation.IsOSPlatform(OSPlatform.Windows))return null;
        // O_RDONLY is zero. No architecture-specific O_DIRECTORY value is needed.
        int fd=open(path,0);
        if(fd<0)throw new IOException("Cannot open export directory for sync (errno "+Marshal.GetLastWin32Error()+")");
        try {
            if(fsync(fd)==0)return null;
            int error=Marshal.GetLastWin32Error();
            if(error==22 || error==95)return "Directory sync is unsupported; safely eject USB before removal.";
            throw new IOException("Export directory sync failed (errno "+error+")");
        } finally {close(fd);}
    }
    static string[] Backups(string folder)=>Directory.GetFiles(folder,"report-*.txt").OrderBy(x=>x,StringComparer.Ordinal).ToArray();
    static void Warning(ReportExportSummary summary,string warning) {
        if(!string.IsNullOrEmpty(warning) && !summary.Warnings.Contains(warning))
            summary.Warnings+=(summary.Warnings.Length==0?"":" ")+warning;
    }
    void EnsureDirectory(string path,ReportExportSummary summary) {
        Operation(summary,"create-directory",path);
        if(!Directory.Exists(path))Directory.CreateDirectory(path);
        // Retry the parent's sync even when a prior attempt created the folder
        // and then failed to persist its directory entry.
        string parent=System.IO.Path.GetDirectoryName(path);
        if(!string.IsNullOrEmpty(parent))Sync(parent,summary);
    }
    internal static bool Matches(string path,byte[] expected) {
        try {
            using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)) {
                if(stream.Length!=expected.LongLength)return false;
                byte[] chunk=new byte[8192];int offset=0;
                while(offset<expected.Length) {
                    int count=stream.Read(chunk,0,Math.Min(chunk.Length,expected.Length-offset));
                    if(count==0)return false;
                    for(int i=0;i<count;i++)if(chunk[i]!=expected[offset+i])return false;
                    offset+=count;
                }
                return true;
            }
        } catch(FileNotFoundException){return false;}catch(DirectoryNotFoundException){return false;}
    }
    void WriteVerified(string final,byte[] bytes,ReportExportSummary summary) {
        if(bytes.Length==0)throw new IOException("Refusing an empty diagnostic report");
        string temporary=final+"."+Guid.NewGuid().ToString("N")+".partial";
        try {
            Operation(summary,"create-temporary",temporary);
            using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {
                Operation(summary,"write-temporary",temporary);
                stream.Write(bytes,0,bytes.Length);
                checkpoint?.Invoke(ReportWriteStage.AfterWrite,temporary);
                Operation(summary,"flush-temporary",temporary);
                checkpoint?.Invoke(ReportWriteStage.BeforeFlush,temporary);
                stream.Flush(true);
                Operation(summary,"close-temporary",temporary);
            }
            Operation(summary,"verify-temporary",temporary);
            checkpoint?.Invoke(ReportWriteStage.BeforeVerify,temporary);
            if(!Matches(temporary,bytes))throw new IOException("Diagnostic report read-back did not match; final copy was not published");
            Operation(summary,"publish-rename",final);
            checkpoint?.Invoke(ReportWriteStage.BeforePublish,temporary);
            PublishStaged(temporary,final,summary);
            Operation(summary,"verify-final",final);
            checkpoint?.Invoke(ReportWriteStage.AfterPublish,final);
            VerifyFinal(final,bytes,summary,"verify-final");
            SyncAndVerifyFinal(final,bytes,summary);
        } finally {
            // Remove only this operation's unpublished file, preserving every final report.
            // USB failures retain the complete internal snapshot; avoid accumulating partials.
            try {if(File.Exists(temporary))File.Delete(temporary);} catch { }
        }
    }
    string UsbDirectory(string pspFolder,ReportExportSummary summary) {
        Operation(summary,"locate-usb",pspFolder);
        if(string.IsNullOrEmpty(pspFolder) || !Directory.Exists(pspFolder))throw new IOException("No scanned USB game folder available; saved reports are retained in the app");
        string directory=System.IO.Path.Combine(pspFolder,usbFolderName);
        EnsureDirectory(directory,summary);return directory;
    }
    bool ExistingEntry(string path,ReportExportSummary summary,out bool accessible) {
        Operation(summary,"inspect-existing",path);accessible=true;
        try {
            checkpoint?.Invoke(ReportWriteStage.BeforeExistingProbe,path);
            return PathExistsStrict(path);
        } catch(Exception error) when(error is IOException || error is UnauthorizedAccessException) {
            // Unlike File.Exists, a failed lookup must not be treated as absence.
            accessible=false;
            Warning(summary,"Existing USB entry could not be inspected; preserved "+System.IO.Path.GetFileName(path)+" ("+error.GetType().Name+").");
            return true;
        }
    }
    bool ExistingMatches(string path,byte[] bytes,ReportExportSummary summary) {
        Operation(summary,"verify-existing",path);
        try {
            checkpoint?.Invoke(ReportWriteStage.BeforeExistingVerify,path);
            if(!Matches(path,bytes))return false;
            // v0.23.2 File.Move could copy to a new inode and then throw. Flush
            // that final inode too before accepting it or pruning its backup.
            Operation(summary,"flush-existing",path);
            checkpoint?.Invoke(ReportWriteStage.BeforeExistingFlush,path);
            using(var stream=new FileStream(path,FileMode.Open,FileAccess.Write,FileShare.Read))stream.Flush(true);
            return true;
        } catch(Exception error) when(error is IOException || error is UnauthorizedAccessException) {
            // A corrupt old USB entry must not prevent a new independently verified copy.
            // Keep strict failures for the staged/new file and internal backup reads.
            Warning(summary,"Existing USB report could not be read or synced; preserved "+System.IO.Path.GetFileName(path)+" ("+error.GetType().Name+").");
            return false;
        }
    }
    string Publish(string backup,string directory,ReportExportSummary summary,out bool copied) {
        Operation(summary,"read-backup",backup);
        byte[] bytes=File.ReadAllBytes(backup);
        if(bytes.Length==0)throw new IOException("Saved diagnostic snapshot is empty: "+System.IO.Path.GetFileName(backup));
        string target=System.IO.Path.Combine(directory,System.IO.Path.GetFileName(backup));
        bool accessible;bool exists=ExistingEntry(target,summary,out accessible);
        if(exists && accessible && ExistingMatches(target,bytes,summary)) {copied=false;SyncAndVerifyFinal(target,bytes,summary);return target;}
        if(exists) {
            string stem=System.IO.Path.GetFileNameWithoutExtension(backup)+"-recovered-";
            Operation(summary,"list-recoveries",directory);
            foreach(string candidate in Directory.GetFiles(directory,stem+"*.txt")) {
                Operation(summary,"verify-existing",candidate);
                if(ExistingMatches(candidate,bytes,summary)){copied=false;SyncAndVerifyFinal(candidate,bytes,summary);return candidate;}
            }
            target=System.IO.Path.Combine(directory,stem+Guid.NewGuid().ToString("N")+".txt");
        }
        WriteVerified(target,bytes,summary);copied=true;return target;
    }
    void PublishAll(string[] backups,string directory,string current,ReportExportSummary summary) {
        foreach(string backup in backups) {
            bool copied;string target=Publish(backup,directory,summary,out copied);
            if(backup==current)summary.Path=target;
            else if(copied)summary.RecoveredReports++;
        }
    }
    internal string Export(string data,string pspFolder,byte[] report,out ReportExportSummary summary) {
        if(report==null)throw new ArgumentNullException(nameof(report));
        summary=new ReportExportSummary{Bytes=report.LongLength};
        string saved=System.IO.Path.Combine(data,"diagnostic-exports");
        try {
            EnsureDirectory(saved,summary);
            Operation(summary,"list-backups",saved);
            string[] existing=Backups(saved);summary.BackupCount=existing.Length;
            string usb=null;
            if(existing.Length>=MaxBackups) {
                // Never evict an unsent snapshot merely to make room for a new one.
                usb=UsbDirectory(pspFolder,summary);
                PublishAll(existing,usb,null,summary);
                foreach(string old in existing.Take(existing.Length-MaxBackups+1)) {
                    Operation(summary,"prune-exported-backup",old);File.Delete(old);
                }
                Sync(saved,summary);
            }
            string name="report-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+"-"+Guid.NewGuid().ToString("N")+".txt";
            summary.BackupPath=System.IO.Path.Combine(saved,name);
            WriteVerified(summary.BackupPath,report,summary);
            Operation(summary,"list-backups",saved);
            existing=Backups(saved);summary.BackupCount=existing.Length;
            if(usb==null)usb=UsbDirectory(pspFolder,summary);
            PublishAll(existing,usb,summary.BackupPath,summary);
            return summary.Path;
        } catch(Exception error) {
            bool retained=false;
            try {
                summary.BackupCount=Directory.Exists(saved)?Backups(saved).Length:0;
                retained=summary.BackupPath!=null && Matches(summary.BackupPath,report);
            } catch { }
            throw new ReportExportException(error,summary,retained,summary.BackupPath==null && summary.BackupCount>=MaxBackups);
        }
    }
}
