using System;
using System.IO;

namespace myseq
{
    /// <summary>
    /// Reassembles the server's responses from socket reads of any size.
    /// A response is a 4 byte record count followed by that many 100 byte records, and a
    /// read can end anywhere inside it: in the middle of the count or of a record.
    /// </summary>
    public sealed class PacketFramer
    {
        public const int HeaderSize = 4;
        public const int RecordSize = 100;
        public const int MaxRecords = 1000000;

        private byte[] pending = new byte[4096];
        private int pendingCount;
        private int expected = -1;      // records announced by the header, -1 until the header is read
        private int processed;

        /// <summary>Records announced by the header of the response being read.</summary>
        public int Expected { get { return expected < 0 ? 0 : expected; } }

        /// <summary>Records of the current response that have been handed out so far.</summary>
        public int Processed { get { return processed; } }

        /// <summary>Drops any partly received response.</summary>
        public void Reset()
        {
            pendingCount = 0;
            expected = -1;
            processed = 0;
        }

        /// <summary>
        /// Adds count bytes from data and hands out every record that is now complete.
        /// onHeader gets the record count when a response starts; onRecord gets a buffer and the
        /// offset of one record in it, valid only for the duration of the call.
        /// Returns true when this call finished a response; bytes after the last record are dropped.
        /// </summary>
        public bool Feed(byte[] data, int count, Action<int> onHeader, Action<byte[], int> onRecord)
        {
            if (count > 0)
            {
                if (pendingCount + count > pending.Length)
                    Array.Resize(ref pending, Math.Max(pending.Length * 2, pendingCount + count));
                Buffer.BlockCopy(data, 0, pending, pendingCount, count);
                pendingCount += count;
            }

            int offset = 0;

            if (expected < 0)
            {
                if (pendingCount < HeaderSize)
                    return false;

                int announced = BitConverter.ToInt32(pending, 0);
                if (announced < 0 || announced > MaxRecords)
                {
                    Reset();
                    throw new InvalidDataException("Bad record count in server response: " + announced);
                }

                expected = announced;
                processed = 0;
                offset = HeaderSize;
                onHeader(announced);
            }

            while (processed < expected && pendingCount - offset >= RecordSize)
            {
                // Count the record first so one that fails cannot be handed out twice.
                int at = offset;
                offset += RecordSize;
                processed++;
                onRecord(pending, at);
            }

            if (processed >= expected)
            {
                expected = -1;
                processed = 0;
                pendingCount = 0;
                return true;
            }

            // Keep the unfinished record (or header) for the next read.
            int remaining = pendingCount - offset;
            if (remaining > 0 && offset > 0)
                Buffer.BlockCopy(pending, offset, pending, 0, remaining);
            pendingCount = remaining;
            return false;
        }
    }
}
