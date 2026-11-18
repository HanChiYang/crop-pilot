using CropPilot.Api.Data;
using CropPilot.Contract.Models;

namespace CropPilot.Api.Fertilizers;

/// <summary>將資料庫保存的庫存單位轉為 API 回應的顯示文字。</summary>
internal static class MaterialUnitLabels
{
    private static readonly Dictionary<PesticideStockUnit, string> Labels = new()
    {
        [PesticideStockUnit.LiterEnum] = "公升",
        [PesticideStockUnit.MilliliterEnum] = "毫升",
        [PesticideStockUnit.KilogramEnum] = "公斤",
        [PesticideStockUnit.GramEnum] = "公克"
    };

    /// <summary>取得庫存單位的顯示文字。農藥的單位代碼轉為中文，肥料的自訂單位原樣回傳。</summary>
    /// <param name="materialType">資材類型，例如 <see cref="AgriculturalMaterial.Pesticide"/>。</param>
    /// <param name="stockUnit">資料庫保存的庫存單位。</param>
    /// <exception cref="KeyNotFoundException">農藥單位代碼不在契約定義內，或契約新增單位卻未加入顯示文字。</exception>
    internal static string Display(string materialType, string stockUnit) =>
        materialType == AgriculturalMaterial.Pesticide
            ? Labels[ContractEnumJsonConverter<PesticideStockUnit>.FromCode(stockUnit)]
            : stockUnit;
}
