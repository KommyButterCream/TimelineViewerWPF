using System.Text.Json;
using Timeline.Format;
using Timeline.IO;

internal static class ValidatorChecks
{
    public static void VerifyHeader()
    {
        Console.WriteLine("\nTEST 2 - TraceRecordValidator(TraceHeaderRecord) 예외 테스트");
        var validHeader = new TraceHeaderRecord
        {
            OriginUtc = new DateTimeOffset(
                2026, 10, 30, 0, 0, 0, TimeSpan.Zero)
        };

        TraceRecordValidator.Validate(validHeader);
        Console.WriteLine("정상 헤더 : 검증 통과");

        var invalidHeader = validHeader with
        {
            Version = -1
        };

        try
        {
            TraceRecordValidator.Validate(invalidHeader);
            throw new Exception("예상과 다름 : 잘못된 헤더가 통과했습니다.");
        }
        catch (ArgumentException exception)
        {
            Console.WriteLine($"잘못된 헤더 : {exception.Message}");
        }
    }

    public static void VerifyTrack()
    {
        Console.WriteLine("\nTEST 3 - TraceRecordValidator(TraceTrackRecord) 예외 테스트");
        var validTrack = new TraceTrackRecord
        {
            Id = "main",
            Name = "Main Thread"
        };

        TraceRecordValidator.Validate(validTrack);
        Console.WriteLine("정상 Track: 검증 통과");

        var invalidTrack = validTrack with
        {
            Id = "   "
        };

        try
        {
            TraceRecordValidator.Validate(invalidTrack);
            throw new Exception("예상과 다름: 잘못된 Track이 통과했습니다.");
        }
        catch (ArgumentException exception)
        {
            Console.WriteLine($"잘못된 Track: {exception.Message}");
        }
    }

    public static void VerifySpan()
    {
        Console.WriteLine("\nTEST 4 - TraceRecordValidator(TraceBeginRecord, TraceEndRecord) 예외 테스트");
        var beginForValidation = new TraceBeginRecord
        {
            Id = "100",
            TrackId = "main",
            Name = "Update",
            TimeUs = 0
        };

        var endForValidation = new TraceEndRecord
        {
            Id = "100",
            TimeUs = TimelineFormat.MaxTimestampMicroseconds
        };

        TraceRecordValidator.Validate(beginForValidation);
        TraceRecordValidator.Validate(endForValidation);

        Console.WriteLine("시간 최솟값과 최댓값: 검증 통과");

        try
        {
            TraceRecordValidator.Validate(
                endForValidation with { TimeUs = -1 });
        }
        catch (ArgumentException exception)
        {
            Console.WriteLine($"음수 시간 거부: {exception.Message}");
        }

        try
        {
            TraceRecordValidator.Validate(
                beginForValidation with { ParentId = "100" });
        }
        catch (ArgumentException exception)
        {
            Console.WriteLine($"자기 부모 지정 거부: {exception.Message}");
        }
    }

    public static void VerifySessionEnd()
    {
        Console.WriteLine("\nTEST 5 - TraceRecordValidator(TraceSessionEndRecord) 예외 테스트");
        TraceRecord invalidSessionEnd = new TraceSessionEndRecord
        {
            TimeUs = 8000,
            DroppedRecordCount = -1
        };

        try
        {
            TraceRecordValidator.Validate(invalidSessionEnd);
            throw new Exception("예상과 다름: 음수 누락 수가 통과했습니다.");
        }
        catch (ArgumentException exception)
        {
            Console.WriteLine($"음수 누락 수 거부: {exception.Message}");
        }
    }

}
