using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Timeline.Viewer.Models;

namespace Timeline.Viewer.Services
{
    public sealed class SourcePreviewService
    {
        public Task<string> ReadAsync(TraceDocument document, long lineNumber,
            CancellationToken cancellationToken = default)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(lineNumber, 1);
            return Task.Run(async () =>
            {
                VerifyUnchanged(document);
                long first = Math.Max(1, lineNumber - 2);
                long last = lineNumber > long.MaxValue - 2 ? long.MaxValue : lineNumber + 2;
                long current = 1;
                bool found = false;
                bool hasCharacters = false;
                bool previousWasCr = false;
                var result = new StringBuilder();
                var line = new StringBuilder();
                bool truncated = false;

                void AppendLine()
                {
                    if (current == lineNumber) found = true;
                    if (current >= first && current <= last)
                        result.Append(current == lineNumber ? "> " : "  ")
                            .Append(current).Append(" | ").Append(line)
                            .AppendLine(truncated ? " … [긴 줄 생략]" : string.Empty);
                    line.Clear();
                    truncated = false;
                    hasCharacters = false;
                }

                // ReadLine 대신 고정 버퍼로 읽어 비정상적으로 긴 줄도 메모리를 제한합니다.
                using var reader = new StreamReader(document.FilePath, Encoding.UTF8, true);
                var buffer = new char[8192];
                while (current <= last)
                {
                    int count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
                    if (count == 0)
                    {
                        if (hasCharacters) AppendLine();
                        break;
                    }
                    for (int index = 0; index < count && current <= last; index++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        char ch = buffer[index];
                        if (ch == '\n' && previousWasCr)
                        {
                            previousWasCr = false;
                            continue;
                        }
                        previousWasCr = ch == '\r';
                        if (ch is '\r' or '\n')
                        {
                            AppendLine();
                            current++;
                        }
                        else
                        {
                            hasCharacters = true;
                            if (current >= first)
                            {
                                if (line.Length < 2000) line.Append(ch);
                                else truncated = true;
                            }
                        }
                    }
                }
                cancellationToken.ThrowIfCancellationRequested();
                VerifyUnchanged(document);
                return found ? result.ToString() : "해당 줄이 원본 파일에 없습니다.";
            }, cancellationToken);
        }

        private static void VerifyUnchanged(TraceDocument document)
        {
            if (document.SourceStamp is null)
                throw new IOException("원본 파일 정보를 확인할 수 없습니다. 파일을 다시 열어주세요.");
            if (SourceFileStamp.Capture(document.FilePath) != document.SourceStamp)
                throw new IOException("파일이 로딩 이후 변경되었습니다. 줄 위치가 달라질 수 있으므로 파일을 다시 열어주세요.");
        }
    }
}
