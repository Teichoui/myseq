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
            Write(path, write, true);
        }

        // keepBackup false is for a file that must not leave an older copy behind, such as one that held a
        // password the user has now chosen not to save: any existing backup is removed too.
        public static void Write(string path, Action<Stream> write, bool keepBackup)
        {
            // Its own name for each save, so two running copies saving the same file cannot mix their files up.
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";

            try
            {
                using (FileStream fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    write(fs);
                    fs.Flush(true);
                }

                if (File.Exists(path))
                    File.Replace(temp, path, keepBackup ? BackupPath(path) : null);
                else
                    File.Move(temp, path);

                if (!keepBackup)
                    File.Delete(BackupPath(path));
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
