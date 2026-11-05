using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PhonePointer
{
    // Callers supply only reason/metrics, never protocol JSON, pairing or resume tokens.
    public sealed class DiagnosticLog : IDisposable
    {
        readonly object gate = new object();
        readonly Queue<string> recent = new Queue<string>();
        readonly BlockingCollection<string> writes = new BlockingCollection<string>(2048);
        readonly Task writer;
        public string FilePath { get; }
        public string FileError { get; private set; }
        public DiagnosticLog(string directory, string prefix)
        {
            Directory.CreateDirectory(directory);
            FilePath = Path.Combine(directory, prefix + "-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".log");
            writer = Task.Run(() =>
            {
                try
                {
                    using (var stream = new StreamWriter(FilePath, false, new UTF8Encoding(false)))
                    {
                        foreach (var entry in writes.GetConsumingEnumerable()) { stream.WriteLine(entry); stream.Flush(); }
                    }
                }
                catch (Exception e) { FileError = e.Message; }
            });
        }
        public void Add(string session, string kind, string detail)
        {
            string line = DateTime.UtcNow.ToString("O") + "\t" + NetworkClock.Now.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)
                + "\t" + Clean(session) + "\t" + Clean(kind) + "\t" + Clean(detail);
            lock (gate) { recent.Enqueue(line); while (recent.Count > 512) recent.Dequeue(); }
            try { if (!writes.TryAdd(line)) FileError = "Diagnostic write queue overflow"; }
            catch (InvalidOperationException) { }
        }
        public string[] Snapshot() { lock (gate) return recent.ToArray(); }
        public Task<string> ExportAsync(string directory)
        {
            var copy = Snapshot();
            return Task.Run(() => { Directory.CreateDirectory(directory); string path = Path.Combine(directory, "phonepointer-export-" + Guid.NewGuid().ToString("N") + ".log"); File.WriteAllLines(path, copy); return path; });
        }
        static string Clean(string value) => (value ?? "").Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ');
        public void Dispose() { if (!writes.IsAddingCompleted) writes.CompleteAdding(); }
    }
}
