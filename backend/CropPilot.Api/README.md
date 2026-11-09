# CropPilot 登入後端

使用 .NET 10、ASP.NET Core Identity、官方 OpenID Connect 中介軟體與 PostgreSQL。帳密登入、Google 登入入口、防偽權杖和登出依照 [OpenAPI](../../docs/openapi/openapi.yaml)。

## 在本機執行

在本機 PostgreSQL 準備一個空的開發資料庫，並建立可操作其結構的資料庫帳號。在 Google Cloud 使用 Web application 類型的 OAuth 用戶端，註冊 `https://localhost:7081/signin-google` 為回呼網址。以下指令在 `backend/` 目錄執行。請以本機設定值替換角括號中的文字。

```powershell
$project = 'CropPilot.Api/CropPilot.Api.csproj'
dotnet user-secrets set 'ConnectionStrings:CropPilot' '<PostgreSQL 連線字串>' --project $project
dotnet user-secrets set 'Authentication:Google:ClientId' '<Google Client ID>' --project $project
dotnet user-secrets set 'Authentication:Google:ClientSecret' '<Google Client Secret>' --project $project

$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet tool restore
dotnet ef migrations script --project $project --output CropPilot.Api/obj/identity.sql
# 先審查 SQL 的資料表、欄位、外鍵及唯一索引，再套用至開發資料庫。
dotnet ef database update --project $project
dotnet run --project $project -- --urls https://localhost:7081
```

以瀏覽器開啟 `https://localhost:7081/api/auth/google` 即可開始登入。登入成功時，回應會設定 `CropPilot.Session` 並導向 `/dashboard`。登入後的頁面由 Angular 提供，整合時請讓 Angular 和 API 同源，並將 `/api` 和 `/signin-google` 轉送給後端。未提供 Google 憑證時，Google 登入入口會回到登入頁並帶上失敗代碼。使用已在 Identity 建立的測試帳號時，帳密登入須先向 `GET /api/auth/csrf` 取得權杖。

## 重新生成登入契約

在 `backend/` 目錄執行：

```powershell
./tools/Generate-AuthContract.ps1 -GeneratorJar '<OpenAPI Generator 7.27.0 JAR 路徑>'
```

生成的 Controller 和 DTO 位於 `Generated`。登入行為在 `Authentication`，Google 回呼 `/signin-google` 由中介軟體接收。
