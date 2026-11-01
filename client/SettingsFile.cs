using System;
using System.IO;

namespace myseq
{
    // Saves a settings file so that an error or a crash part way through cannot leave it empty or cut off.
    // The new copy is written next to it first and swapped in when complete; the copy it replaces is kept as
    // <name>.bak, which the loaders fall back to when the main file cannot be read.
    internal static class SettingsFile
    {
        public static string BackupPath(string path)
        {
            return path + ".bak";
        }

        public static void Write(string path, Action<Stream> write)
        {
            string temp = path + ".tmp";

            try
            {
                using (FileStream fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    write(fs);
                    fs.Flush(true);
                }

                if (File.Exists(path))
                    File.Replace(temp, path, BackupPath(path));
                else
                    File.Move(temp, path);
            }
            catch
            {
                try { if (File.Exists(temp)) File.Delete(temp); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }

                throw;
            }
        }
    }
}
