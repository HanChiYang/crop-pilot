using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Serialization;
using CropPilot.Api.Constants;

namespace CropPilot.Api.Plantings;

/// <summary>以 OpenAPI 產生的 EnumMember 值讀寫栽培狀態。</summary>
internal sealed class PlantingStatusJsonConverter<TStatus> : JsonConverter<TStatus> where TStatus : struct, Enum
{
    private static readonly Dictionary<TStatus, string> Names = typeof(TStatus)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .ToDictionary(field => (TStatus)field.GetValue(null)!,
            field => field.GetCustomAttribute<EnumMemberAttribute>()!.Value!);
    private static readonly Dictionary<string, TStatus> Values = Names
        .ToDictionary(entry => entry.Value, entry => entry.Key, StringComparer.Ordinal);

    /// <summary>只接受契約定義的小寫字串值。</summary>
    public override TStatus Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String && Values.TryGetValue(reader.GetString()!, out var value)
            ? value
            : throw new JsonException(ErrorMessages.PlantingStatusInvalid);

    /// <summary>以契約定義的字串回傳狀態。</summary>
    public override void Write(Utf8JsonWriter writer, TStatus value, JsonSerializerOptions options) =>
        writer.WriteStringValue(Names[value]);
}
