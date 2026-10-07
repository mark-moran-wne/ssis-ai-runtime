using System;
using System.IO;

namespace SsisAiRuntime.Mutations
{
    public sealed class MutationArtifactStager
    {
        public void StageAndPublish(string sourcePath, string destinationPath,
            Action<Stream> writeArtifact, Func<Stream, bool> verifyArtifact)
        {
            if (writeArtifact == null) { throw new ArgumentNullException(nameof(writeArtifact)); }
            if (verifyArtifact == null) { throw new ArgumentNullException(nameof(verifyArtifact)); }
            var source = PackagePath(sourcePath);
            var destination = PackagePath(destinationPath);
            if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
            { throw new InvalidOperationException("mutation.destination.same_as_source"); }
            RejectReparsePoints(source);
            RejectReparsePoints(destination);
            if (File.Exists(destination) || Directory.Exists(destination))
            { throw new InvalidOperationException("mutation.destination.exists"); }

            var directory = Path.GetDirectoryName(destination);
            if (!Directory.Exists(directory))
            { throw new InvalidOperationException("mutation.destination.directory_missing"); }
            var stagedPath = Path.Combine(directory, ".ssis-mutation-" + Guid.NewGuid().ToString("N") + ".dtsx");
            var ownsStagingFile = false;
            using (var sourceLock = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                try
                {
                    using (var staging = new FileStream(stagedPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        ownsStagingFile = true;
                        writeArtifact(staging);
                        staging.Flush(true);
                    }
                    using (var staging = new FileStream(stagedPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        if (staging.Length == 0 || !verifyArtifact(staging))
                        { throw new InvalidOperationException("mutation.staging.verification_failed"); }
                    }
                    RejectReparsePoints(destination);
                    File.Move(stagedPath, destination);
                    ownsStagingFile = false;
                }
                finally
                {
                    if (ownsStagingFile) { File.Delete(stagedPath); }
                }
            }
        }

        private static string PackagePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) { throw new ArgumentException("A package path is required.", nameof(path)); }
            var fullPath = Path.GetFullPath(path);
            if (!string.Equals(Path.GetExtension(fullPath), ".dtsx", StringComparison.OrdinalIgnoreCase))
            { throw new ArgumentException("mutation.path.extension_invalid", nameof(path)); }
            return fullPath;
        }

        private static void RejectReparsePoints(string path)
        {
            var current = path;
            while (!string.IsNullOrEmpty(current))
            {
                if ((File.Exists(current) || Directory.Exists(current)) &&
                    (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                { throw new InvalidOperationException("mutation.path.reparse_point"); }
                current = Path.GetDirectoryName(current);
            }
        }
    }
}