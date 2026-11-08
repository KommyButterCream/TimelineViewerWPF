using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Timeline.Analysis;
using Timeline.Format;
using Timeline.IO;
using Timeline.Viewer.Models;

namespace Timeline.Viewer.Services
{
    public sealed class TraceLoadService : ITraceLoadService
    {
        public Task<TraceDocument> LoadAsync(
            string filePath,
            TraceReadMode mode = TraceReadMode.Recovery,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

            string fullPath = Path.GetFullPath(filePath);

            return Task.Run(
                () => LoadCoreAsync(fullPath, mode, cancellationToken), cancellationToken);
        }

        private static async Task<TraceDocument> LoadCoreAsync(
            string filePath,
            TraceReadMode mode,
            CancellationToken cancellationToken)
        {
            var reader = new JsonlTraceReader(new TraceReadOptions
            {
                Mode = mode
            });

            var assembler = new TraceAssembler();
            var readDiagnostics = new List<TraceReadDiagnostic>();

            await foreach(TraceReadResult result in reader.ReadAsync(filePath, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if(result.Diagnostic is TraceReadDiagnostic diagnostic)
                {
                    readDiagnostics.Add(diagnostic);

                    if(diagnostic.IsFatal)
                    {
                        throw new InvalidDataException(
                            $"{diagnostic.LineNumber}행 "
                            + $"[{diagnostic.Code}]: {diagnostic.Message}");
                    }

                    continue;
                }

                if(result.Record is TraceRecord record)
                {
                    assembler.Add(record, result.LineNumber);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            TraceAnalysisResult analysis = assembler.Build();

            cancellationToken.ThrowIfCancellationRequested();

            return new TraceDocument
            {
                FilePath = filePath,
                Analysis = analysis,
                ReadDiagnostics = readDiagnostics.AsReadOnly()
            };
        }
    }
}
