using Microsoft.EntityFrameworkCore;

namespace CropPilot.Api.Data;

/// <summary>在同一田區的寫入交易中鎖定田區並查詢目前占用面積。</summary>
internal static class FieldAreaQueries
{
    /// <summary>鎖定帳號擁有的田區並回傳總面積，找不到時回傳 null。</summary>
    internal static async Task<decimal?> LockAsync(CropPilotDbContext database, Guid accountId, Guid fieldId,
        CancellationToken cancellationToken)
    {
        var areas = await database.Database.SqlQuery<decimal>($"""
            SELECT area_square_meters AS "Value" FROM fields
            WHERE main_account_id = {accountId} AND id = {fieldId} FOR UPDATE
            """).ToListAsync(cancellationToken);
        return areas.Count == 0 ? null : areas[0];
    }

    /// <summary>合計未結束紀錄的占用面積，編輯時排除正在更新的紀錄。</summary>
    internal static Task<decimal> AllocatedAsync(CropPilotDbContext database, Guid fieldId, Guid? excludedId,
        CancellationToken cancellationToken) =>
        database.Plantings.Where(value => value.FieldId == fieldId && value.Status != Planting.Completed &&
            (excludedId == null || value.Id != excludedId))
            .SumAsync(value => value.AreaSquareMeters, cancellationToken);
}
