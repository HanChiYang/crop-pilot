using CropPilot.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CropPilot.Api.Irrigations;

/// <summary>依帳號限制灌溉查詢，並序列化同一栽培的寫入。</summary>
public sealed class IrrigationRepository(CropPilotDbContext db)
{
    /// <summary>取得登入帳號擁有的栽培。</summary>
    internal IQueryable<Planting> Plantings(Guid accountId) =>
        db.Plantings.Where(value => value.Field.MainAccountId == accountId);

    /// <summary>取得登入帳號擁有的灌溉作業。</summary>
    internal IQueryable<FieldOperation> Operations(Guid accountId) =>
        db.FieldOperations.Where(value => value.OperationType == FieldOperation.Irrigation &&
            value.Planting.Field.MainAccountId == accountId);

    /// <summary>開始交易，使栽培鎖維持到寫入完成。</summary>
    internal Task<IDbContextTransaction> BeginAsync(CancellationToken ct) => db.Database.BeginTransactionAsync(ct);

    /// <summary>鎖定登入帳號擁有的栽培並取得當前狀態與結束日，查無時回傳 null。</summary>
    internal async Task<PlantingLockState?> LockPlantingStatusAsync(Guid accountId, Guid plantingId, CancellationToken ct)
    {
        var states = await db.Database.SqlQuery<PlantingLockState>($"""
            SELECT p.status, p.completion_date
            FROM plantings p JOIN fields f ON f.id = p.field_id
            WHERE p.id = {plantingId} AND f.main_account_id = {accountId}
            FOR UPDATE OF p
            """).ToListAsync(ct);
        return states.SingleOrDefault();
    }

    /// <summary>將作業及明細加入交易。</summary>
    internal void Add(FieldOperation operation) => db.FieldOperations.Add(operation);

    /// <summary>移除灌溉作業及其明細，回傳是否刪到紀錄。</summary>
    internal async Task<bool> DeleteAsync(Guid accountId, Guid id, CancellationToken ct) =>
        await Operations(accountId).Where(value => value.Id == id).ExecuteDeleteAsync(ct) == 1;

    /// <summary>儲存交易內的作業與明細。</summary>
    internal Task SaveAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
