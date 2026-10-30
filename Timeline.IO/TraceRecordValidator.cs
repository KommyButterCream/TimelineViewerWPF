using Timeline.Format;

namespace Timeline.IO
{
    public static class TraceRecordValidator
    {
        public static void Validate(TraceRecord record)
        {
            ArgumentNullException.ThrowIfNull(record);

            switch (record)
            {
                case TraceHeaderRecord header:
                    Validate(header);
                    break;

                case TraceTrackRecord track:
                    Validate(track);
                    break;

                case TraceBeginRecord begin:
                    Validate(begin);
                    break;

                case TraceEndRecord end:
                    Validate(end);
                    break;

                case TraceSessionEndRecord sessionEnd:
                    Validate(sessionEnd);
                    break;

                default:
                    throw new ArgumentException(
                        $"지원하지 않는 레코드 타입입니다: {record.GetType().Name}",
                        nameof(record));
            }
        }

        public static void Validate(TraceHeaderRecord record)
        {
            ArgumentNullException.ThrowIfNull(record);

            if (record.Format != TimelineFormat.FormatName)
            {
                throw new ArgumentException(
                    $"포맷 이름은 '{TimelineFormat.FormatName}'이어야 합니다.",
                    nameof(record));
            }

            if (record.Version != TimelineFormat.CurrentVersion)
            {
                throw new ArgumentException(
                    $"지원하지 않는 포맷 버전입니다: {record.Version}",
                    nameof(record));
            }

            if (record.TimeUnit != TimelineFormat.TimeUnit)
            {
                throw new ArgumentException(
                    $"시간 단위는 '{TimelineFormat.TimeUnit}'이어야 합니다.",
                    nameof(record));
            }

            if (record.OriginUtc.Offset != TimeSpan.Zero)
            {
                throw new ArgumentException(
                    "OriginUtc는 UTC 오프셋이 0이어야 합니다.",
                    nameof(record));
            }
        }

        public static void Validate(TraceTrackRecord record)
        {
            ArgumentNullException.ThrowIfNull(record);

            ValidateId(record.Id, nameof(record.Id));

            if (string.IsNullOrWhiteSpace(record.Name))
            {
                throw new ArgumentException(
                    "Track 이름은 비어 있거나 공백으로만 구성될 수 없습니다.",
                    nameof(record.Name));
            }
        }

        public static void Validate(TraceBeginRecord record)
        {
            ArgumentNullException.ThrowIfNull(record);

            ValidateId(record.Id, nameof(record.Id));
            ValidateId(record.TrackId, nameof(record.TrackId));
            ValidateTime(record.TimeUs, nameof(record.TimeUs));

            if (string.IsNullOrWhiteSpace(record.Name))
            {
                throw new ArgumentException(
                    "작업 이름은 비어 있거나 공백으로만 구성될 수 없습니다.",
                    nameof(record.Name));
            }

            if (record.ParentId is not null)
            {
                ValidateId(record.ParentId, nameof(record.ParentId));

                if (string.Equals(
                    record.Id,
                    record.ParentId,
                    StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        "작업은 자기 자신을 부모로 지정할 수 없습니다.",
                        nameof(record.ParentId));
                }
            }
        }

        public static void Validate(TraceSessionEndRecord record)
        {
            ArgumentNullException.ThrowIfNull(record);

            ValidateTime(record.TimeUs, nameof(record.TimeUs));

            if (record.DroppedRecordCount < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(record.DroppedRecordCount),
                    record.DroppedRecordCount,
                    "누락 레코드 수는 0 이상이어야 합니다.");
            }
        }

        public static void Validate(TraceEndRecord record)
        {
            ArgumentNullException.ThrowIfNull(record);

            ValidateId(record.Id, nameof(record.Id));
            ValidateTime(record.TimeUs, nameof(record.TimeUs));
        }

        private static void ValidateId(string? value, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    $"{fieldName}는 비어 있거나 공백으로만 구성될 수 없습니다.",
                    fieldName);
            }
        }

        private static void ValidateTime(long timeUs, string fieldName)
        {
            if (timeUs < 0 || timeUs > TimelineFormat.MaxTimestampMicroseconds)
            {
                throw new ArgumentOutOfRangeException(
                    fieldName,
                    timeUs,
                    $"상대 시간은 0부터 {TimelineFormat.MaxTimestampMicroseconds}까지여야 합니다.");
            }
        }
    }
}
