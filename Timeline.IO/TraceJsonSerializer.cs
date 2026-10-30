using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Timeline.Format;

namespace Timeline.IO
{
    public static class TraceJsonSerializer
    {
        private static readonly JsonSerializerOptions Options = CreateOptions();

        private static JsonSerializerOptions CreateOptions()
        {
            var resolver = new DefaultJsonTypeInfoResolver();
            resolver.Modifiers.Add(ConfigureRecord);

            return new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase, // 속성 이름을 소문자로 시작
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, // null 인 속성을 출력/저장하지 않음
                WriteIndented = false, // 들여쓰기 끄기
                AllowOutOfOrderMetadataProperties = true, // 프로퍼티 순서에 영향 없이 읽을 수 있도록
                TypeInfoResolver = resolver
            };
        }

        private static void ConfigureRecord(JsonTypeInfo typeInfo)
        {
            if (!typeof(TraceRecord).IsAssignableFrom(typeInfo.Type))
            {
                return;
            }

            if (typeInfo.Type == typeof(TraceRecord))
            {
                typeInfo.PolymorphismOptions = new JsonPolymorphismOptions
                {
                    TypeDiscriminatorPropertyName = "type",
                    DerivedTypes =
                    {
                        new JsonDerivedType(typeof(TraceHeaderRecord), "header"),
                        new JsonDerivedType(typeof(TraceTrackRecord), "track"),
                        new JsonDerivedType(typeof(TraceBeginRecord), "begin"),
                        new JsonDerivedType(typeof(TraceEndRecord), "end"),
                        new JsonDerivedType(typeof(TraceSessionEndRecord), "sessionEnd"),
                    }
                };
            }

            foreach (var property in typeInfo.Properties)
            {
                property.Name = property.Name switch
                {
                    "trackId" => "track",
                    "parentId" => "parent",
                    "timeUs" => "time",
                    _ => property.Name
                };
            }
        }

        public static string Serialize(TraceRecord record)
        {
            ArgumentNullException.ThrowIfNull(record);

            if (record is TraceBeginRecord { Args: not null } begin)
            {
                foreach (JsonElement value in begin.Args.Values)
                {
                    ValidateUniqueProperties(value);
                }
            }

            return JsonSerializer.Serialize<TraceRecord>(record, Options);
        }

        public static TraceRecord Deserialize(string json)
        {
            ArgumentNullException.ThrowIfNull(json);

            using var document = JsonDocument.Parse(json);

            return Deserialize(document.RootElement);
        }

        public static TraceRecord Deserialize(JsonElement root)
        {
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("레코드는 JSON 객체여야 합니다.");
            }

            ValidateUniqueProperties(root);

            if (!root.TryGetProperty("type", out JsonElement typeElement) ||
                typeElement.ValueKind != JsonValueKind.String)
            {
                throw new JsonException("문자열 형식의 type 필드가 필요합니다.");
            }

            string[] requiredFields = typeElement.GetString() switch
            {
                "header" => ["format", "version", "timeUnit", "originUtc"],
                "track" => ["id", "name"],
                "begin" => ["id", "track", "name", "time"],
                "end" => ["id", "time"],
                "sessionEnd" => ["time", "droppedRecordCount"],
                _ => throw new JsonException($"지원하지 않는 레코드 종류입니다: {typeElement.GetString()}")
            };

            foreach (string fieldName in requiredFields)
            {
                if (!root.TryGetProperty(fieldName, out JsonElement value) ||
                    value.ValueKind == JsonValueKind.Null)
                {
                    throw new JsonException($"필수 필드가 없거나 null입니다: {fieldName}");
                }
            }

            return root.Deserialize<TraceRecord>(Options) ?? throw new JsonException("레코드를 생성할 수 없습니다.");
        }

        private static void ValidateUniqueProperties(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (!names.Add(property.Name))
                    {
                        throw new JsonException($"중복 JSON 필드는 허용하지 않습니다: {property.Name}");
                    }
                    ValidateUniqueProperties(property.Value);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in element.EnumerateArray())
                {
                    ValidateUniqueProperties(item);
                }
            }
        }
    }
}
