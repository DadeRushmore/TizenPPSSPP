using System;
using System.Xml;

// Embed the same manifest used by the package: UI and diagnostic labels must agree.
internal static class AppVersion
{
    internal static readonly string Current=Read();
    static string Read() {
        using(var stream=typeof(AppVersion).Assembly.GetManifestResourceStream("TizenPSP.Manifest"))
        using(var reader=XmlReader.Create(stream)) {
            reader.MoveToContent();
            string version=reader.GetAttribute("version");
            if(string.IsNullOrEmpty(version))throw new InvalidOperationException("Package manifest has no version");
            return version;
        }
    }
}
