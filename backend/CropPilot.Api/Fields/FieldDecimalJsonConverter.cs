using System.Buffers;
using System.Buffers.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using CropPilot.Api.Constants;
using CropPilot.Contract.Models;

namespace CropPilot.Api.Fields;

/// <summary>檢查田區與栽培寫入請求中數值的小數位數。</summary>
internal sealed class FieldDecimalJsonConverter : JsonConverter<decimal>
{
    /// <summary>為生成的新增及編輯請求註冊面積及座標轉換器。</summary>
    internal static void ConfigureFieldSave(JsonTypeInfo type)
    {
        if (type.Type != typeof(CreateFieldRqDto) && type.Type != typeof(EditFieldRqDto) &&
            type.Type != typeof(CreatePlantingRqDto) && type.Type != typeof(EditPlantingRqDto))
        {
            return;
        }

        foreach (var property in type.Properties)
        {
            if (property.PropertyType == typeof(decimal))
            {
                property.CustomConverter = new FieldDecimalJsonConverter();
            }
            else if (property.PropertyType == typeof(decimal?))
            {
                property.CustomConverter = new CoordinateConverter();
            }
        }
    }

    /// <summary>讀取最多四位小數的面積。</summary>
    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        ReadNumber(ref reader, 4);

    /// <summary>以 JSON 數值寫出面積。</summary>
    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(value);

    /// <summary>檢查原始 JSON 數值的小數位數，避免捨入掩蓋超出精度的輸入。</summary>
    private static decimal ReadNumber(ref Utf8JsonReader reader, int maxScale)
    {
        if (reader.TokenType != JsonTokenType.Number || !reader.TryGetDecimal(out var value))
        {
            throw new JsonException(ErrorMessages.FieldDecimalExpected);
        }

        ReadOnlySpan<byte> number = reader.HasValueSequence ? reader.ValueSequence.ToArray() : reader.ValueSpan;
        var exponentIndex = number.IndexOfAny((byte)'e', (byte)'E');
        var mantissa = exponentIndex < 0 ? number : number[..exponentIndex];
        var lastSignificantDigit = mantissa.LastIndexOfAnyInRange((byte)'1', (byte)'9');
        if (lastSignificantDigit < 0)
        {
            return value;
        }

        var point = mantissa.IndexOf((byte)'.');
        var unitsDigit = point < 0 ? mantissa.Length - 1 : point - 1;
        var scale = lastSignificantDigit - unitsDigit;
        if (point >= 0 && lastSignificantDigit > point)
        {
            scale--;
        }

        var exponent = 0;
        if (exponentIndex >= 0 && !Utf8Parser.TryParse(number[(exponentIndex + 1)..], out exponent, out _)
            || scale - (long)exponent > maxScale)
        {
            throw new JsonException(ErrorMessages.FieldDecimalPrecisionExceeded);
        }

        return value;
    }

    /// <summary>檢查座標的小數位數，保留座標的空值。</summary>
    private sealed class CoordinateConverter : JsonConverter<decimal?>
    {
        /// <summary>讀取空值或最多六位小數的 JSON 座標。</summary>
        public override decimal? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.Null ? null : ReadNumber(ref reader, 6);

        /// <summary>以 JSON 數值或空值寫出座標。</summary>
        public override void Write(Utf8JsonWriter writer, decimal? value, JsonSerializerOptions options)
        {
            if (value is { } number)
            {
                writer.WriteNumberValue(number);
            }
            else
            {
                writer.WriteNullValue();
            }
        }
    }
}
