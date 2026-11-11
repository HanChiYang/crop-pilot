using System.Buffers;
using System.Buffers.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using CropPilot.Api.Constants;
using CropPilot.Contract.Models;

namespace CropPilot.Api.Fields;

/// <summary>檢查寫入請求中數值的小數位數。</summary>
internal sealed class FieldDecimalJsonConverter(int scale) : JsonConverter<decimal>
{
    /// <summary>為生成的田區、栽培及肥料寫入請求註冊精度檢查。</summary>
    internal static void ConfigureFieldSave(JsonTypeInfo type)
    {
        var hasFertilizerQuantity = type.Type == typeof(CreateFertilizerLotRqDto)
            || type.Type == typeof(EditFertilizerLotRqDto)
            || type.Type == typeof(FertilizationDetailRqDto)
            || type.Type == typeof(EditFertilizationDetailRqDto);
        var hasArea = type.Type == typeof(CreateFieldRqDto) || type.Type == typeof(EditFieldRqDto)
            || type.Type == typeof(CreatePlantingRqDto) || type.Type == typeof(EditPlantingRqDto);
        if (!hasArea && !hasFertilizerQuantity)
            return;

        foreach (var property in type.Properties)
        {
            if (property.PropertyType == typeof(decimal))
            {
                property.CustomConverter = new FieldDecimalJsonConverter(hasArea ? 4 : 6);
            }
            else if (property.PropertyType == typeof(decimal?))
            {
                property.CustomConverter = new NullableDecimalConverter();
            }
        }
    }

    /// <summary>讀取數值並檢查允許的小數位數。</summary>
    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        ReadNumber(ref reader, scale);

    /// <summary>以 JSON 數值寫出欄位。</summary>
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

    /// <summary>檢查選填數值的小數位數，並保留空值。</summary>
    private sealed class NullableDecimalConverter : JsonConverter<decimal?>
    {
        /// <summary>讀取空值或最多六位小數的 JSON 數值。</summary>
        public override decimal? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.Null ? null : ReadNumber(ref reader, 6);

        /// <summary>以 JSON 數值或空值寫出欄位。</summary>
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
