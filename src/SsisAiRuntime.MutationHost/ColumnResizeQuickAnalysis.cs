using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualBasic.FileIO;
using Microsoft.SqlServer.Dts.Pipeline.Wrapper;
using Microsoft.SqlServer.Dts.Runtime.Wrapper;

namespace SsisAiRuntime.MutationHost
{
    public sealed class ColumnResizeQuickAnalysis
    {
        internal ColumnResizeQuickAnalysis(string sourceArtifactHash, string taskNativeId, int sourceComponentId,
            int outputColumnId, int destinationComponentId, int proposedWidth, string inputFilePath,
            string inputFileHash, int rowsScanned, int sampleRowLimit, int? maximumObservedWidth,
            bool reachedEndOfFile, bool wouldTruncateObservedData, string diagnosticCode)
        {
            SourceArtifactHash = sourceArtifactHash ?? string.Empty;
            TaskNativeId = taskNativeId ?? string.Empty;
            SourceComponentId = sourceComponentId;
            OutputColumnId = outputColumnId;
            DestinationComponentId = destinationComponentId;
            ProposedWidth = proposedWidth;
            InputFilePath = inputFilePath ?? string.Empty;
            InputFileHash = inputFileHash ?? string.Empty;
            RowsScanned = rowsScanned;
            SampleRowLimit = sampleRowLimit;
            MaximumObservedWidth = maximumObservedWidth;
            ReachedEndOfFile = reachedEndOfFile;
            WouldTruncateObservedData = wouldTruncateObservedData;
            DiagnosticCode = diagnosticCode ?? string.Empty;
        }

        public string SourceArtifactHash { get; }
        public string TaskNativeId { get; }
        public int SourceComponentId { get; }
        public int OutputColumnId { get; }
        public int DestinationComponentId { get; }
        public int ProposedWidth { get; }
        public int RowsScanned { get; }
        public int SampleRowLimit { get; }
        public int? MaximumObservedWidth { get; }
        public bool ReachedEndOfFile { get; }
        public bool WouldTruncateObservedData { get; }
        public string DiagnosticCode { get; }
        public bool Succeeded => string.IsNullOrEmpty(DiagnosticCode);
        public bool IsSampled => !ReachedEndOfFile;
        public string DataLossConfirmationMessage => MaximumObservedWidth.HasValue
            ? "The scanned data's maximum encoded width is " + MaximumObservedWidth.Value + " across " + RowsScanned +
                " rows" + (IsSampled ? " (sample only)" : "") + ". Resizing may truncate longer existing or future data. Confirm the risk before shrinking."
            : "The data width could not be measured. Resizing may truncate existing or future data. Confirm the risk before shrinking.";

        internal string InputFilePath { get; }
        internal string InputFileHash { get; }

