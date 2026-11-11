using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using CropPilot.Api.Constants;
using CropPilot.Contract.Models;

namespace CropPilot.Api.Fertilizers;

/// <summary>區分編輯明細時省略識別碼與明確傳入空 UUID。</summary>
internal sealed class FertilizationDetailIdJsonConverter : JsonConverter<Guid>
{
    /// <summary>只對編輯施肥明細的選填識別碼套用驗證。</summary>
    internal static void Configure(JsonTypeInfo type)
    {
        if (type.Type == typeof(EditFertilizationDetailRqDto))
        {
            type.Properties.Single(property => property.Name == "id").CustomConverter =
                new FertilizationDetailIdJsonConverter();
        }
    }

    /// <summary>讀取既有明細識別碼，拒絕空 UUID 或格式錯誤。</summary>
    public override Guid Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String && reader.TryGetGuid(out var value) && value != Guid.Empty
            ? value
            : throw new JsonException(ErrorMessages.FertilizerDetailInvalid);

    /// <summary>依 UUID 格式寫出識別碼。</summary>
    public override void Write(Utf8JsonWriter writer, Guid value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);
}
