using CropPilot.Api.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CropPilot.Api.Plantings;

/// <summary>依田區的擁有者帳號讀寫栽培紀錄。</summary>
public sealed class PlantingRepository(CropPilotDbContext database)
{
    /// <summary>新增栽培紀錄。田區無法存取或已被刪除時回傳 false。</summary>
    internal async Task<bool> TryAddAsync(Guid accountId, Planting planting, CancellationToken cancellationToken)
    {
        if (!await OwnedFields(accountId).AnyAsync(field => field.Id == planting.FieldId, cancellationToken))
        {
            return false;
        }

        database.Plantings.Add(planting);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.ForeignKeyViolation,
            ConstraintName: Planting.FieldForeignKey
        })
        {
            database.Entry(planting).State = EntityState.Detached;
            return false;
        }
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
