using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace myseq.Tests
{
    internal static class Program
    {
        private static int failures;

        private static void Check(bool ok, string what)
        {
            Console.WriteLine((ok ? "  ok    " : "  FAIL  ") + what);
            if (!ok) failures++;
        }

        private static int Main()
        {
            Console.WriteLine("PacketFramer");
            FramerWholeResponse();
            FramerEverySplit();
            FramerRandomChunks();
            FramerFragmentedRecord();
            FramerFragmentedHeader();
            FramerEmptyResponse();
            FramerBackToBack();
            FramerBadHeader();
            FramerReset();

            Console.WriteLine("\nSyncHashtable");
            SyncBasics();
            SyncConcurrentEnumeration();

            Console.WriteLine("\nLauncherCopies");
            LauncherSelectsOnlyItsOwnCopies();
            LauncherSurvivesRunningCopy();
            LauncherOldBehaviourControl();

            Console.WriteLine("\n" + (failures == 0 ? "PASSED" : "FAILED") + " (" + failures + " failure" + (failures == 1 ? "" : "s") + ")");
            return failures == 0 ? 0 : 1;
        }

        // ---------- helpers for the packet tests ----------

        private const int Rec = PacketFramer.RecordSize;

        // Record i starts with its own number so a mixed-up or shifted record is easy to spot.
        private static byte[] MakeRecord(int i)
        {
            byte[] r = new byte[Rec];
            for (int j = 0; j < Rec; j++) r[j] = (byte)((i * 7 + j * 3 + 1) & 0xFF);
            BitConverter.GetBytes(i).CopyTo(r, 0);
            return r;
        }

        private static byte[] MakeResponse(int records)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                ms.Write(BitConverter.GetBytes(records), 0, 4);
                for (int i = 0; i < records; i++) { byte[] r = MakeRecord(i); ms.Write(r, 0, r.Length); }
                return ms.ToArray();
            }
        }

        private sealed class Collector
        {
            public readonly List<byte[]> Records = new List<byte[]>();
            public readonly List<int> Headers = new List<int>();
            public int Completed;

            public void Feed(PacketFramer f, byte[] stream, int from, int count)
            {
                // The real socket buffer is bigger than what was read, so mimic that.
                byte[] raw = new byte[65536];
                Buffer.BlockCopy(stream, from, raw, 0, count);
                if (f.Feed(raw, count, n => Headers.Add(n), (b, o) => { byte[] c = new byte[Rec]; Buffer.BlockCopy(b, o, c, 0, Rec); Records.Add(c); }))
                    Completed++;
            }

            public bool Matches(int expectedRecords)
            {
                if (Records.Count != expectedRecords) return false;
                for (int i = 0; i < expectedRecords; i++)
                    if (!Records[i].SequenceEqual(MakeRecord(i))) return false;
                return true;
            }
        }

        // ---------- PacketFramer ----------

        private static void FramerWholeResponse()
        {
            Console.WriteLine(" one read holding the whole response");
            byte[] s = MakeResponse(3);
            PacketFramer f = new PacketFramer();
            Collector c = new Collector();
            c.Feed(f, s, 0, s.Length);
            Check(c.Completed == 1, "response completes");
            Check(c.Headers.Count == 1 && c.Headers[0] == 3, "header reports 3 records");
            Check(c.Matches(3), "all 3 records arrive intact and in order");
        }

        private static void FramerEverySplit()
        {
            Console.WriteLine(" response cut in two at every possible byte");
            int records = 3;
            byte[] s = MakeResponse(records);
            bool allOk = true;
            for (int cut = 1; cut < s.Length; cut++)
            {
                PacketFramer f = new PacketFramer();
                Collector c = new Collector();
                c.Feed(f, s, 0, cut);
                bool midway = c.Completed == 0;
                c.Feed(f, s, cut, s.Length - cut);
                if (!(midway && c.Completed == 1 && c.Headers.Count == 1 && c.Matches(records)))
                {
                    allOk = false;
                    Console.WriteLine("        split at " + cut + " failed");
                }
            }
            Check(allOk, "every two-part split gives the same records, completing once");
        }

        private static void FramerRandomChunks()
        {
            Console.WriteLine(" random read sizes");
            Random rnd = new Random(12345);
            bool allOk = true;
            for (int round = 0; round < 2000; round++)
            {
                int records = rnd.Next(0, 40);
                byte[] s = MakeResponse(records);
                PacketFramer f = new PacketFramer();
                Collector c = new Collector();
                int pos = 0;
                int lastCompleted = 0;
                while (pos < s.Length)
                {
                    int n = Math.Min(s.Length - pos, rnd.Next(1, 260));
                    c.Feed(f, s, pos, n);
                    pos += n;
                    if (pos < s.Length) lastCompleted = c.Completed;
                }
                if (!(lastCompleted == 0 && c.Completed == 1 && c.Matches(records)))
                {
                    allOk = false;
                    Console.WriteLine("        round " + round + " (" + records + " records) failed");
                    break;
                }
            }
            Check(allOk, "2000 responses cut into random pieces all come out exact");
        }

        private static void FramerFragmentedRecord()
        {
            Console.WriteLine(" one record arriving in three pieces (20 + 20 + 60 bytes)");
            // Header and the first 60 bytes of record 0, then the rest of record 0 in 20/20/60 -> sizes below.
            byte[] s = MakeResponse(2);
            int[] sizes = { 4 + 60, 20, 20, s.Length - (4 + 60 + 20 + 20) };

            PacketFramer f = new PacketFramer();
            Collector c = new Collector();
            int pos = 0;
            foreach (int n in sizes) { c.Feed(f, s, pos, n); pos += n; }
            Check(c.Completed == 1 && c.Matches(2), "framer rebuilds both records exactly");

            // Control: the logic this replaced, copied from the old ProcessPacket.
            LegacyAssembler old = new LegacyAssembler();
            pos = 0;
            foreach (int n in sizes) { old.Feed(s, pos, n); pos += n; }
            bool oldCorrupt = old.Records.Count != 2 || !old.Records[0].SequenceEqual(MakeRecord(0));
            Check(oldCorrupt, "control: the old logic corrupts the same stream");
        }

        private static void FramerFragmentedHeader()
        {
            Console.WriteLine(" the 4-byte count arriving 2 + 2 bytes");
            byte[] s = MakeResponse(1);
            PacketFramer f = new PacketFramer();
            Collector c = new Collector();
            c.Feed(f, s, 0, 2);
            Check(c.Headers.Count == 0 && c.Completed == 0, "nothing is reported until the count is complete");
            c.Feed(f, s, 2, s.Length - 2);
            Check(c.Completed == 1 && c.Headers[0] == 1 && c.Matches(1), "then the response comes out exact");

            // Control: the old code read the count straight from the first read. With 300 records
            // (0x012C) a read that ends after the first byte makes it read 44 instead.
            byte[] big = MakeResponse(300);
            LegacyAssembler old = new LegacyAssembler();
            old.Feed(big, 0, 1);
            Check(old.NumPackets != 300, "control: the old logic mis-reads a count that is split (read " + old.NumPackets + " instead of 300)");

            PacketFramer f2 = new PacketFramer();
            Collector c2 = new Collector();
            c2.Feed(f2, big, 0, 1);
            c2.Feed(f2, big, 1, big.Length - 1);
            Check(c2.Completed == 1 && c2.Headers[0] == 300 && c2.Matches(300), "framer gets all 300 records from the same split");
        }

        private static void FramerEmptyResponse()
        {
            Console.WriteLine(" response with zero records");
            byte[] s = MakeResponse(0);
            PacketFramer f = new PacketFramer();
            Collector c = new Collector();
            c.Feed(f, s, 0, s.Length);
            Check(c.Completed == 1 && c.Headers.Count == 1 && c.Headers[0] == 0 && c.Records.Count == 0, "completes straight after the count");
        }

        private static void FramerBackToBack()
        {
            Console.WriteLine(" two responses one after the other");
            PacketFramer f = new PacketFramer();
            Collector c = new Collector();
            byte[] a = MakeResponse(2);
            byte[] b = MakeResponse(5);
            c.Feed(f, a, 0, a.Length);
            Check(c.Completed == 1 && c.Matches(2), "first response");
            c.Records.Clear();
            c.Feed(f, b, 0, b.Length);
            Check(c.Completed == 2 && c.Matches(5), "second response, with no state carried over");
        }

        private static void FramerBadHeader()
        {
            Console.WriteLine(" corrupt count");
            foreach (int bad in new[] { -1, int.MinValue, PacketFramer.MaxRecords + 1, int.MaxValue })
            {
                PacketFramer f = new PacketFramer();
                Collector c = new Collector();
                byte[] junk = new byte[4 + Rec];
                BitConverter.GetBytes(bad).CopyTo(junk, 0);
                bool threw = false;
                try { c.Feed(f, junk, 0, junk.Length); } catch (InvalidDataException) { threw = true; }
                Check(threw && c.Records.Count == 0, "count " + bad + " is rejected without handing out records");

                byte[] good = MakeResponse(2);
                c.Feed(f, good, 0, good.Length);
                Check(c.Completed == 1 && c.Matches(2), "  and the next good response still works");
            }
        }

        private static void FramerReset()
        {
            Console.WriteLine(" reconnect in the middle of a response");
            byte[] s = MakeResponse(3);
            PacketFramer f = new PacketFramer();
            Collector c = new Collector();
            c.Feed(f, s, 0, 150);
            f.Reset();
            c.Records.Clear();
            c.Feed(f, s, 0, s.Length);
            Check(c.Completed == 1 && c.Matches(3), "Reset() drops the half-received response");
        }

        // Copy of the reassembly logic the client used before PacketFramer, for the controls above.
        private sealed class LegacyAssembler
        {
            public int NumPackets, NumProcessed, IncompleteCount;
            public readonly byte[] Incomplete = new byte[2048];
            public readonly List<byte[]> Records = new List<byte[]>();

            public void Feed(byte[] stream, int from, int count)
            {
                byte[] packet = new byte[65536];
                Buffer.BlockCopy(stream, from, packet, 0, count);
                Process(packet, count);
            }

            private void Process(byte[] packet, int bytes)
            {
                int offset = 0;
                const int SIZE_OF_PACKET = 100;
                try
                {
                    if (bytes > 0)
                    {
                        if (NumPackets == 0) { NumPackets = BitConverter.ToInt32(packet, 0); offset = 4; }
                        else { offset = -IncompleteCount; }

                        for (; (offset + SIZE_OF_PACKET) <= bytes; offset += SIZE_OF_PACKET)
                        {
                            byte[] rec = new byte[SIZE_OF_PACKET];
                            if (offset < 0)
                            {
                                try
                                {
                                    if (IncompleteCount > 0 && packet.Length > 0)
                                        Array.Copy(packet, 0, Incomplete, IncompleteCount, (SIZE_OF_PACKET - IncompleteCount));
                                }
                                catch (Exception) { }
                                IncompleteCount = 0;
                                Array.Copy(Incomplete, 0, rec, 0, SIZE_OF_PACKET);
                            }
                            else Array.Copy(packet, offset, rec, 0, SIZE_OF_PACKET);
                            NumProcessed++;
                            Records.Add(rec);
                        }
                    }
                }
                catch (Exception) { }

                if (NumProcessed < NumPackets)
                {
                    if (offset < bytes)
                    {
                        IncompleteCount = bytes - offset;
                        try { Array.Copy(packet, offset, Incomplete, 0, IncompleteCount); }
                        catch (Exception) { }
                    }
                }
                else
                {
                    NumPackets = NumProcessed = 0;
                    IncompleteCount = 0;
                    Array.Clear(Incomplete, 0, Incomplete.Length);
                }
            }
        }

        // ---------- SyncHashtable ----------

        private static void SyncBasics()
        {
            Console.WriteLine(" lookups and changes");
            SyncHashtable t = new SyncHashtable();
            t.Add(1u, "a");
            t.Add(2u, "b");
            t[3u] = "c";
            Check(t.Count == 3 && t.ContainsKey(2u) && (string)t[3u] == "c", "add, indexer set and lookup");
            Check(t[99u] == null && !t.ContainsKey(99u), "a missing key reads as null, like Hashtable");
            Check(!t.ContainsKey(2), "keys compare by type, like Hashtable (uint 2 is not int 2)");
            t.Remove(2u);
            Check(t.Count == 2 && !t.ContainsKey(2u), "remove");
            object[] snap = t.Snapshot();
            Check(snap.Length == 2 && snap.Contains("a") && snap.Contains("c"), "snapshot holds the current values");
            t.Add(4u, "d");
            Check(snap.Length == 2, "an existing snapshot does not change when the table does");
            t.Clear();
            Check(t.Count == 0 && t.Snapshot().Length == 0, "clear");
            bool threw = false;
            try { t.Add(1u, "x"); t.Add(1u, "y"); } catch (ArgumentException) { threw = true; }
            Check(threw, "adding a duplicate key still throws, like Hashtable");
        }

        private static void SyncConcurrentEnumeration()
        {
            Console.WriteLine(" one thread changes the table while another loops over it");
            TimeSpan run = TimeSpan.FromSeconds(2);

            // Control: the plain Hashtable the client used before, looped over directly.
            Hashtable plain = new Hashtable();
            int plainErrors = Hammer(
                add: k => { lock (plain) { plain[k] = k; } },
                remove: k => { lock (plain) { plain.Remove(k); } },
                loop: () => { foreach (object v in plain.Values) { if (v == null) throw new InvalidOperationException(); } },
                run: run);
            Check(plainErrors > 0, "control: looping over a plain Hashtable fails while it is changing (" + plainErrors + " errors)");

            SyncHashtable safe = new SyncHashtable();
            int safeErrors = Hammer(
                add: k => safe[k] = k,
                remove: k => safe.Remove(k),
                loop: () => { foreach (object v in safe.Snapshot()) { if (v == null) throw new InvalidOperationException(); } },
                run: run);
            Check(safeErrors == 0, "looping over Snapshot() never fails (" + safeErrors + " errors)");
        }

        // One writer adds and removes keys; the calling thread keeps looping. Returns how many loops threw.
        private static int Hammer(Action<int> add, Action<int> remove, Action loop, TimeSpan run)
        {
            volatileStop = false;
            Thread writer = new Thread(() =>
            {
                int k = 0;
                while (!volatileStop)
                {
                    add(k);
                    if (k >= 200) remove(k - 200);
                    k++;
                }
            });
            writer.Start();

            int errors = 0;
            DateTime end = DateTime.UtcNow + run;
            while (DateTime.UtcNow < end)
            {
                try { loop(); }
                catch (InvalidOperationException) { errors++; }
            }

            volatileStop = true;
            writer.Join();
            return errors;
        }

        private static volatile bool volatileStop;

        // ---------- LauncherCopies ----------

        private static string NewTempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "myseq-launcher-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static void WriteFile(string dir, string name, byte[] content)
        {
            File.WriteAllBytes(Path.Combine(dir, name), content);
        }

        private static readonly byte[] Exe = { (byte)'M', (byte)'Z', 0x90, 0, 3, 0, 0, 0 };
        private static readonly byte[] NotExe = { (byte)'h', (byte)'e', (byte)'l', (byte)'l', (byte)'o' };

        private static void LauncherSelectsOnlyItsOwnCopies()
        {
            Console.WriteLine(" which .bin files are removed");
            string dir = NewTempDir();
            try
            {
                WriteFile(dir, "aB3dE9xQ.bin", Exe);        // an old launcher copy
                WriteFile(dir, "Zk29.bin", Exe);            // shortest name the launcher makes
                WriteFile(dir, "backup.bin", NotExe);       // user data with a name that fits the pattern
                WriteFile(dir, "save-data.bin", Exe);       // hyphen: not a launcher name
                WriteFile(dir, "abc.bin", Exe);             // too short
                WriteFile(dir, "abcdefghijklmnop.bin", Exe);// too long (16)
                WriteFile(dir, "notes.txt", Exe);           // not .bin
                WriteFile(dir, "empty.bin", new byte[0]);   // empty

                int deleted = LauncherCopies.DeleteOldCopies(dir);

                Check(deleted == 2, "exactly the two launcher copies are deleted (" + deleted + ")");
                Check(!File.Exists(Path.Combine(dir, "aB3dE9xQ.bin")) && !File.Exists(Path.Combine(dir, "Zk29.bin")), "the copies are gone");
                string[] kept = { "backup.bin", "save-data.bin", "abc.bin", "abcdefghijklmnop.bin", "notes.txt", "empty.bin" };
                Check(kept.All(n => File.Exists(Path.Combine(dir, n))), "every other file is untouched");
            }
            finally { Directory.Delete(dir, true); }
        }

        private static void LauncherSurvivesRunningCopy()
        {
            Console.WriteLine(" a copy that is still running");
            if (!OperatingSystem.IsWindows())
            {
                // Only Windows refuses to delete a file another process has open.
                Console.WriteLine("  skip  relies on Windows file sharing");
                return;
            }
            string dir = NewTempDir();
            try
            {
                WriteFile(dir, "runningA1.bin", Exe);
                WriteFile(dir, "staleCopy.bin", Exe);

                // A running exe is held open for reading with deletes refused.
                using (FileStream running = new FileStream(Path.Combine(dir, "runningA1.bin"), FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    int deleted = 0;
                    Exception error = null;
                    try { deleted = LauncherCopies.DeleteOldCopies(dir); } catch (Exception e) { error = e; }

                    Check(error == null, "cleanup does not throw" + (error == null ? "" : ": " + error.GetType().Name));
                    Check(File.Exists(Path.Combine(dir, "runningA1.bin")), "the running copy is left alone");
                    Check(!File.Exists(Path.Combine(dir, "staleCopy.bin")) && deleted == 1, "the stale copy is still removed");
                }
            }
            finally { Directory.Delete(dir, true); }
        }

        private static void LauncherOldBehaviourControl()
        {
            Console.WriteLine(" control: the old startup code");
            if (!OperatingSystem.IsWindows())
            {
                // Only Windows refuses to delete a file another process has open.
                Console.WriteLine("  skip  relies on Windows file sharing");
                return;
            }
            string dir = NewTempDir();
            try
            {
                WriteFile(dir, "backup.bin", NotExe);
                WriteFile(dir, "runningA1.bin", Exe);

                // The old code: delete every *.bin, with no error handling.
                Exception error = null;
                using (FileStream running = new FileStream(Path.Combine(dir, "runningA1.bin"), FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    try
                    {
                        foreach (string f in Directory.GetFiles(dir, "*.bin")) File.Delete(f);
                    }
                    catch (Exception e) { error = e; }
                }

                Check(error != null, "old code throws on a running copy, which would crash startup");
                // Order of GetFiles is not fixed, so check on a clean directory what it does to unrelated files.
                WriteFile(dir, "backup.bin", NotExe);
                foreach (string f in Directory.GetFiles(dir, "backup.bin")) File.Delete(f);
                Check(!File.Exists(Path.Combine(dir, "backup.bin")), "old code deletes any .bin, including unrelated data");
            }
            finally { Directory.Delete(dir, true); }
        }
    }
}
