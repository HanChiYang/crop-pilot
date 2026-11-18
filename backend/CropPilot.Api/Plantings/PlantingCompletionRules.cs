using System.Globalization;
using CropPilot.Api.Constants;
using CropPilot.Api.Data;

namespace CropPilot.Api.Plantings;

/// <summary>已結束栽培的日期規則，供採收及田間作業的 Service 共用。</summary>
internal static class PlantingCompletionRules
{
    /// <summary>已結束的栽培，採收及作業日期不得晚於結束日。</summary>
    /// <param name="planting">交易內鎖定後讀取的栽培狀態與結束日。</param>
    /// <param name="date">要寫入的採收或作業日期。</param>
    /// <returns>日期晚於結束日時的錯誤訊息；可寫入時為 null。</returns>
    internal static string? DateAfterCompletionError(PlantingLockState planting, DateOnly date) =>
        planting.CompletionDate is { } end && date > end
            ? string.Format(CultureInfo.InvariantCulture, ErrorMessages.DateAfterCompletion, end)
            : null;
}
