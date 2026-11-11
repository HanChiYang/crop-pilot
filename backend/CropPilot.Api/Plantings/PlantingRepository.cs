using CropPilot.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace CropPilot.Api.Plantings;

/// <summary>依田區的擁有者帳號讀寫栽培紀錄。</summary>
public sealed class PlantingRepository(CropPilotDbContext database)
{
    /// <summary>鎖定田區後取得目前紀錄與可分配面積。業務驗證成功時提交新增、編輯或刪除。</summary>
    internal async Task<PlantingWriteResult> WriteAsync(Guid accountId, Guid fieldId, Guid? plantingId,
        Func<Planting?, decimal, PlantingWriteResult> apply, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var area = await FieldAreaQueries.LockAsync(database, accountId, fieldId, cancellationToken);
        if (area is null)
        {
            return new(PlantingWriteStatus.NotFound);
        }

        Planting? current = null;
        if (plantingId is Guid id)
        {
            current = await database.Plantings.SingleOrDefaultAsync(
                value => value.FieldId == fieldId && value.Id == id, cancellationToken);
            if (current is null)
            {
                return new(PlantingWriteStatus.NotFound);
            }
        }

        var allocated = await FieldAreaQueries.AllocatedAsync(database, fieldId, plantingId, cancellationToken);
        var result = apply(current, area.Value - allocated);
        if (result.Status != PlantingWriteStatus.Success)
        {
            return result;
        }
        if (current is null)
        {
            database.Plantings.Add(result.Planting!);
        }
        else if (result.Planting is null)
        {
            database.Plantings.Remove(current);
        }
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    /// <summary>以擁有者、田區及紀錄識別碼查詢一筆栽培紀錄。</summary>
    internal Task<Planting?> FindAsync(Guid accountId, Guid fieldId, Guid plantingId, CancellationToken cancellationToken) =>
        database.Plantings.AsNoTracking().SingleOrDefaultAsync(planting =>
            planting.Id == plantingId && planting.FieldId == fieldId &&
            planting.Field.MainAccountId == accountId, cancellationToken);

    /// <summary>列出指定田區目前的栽培紀錄。田區無法存取時回傳 null。</summary>
    internal Task<FieldPlantings?> ListFieldAsync(Guid accountId, Guid fieldId, CancellationToken cancellationToken) =>
        WithCurrentPlantings(OwnedFields(accountId).Where(field => field.Id == fieldId))
            .SingleOrDefaultAsync(cancellationToken);

    /// <summary>依田區名稱排序，列出帳號擁有的田區及目前栽培紀錄。</summary>
    internal Task<List<FieldPlantings>> ListOverviewAsync(Guid accountId, CancellationToken cancellationToken) =>
        WithCurrentPlantings(OwnedFields(accountId).OrderBy(field => field.Name)).ToListAsync(cancellationToken);

    /// <summary>確認指定田區屬於目前帳號，供歷史查詢區分空清單與找不到田區。</summary>
    internal Task<bool> OwnsFieldAsync(Guid accountId, Guid fieldId, CancellationToken cancellationToken) =>
        OwnedFields(accountId).AnyAsync(field => field.Id == fieldId, cancellationToken);

    /// <summary>依田區與完整作物名稱篩選已結束紀錄，按實際栽種日及紀錄 ID 排序。</summary>
    internal Task<List<PlantingHistoryEntry>> ListHistoryAsync(
        Guid accountId, Guid? fieldId, string? cropName, CancellationToken cancellationToken)
    {
        var query = CompletedPlantings(accountId, fieldId);
        if (cropName is not null)
        {
            query = query.Where(planting => EF.Functions.Collate(planting.CropName, "C") == cropName);
        }

        return query.OrderByDescending(planting => planting.ActualPlantingDate)
            .ThenBy(planting => planting.Id)
            .Select(planting => new PlantingHistoryEntry(planting.Id, planting.FieldId, planting.Field.Name,
                planting.CropName, planting.Variety, planting.ActualPlantingDate!.Value, planting.AreaSquareMeters))
            .ToListAsync(cancellationToken);
    }

    /// <summary>取得已結束紀錄的作物名稱，以 PostgreSQL C 定序去重及排序。</summary>
    internal Task<List<string>> ListHistoryCropsAsync(Guid accountId, Guid? fieldId, CancellationToken cancellationToken) =>
        CompletedPlantings(accountId, fieldId).Select(planting => EF.Functions.Collate(planting.CropName, "C"))
            .Distinct().OrderBy(cropName => cropName).ToListAsync(cancellationToken);

    /// <summary>建立帳號與選用田區共用的已結束紀錄查詢。</summary>
    private IQueryable<Planting> CompletedPlantings(Guid accountId, Guid? fieldId)
    {
        var query = database.Plantings.AsNoTracking().Where(planting =>
            planting.Status == Planting.Completed && planting.Field.MainAccountId == accountId);
        return fieldId is Guid id ? query.Where(planting => planting.FieldId == id) : query;
    }

    /// <summary>建立指定帳號的田區查詢。</summary>
    private IQueryable<Field> OwnedFields(Guid accountId) =>
        database.Fields.AsNoTracking().Where(field => field.MainAccountId == accountId);

    /// <summary>在同一次查詢取得每塊田區及其目前紀錄，保留空田區。</summary>
    private IQueryable<FieldPlantings> WithCurrentPlantings(IQueryable<Field> fields) =>
        fields.Select(field => new FieldPlantings(field, database.Plantings
            .Where(planting => planting.FieldId == field.Id && planting.Status != Planting.Completed)
            .OrderBy(planting => planting.ActualPlantingDate ?? planting.PlannedPlantingDate)
            .ThenBy(planting => planting.Id)
            .ToList()));
}

/// <summary>包含田區及其目前栽培紀錄。</summary>
internal sealed record FieldPlantings(Field Field, List<Planting> Plantings);

/// <summary>歷史清單顯示的已結束紀錄與目前田區名稱。</summary>
internal sealed record PlantingHistoryEntry(Guid Id, Guid FieldId, string FieldName, string CropName,
    string? Variety, DateOnly ActualPlantingDate, decimal AreaSquareMeters);
