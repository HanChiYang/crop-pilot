# CropPilot 登入後端

使用 .NET 10、ASP.NET Core Identity、官方 OpenID Connect 中介軟體與 PostgreSQL。帳密登入、Google 登入、工作階段查詢、防偽權杖與登出依照 OpenAPI 契約。[OpenAPI](../../docs/openapi/openapi.yaml)

## 在本機執行

在本機 PostgreSQL 準備開發資料庫，並建立可操作其結構的資料庫帳號。若要搭配 Angular 開發伺服器使用 Google 登入，在 Google Cloud 的 Web application OAuth 用戶端註冊 `https://localhost:4200/signin-google`。從後端直接啟動 Google 登入時，回呼網址使用 `https://localhost:7081/signin-google`。以下指令在 `backend/` 目錄執行，將角括號中的文字替換成本機設定值。

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
dotnet dev-certs https --trust
dotnet run --project $project -- --urls https://localhost:7081
```

在 `https://localhost:4200/login` 操作前端。登入成功後，Angular 以 `GET /api/auth/session` 確認 `CropPilot.Session` Cookie 有效才顯示總覽。使用帳密登入和登出前，Angular 會先向 `GET /api/auth/csrf` 取得防偽權杖。Google 登入失敗會返回登入頁顯示提示。[前端操作說明](../../dashboard/README.md)

若開發庫沒有帳密測試帳號，可用以下指令建立一個已啟用的 `USER`。種子指令僅能在 `Development` 執行，帳號已存在時會保留原本的密碼。

```powershell
dotnet user-secrets set 'DevelopmentAccount:Username' 'Farmer01' --project $project
dotnet user-secrets set 'DevelopmentAccount:Password' '<本機測試密碼>' --project $project
dotnet run --project $project -- --SeedDevelopmentAccount=true
```

## 重新生成登入契約

在 `backend/` 目錄執行：

```powershell
./tools/Generate-AuthContract.ps1 -GeneratorJar '<OpenAPI Generator 7.27.0 JAR 路徑>'
```

生成的 Controller 和 DTO 位於 `Generated`，Angular client 位於 `dashboard/src/app/generated`。登入行為在 `Authentication`，Google 回呼 `/signin-google` 由中介軟體接收。
