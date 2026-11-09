using System;
using System.IO;

namespace Timeline.Viewer.Models
{
    // 길이와 최종 수정 시각으로 일반적인 외부 변경을 감지합니다.
    public sealed record SourceFileStamp(long Length, DateTime LastWriteTimeUtc)
    {
        public static SourceFileStamp Capture(string path)
        {
            var file = new FileInfo(path);
            if (!file.Exists)
                throw new FileNotFoundException("원본 파일을 찾을 수 없습니다.", path);
            return new(file.Length, file.LastWriteTimeUtc);
        }
    }
}
