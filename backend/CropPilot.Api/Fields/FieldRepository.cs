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
