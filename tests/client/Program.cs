using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
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

        private static string SettingsTestDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "myseq-settingsfile-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static void WriteText(string path, string text)
        {
            SettingsFile.Write(path, delegate (Stream s)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(text);
                s.Write(bytes, 0, bytes.Length);
            });
        }

        private static void SettingsFileWritesNewFile()
        {
            string dir = SettingsTestDir();
            try
            {
                string path = Path.Combine(dir, "prefs.xml");
                WriteText(path, "one");
                Check(File.ReadAllText(path) == "one", "a new file gets its content");
                Check(Directory.GetFiles(dir, "*.tmp").Length == 0, "no temporary file is left behind");
                Check(!File.Exists(SettingsFile.BackupPath(path)), "there is no backup when nothing was replaced");
            }
            finally { Directory.Delete(dir, true); }
        }

        private static void SettingsFileKeepsBackup()
        {
            string dir = SettingsTestDir();
            try
            {
                string path = Path.Combine(dir, "prefs.xml");
                WriteText(path, "one");
                WriteText(path, "two");
                Check(File.ReadAllText(path) == "two", "the file holds the latest save");
                Check(File.ReadAllText(SettingsFile.BackupPath(path)) == "one", "the previous save is kept as the backup");
                WriteText(path, "three");
                Check(File.ReadAllText(SettingsFile.BackupPath(path)) == "two", "the backup moves on with each save");
                Check(Directory.GetFiles(dir, "*.tmp").Length == 0, "no temporary file is left behind after replacing");
            }
            finally { Directory.Delete(dir, true); }
        }

        private static void SettingsFileFailedWriteKeepsOriginal()
        {
            string dir = SettingsTestDir();
            try
            {
                string path = Path.Combine(dir, "prefs.xml");
                WriteText(path, "good");
                bool threw = false;
                try
                {
                    SettingsFile.Write(path, delegate (Stream s)
                    {
                        byte[] bytes = Encoding.UTF8.GetBytes("half of a new file");
                        s.Write(bytes, 0, bytes.Length);
                        throw new InvalidOperationException("save failed part way");
                    });
                }
                catch (InvalidOperationException) { threw = true; }

                Check(threw, "the error from a failed save is passed on");
                Check(File.ReadAllText(path) == "good", "a failed save leaves the old file as it was");
                Check(Directory.GetFiles(dir, "*.tmp").Length == 0, "a failed save removes its temporary file");
                Check(!File.Exists(SettingsFile.BackupPath(path)), "a failed save does not touch the backup");
            }
            finally { Directory.Delete(dir, true); }
        }

        private static void SettingsFileFailedFirstWriteLeavesNothing()
        {
            string dir = SettingsTestDir();
            try
            {
                string path = Path.Combine(dir, "prefs.xml");
                try
                {
                    SettingsFile.Write(path, delegate (Stream s) { throw new InvalidOperationException("save failed"); });
                }
                catch (InvalidOperationException) { }

                Check(!File.Exists(path), "a failed first save does not create a file");
                Check(Directory.GetFiles(dir, "*.tmp").Length == 0, "a failed first save removes its temporary file");
            }
            finally { Directory.Delete(dir, true); }
        }
        private static void SettingsFileWithoutBackupLeavesNoOldCopy()
        {
            string dir = SettingsTestDir();
            try
            {
                string path = Path.Combine(dir, "myseq.xml");
                WriteText(path, "password: secret");
                WriteText(path, "password: secret again");
                Check(File.Exists(SettingsFile.BackupPath(path)), "a normal save keeps a backup");

                SettingsFile.Write(path, delegate (Stream s)
                {
                    byte[] bytes = Encoding.UTF8.GetBytes("password: ");
                    s.Write(bytes, 0, bytes.Length);
                }, false);

                Check(File.ReadAllText(path) == "password: ", "a save without backup still replaces the file");
                Check(!File.Exists(SettingsFile.BackupPath(path)), "a save without backup removes the old backup, so no older copy is left");
                Check(Directory.GetFiles(dir, "*.tmp").Length == 0, "no temporary file is left behind");
            }
            finally { Directory.Delete(dir, true); }
        }
        private static void SettingsFileWithoutBackupFailsIfOldCopyStays()
        {
            // A read-only file blocks deleting it on Windows only.
            if (!OperatingSystem.IsWindows()) { Console.WriteLine("  skip  a backup that cannot be removed (Windows only)"); return; }

            string dir = SettingsTestDir();
            string path = Path.Combine(dir, "myseq.xml");
            string backup = SettingsFile.BackupPath(path);
            try
            {
                WriteText(path, "password: secret");
                WriteText(path, "password: secret again");
                File.SetAttributes(backup, FileAttributes.ReadOnly);

                bool threw = false;
                try { WriteText2(path, "password: ", false); }
                catch (UnauthorizedAccessException) { threw = true; }
                catch (IOException) { threw = true; }

                Check(threw, "a save that cannot remove the old backup reports the failure");
                Check(File.ReadAllText(path) == "password: secret again", "and it leaves the main file as it was, with nothing replaced");
                Check(Directory.GetFiles(dir, "*.tmp").Length == 0, "and removes its temporary file");
            }
            finally
            {
                if (File.Exists(backup)) File.SetAttributes(backup, FileAttributes.Normal);
                Directory.Delete(dir, true);
            }
        }

        private static void WriteText2(string path, string text, bool keepBackup)
        {
            SettingsFile.Write(path, delegate (Stream s)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(text);
                s.Write(bytes, 0, bytes.Length);
            }, keepBackup);
        }
        private static void SettingsFileRestoreFromBackup()
        {
            string dir = SettingsTestDir();
            try
            {
                string path = Path.Combine(dir, "prefs.xml");
                WriteText(path, "good");
                WriteText(path, "good again");
                // the main file gets damaged, the backup still holds the last good copy
                File.WriteAllText(path, "half");
                SettingsFile.RestoreFromBackup(path);
                Check(File.ReadAllText(path) == "good", "after a recovery the main file is the good copy again");
                WriteText(path, "next save");
                Check(File.ReadAllText(SettingsFile.BackupPath(path)) == "good", "so the next save keeps the good copy as the backup, not the damaged one");

                // no backup: nothing happens and nothing fails
                string other = Path.Combine(dir, "other.xml");
                File.WriteAllText(other, "only copy");
                SettingsFile.RestoreFromBackup(other);
                Check(File.ReadAllText(other) == "only copy", "restoring without a backup leaves the file alone");
            }
            finally { Directory.Delete(dir, true); }
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

            Console.WriteLine("\nDiscordWebhook");
            WebhookPayload();
            WebhookTruncation();
            WebhookSend();
            WebhookRateLimit();
            WebhookErrors();
            WebhookBadUrls();
            WebhookRedirect();
            WebhookTimeoutAndUnreachable();

            Console.WriteLine("\nLauncherCopies");
            LauncherSelectsOnlyItsOwnCopies();
            LauncherSurvivesRunningCopy();
            LauncherOldBehaviourControl();

            Console.WriteLine("\nSettingsFile");
            SettingsFileWritesNewFile();
            SettingsFileKeepsBackup();
            SettingsFileFailedWriteKeepsOriginal();
            SettingsFileFailedFirstWriteLeavesNothing();
            SettingsFileWithoutBackupLeavesNoOldCopy();
            SettingsFileWithoutBackupFailsIfOldCopyStays();
            SettingsFileRestoreFromBackup();

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

        // ---------- DiscordWebhook ----------

        // A webhook endpoint on localhost. respond gets the request number (0, 1, ...) and the response to fill in.
        private sealed class FakeWebhook : IDisposable
        {
            private readonly HttpListener listener = new HttpListener();
            public readonly string Url;
            public readonly List<string> Bodies = new List<string>();
            public readonly List<string> Methods = new List<string>();
            public readonly List<string> ContentTypes = new List<string>();

            public FakeWebhook(Action<int, HttpListenerResponse> respond)
            {
                int port;
                TcpListener probe = new TcpListener(IPAddress.Loopback, 0);
                probe.Start();
                port = ((IPEndPoint)probe.LocalEndpoint).Port;
                probe.Stop();

                Url = "http://127.0.0.1:" + port + "/api/webhooks/1234/secret-token";
                listener.Prefixes.Add("http://127.0.0.1:" + port + "/");
                listener.Start();

                Thread worker = new Thread(() =>
                {
                    int n = 0;
                    while (true)
                    {
                        HttpListenerContext ctx;
                        try { ctx = listener.GetContext(); }
                        catch (Exception) { return; }

                        string body;
                        using (StreamReader r = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
                            body = r.ReadToEnd();
                        lock (Bodies) { Bodies.Add(body); Methods.Add(ctx.Request.HttpMethod); ContentTypes.Add(ctx.Request.ContentType); }

                        try { respond(n++, ctx.Response); ctx.Response.Close(); }
                        catch (Exception) { }
                    }
                });
                worker.IsBackground = true;
                worker.Start();
            }

            public int Requests { get { lock (Bodies) { return Bodies.Count; } } }

            public void Dispose() { try { listener.Close(); } catch (Exception) { } }
        }

        private static string ContentOf(string json)
        {
            using (JsonDocument doc = JsonDocument.Parse(json))
                return doc.RootElement.GetProperty("content").GetString();
        }

        private static Exception Catch(Action a)
        {
            try { a(); return null; } catch (Exception e) { return e; }
        }

        private static void WebhookPayload()
        {
            Console.WriteLine(" the JSON body");
            string tricky = "a\"b\\c\n\r\t\u0001 \u00e9 \U0001F600 </script> {\"x\":1}";
            string json = DiscordWebhook.BuildPayload(tricky);
            Check(json == "{\"content\":\"a\\\"b\\\\c\\n\\r\\t\\u0001 \u00e9 \U0001F600 </script> {\\\"x\\\":1}\"}", "quotes, backslashes and control characters are escaped");
            Check(ContentOf(json) == tricky, "a real JSON parser reads back exactly what was sent");
            Check(ContentOf(DiscordWebhook.BuildPayload(null)) == "" && ContentOf(DiscordWebhook.BuildPayload("")) == "", "null and empty become an empty message");

            Random rnd = new Random(7);
            string alphabet = "ab \"\\/\n\r\t\u0000\u0007\u001f\u007f\u00e9\u4e2d{}[]:,";
            bool allOk = true;
            for (int i = 0; i < 2000 && allOk; i++)
            {
                StringBuilder sb = new StringBuilder();
                int len = rnd.Next(0, 60);
                for (int j = 0; j < len; j++) sb.Append(alphabet[rnd.Next(alphabet.Length)]);
                string s = sb.ToString();
                allOk = ContentOf(DiscordWebhook.BuildPayload(s)) == s;
            }
            Check(allOk, "2000 random strings round-trip through a JSON parser");
        }

        private static void WebhookTruncation()
        {
            Console.WriteLine(" the 2000 character limit");
            string longText = new string('x', 2500);
            Check(ContentOf(DiscordWebhook.BuildPayload(longText)).Length == DiscordWebhook.MaxContentLength, "a longer message is cut to 2000 characters");
            Check(ContentOf(DiscordWebhook.BuildPayload(new string('y', 2000))).Length == 2000, "exactly 2000 is left alone");

            // A smiley is two UTF-16 characters; cutting between them would send a broken character.
            string splitsEmoji = new string('z', 1999) + "\U0001F600";
            string cut = ContentOf(DiscordWebhook.BuildPayload(splitsEmoji));
            Check(cut.Length == 1999 && !char.IsHighSurrogate(cut[cut.Length - 1]), "a cut never leaves half of an emoji");
        }

        private static void WebhookSend()
        {
            Console.WriteLine(" posting a message");
            using (FakeWebhook server = new FakeWebhook((n, resp) => { resp.StatusCode = 204; }))
            {
                string message = "Orc Pawn spawned at 7:15 PM \"today\"";
                Exception e = Catch(() => DiscordWebhook.Send(server.Url, message));
                Check(e == null, "a 204 answer counts as sent" + (e == null ? "" : " (" + e.Message + ")"));
                Check(server.Requests == 1 && server.Methods[0] == "POST", "one POST request was made");
                Check(server.ContentTypes[0] != null && server.ContentTypes[0].StartsWith("application/json"), "sent as application/json");
                Check(ContentOf(server.Bodies[0]) == message, "the message arrived intact");

                e = Catch(() => DiscordWebhook.Send("  " + server.Url + "  ", "padded"));
                Check(e == null && ContentOf(server.Bodies[1]) == "padded", "spaces around the saved URL are ignored");
            }
        }

        private static void WebhookRateLimit()
        {
            Console.WriteLine(" Discord asking us to slow down (HTTP 429)");

            using (FakeWebhook server = new FakeWebhook((n, resp) =>
            {
                if (n == 0) { resp.StatusCode = 429; resp.AddHeader("Retry-After", "1"); }
                else resp.StatusCode = 204;
            }))
            {
                DateTime t0 = DateTime.UtcNow;
                Exception e = Catch(() => DiscordWebhook.Send(server.Url, "again"));
                double secs = (DateTime.UtcNow - t0).TotalSeconds;
                Check(e == null && server.Requests == 2, "waits as asked and retries once, then succeeds");
                Check(secs >= 0.9 && secs < 5, "after about the 1 second Discord asked for (" + secs.ToString("0.0") + " s)");
            }

            // Discord states the wait in seconds with fractions; the retry must not come back earlier than that.
            double bodyWait = TimeRetry(resp =>
            {
                resp.StatusCode = 429;
                resp.AddHeader("Retry-After", "1");
                byte[] b = Encoding.UTF8.GetBytes("{\"message\": \"You are being rate limited.\", \"retry_after\": 1.5, \"global\": false}");
                resp.OutputStream.Write(b, 0, b.Length);
            });
            Check(bodyWait >= 1.4 && bodyWait < 5, "waits the fractional retry_after from Discord's body, not the whole-second header (" + bodyWait.ToString("0.00") + " s)");

            double headerWait = TimeRetry(resp => { resp.StatusCode = 429; resp.AddHeader("Retry-After", "1.75"); });
            Check(headerWait >= 1.65 && headerWait < 5, "reads a fractional Retry-After header when there is no body value (" + headerWait.ToString("0.00") + " s)");

            double junkWait = TimeRetry(resp => { resp.StatusCode = 429; resp.AddHeader("Retry-After", "-3"); });
            Check(junkWait >= 0.9 && junkWait < 5, "a negative Retry-After falls back to one second (" + junkWait.ToString("0.00") + " s)");

            using (FakeWebhook server = new FakeWebhook((n, resp) =>
            {
                resp.StatusCode = 429;
                byte[] b = Encoding.UTF8.GetBytes("{\"retry_after\": 20.5}");
                resp.OutputStream.Write(b, 0, b.Length);
            }))
            {
                DateTime t0 = DateTime.UtcNow;
                Exception e = Catch(() => DiscordWebhook.Send(server.Url, "again"));
                Check(e is HttpRequestException && server.Requests == 1 && (DateTime.UtcNow - t0).TotalSeconds < 3, "a retry_after of 20.5 s in the body is not sat through either");
            }

            using (FakeWebhook server = new FakeWebhook((n, resp) => { resp.StatusCode = 429; resp.AddHeader("Retry-After", "1"); }))
            {
                Exception e = Catch(() => DiscordWebhook.Send(server.Url, "again"));
                Check(e is HttpRequestException && e.Message.Contains("429") && server.Requests == 2, "gives up after one retry");
            }

            using (FakeWebhook server = new FakeWebhook((n, resp) => { resp.StatusCode = 429; resp.AddHeader("Retry-After", "60"); }))
            {
                DateTime t0 = DateTime.UtcNow;
                Exception e = Catch(() => DiscordWebhook.Send(server.Url, "again"));
                Check(e is HttpRequestException && server.Requests == 1 && (DateTime.UtcNow - t0).TotalSeconds < 3, "a long wait is not sat through; fails at once");
            }
        }

        // Sends one message to a server that answers the first request with the given 429, then accepts
        // the retry. Returns how many seconds the whole call took, or -1 if it did not end up sent.
        private static double TimeRetry(Action<HttpListenerResponse> rateLimited)
        {
            using (FakeWebhook server = new FakeWebhook((n, resp) =>
            {
                if (n == 0) rateLimited(resp);
                else resp.StatusCode = 204;
            }))
            {
                DateTime t0 = DateTime.UtcNow;
                Exception e = Catch(() => DiscordWebhook.Send(server.Url, "again"));
                double secs = (DateTime.UtcNow - t0).TotalSeconds;
                return (e == null && server.Requests == 2) ? secs : -1;
            }
        }

        private static void WebhookErrors()
        {
            Console.WriteLine(" error answers");
            using (FakeWebhook server = new FakeWebhook((n, resp) =>
            {
                resp.StatusCode = 404;
                byte[] b = Encoding.UTF8.GetBytes("{\"message\": \"Unknown Webhook\", \"code\": 10015}");
                resp.OutputStream.Write(b, 0, b.Length);
            }))
            {
                Exception e = Catch(() => DiscordWebhook.Send(server.Url, "x"));
                Check(e is HttpRequestException && e.Message.Contains("404") && e.Message.Contains("Unknown Webhook"), "the status and Discord's reason are in the error");
                Check(server.Requests == 1, "an error other than 429 is not retried");
            }

            using (FakeWebhook server = new FakeWebhook((n, resp) =>
            {
                resp.StatusCode = 500;
                byte[] b = Encoding.UTF8.GetBytes(new string('e', 5000));
                resp.OutputStream.Write(b, 0, b.Length);
            }))
            {
                Exception e = Catch(() => DiscordWebhook.Send(server.Url, "x"));
                Check(e != null && e.Message.Length < 400, "a huge error page is trimmed in the message");
            }
        }

        private static void WebhookBadUrls()
        {
            Console.WriteLine(" URLs that cannot work");
            foreach (string bad in new string[] { null, "", "   ", "not a url", "discord.com/api/webhooks/1/x", "ftp://example.com/x", "file:///c:/windows/win.ini",
                                                  "http://example.com/api/webhooks/1/x", "http://192.168.1.5/api/webhooks/1/x" })
            {
                Exception e = Catch(() => DiscordWebhook.Send(bad, "x"));
                Check(e is ArgumentException, "rejected before any request: " + (bad == null ? "null" : "\"" + bad + "\""));
            }

            // https is accepted (it fails to connect here, which is a different error), and so is http to localhost.
            Exception https = Catch(() => DiscordWebhook.Send("https://127.0.0.1:1/api/webhooks/1/x", "x", timeoutSeconds: 3));
            Check(https is HttpRequestException, "an https address passes the check (and then fails to connect)");
        }

        private static void WebhookRedirect()
        {
            Console.WriteLine(" a redirect");

            // HttpClient follows redirects on its own by default, and a 307 re-sends the POST body.
            using (FakeWebhook elsewhere = new FakeWebhook((n, resp) => { resp.StatusCode = 204; }))
            using (FakeWebhook redirector = new FakeWebhook((n, resp) => { resp.StatusCode = 307; resp.AddHeader("Location", elsewhere.Url); }))
            {
                Exception e = Catch(() => DiscordWebhook.Send(redirector.Url, "secret message"));
                Check(e is HttpRequestException && e.Message.Contains("307"), "a redirect is reported as an error");
                Check(redirector.Requests == 1 && elsewhere.Requests == 0, "and the message is not sent on to the address it points at");
            }
        }

        private static void WebhookTimeoutAndUnreachable()
        {
            Console.WriteLine(" no answer");
            using (FakeWebhook server = new FakeWebhook((n, resp) => { Thread.Sleep(4000); resp.StatusCode = 204; }))
            {
                DateTime t0 = DateTime.UtcNow;
                Exception e = Catch(() => DiscordWebhook.Send(server.Url, "x", timeoutSeconds: 1));
                double secs = (DateTime.UtcNow - t0).TotalSeconds;
                Check(e is TimeoutException, "a server that never answers gives a TimeoutException");
                Check(secs >= 0.8 && secs < 3.5, "after about the timeout (" + secs.ToString("0.0") + " s)");
            }

            string closed;
            using (FakeWebhook gone = new FakeWebhook((n, resp) => { })) { closed = gone.Url; }
            Exception unreachable = Catch(() => DiscordWebhook.Send(closed, "x", timeoutSeconds: 3));
            Check(unreachable != null && !(unreachable is ArgumentException), "an address nothing is listening on fails with an error");

            Exception root = unreachable;
            while (root != null && root.InnerException != null) root = root.InnerException;
            Check(unreachable is HttpRequestException && root != null && root != unreachable && unreachable.Message.Contains(root.Message),
                  "the error says why (\"" + (unreachable == null ? "" : unreachable.Message.Replace("\r", " ").Replace("\n", " ")) + "\")");
            Check(unreachable != null && !unreachable.Message.StartsWith("An error occurred while sending the request"), "and is not just HttpClient's generic message");
        }

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
