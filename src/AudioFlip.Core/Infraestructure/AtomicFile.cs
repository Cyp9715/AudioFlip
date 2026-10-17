namespace AudioFlip;

internal static class AtomicFile
{
    public static void WriteAllText(string destination, string contents, string? backupPath = null)
    {
        destination = Path.GetFullPath(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporaryPath = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                using (var writer = new StreamWriter(stream, leaveOpen: true))
                {
                    writer.Write(contents);
                    writer.Flush();
                }

                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(destination))
                File.Replace(temporaryPath, destination, backupPath);
            else
                File.Move(temporaryPath, destination);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}
