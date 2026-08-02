using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.IO;

namespace CueGen.Workflow
{
    public static class WorkflowImportParser
    {
        public static WorkflowImportDocument Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new JsonException("The import document is empty");

            using var stringReader = new StringReader(json);
            using var jsonReader = new JsonTextReader(stringReader)
            {
                DateParseHandling = DateParseHandling.None
            };

            var token = JToken.ReadFrom(jsonReader, new JsonLoadSettings
            {
                CommentHandling = CommentHandling.Ignore,
                DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                LineInfoHandling = LineInfoHandling.Load
            });

            while (jsonReader.Read())
            {
                if (jsonReader.TokenType != JsonToken.Comment)
                    throw new JsonException("Unexpected content after the import document");
            }

            if (token.Type != JTokenType.Object)
                throw new JsonException("The import document must be a JSON object");

            var serializer = JsonSerializer.Create(new JsonSerializerSettings
            {
                MissingMemberHandling = MissingMemberHandling.Error
            });

            return token.ToObject<WorkflowImportDocument>(serializer)
                ?? throw new JsonException("The import document could not be parsed");
        }
    }
}
