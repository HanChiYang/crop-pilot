namespace CropPilot.Api.Constants;

/// <summary>集中定義驗證與身分驗證的錯誤訊息。</summary>
internal static class ErrorMessages
{
    internal const string InvalidAccessTokenSubject = "The access token subject must be a non-empty account UUID.";
    internal const string FieldNameAlreadyUsed = "此名稱已使用。";
    internal const string FieldNameInvalid = "田區名稱須為 1 至 120 個字元，且不得含有無效字元。";
    internal const string FieldAreaInvalid = "面積須大於零、不超過 9999999999.9999，且最多四位小數。";
    internal const string FieldCoordinatesMustBePaired = "緯度與經度須成對填寫。";
    internal const string FieldLatitudeInvalid = "緯度須介於 -90 至 90，且最多六位小數。";
    internal const string FieldLongitudeInvalid = "經度須介於 -180 至 180，且最多六位小數。";
    internal const string FieldNotesInvalid = "備註最多 1000 個字元，且不得含有無效字元。";
    internal const string FieldDecimalExpected = "Expected a decimal number.";
    internal const string FieldDecimalPrecisionExceeded = "The number has too many decimal places.";
    internal const string FieldDeleteIdsInvalid = "請指定至少一個田區，且不可重複。";
    internal const string FieldHasPlantings = "有栽培紀錄的田區不能刪除。";
    internal const string UnexpectedFieldDeleteResult = "Unexpected field deletion result.";
    internal const string PlantingCropNameInvalid = "作物名稱須為 1 至 120 個字元，且不得含有無效字元。";
    internal const string PlantingHistoryFieldIdInvalid = "田區篩選條件須為有效的 UUID。";
    internal const string PlantingVarietyInvalid = "品種最多 120 個字元，且不得含有無效字元。";
    internal const string PlantingNotesInvalid = "備註最多 1000 個字元，且不得含有無效字元。";
    internal const string PlantingActualDateRequired = "栽培中或已結束須填寫實際栽種日。";
    internal const string PlantingStatusInvalid = "栽培狀態不正確。";
    internal const string PlantingTransitionInvalid = "目前的栽培狀態無法變更為所選狀態。";
    internal const string PlantingDeleteInvalid = "只有規劃中的栽培紀錄可以刪除。";
    internal const string PlantingAreaInvalid = "占用面積須大於 0，最多四位小數，且不得超過 9999999999.9999 m²。";
    internal const string PlantingAreaExceedsAvailable = "占用面積超過可分配的 {0:0.####} m²。";
    internal const string FieldAreaBelowAllocated = "田區面積不得小於已分配面積。";
    internal const string FertilizerTextInvalid = "請輸入有效的名稱或庫存單位。";
    internal const string FertilizerUnitLocked = "已有庫存批次，無法修改庫存單位。";
    internal const string FertilizerNameConflict = "此肥料名稱已使用。";
    internal const string FertilizerMaterialInUse = "仍有庫存批次，無法刪除肥料資材。";
    internal const string FertilizerLotInUse = "批次已有施肥紀錄，無法刪除。";
    internal const string FertilizerQuantityInvalid = "數量須大於零、小於 10 億，且最多六位小數。";
    internal const string FertilizerPriceInvalid = "單價須大於等於零且小於 10 億，且最多六位小數。";
    internal const string FertilizerDateInvalid = "請填寫符合日期限制的日期。";
    internal const string FertilizerQuantityBelowUsed = "批次數量不得低於已施用量。";
    internal const string FertilizerLotDateInUse = "批次生效日不得晚於最早的施肥日期。";
    internal const string FertilizerPlantingPlanned = "規劃中的栽培無法登記施肥。";
    internal const string FertilizerDetailsRequired = "請至少填寫一項肥料明細。";
    internal const string FertilizerDetailInvalid = "施肥明細不存在或重複。";
    internal const string FertilizerSourceInvalid = "請選擇進貨或期初庫存。";
    internal const string FertilizerCurrencyInvalid = "首版肥料庫存僅支援新臺幣。";
    internal const string FertilizerLotUnavailable = "施肥日期不得早於批次生效日。";
    internal const string FertilizerStockInsufficient = "此批次的庫存餘量不足。";
}
