using System.Data;
using CropPilot.Api.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CropPilot.Api.Plantings;

/// <summary>依田區的擁有者帳號讀寫栽培紀錄。</summary>
public sealed class PlantingRepository(CropPilotDbContext database)
{
    /// <summary>鎖定田區與既有栽培後取得目前紀錄、可分配面積及最後採收或作業日。業務驗證成功時提交新增或編輯。</summary>
    internal async Task<PlantingWriteResult> WriteAsync(Guid accountId, Guid? requestedFieldId, Guid? plantingId,
        Func<Planting?, decimal, DateOnly?, PlantingWriteResult> apply, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var resolvedFieldId = requestedFieldId ?? await OwnedPlantings(accountId)
            .Where(planting => planting.Id == plantingId)
            .Select(planting => (Guid?)planting.FieldId).SingleOrDefaultAsync(cancellationToken);
        if (resolvedFieldId is not Guid fieldId)
        {
            return new(PlantingWriteStatus.NotFound);
        }

        var area = await FieldAreaQueries.LockAsync(database, accountId, fieldId, cancellationToken);
        if (area is null)
        {
            return new(PlantingWriteStatus.NotFound);
        }

        Planting? current = null;
        DateOnly? latestActivity = null;
        if (plantingId is Guid id)
        {
            // 採收與作業寫入會鎖定同一列，讓結束日與最後活動日的核對不受並行寫入影響。
            await database.Database.ExecuteSqlInterpolatedAsync(
                $"""SELECT id FROM plantings WHERE id = {id} AND field_id = {fieldId} FOR UPDATE""", cancellationToken);
            current = await database.Plantings.SingleOrDefaultAsync(
                value => value.FieldId == fieldId && value.Id == id, cancellationToken);
            if (current is null)
            {
                return new(PlantingWriteStatus.NotFound);
            }
            latestActivity = await LatestActivityDateAsync(id, cancellationToken);
        }

        var allocated = await FieldAreaQueries.AllocatedAsync(database, fieldId, plantingId, cancellationToken);
        var result = apply(current, area.Value - allocated, latestActivity);
        if (result.Status != PlantingWriteStatus.Success)
        {
            return result;
        }
        if (current is null)
        {
            database.Plantings.Add(result.Planting!);
        }
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    /// <summary>取得栽培最後一筆採收或田間作業的日期，沒有紀錄時回傳 null。</summary>
    private async Task<DateOnly?> LatestActivityDateAsync(Guid plantingId, CancellationToken ct)
    {
        var harvest = await database.Harvests.Where(value => value.PlantingId == plantingId)
            .MaxAsync(value => (DateOnly?)value.HarvestDate, ct);
        var operation = await database.FieldOperations.Where(value => value.PlantingId == plantingId)
            .MaxAsync(value => (DateOnly?)value.OperationDate, ct);
        return harvest > operation || operation is null ? harvest : operation;
    }

    /// <summary>取得栽培的最後採收日、資材施用日與灌溉日，沒有該類紀錄時回傳 null。</summary>
    private async Task<PlantingActivityMaxDates> LatestActivityMaxDatesAsync(Guid plantingId,
        CancellationToken cancellationToken)
    {
        var harvest = await database.Harvests
            .Where(value => value.PlantingId == plantingId)
            .MaxAsync(value => (DateOnly?)value.HarvestDate, cancellationToken);
        var materialApplication = await database.FieldOperations
            .Where(value => value.PlantingId == plantingId && value.OperationType == FieldOperation.MaterialApplication)
            .MaxAsync(value => (DateOnly?)value.OperationDate, cancellationToken);
        var irrigation = await database.FieldOperations
            .Where(value => value.PlantingId == plantingId && value.OperationType == FieldOperation.Irrigation)
            .MaxAsync(value => (DateOnly?)value.OperationDate, cancellationToken);
        return new(harvest, materialApplication, irrigation);
    }

    /// <summary>從同一資料庫快照讀取帳號擁有的栽培及各項活動最新日期，不鎖定資料。</summary>
    internal async Task<PlantingCompletionDateSnapshot?> ReadCompletionDateSnapshotAsync(Guid accountId,
        Guid plantingId, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var planting = await OwnedPlantings(accountId).AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == plantingId, cancellationToken);
        if (planting is null)
        {
            return null;
        }
        if (planting.Status != Planting.Completed)
        {
            await transaction.CommitAsync(cancellationToken);
            return new(planting, null);
        }

        var latestActivities = await LatestActivityMaxDatesAsync(plantingId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(planting, latestActivities);
    }

    /// <summary>鎖定帳號擁有的栽培列並取得最新活動日期，業務驗證通過且有變更時提交結束日修正。</summary>
    internal async Task<PlantingWriteResult> EditCompletionDateAsync(Guid accountId, Guid plantingId,
        Func<Planting, PlantingActivityMaxDates?, PlantingWriteResult> apply, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        // 與採收、灌溉及資材施用共用同一列 plantings 鎖，避免並行補登與修正結束日互相交錯。
        var lockedIds = await database.Database.SqlQuery<Guid>($"""
            SELECT p.id AS "Value"
            FROM plantings p JOIN fields f ON f.id = p.field_id
            WHERE p.id = {plantingId} AND f.main_account_id = {accountId}
            FOR UPDATE OF p
            """).ToListAsync(cancellationToken);
        if (lockedIds.Count == 0)
        {
            return new(PlantingWriteStatus.NotFound);
        }

        var planting = await database.Plantings.SingleAsync(value => value.Id == plantingId, cancellationToken);
        var latestActivities = planting.Status == Planting.Completed
            ? await LatestActivityMaxDatesAsync(plantingId, cancellationToken)
            : null;
        var result = apply(planting, latestActivities);
        if (result.Status != PlantingWriteStatus.Success)
        {
            return result;
        }

        if (database.ChangeTracker.HasChanges())
        {
            await database.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    /// <summary>從同一資料庫快照讀取栽培與作業，供刪除確認使用。</summary>
    internal async Task<PlantingDeletion?> ReadDeletionAsync(Guid accountId, Guid plantingId, CancellationToken ct)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var planting = await FindForDeletionAsync(accountId, plantingId, ct);
        var deletion = planting is null ? null : await ReadDeletionAsync(planting, ct);
        await transaction.CommitAsync(ct);
        return deletion;
    }

    /// <summary>鎖定田區、栽培及批次，確認摘要後原子刪除栽培與作業。</summary>
    internal async Task<PlantingWriteStatus> DeleteWithOperationsAsync(Guid accountId, Guid plantingId,
        Func<PlantingDeletion, PlantingWriteStatus> validate, CancellationToken ct)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(ct);
        var fieldId = await OwnedPlantings(accountId).Where(value => value.Id == plantingId)
            .Select(value => (Guid?)value.FieldId).SingleOrDefaultAsync(ct);
        if (fieldId is not Guid id || await FieldAreaQueries.LockAsync(database, accountId, id, ct) is null)
            return PlantingWriteStatus.NotFound;

        await database.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT id FROM plantings WHERE id = {plantingId} AND field_id = {id} FOR UPDATE""", ct);
        var planting = await FindForDeletionAsync(accountId, plantingId, ct);
        if (planting is null)
            return PlantingWriteStatus.NotFound;

        var lots = await database.MaterialApplicationDetails
            .Where(detail => detail.Application.FieldOperation.PlantingId == plantingId)
            .Select(detail => detail.MaterialPurchaseLotId).Distinct().ToListAsync(ct);
        foreach (var lotId in lots.Order())
        {
            await database.Database.ExecuteSqlInterpolatedAsync(
                $"""SELECT id FROM material_purchase_lots WHERE id = {lotId} FOR UPDATE""", ct);
        }

        var deletion = await ReadDeletionAsync(planting, ct);
        var status = validate(deletion);
        if (status != PlantingWriteStatus.Success)
            return status;

        try
        {
            await database.FieldOperations.Where(value => value.PlantingId == plantingId).ExecuteDeleteAsync(ct);
            await database.Plantings.Where(value => value.Id == plantingId).ExecuteDeleteAsync(ct);
            await transaction.CommitAsync(ct);
            return PlantingWriteStatus.Success;
        }
        catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.ForeignKeyViolation &&
            error.ConstraintName == Harvest.PlantingForeignKey)
        {
            return PlantingWriteStatus.HasHarvests;
        }
    }

    /// <summary>取得確認視窗需要的栽培及田區名稱。</summary>
    private Task<Planting?> FindForDeletionAsync(Guid accountId, Guid plantingId, CancellationToken ct) =>
        OwnedPlantings(accountId).AsNoTracking().Include(value => value.Field)
            .SingleOrDefaultAsync(value => value.Id == plantingId, ct);

    /// <summary>讀取作業版本及各明細所屬批次的摘要資料。</summary>
    private async Task<PlantingDeletion> ReadDeletionAsync(Planting planting, CancellationToken ct)
    {
        var operations = await database.FieldOperations.AsNoTracking()
            .Where(value => value.PlantingId == planting.Id).OrderBy(value => value.Id)
            .Select(value => new PlantingOperationVersion(value.Id, value.OperationType, value.UpdatedAt))
            .ToListAsync(ct);
        var details = await database.MaterialApplicationDetails.AsNoTracking()
            .Where(value => value.Application.FieldOperation.PlantingId == planting.Id)
            .OrderBy(value => value.Id)
            .Select(value => new
            {
                value.MaterialPurchaseLotId, MaterialName = value.Lot.Material.Name,
                Unit = value.Lot.Material.StockUnit, value.Lot.Material.MaterialType, value.Quantity,
                value.Lot.Source, value.Lot.AvailableFrom
            }).ToListAsync(ct);

        var inventory = details.GroupBy(value => value.MaterialPurchaseLotId).Select(group =>
        {
            var first = group.First();
            return new PlantingInventoryReturn(group.Key, first.MaterialName, first.Source, first.AvailableFrom,
                group.Sum(value => value.Quantity), first.Unit, first.MaterialType);
        }).OrderBy(value => value.MaterialName, StringComparer.Ordinal).ThenBy(value => value.AvailableFrom)
            .ThenBy(value => value.LotId).ToList();

        var hasHarvests = await database.Harvests.AnyAsync(harvest => harvest.PlantingId == planting.Id, ct);
        return new(planting, operations, inventory, hasHarvests);
    }

    /// <summary>依栽培識別碼查詢一筆登入帳號的栽培紀錄。</summary>
    internal Task<Planting?> FindAsync(Guid accountId, Guid plantingId, CancellationToken cancellationToken) =>
        OwnedPlantings(accountId).AsNoTracking().SingleOrDefaultAsync(planting =>
            planting.Id == plantingId, cancellationToken);

    /// <summary>依栽培所屬田區篩選登入帳號擁有的栽培。</summary>
    private IQueryable<Planting> OwnedPlantings(Guid accountId) =>
        database.Plantings.Where(planting => planting.Field.MainAccountId == accountId);

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
                planting.CropName, planting.Variety, planting.ActualPlantingDate!.Value, planting.CompletionDate!.Value,
                planting.AreaSquareMeters))
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
    string? Variety, DateOnly ActualPlantingDate, DateOnly CompletionDate, decimal AreaSquareMeters);

/// <summary>包含栽培各項活動在資料庫中的最新日期，沒有紀錄時為 null。</summary>
internal sealed record PlantingActivityMaxDates(DateOnly? HarvestDate, DateOnly? MaterialApplicationDate,
    DateOnly? IrrigationDate);

/// <summary>包含同一快照讀取的栽培紀錄與已結束栽培的最新活動日期。</summary>
internal sealed record PlantingCompletionDateSnapshot(Planting Planting, PlantingActivityMaxDates? LatestActivities);
