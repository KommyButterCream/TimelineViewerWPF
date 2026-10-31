using System.Text.Json;
using Timeline.Format;
using Timeline.IO;

internal static class SerializerChecks
{
    public static void VerifySerialize()
    {
        Console.WriteLine("\nTEST 1 - 레코드 Json Serialize 출력");
        TraceRecord[] records = SampleTraceData.CreateRecords();

        foreach (TraceRecord record in records)
        {
            TraceRecordValidator.Validate(record);

            string json = TraceJsonSerializer.Serialize(record);
            Console.WriteLine(json);
        }
    }

    public static void VerifyFieldOrder()
    {
        Console.WriteLine("\nTEST 11 - type 필드가 마지막에 있는 JSON 읽기");
        string reorderedJson = """{"id":"42","time":7000,"type":"end"}""";

        TraceRecord reorderedRecord = TraceJsonSerializer.Deserialize(reorderedJson);

        TraceRecordValidator.Validate(reorderedRecord);

        if (reorderedRecord is not TraceEndRecord end || end.Id != "42" || end.TimeUs != 7000)
        {
            throw new Exception("검증 실패: 필드 순서가 다른 End 레코드를 올바르게 읽지 못했습니다.");
        }

        Console.WriteLine("JSON 필드 순서와 무관한 역직렬화 확인 완료");
    }

    public static void VerifyMissingField()
    {
        Console.WriteLine("\nTEST 12 - 헤더의 version 필드 누락 검사");
        string missingVersionJson =
            """
            {
                "type": "header",
                "format": "timeline",
                "timeUnit": "us",
                "originUtc": "2026-10-30T00:00:00Z"
            }
            """;

        bool missingVersionRejected = false;

        try
        {
            TraceJsonSerializer.Deserialize(missingVersionJson);
        }
        catch (System.Text.Json.JsonException exception)
        {
            missingVersionRejected = true;
            Console.WriteLine($"예상한 거부: {exception.Message}");
        }

        if (!missingVersionRejected)
        {
            throw new Exception("검증 실패 : version이 없는 헤더가 허용되었습니다.");
        }

        Console.WriteLine("필수 필드 누락 거부 확인 완료");
    }

    public static void VerifyErrorKinds()
    {
        Console.WriteLine("\nTEST 13 - JSON 문법 오류와 레코드 형식 오류 구분");
        string[] invalidInputs =
            [
                """{"type":"end","id":"42","time":""",
                """{"type":"end","id":"42"}"""
            ];

        foreach (string input in invalidInputs)
        {
            JsonDocument document;

            try
            {
                document = JsonDocument.Parse(input);
            }
            catch (JsonException exception)
            {
                Console.WriteLine($"InvalidJson : {exception.Message}");
                continue;
            }

            using (document)
            {
                try
                {
                    TraceRecord record = TraceJsonSerializer.Deserialize(document.RootElement);

                    TraceRecordValidator.Validate(record);

                    throw new Exception("검증 실패 : 잘못된 입력이 통과했습니다.");
                }
                catch (JsonException exception)
                {
                    Console.WriteLine($"InvalidRecord : {exception.Message}");
                }
                catch (ArgumentException exception)
                {
                    Console.WriteLine($"InvalidRecord : {exception.Message}");
                }
            }
        }
    }
}
