using System.Data;
using CropPilot.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CropPilot.Api.Harvests;

/// <summary>依登入帳號查詢採收，並與栽培刪除共用同一筆栽培的寫入鎖。</summary>
public sealed class HarvestRepository(CropPilotDbContext database)
{
    /// <summary>從同一資料快照查詢符合篩選條件的清單與整批累計。無法存取栽培時回傳空值。</summary>
    internal async Task<HarvestList?> ListAsync(Guid accountId, Guid plantingId, DateOnly? startDate,
        DateOnly? endDate, bool byWeight, bool ascending, CancellationToken ct)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        if (!await database.Plantings.AnyAsync(planting =>
                planting.Id == plantingId && planting.Field.MainAccountId == accountId, ct))
            return null;

        var batch = OwnedHarvests(accountId).AsNoTracking().Where(harvest => harvest.PlantingId == plantingId);
        var totalWeight = await batch.SumAsync(harvest => harvest.WeightKg, ct);
        var filtered = batch;
        if (startDate is { } from)
            filtered = filtered.Where(harvest => harvest.HarvestDate >= from);
        if (endDate is { } to)
            filtered = filtered.Where(harvest => harvest.HarvestDate <= to);

        var ordered = (byWeight, ascending) switch
        {
            (true, true) => filtered.OrderBy(harvest => harvest.WeightKg).ThenByDescending(harvest => harvest.HarvestDate),
            (true, false) => filtered.OrderByDescending(harvest => harvest.WeightKg).ThenByDescending(harvest => harvest.HarvestDate),
            (false, true) => filtered.OrderBy(harvest => harvest.HarvestDate),
            _ => filtered.OrderByDescending(harvest => harvest.HarvestDate)
        };
        var harvests = await ordered.ThenByDescending(harvest => harvest.CreatedAt)
            .ThenByDescending(harvest => harvest.Id).ToListAsync(ct);
        await transaction.CommitAsync(ct);
        return new(totalWeight, harvests);
    }

    /// <summary>取得帳號可操作的採收所屬栽培識別碼。</summary>
    internal Task<Guid?> FindPlantingIdAsync(Guid accountId, Guid harvestId, CancellationToken ct) =>
        OwnedHarvests(accountId).Where(harvest => harvest.Id == harvestId)
            .Select(harvest => (Guid?)harvest.PlantingId).SingleOrDefaultAsync(ct);

    /// <summary>讀取帳號可操作的採收供更新。</summary>
    internal Task<Harvest?> FindAsync(Guid accountId, Guid plantingId, Guid harvestId, CancellationToken ct) =>
        OwnedHarvests(accountId).SingleOrDefaultAsync(harvest =>
            harvest.Id == harvestId && harvest.PlantingId == plantingId, ct);

    /// <summary>開始採收寫入交易，栽培鎖在交易結束時釋放。</summary>
    internal Task<IDbContextTransaction> BeginAsync(CancellationToken ct) => database.Database.BeginTransactionAsync(ct);

    /// <summary>鎖定本帳號的栽培並讀取當前狀態，不存在或不屬於帳號時回傳空值。</summary>
    internal async Task<string?> LockPlantingStatusAsync(Guid accountId, Guid plantingId, CancellationToken ct)
    {
        var statuses = await database.Database.SqlQuery<string>($"""
            SELECT p.status AS "Value"
            FROM plantings p JOIN fields f ON f.id = p.field_id
            WHERE p.id = {plantingId} AND f.main_account_id = {accountId}
            FOR UPDATE OF p
            """).ToListAsync(ct);
        return statuses.SingleOrDefault();
    }

    /// <summary>將新的採收加入目前交易。</summary>
    internal void Add(Harvest harvest) => database.Harvests.Add(harvest);

    /// <summary>刪除帳號所屬的一筆採收，回傳是否刪除成功。</summary>
    internal async Task<bool> DeleteAsync(Guid accountId, Guid plantingId, Guid harvestId, CancellationToken ct) =>
        await OwnedHarvests(accountId).Where(harvest => harvest.Id == harvestId && harvest.PlantingId == plantingId)
            .ExecuteDeleteAsync(ct) == 1;

    /// <summary>儲存已追蹤的採收變更。</summary>
    internal Task SaveAsync(CancellationToken ct) => database.SaveChangesAsync(ct);

    /// <summary>以採收所屬田區的主帳號限制查詢。</summary>
    private IQueryable<Harvest> OwnedHarvests(Guid accountId) =>
        database.Harvests.Where(harvest => harvest.Planting.Field.MainAccountId == accountId);
}

/// <summary>單批總重量與符合條件的採收明細。</summary>
internal sealed record HarvestList(decimal TotalWeightKg, List<Harvest> Records);
