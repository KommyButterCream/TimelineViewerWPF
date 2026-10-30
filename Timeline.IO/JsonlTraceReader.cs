using System.Text;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Timeline.Format;

namespace Timeline.IO
{
    public sealed class JsonlTraceReader
    {
        private readonly TraceReadOptions _options;

        public JsonlTraceReader(TraceReadOptions? options = null)
        {
            options ??= new TraceReadOptions();

            if (!Enum.IsDefined(typeof(TraceReadMode), options.Mode))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    options.Mode,
                    "지원하지 않는 읽기 모드입니다.");
            }

            if (options.MaxLineLength <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    options.MaxLineLength,
                    "최대 줄 길이는 1 이상이어야 합니다.");
            }

            _options = options;
        }

        public async IAsyncEnumerable<TraceReadResult> ReadAsync(
            string filePath,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            bool headerRead = false;
            bool sessionEnded = false;
            long lastLineNumber = 0;

            await foreach (var item in ReadLinesAsync(filePath, cancellationToken))
            {
                lastLineNumber = item.LineNumber;

                if (sessionEnded)
                {
                    TraceReadResult trailingResult = CreateDiagnostic(
                        item.LineNumber,
                        TraceReadDiagnosticCode.RecordAfterSessionEnd,
                        "세션 종료 이후에 추가 줄이 있습니다.");

                    yield return trailingResult;

                    if (trailingResult.Diagnostic is { IsFatal: true })
                    {
                        yield break;
                    }

                    continue;
                }

                TraceReadResult result = ParseLine(item.LineNumber, item.Line);

                if (!headerRead)
                {
                    if (result.Diagnostic is TraceReadDiagnostic diagnostic)
                    {
                        yield return CreateDiagnostic(
                            item.LineNumber,
                            TraceReadDiagnosticCode.InvalidHeader,
                            $"첫 줄의 헤더를 읽을 수 없습니다: {diagnostic.Message}",
                            forceFatal: true);
                        yield break;
                    }

                    if (result.Record is not TraceHeaderRecord)
                    {
                        yield return CreateDiagnostic(
                            item.LineNumber,
                            TraceReadDiagnosticCode.MissingHeader,
                            "첫 번째 레코드는 헤더여야 합니다.",
                            forceFatal: true);

                        yield break;
                    }

                    headerRead = true;

                    yield return result;
                    continue;
                }

                if (result.Record is TraceHeaderRecord)
                {
                    result = CreateDiagnostic(
                        item.LineNumber,
                        TraceReadDiagnosticCode.DuplicateHeader,
                        "헤더는 파일의 첫 줄에 한 번만 기록할 수 있습니다.");
                }
                else if (result.Record is TraceSessionEndRecord)
                {
                    sessionEnded = true;
                }

                yield return result;

                if (result.Diagnostic is { IsFatal: true })
                {
                    yield break;
                }
            }

            if (!headerRead)
            {
                yield return CreateDiagnostic(
                    1,
                    TraceReadDiagnosticCode.MissingHeader,
                    "파일이 비어 있어 헤더가 없습니다.",
                    forceFatal: true);

                yield break;
            }

            if (!sessionEnded)
            {
                yield return CreateDiagnostic(
                    lastLineNumber,
                    TraceReadDiagnosticCode.MissingSessionEnd,
                    "세션 종료 레코드가 없어 정상 종료를 확인할 수 없습니다.");
            }
        }

        private async IAsyncEnumerable<(long LineNumber, BoundedLine Line)> ReadLinesAsync(
            string filePath,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            cancellationToken.ThrowIfCancellationRequested();

            await using var stream = new FileStream(
                filePath,
                new FileStreamOptions
                {
                    Mode = FileMode.Open,
                    Access = FileAccess.Read,
                    Share = FileShare.Read,
                    Options = FileOptions.Asynchronous | FileOptions.SequentialScan
                });

            using var reader = new StreamReader(
                stream,
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false,
                    throwOnInvalidBytes: true),
                detectEncodingFromByteOrderMarks: false,
                bufferSize: 4096,
                leaveOpen: true);

            var lineReader = new BoundedLineReader(reader, _options.MaxLineLength);

            long lineNumber = 0;

            while (true)
            {
                BoundedLine? line = await lineReader.ReadLineAsync(cancellationToken);

                if (line is not BoundedLine currentLine)
                {
                    yield break;
                }

                lineNumber++;

                yield return (lineNumber, currentLine);
            }
        }

        private TraceReadResult CreateDiagnostic(
            long lineNumber,
            TraceReadDiagnosticCode code,
            string message,
            bool forceFatal = false)
        {
            return TraceReadResult.FromDiagnostic(
                new TraceReadDiagnostic
                {
                    LineNumber = lineNumber,
                    Code = code,
                    Message = message,
                    IsFatal = forceFatal || _options.Mode == TraceReadMode.Strict
                });
        }

        private TraceReadResult ParseLine(long lineNumber, BoundedLine line)
        {
            if (line.IsTooLong)
            {
                return CreateDiagnostic(
                    lineNumber,
                    TraceReadDiagnosticCode.LineTooLong,
                    $"최대 줄 길이 {_options.MaxLineLength}를 초과했습니다.");
            }

            JsonDocument document;

            try
            {
                document = JsonDocument.Parse(line.Text);
            }
            catch (JsonException exception)
            {
                return CreateDiagnostic(
                    lineNumber,
                    TraceReadDiagnosticCode.InvalidJson,
                    exception.Message);
            }

            using (document)
            {
                try
                {
                    TraceRecord record = TraceJsonSerializer.Deserialize(document.RootElement);

                    TraceRecordValidator.Validate(record);

                    return TraceReadResult.FromRecord(lineNumber, record);
                }
                catch (JsonException exception)
                {
                    return CreateDiagnostic(
                        lineNumber,
                        TraceReadDiagnosticCode.InvalidRecord,
                        exception.Message);
                }
                catch (ArgumentException exception)
                {
                    return CreateDiagnostic(
                        lineNumber,
                        TraceReadDiagnosticCode.InvalidRecord,
                        exception.Message);
                }
            }
        }
    }
}
