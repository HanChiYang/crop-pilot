namespace CropPilot.Api.Authentication;

/// <summary>提供目前請求使用的主帳號 ID，由 Bearer 權杖驗證流程設定。</summary>
/// <param name="Id">從已簽章的 <c>sub</c> 取得的主帳號 UUID。</param>
internal sealed record AuthenticatedAccount(Guid Id);
