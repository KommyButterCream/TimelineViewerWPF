using System.Text;
using Timeline.Format;

namespace Timeline.IO
{
    public sealed class JsonlTraceWriter : ITraceWriter
    {
        private readonly StreamWriter _writer;

        private bool _headerWritten;
        private bool _sessionEnded;
        private bool _faulted;
        private bool _disposed;

        public JsonlTraceWriter(string filePath)
            : this(OpenFile(filePath))
        {
        }

        private static FileStream OpenFile(string filePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

            return new FileStream(
                filePath,
                new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.Write,
                    Share = FileShare.Read,
                    Options = FileOptions.Asynchronous
                });

        }

        /// <summary>
        /// 쓰기 가능한 스트림에 기록합니다. leaveOpen이 false이면 스트림을 소유하고 정리합니다.
        /// </summary>
        public JsonlTraceWriter(Stream stream, bool leaveOpen = false)
        {
            ArgumentNullException.ThrowIfNull(stream);

            try
            {
                _writer = new StreamWriter(
                    stream,
                    new UTF8Encoding(
                        encoderShouldEmitUTF8Identifier: false,
                        throwOnInvalidBytes: true),
                    bufferSize: 1024,
                    leaveOpen: leaveOpen)
                {
                    NewLine = "\n"
                };
            }
            catch
            {
                if (!leaveOpen)
                {
                    stream.Dispose();
                }
                throw;
            }
        }

        public async Task WriteAsync(TraceRecord record, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (_faulted)
            {
                throw new InvalidOperationException("이전 I/O 작업이 실패하여 Writer를 계속 사용할 수 없습니다.");
            }

            if (_sessionEnded)
            {
                throw new InvalidOperationException("세션 종료 이후에는 레코드를 기록할 수 없습니다.");
            }

            cancellationToken.ThrowIfCancellationRequested();

            TraceRecordValidator.Validate(record);

            bool isHeader = record is TraceHeaderRecord;

            if (!_headerWritten && !isHeader)
            {
                throw new InvalidOperationException("첫 번째 레코드는 헤더여야 합니다.");
            }

            if (_headerWritten && isHeader)
            {
                throw new InvalidOperationException("헤더는 한 번만 기록할 수 있습니다.");
            }

            string json = TraceJsonSerializer.Serialize(record);

            try
            {
                await _writer.WriteLineAsync(json.AsMemory(), cancellationToken);
            }
            catch
            {
                _faulted = true;
                throw;
            }

            if (isHeader)
            {
                _headerWritten = true;
            }

            if (record is TraceSessionEndRecord)
            {
                _sessionEnded = true;
            }
        }

        public async Task FlushAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (_faulted)
            {
                throw new InvalidOperationException("이전 I/O 작업이 실패하여 Writer를 계속 사용할 수 없습니다.");
            }

            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await _writer.FlushAsync(cancellationToken);
            }
            catch
            {
                _faulted = true;
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            await _writer.DisposeAsync();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(JsonlTraceWriter));
            }
        }
    }
}
