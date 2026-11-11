using System.ComponentModel.DataAnnotations;
using CropPilot.Contract.Models;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;

namespace CropPilot.Api.Fields;

/// <summary>調整生成的田區及栽培儲存請求在 MVC 中使用的驗證規則。</summary>
internal sealed class FieldSaveValidationMetadata : IValidationMetadataProvider
{
    /// <summary>移除 MVC 的字串長度驗證，交由服務以 Unicode 碼點計算字數。</summary>
    public void CreateValidationMetadata(ValidationMetadataProviderContext context)
    {
        if (context.Key.ContainerType != typeof(CreateFieldRqDto) &&
            context.Key.ContainerType != typeof(EditFieldRqDto) &&
            context.Key.ContainerType != typeof(CreatePlantingRqDto) &&
            context.Key.ContainerType != typeof(EditPlantingRqDto))
        {
            return;
        }

        foreach (var attribute in context.Attributes.OfType<MaxLengthAttribute>())
        {
            context.ValidationMetadata.ValidatorMetadata.Remove(attribute);
        }
    }
}
