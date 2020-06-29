using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Blueprint.Http
{
    internal class ValidationProblemDetailsJsonConverter : JsonConverter<ValidationProblemDetails>
    {
        private static readonly JsonEncodedText Errors = JsonEncodedText.Encode("errors");

        public override ValidationProblemDetails Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var problemDetails = new ValidationProblemDetails();

            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException("Unexpected end when reading JSON.");
            }

            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.ValueTextEquals(Errors.EncodedUtf8Bytes))
                {
                    var errors = JsonSerializer.Deserialize<Dictionary<string, string[]>>(ref reader, options);
                    foreach (var item in errors)
                    {
                        problemDetails.Errors[item.Key] = item.Value;
                    }
                }
                else
                {
                    ProblemDetailsJsonConverter.ReadValue(ref reader, problemDetails, options);
                }
            }

            if (reader.TokenType != JsonTokenType.EndObject)
            {
                throw new JsonException("Unexpected end when reading JSON.");
            }

            return problemDetails;
        }

        public override void Write(Utf8JsonWriter writer, ValidationProblemDetails value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            ProblemDetailsJsonConverter.WriteProblemDetails(writer, value, options);

            writer.WriteStartObject(Errors);

            foreach (var kvp in value.Errors)
            {
                writer.WritePropertyName(options.DictionaryKeyPolicy.ConvertName(kvp.Key));
                JsonSerializer.Serialize(writer, kvp.Value, kvp.Value?.GetType() ?? typeof(object), options);
            }

            writer.WriteEndObject();

            writer.WriteEndObject();
        }
    }
}