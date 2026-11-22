using System;
internal static class UsbGameChecks {
    internal static void Run() {
        if(UsbGames.UnescapeMount(@"/media/My\040USB/PSP")!="/media/My USB/PSP")throw new Exception("Mount escaping failed");
        foreach(var file in new[]{"game.ISO","game.cso","EBOOT.PBP","demo.elf"})
            if(!UsbGames.Supported(file))throw new Exception("Content extension rejected");
        foreach(var file in new[]{"game.7z","game.iso.zip","game.zip","README"})
            if(UsbGames.Supported(file))throw new Exception("Archive/non-content accepted");
        Console.WriteLine("PASS: USB mount escaping and content/archive filtering");
    }
}
