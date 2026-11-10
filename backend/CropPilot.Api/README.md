# CropPilot 登入後端

使用 .NET 10、ASP.NET Core Identity、官方 OpenID Connect 中介軟體與 PostgreSQL。帳密登入、Google 登入、工作階段查詢、防偽權杖與登出依照 OpenAPI 契約。[OpenAPI](../../docs/openapi/openapi.yaml)

## 在本機執行

在本機 PostgreSQL 準備開發資料庫，並建立可操作其結構的資料庫帳號。若要搭配 Angular 開發伺服器使用 Google 登入，在 Google Cloud 的 Web application OAuth 用戶端註冊 `http://localhost:4200/signin-google`。從後端直接啟動 Google 登入時，回呼網址使用 `http://localhost:7081/signin-google`。以下指令在 `backend/` 目錄執行，將角括號中的文字替換成本機設定值。

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
dotnet run --project $project -- --urls http://localhost:7081
```

在 `http://localhost:4200/login` 操作前端。Angular 用 Bearer Access Token 呼叫 `GET /api/v1/auth/session` 確認登入狀態。重新整理或完成 Google 登入後，Angular 用 `CropPilot.Renewal` Cookie 呼叫續期端點取得 Access Token。帳密登入、續期與登出前會先取得防偽權杖。[前端操作說明](../../dashboard/README.md)

本機以 `Development` 環境使用 HTTP。部署時使用 HTTPS，並設定部署網域的 Google OAuth HTTPS 回呼網址。非開發環境會啟用 HTTPS 轉址，且登入及防偽 Cookie 會加上 `Secure`。

JWT 使用 HS256 簽章。部署時透過 `Authentication__Jwt__SigningKey` 提供至少 32 個隨機位元組的 Base64 值，多執行個體共用同一把 JWT 簽章金鑰。本機開發可省略此設定，程式會在每次啟動時產生暫用金鑰。

續期 Cookie 與防偽權杖使用 ASP.NET Core Data Protection。部署時保存金鑰環，多執行個體共用同一組金鑰環與應用程式名稱。

若開發庫沒有帳密測試帳號，可用以下指令建立一個已啟用的 `USER`。種子指令僅能在 `Development` 執行，帳號已存在時會保留原本的密碼。

```powershell
dotnet user-secrets set 'DevelopmentAccount:Username' 'Farmer01' --project $project
dotnet user-secrets set 'DevelopmentAccount:Password' '<本機測試密碼>' --project $project
dotnet run --project $project -- --SeedDevelopmentAccount=true
```

## 重新生成 API 契約

在 `backend/` 目錄執行：

```powershell
./tools/Generate-ApiContract.ps1 -GeneratorJar '<OpenAPI Generator 7.27.0 JAR 路徑>'
```

生成的 Controller 和 DTO 位於 `Generated`，Angular client 位於 `dashboard/src/app/generated`。登入行為在 `Authentication`，Google 回呼 `/signin-google` 由中介軟體接收。
