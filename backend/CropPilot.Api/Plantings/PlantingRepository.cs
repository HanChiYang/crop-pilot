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