        internal static ColumnResizeQuickAnalysis Analyze(string filePath, bool unicode, int codePage,
            bool hasHeader, bool textQualified, IEnumerable<string> delimiters, int columnIndex,
            DataType dataType, string sourceArtifactHash, string taskNativeId, int sourceComponentId,
            int outputColumnId, int destinationComponentId, int targetWidth, int sampleRowLimit)
        {
            if (sampleRowLimit < 1 || sampleRowLimit > 10000)
            { throw new ArgumentOutOfRangeException(nameof(sampleRowLimit)); }
            try { filePath = Path.GetFullPath(filePath); }
            catch (Exception)
            { return Unavailable(sourceArtifactHash, taskNativeId, sourceComponentId, outputColumnId, destinationComponentId,
                targetWidth, string.Empty, sampleRowLimit, "mutation.resize.analysis.source_unavailable"); }
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            { return Unavailable(sourceArtifactHash, taskNativeId, sourceComponentId, outputColumnId, destinationComponentId,
                targetWidth, filePath, sampleRowLimit, "mutation.resize.analysis.source_unavailable"); }
            if (columnIndex < 0 || (dataType != DataType.DT_WSTR && dataType != DataType.DT_STR))
            { return Unavailable(sourceArtifactHash, taskNativeId, sourceComponentId, outputColumnId, destinationComponentId,
                targetWidth, filePath, sampleRowLimit, "mutation.resize.analysis.column_unsupported"); }

            try
            {
                var encoding = unicode ? Encoding.Unicode : Encoding.GetEncoding(codePage);
                var inputHash = HashFile(filePath);
                using (var parser = new TextFieldParser(filePath, encoding, detectEncoding: true))
                {
                    parser.TextFieldType = FieldType.Delimited;
                    parser.HasFieldsEnclosedInQuotes = textQualified;
                    var fieldDelimiters = (delimiters ?? Enumerable.Empty<string>())
                        .Where(delimiter => !string.IsNullOrEmpty(delimiter) && delimiter != "\r" && delimiter != "\n" &&
                            delimiter != "\r\n" && delimiter != "\n\r")
                        .Distinct(StringComparer.Ordinal).ToArray();
                    if (fieldDelimiters.Length > 0) { parser.SetDelimiters(fieldDelimiters); }

                    if (hasHeader && !parser.EndOfData) { parser.ReadFields(); }
                    var rows = 0;
                    var maximum = 0;
                    while (!parser.EndOfData && rows < sampleRowLimit)
                    {
                        var fields = parser.ReadFields();
                        if (fields == null || columnIndex >= fields.Length)
                        { return new ColumnResizeQuickAnalysis(sourceArtifactHash, taskNativeId, sourceComponentId, outputColumnId,
                            destinationComponentId, targetWidth, filePath, inputHash, rows, sampleRowLimit, null, false, false,
                            "mutation.resize.analysis.row_shape_unavailable"); }
                        var value = fields[columnIndex] ?? string.Empty;
                        var observedWidth = dataType == DataType.DT_WSTR
                            ? value.Length
                            : encoding.GetByteCount(value);
                        maximum = Math.Max(maximum, observedWidth);
                        rows++;
                    }
                    var reachedEnd = parser.EndOfData;
                    if (HashFile(filePath) != inputHash)
                    { return Unavailable(sourceArtifactHash, taskNativeId, sourceComponentId, outputColumnId, destinationComponentId,
                        targetWidth, filePath, sampleRowLimit, "mutation.resize.analysis.source_changed"); }
                    return new ColumnResizeQuickAnalysis(sourceArtifactHash, taskNativeId, sourceComponentId, outputColumnId,
                        destinationComponentId, targetWidth, filePath, inputHash, rows, sampleRowLimit, maximum, reachedEnd,
                        maximum > targetWidth, string.Empty);
                }
            }
            catch (MalformedLineException)
            { return Unavailable(sourceArtifactHash, taskNativeId, sourceComponentId, outputColumnId, destinationComponentId,
                targetWidth, filePath, sampleRowLimit, "mutation.resize.analysis.malformed_input"); }
            catch (Exception)
            { return Unavailable(sourceArtifactHash, taskNativeId, sourceComponentId, outputColumnId, destinationComponentId,
                targetWidth, filePath, sampleRowLimit, "mutation.resize.analysis.unavailable"); }
        }

        internal bool InputFileUnchanged()
        {
            try { return File.Exists(InputFilePath) && HashFile(InputFilePath) == InputFileHash; }
            catch (Exception) { return false; }
        }

        internal static ColumnResizeQuickAnalysis Unavailable(string sourceHash, string taskId, int sourceId,
            int columnId, int destinationId, int width, string inputPath, int sampleRowLimit, string code) =>
            new ColumnResizeQuickAnalysis(sourceHash, taskId, sourceId, columnId, destinationId, width,
                inputPath, string.Empty, 0, sampleRowLimit, null, false, false, code);

        private static string HashFile(string path)
        {
            using (var algorithm = SHA256.Create())
            using (var stream = File.OpenRead(path))
            { return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", ""); }
        }
    }
}