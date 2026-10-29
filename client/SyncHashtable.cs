using System.Collections;

namespace myseq
{
    /// <summary>
    /// A Hashtable shared between the socket thread, which adds and removes entries, and the
    /// UI thread, which draws them. Writes take a lock, and Snapshot() copies the values under
    /// that lock so callers can loop over them while the table changes.
    /// Single lookups need no lock: a Hashtable allows readers alongside one writer.
    /// </summary>
    public sealed class SyncHashtable
    {
        private readonly Hashtable table = new Hashtable();
        private readonly object sync = new object();

        public object this[object key]
        {
            get { return table[key]; }
            set { lock (sync) { table[key] = value; } }
        }

        public int Count { get { return table.Count; } }

        public bool ContainsKey(object key) { return table.ContainsKey(key); }

        public void Add(object key, object value) { lock (sync) { table.Add(key, value); } }

        public void Remove(object key) { lock (sync) { table.Remove(key); } }

        public void Clear() { lock (sync) { table.Clear(); } }

        /// <summary>A copy of the values that stays valid while the table is modified.</summary>
        public object[] Snapshot()
        {
            lock (sync)
            {
                object[] values = new object[table.Count];
                table.Values.CopyTo(values, 0);
                return values;
            }
        }
    }
}
