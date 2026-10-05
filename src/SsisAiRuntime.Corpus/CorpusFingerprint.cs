using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SsisAiRuntime.Corpus
{
    public interface ICorpusFingerprintProvider
    {
        CorpusFingerprint Create(CorpusSnapshot snapshot);
    }

    public sealed class CorpusFingerprint
    {
        internal CorpusFingerprint(string schemaVersion, string sha256, bool isComplete)
        {
            SchemaVersion = schemaVersion;
            Sha256 = sha256;
            IsComplete = isComplete;
        }

        public string SchemaVersion { get; }
        public string Sha256 { get; }
        public bool IsComplete { get; }
    }

    public sealed class CorpusFingerprintProvider : ICorpusFingerprintProvider
    {
        private const string FingerprintFormat = "ssis-ai-runtime.corpus.fingerprint";

        public CorpusFingerprint Create(CorpusSnapshot snapshot)
        {
            if (snapshot == null) { throw new ArgumentNullException(nameof(snapshot)); }

            byte[] canonicalBytes;
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream, new UTF8Encoding(false), true))
                {
                    writer.Write(FingerprintFormat);
                    writer.Write(snapshot.SchemaVersion);
                    writer.Write(snapshot.PackageId);
                    writer.Write(snapshot.PackageName);

                    var nodes = snapshot.Nodes.OrderBy(node => node.Key, StringComparer.Ordinal)
                        .ThenBy(node => node.Kind)
                        .ThenBy(node => node.Name, StringComparer.Ordinal)
                        .ThenBy(node => node.NativeId, StringComparer.Ordinal)
                        .ThenBy(node => node.ParentId, StringComparer.Ordinal)
                        .ToArray();
                    writer.Write(nodes.Length);
                    foreach (var node in nodes)
                    {
                        writer.Write(node.Key);
                        writer.Write(node.Kind.ToString());
                        writer.Write(node.Name);
                        writer.Write(node.NativeId);
                        writer.Write(node.ParentId);
                    }

                    var edges = snapshot.Edges.OrderBy(edge => edge.From, StringComparer.Ordinal)
                        .ThenBy(edge => edge.To, StringComparer.Ordinal)
                        .ThenBy(edge => edge.Kind)
                        .ThenBy(edge => edge.Evidence, StringComparer.Ordinal)
                        .ToArray();
                    writer.Write(edges.Length);
                    foreach (var edge in edges)
                    {
                        writer.Write(edge.From);
                        writer.Write(edge.To);
                        writer.Write(edge.Kind.ToString());
                        writer.Write(edge.Evidence);
                    }

                    var coverage = snapshot.CoverageGaps.OrderBy(gap => gap.ReasonCode, StringComparer.Ordinal)
                        .ThenBy(gap => gap.Count)
                        .ToArray();
                    writer.Write(coverage.Length);
                    foreach (var gap in coverage)
                    {
                        writer.Write(gap.ReasonCode);
                        writer.Write(gap.Count);
                    }
                    writer.Flush();
                }
                canonicalBytes = stream.ToArray();
            }

            using (var algorithm = SHA256.Create())
            {
                var digest = algorithm.ComputeHash(canonicalBytes);
                var sha256 = BitConverter.ToString(digest).Replace("-", string.Empty);
                return new CorpusFingerprint(snapshot.SchemaVersion, sha256, snapshot.IsComplete);
            }
        }
    }
}