using System.Security.Cryptography;
using System.Text.Json;
using CropPilot.Api.Data;

namespace CropPilot.Api.Plantings;

/// <summary>保存栽培刪除的資料及確認時使用的版本。</summary>
internal sealed record PlantingDeletion(Planting Planting, List<PlantingOperationVersion> Operations,
    List<PlantingInventoryReturn> InventoryReturns)
{
    /// <summary>從確認畫面與作業版本產生可跨請求核對的雜湊值。</summary>
    internal string Version() => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
    {
        Planting.Id, Planting.Field.Name, Planting.CropName, Planting.Status, Planting.AreaSquareMeters,
        Planting.Variety, Planting.PlannedPlantingDate, Planting.ActualPlantingDate, Planting.Description,
        Operations, InventoryReturns
    })));
}

/// <summary>記錄作業類型與最近寫入時間，以辨識預覽後的新增、編輯或刪除。</summary>
internal sealed record PlantingOperationVersion(Guid Id, string Type, DateTimeOffset UpdatedAt);

/// <summary>同一原庫存批次在刪除時會恢復的合計用量。</summary>
internal sealed record PlantingInventoryReturn(Guid LotId, string MaterialName, string Source,
    DateOnly AvailableFrom, decimal Quantity, string Unit);
