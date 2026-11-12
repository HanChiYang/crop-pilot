using System.Text;
using CropPilot.Api.Constants;
using CropPilot.Api.Data;
using CropPilot.Contract.Models;
using Microsoft.EntityFrameworkCore;

namespace CropPilot.Api.Irrigations;

/// <summary>驗證並管理單筆栽培的灌溉紀錄。</summary>
public sealed class IrrigationService(IrrigationRepository repository)
{
    /// <summary>依實際作業日、建立時間與 ID 回傳清單，栽培無法存取時回傳 null。</summary>
    internal async Task<List<FieldOperation>?> ListAsync(Guid accountId, Guid plantingId, CancellationToken ct)
    {
        if (!await repository.Plantings(accountId).AnyAsync(value => value.Id == plantingId, ct))
        {
            return null;
        }

        return await repository.Operations(accountId).AsNoTracking().Where(value => value.PlantingId == plantingId)
            .Include(value => value.IrrigationDetail)
            .OrderByDescending(value => value.OperationDate).ThenByDescending(value => value.CreatedAt)
            .ThenByDescending(value => value.Id).ToListAsync(ct);
    }

    /// <summary>依栽培 ID 新增灌溉作業。</summary>
    internal Task<IrrigationWriteResult> CreateAsync(Guid accountId, Guid plantingId, SaveIrrigationRqDto request,
        CancellationToken ct) => SaveAsync(accountId, plantingId, null, request, ct);

    /// <summary>依作業 ID 更新灌溉紀錄，保留原識別碼。</summary>
    internal async Task<IrrigationWriteResult> EditAsync(Guid accountId, Guid id, SaveIrrigationRqDto request, CancellationToken ct)
    {
        var plantingId = await FindPlantingIdAsync(accountId, id, ct);
        return plantingId is { } parent ? await SaveAsync(accountId, parent, id, request, ct)
            : new(IrrigationWriteStatus.NotFound);
    }

    /// <summary>刪除帳號擁有的灌溉作業，無法存取或已刪除時回傳 false。</summary>
    internal async Task<bool> DeleteAsync(Guid accountId, Guid id, CancellationToken ct)
    {
        var plantingId = await FindPlantingIdAsync(accountId, id, ct);
        if (plantingId is null)
        {
            return false;
        }

        await using var transaction = await repository.BeginAsync(ct);
        if (await repository.LockPlantingStatusAsync(accountId, plantingId.Value, ct) is null ||
            !await repository.DeleteAsync(accountId, id, ct))
        {
            return false;
        }

        await transaction.CommitAsync(ct);
        return true;
    }

    /// <summary>從帳號可存取的灌溉作業取得所屬栽培 ID。</summary>
    private Task<Guid?> FindPlantingIdAsync(Guid accountId, Guid id, CancellationToken ct) =>
        repository.Operations(accountId).Where(value => value.Id == id)
            .Select(value => (Guid?)value.PlantingId).SingleOrDefaultAsync(ct);

    /// <summary>核對栽培狀態及表單，將共用作業和灌溉明細一起儲存。</summary>
    private async Task<IrrigationWriteResult> SaveAsync(Guid accountId, Guid plantingId, Guid? operationId,
        SaveIrrigationRqDto request, CancellationToken ct)
    {
        await using var transaction = await repository.BeginAsync(ct);
        var status = await repository.LockPlantingStatusAsync(accountId, plantingId, ct);
        if (status is null)
        {
            return new(IrrigationWriteStatus.NotFound);
        }

        var operation = operationId is null
            ? new FieldOperation { PlantingId = plantingId, OperationType = FieldOperation.Irrigation }
            : await repository.Operations(accountId).Include(value => value.IrrigationDetail)
                .SingleOrDefaultAsync(value => value.Id == operationId && value.PlantingId == plantingId, ct);
        if (operation is null)
        {
            return new(IrrigationWriteStatus.NotFound);
        }
        if (status == Planting.Planned)
        {
            return new(IrrigationWriteStatus.Conflict, Message: ErrorMessages.IrrigationPlantingPlanned);
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8));
        if (request.OperationDate == default || request.OperationDate > today)
        {
            return new(IrrigationWriteStatus.Invalid, Message: ErrorMessages.IrrigationDateInvalid, Field: "operationDate");
        }
        var method = ContractEnumJsonConverter<IrrigationMethod>.GetName(request.Method);
        if (method is null)
        {
            return new(IrrigationWriteStatus.Invalid, Message: ErrorMessages.IrrigationMethodInvalid, Field: "method");
        }
        if (request.WaterVolumeLiters is { } liters &&
            (liters <= 0 || liters >= 1_000_000_000 || decimal.Round(liters, 6) != liters))
        {
            return new(IrrigationWriteStatus.Invalid, Message: ErrorMessages.IrrigationVolumeInvalid, Field: "waterVolumeLiters");
        }

        if (request.Description is { } description &&
            (description.Contains('\0') || description.EnumerateRunes().Count() > 500))
        {
            return new(IrrigationWriteStatus.Invalid, Message: ErrorMessages.IrrigationDescriptionInvalid, Field: "description");
        }

        operation.OperationDate = request.OperationDate;
        operation.Description = request.Description;
        operation.UpdatedAt = DateTimeOffset.UtcNow;
        operation.IrrigationDetail ??= new IrrigationDetail { FieldOperationId = operation.Id, Method = method };
        operation.IrrigationDetail.Method = method;
        operation.IrrigationDetail.WaterVolumeLiters = request.WaterVolumeLiters;
        if (operationId is null)
        {
            repository.Add(operation);
        }

        await repository.SaveAsync(ct);
        await transaction.CommitAsync(ct);
        return new(IrrigationWriteStatus.Success, operation);
    }
}

/// <summary>灌溉新增或編輯的業務結果。</summary>
internal sealed record IrrigationWriteResult(IrrigationWriteStatus Status, FieldOperation? Operation = null,
    string? Message = null, string? Field = null);

/// <summary>灌溉寫入的成功或預期失敗類型。</summary>
internal enum IrrigationWriteStatus { Success, NotFound, Invalid, Conflict }
