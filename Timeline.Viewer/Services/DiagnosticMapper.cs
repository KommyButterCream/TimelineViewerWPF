using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Timeline.Analysis;
using Timeline.IO;
using Timeline.Viewer.Models;

namespace Timeline.Viewer.Services
{
    public static class DiagnosticMapper
    {
        public static IReadOnlyList<DiagnosticItem> Map(
            TraceDocument document, 
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(document);

            var items = new List<DiagnosticItem>();

            foreach (TraceReadDiagnostic diagnostic in document.ReadDiagnostics)
            {
                cancellationToken.ThrowIfCancellationRequested();

                items.Add(new DiagnosticItem
                {
                    Source = DiagnosticSource.Reader,
                    Code = diagnostic.Code.ToString(),
                    Message = diagnostic.Message,
                    FilePath = document.FilePath,
                    LineNumber = NormalizeLineNumber(diagnostic.LineNumber),
                    IsFatal = diagnostic.IsFatal
                });
            }

            foreach (TraceAnalysisDiagnostic diagnostic in document.Analysis.Diagnostics)
            {
                cancellationToken.ThrowIfCancellationRequested();

                items.Add(new DiagnosticItem
                {
                    Source = DiagnosticSource.Analysis,
                    Code = diagnostic.Code.ToString(),
                    Message = diagnostic.Message,
                    FilePath = document.FilePath,
                    LineNumber = NormalizeLineNumber(diagnostic.LineNumber),
                    RelatedLineNumber = NormalizeLineNumber(diagnostic.RelatedLineNumber),
                    SpanId = diagnostic.SpanId,
                    TrackId = diagnostic.TrackId,
                    IsFatal = false
                });
            }

            cancellationToken.ThrowIfCancellationRequested();

            return items.AsReadOnly();
        }

        private static long? NormalizeLineNumber(long? lineNumber)
        {
            return lineNumber is > 0 ? lineNumber : null;
        }
    }
}
