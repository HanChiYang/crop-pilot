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
    internal const string PlantingVarietyInvalid = "品種最多 120 個字元，且不得含有無效字元。";
    internal const string PlantingNotesInvalid = "備註最多 1000 個字元，且不得含有無效字元。";
    internal const string PlantingActualDateRequired = "栽培中須填寫實際栽種日。";
    internal const string PlantingStatusInvalid = "栽培狀態不正確。";
}
