using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
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

            return JsonSerializer.Serialize<TraceRecord>(record, Options);
        }
    }
}
