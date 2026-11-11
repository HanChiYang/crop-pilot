using CropPilot.Api.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CropPilot.Api.Fields;

/// <summary>儲存田區，並依擁有者帳號限制查詢範圍。</summary>
public sealed class FieldRepository(CropPilotDbContext database)
{
    /// <summary>依名稱排序，列出指定帳號的田區。</summary>
    internal Task<List<Field>> ListAsync(Guid accountId, CancellationToken cancellationToken) =>
        OwnedFields(accountId).OrderBy(field => field.Name).ToListAsync(cancellationToken);

    /// <summary>在指定帳號的田區範圍內查詢單筆田區。</summary>
    internal Task<Field?> FindAsync(Guid accountId, Guid fieldId, CancellationToken cancellationToken) =>
        OwnedFields(accountId).SingleOrDefaultAsync(field => field.Id == fieldId, cancellationToken);

    /// <summary>在擁有者範圍內更新田區，並辨識名稱衝突及已刪除的田區。</summary>
    internal async Task<FieldWriteStatus> UpdateAsync(Field field, CancellationToken cancellationToken)
    {
        try
        {
            var count = await OwnedFields(field.MainAccountId).Where(value => value.Id == field.Id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(value => value.Name, field.Name)
                    .SetProperty(value => value.AreaSquareMeters, field.AreaSquareMeters)
                    .SetProperty(value => value.Latitude, field.Latitude)
                    .SetProperty(value => value.Longitude, field.Longitude)
                    .SetProperty(value => value.Description, field.Description), cancellationToken);
            return count == 0 ? FieldWriteStatus.NotFound : FieldWriteStatus.Success;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation &&
            exception.ConstraintName == "ix_fields_main_account_id_name")
        {
            return FieldWriteStatus.NameConflict;
        }
    }

    /// <summary>以單一交易刪除帳號擁有的指定田區；任一筆找不到時回復整批。</summary>
    /// <returns>刪除結果。外鍵限制拒絕刪除時回傳關聯資料衝突。</returns>
    internal async Task<FieldWriteStatus> DeleteAsync(Guid accountId, Guid[] fieldIds, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var ownedIds = await database.Database.SqlQuery<Guid>($"""
                SELECT id AS "Value" FROM fields
                WHERE main_account_id = {accountId} AND id = ANY({fieldIds})
                ORDER BY id FOR UPDATE
                """).ToListAsync(cancellationToken);
            if (ownedIds.Count != fieldIds.Length)
            {
                return FieldWriteStatus.NotFound;
            }

            await OwnedFields(accountId).Where(field => fieldIds.Contains(field.Id))
                .ExecuteDeleteAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return FieldWriteStatus.Success;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.ForeignKeyViolation &&
            exception.ConstraintName == Planting.FieldForeignKey)
        {
            await transaction.RollbackAsync(cancellationToken);
            return FieldWriteStatus.RelatedPlantings;
        }
    }

    /// <summary>儲存田區，並處理帳號與名稱唯一索引的衝突。</summary>
    /// <returns>儲存成功回傳 <see langword="true"/>。同帳號已有同名田區時回傳 <see langword="false"/>。</returns>
    internal async Task<bool> TryAddAsync(Field field, CancellationToken cancellationToken)
    {
        database.Fields.Add(field);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "ix_fields_main_account_id_name"
        })
        {
            database.Entry(field).State = EntityState.Detached;
            return false;
        }
    }

    /// <summary>建立依帳號篩選且停用追蹤的查詢。</summary>
    private IQueryable<Field> OwnedFields(Guid accountId) =>
        database.Fields.AsNoTracking().Where(field => field.MainAccountId == accountId);
}

/// <summary>表示田區寫入或刪除的可預期結果。</summary>
internal enum FieldWriteStatus
{
    Success,
    InvalidInput,
    NotFound,
    NameConflict,
    RelatedPlantings
}
