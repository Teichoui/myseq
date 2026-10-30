using System;
using System.IO;
using System.Text.RegularExpressions;

namespace myseq
{
    /// <summary>
    /// At startup the client copies itself to a randomly named .bin file and runs that copy.
    /// This finds and removes the copies left by earlier runs without touching other files.
    /// </summary>
    public static class LauncherCopies
    {
        // The launcher names its copy with 4 to 15 letters or digits, then .bin.
        private static readonly Regex NamePattern = new Regex("^[A-Za-z0-9]{4,15}\\.bin$", RegexOptions.Compiled);

        /// <summary>True for a file that looks like a copy made by the launcher: its name fits and it is an executable.</summary>
        public static bool IsLauncherCopy(string path)
        {
            if (!NamePattern.IsMatch(Path.GetFileName(path)))
                return false;

            try
            {
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    return fs.ReadByte() == 'M' && fs.ReadByte() == 'Z';
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        /// <summary>
        /// Deletes old launcher copies in directory. A copy that is still running is locked and is
        /// skipped. Returns the number of files deleted.
        /// </summary>
        public static int DeleteOldCopies(string directory)
        {
            int deleted = 0;

            foreach (string file in Directory.GetFiles(directory, "*.bin"))
            {
                if (!IsLauncherCopy(file))
                    continue;

                try
                {
                    File.Delete(file);
                    deleted++;
                }
                catch (IOException) { }                     // in use by a running copy
                catch (UnauthorizedAccessException) { }     // running, or read-only
            }

            return deleted;
        }
    }
}
