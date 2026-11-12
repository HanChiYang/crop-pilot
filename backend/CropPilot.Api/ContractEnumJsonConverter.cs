using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CropPilot.Api;

/// <summary>以 OpenAPI 產生的 EnumMember 值讀寫契約列舉。</summary>
internal sealed class ContractEnumJsonConverter<TValue>(string invalidMessage) : JsonConverter<TValue> where TValue : struct, Enum
{
    private static readonly Dictionary<TValue, string> Names = typeof(TValue)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .ToDictionary(field => (TValue)field.GetValue(null)!,
            field => field.GetCustomAttribute<EnumMemberAttribute>()!.Value!);
    private static readonly Dictionary<string, TValue> Values = Names
        .ToDictionary(entry => entry.Value, entry => entry.Key, StringComparer.Ordinal);

    /// <summary>取得列舉對應的契約值，未定義時回傳 null。</summary>
    internal static string? GetName(TValue value) => Names.GetValueOrDefault(value);

    /// <summary>以已知的契約值還原列舉。</summary>
    internal static TValue FromName(string name) => Values[name];

    /// <summary>只接受契約定義的字串值。</summary>
    public override TValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String && Values.TryGetValue(reader.GetString()!, out var value)
            ? value
            : throw new JsonException(invalidMessage);

    /// <summary>以契約定義的字串回傳列舉值。</summary>
    public override void Write(Utf8JsonWriter writer, TValue value, JsonSerializerOptions options) =>
        writer.WriteStringValue(Names[value]);
}
