using CropPilot.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace CropPilot.Api.Fertilizers;

/// <summary>查詢帳號的肥料庫存與施肥紀錄，並提供交易內的鎖定查詢。</summary>
public sealed class FertilizerRepository(CropPilotDbContext db)
{
    /// <summary>限制資材查詢為登入帳號的肥料。</summary>
    internal IQueryable<AgriculturalMaterial> Materials(Guid accountId, string? type = AgriculturalMaterial.Fertilizer) =>
        db.AgriculturalMaterials.Where(x => x.MainAccountId == accountId && (type == null || x.MaterialType == type));

    /// <summary>限制批次查詢為登入帳號的肥料批次。</summary>
    internal IQueryable<MaterialPurchaseLot> Lots(Guid accountId, string? type = AgriculturalMaterial.Fertilizer) =>
        db.MaterialPurchaseLots.Where(x => Materials(accountId, type).Any(m => m.Id == x.MaterialId));

    /// <summary>查詢登入帳號擁有的栽培紀錄。</summary>
    internal IQueryable<Planting> Plantings(Guid accountId) =>
        db.Plantings.Where(x => x.Field.MainAccountId == accountId);

    /// <summary>載入已核對歸屬的栽培之施肥作業、明細及批次。</summary>
    internal IQueryable<FieldOperation> Operations(Guid plantingId) =>
        db.FieldOperations.Where(x => x.PlantingId == plantingId && x.OperationType == FieldOperation.MaterialApplication)
            .Include(x => x.Application!).ThenInclude(x => x.Details).ThenInclude(x => x.Lot).ThenInclude(x => x.Material);

    /// <summary>查詢指定施肥作業所屬的栽培 ID。作業不屬於帳號或類型不符時回傳 null。</summary>
    internal Task<Guid?> FindOperationPlantingIdAsync(Guid accountId, Guid operationId, CancellationToken ct) =>
        db.FieldOperations.Where(x => x.Id == operationId && x.OperationType == FieldOperation.MaterialApplication
            && x.Planting.Field.MainAccountId == accountId)
            .Select(x => (Guid?)x.PlantingId).SingleOrDefaultAsync(ct);

    /// <summary>查詢扣用指定庫存批次的全部明細。</summary>
    internal IQueryable<MaterialApplicationDetail> Usage(Guid lotId) =>
        db.MaterialApplicationDetails.Where(x => x.MaterialPurchaseLotId == lotId);

    /// <summary>開始施肥或庫存寫入交易，鎖定維持至提交或回滾。</summary>
    internal Task<IDbContextTransaction> BeginAsync(CancellationToken ct) => db.Database.BeginTransactionAsync(ct);

    /// <summary>鎖定已核對歸屬的資材，序列化批次建立及資材變更。</summary>
    internal Task LockMaterialAsync(Guid id, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""SELECT id FROM agricultural_materials WHERE id = {id} FOR UPDATE""", ct);

    /// <summary>鎖定已核對歸屬的批次，序列化扣量及批次變更。</summary>
    internal Task LockLotAsync(Guid id, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""SELECT id FROM material_purchase_lots WHERE id = {id} FOR UPDATE""", ct);

    /// <summary>鎖定已核對歸屬的栽培，避免狀態與施肥同時變更。</summary>
    internal Task LockPlantingAsync(Guid id, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""SELECT id FROM plantings WHERE id = {id} FOR UPDATE""", ct);

    /// <summary>在已鎖定的栽培下鎖定待修改的施肥作業。</summary>
    internal Task LockOperationAsync(Guid plantingId, Guid operationId, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT id FROM field_operations
            WHERE planting_id = {plantingId} AND id = {operationId} AND operation_type = {FieldOperation.MaterialApplication}
            FOR UPDATE
            """, ct);

    /// <summary>將新增的實體加入目前的寫入交易。</summary>
    internal void Add<T>(T entity) where T : class => db.Add(entity);

    /// <summary>標記要從目前交易移除的實體。</summary>
    internal void Remove<T>(T entity) where T : class => db.Remove(entity);

    /// <summary>儲存變更，資材名稱唯一索引衝突時回傳 false。</summary>
    internal async Task<bool> SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: FertilizationModel.MaterialNameIndex })
        {
            return false;
        }
    }

    /// <summary>查詢資材及其所有批次的即時餘量。</summary>
    internal IQueryable<MaterialStock> MaterialStocks(Guid accountId, Guid? materialId = null, string type = AgriculturalMaterial.Fertilizer) =>
        Materials(accountId, type).Where(x => materialId == null || x.Id == materialId)
            .OrderBy(x => x.Name).Select(material => new MaterialStock(material,
                (db.MaterialPurchaseLots.Where(lot => lot.MaterialId == material.Id)
                    .Sum(lot => (decimal?)lot.ReceivedQuantity) ?? 0)
                - (db.MaterialApplicationDetails.Where(x => x.Lot.MaterialId == material.Id)
                    .Sum(x => (decimal?)x.Quantity) ?? 0)));

    /// <summary>查詢批次及其即時餘量。</summary>
    internal IQueryable<LotStock> LotStocks(Guid accountId, Guid materialId, string type = AgriculturalMaterial.Fertilizer) =>
        Lots(accountId, type).Where(lot => lot.MaterialId == materialId)
            .OrderByDescending(x => x.AvailableFrom).ThenByDescending(x => x.Id)
            .Select(lot => new LotStock(lot, lot.ReceivedQuantity -
                (db.MaterialApplicationDetails.Where(x => x.MaterialPurchaseLotId == lot.Id)
                    .Sum(x => (decimal?)x.Quantity) ?? 0)));
}

/// <summary>肥料資材及全部批次合計的剩餘量。</summary>
internal sealed record MaterialStock(AgriculturalMaterial Material, decimal Remaining);

/// <summary>肥料批次及該批次的剩餘量。</summary>
internal sealed record LotStock(MaterialPurchaseLot Lot, decimal Remaining);

