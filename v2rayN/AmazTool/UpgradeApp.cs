using System.Diagnostics;
using System.IO.Compression;
using System.Text;

namespace AmazTool;

internal class UpgradeApp
{
    public static void Upgrade(string fileName)
    {
        Console.WriteLine($"{Resx.Resource.StartUnzipping}\n{fileName}");

        Utils.Waiting(5);

        if (!File.Exists(fileName))
        {
            Console.WriteLine(Resx.Resource.UpgradeFileNotFound);
            return;
        }

        Console.WriteLine(Resx.Resource.TryTerminateProcess);
        try
        {
            var existing = Process.GetProcessesByName(Utils.V2rayN);
            foreach (var pp in existing)
            {
                var path = pp.MainModule?.FileName ?? "";
                if (path.StartsWith(Utils.GetPath(Utils.V2rayN)))
                {
                    pp?.Kill();
                    pp?.WaitForExit(1000);
                }
            }
        }
        catch (Exception ex)
        {
            // Access may be denied without admin right. The user may not be an administrator.
            Console.WriteLine(Resx.Resource.FailedTerminateProcess + ex.StackTrace);
        }

        Console.WriteLine(Resx.Resource.StartUnzipping);
        StringBuilder sb = new();
        try
        {
            var thisAppOldFile = $"{Utils.GetExePath()}.tmp";
            File.Delete(thisAppOldFile);
            var splitKey = "/";

            using var archive = ZipFile.OpenRead(fileName);
            // NiN packages keep their files at the zip root. Upstream zips them
            // inside a version folder and unconditionally dropped each entry's
            // first segment, which for a flat zip skipped the executables and
            // relocated the bin folders -- the update replaced nothing and the
            // app restarted on the old version. Only strip a segment when the
            // whole zip really is wrapped in one top-level folder.
            var wrapper = FindWrapperFolder(archive.Entries);
            foreach (var entry in archive.Entries)
            {
                try
                {
                    if (entry.Length == 0)
                    {
                        continue;
                    }

                    Console.WriteLine(entry.FullName);

                    var fullName = wrapper.Length > 0
                        ? entry.FullName[(wrapper.Length + splitKey.Length)..]
                        : entry.FullName;

                    if (string.Equals(Utils.GetExePath(), Utils.GetPath(fullName), StringComparison.OrdinalIgnoreCase))
                    {
                        File.Move(Utils.GetExePath(), thisAppOldFile);
                    }

                    var entryOutputPath = Utils.GetPath(fullName);
                    Directory.CreateDirectory(Path.GetDirectoryName(entryOutputPath)!);
                    //In the bin folder, if the file already exists, it will be skipped
                    if (fullName.StartsWith("bin") && File.Exists(entryOutputPath))
                    {
                        continue;
                    }

                    TryExtractToFile(entry, entryOutputPath);

                    Console.WriteLine(entryOutputPath);
                }
                catch (Exception ex)
                {
                    sb.Append(ex.StackTrace);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(Resx.Resource.FailedUpgrade + ex.StackTrace);
            //return;
        }
        if (sb.Length > 0)
        {
            Console.WriteLine(Resx.Resource.FailedUpgrade + sb.ToString());
            //return;
        }

        Console.WriteLine(Resx.Resource.Restartv2rayN);
        Utils.Waiting(2);

        Utils.StartV2RayN();
    }

    /// <summary>
    /// Returns the single top-level folder that wraps every file in the archive,
    /// or an empty string when the zip is flat. A zip whose files sit at the root
    /// has no wrapper: treating its first segments as one would skip the
    /// executables and move the bin folders to the wrong place.
    /// </summary>
    private static string FindWrapperFolder(IEnumerable<ZipArchiveEntry>? entries)
    {
        if (entries is null)
        {
            return string.Empty;
        }
        string? wrapper = null;
        foreach (var entry in entries)
        {
            if (entry.Length == 0)
            {
                continue;
            }
            var idx = entry.FullName.IndexOf('/');
            if (idx < 0)
            {
                // A file at the zip root means the zip is flat: no wrapper.
                return string.Empty;
            }
            var first = entry.FullName[..idx];
            if (wrapper == null)
            {
                wrapper = first;
            }
            else if (wrapper != first)
            {
                // Several top-level folders: there is no single wrapper.
                return string.Empty;
            }
        }
        return wrapper ?? string.Empty;
    }

    private static bool TryExtractToFile(ZipArchiveEntry entry, string outputPath)
    {
        var retryCount = 5;
        var delayMs = 1000;

        for (var i = 1; i <= retryCount; i++)
        {
            try
            {
                entry.ExtractToFile(outputPath, true);
                return true;
            }
            catch
            {
                Thread.Sleep(delayMs * i);
            }
        }
        return false;
    }
}
