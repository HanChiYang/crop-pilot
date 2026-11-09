# CropPilot

| 目錄 | 用途 |
| --- | --- |
| [dashboard/](dashboard/) | Angular 前端，目前只有開發入口說明。 |
| [backend/](backend/) | ASP.NET Core API、EF Core migrations 和後端工具。 |
| [docs/](docs/) | 前後端共用的需求、設計、原型和 [OpenAPI 契約](docs/openapi/openapi.yaml)。 |

從專案根目錄建置後端：

```powershell
dotnet build backend/CropPilot.Api/CropPilot.Api.csproj
```

本機登入、資料庫與契約生成指令見[後端操作說明](backend/CropPilot.Api/README.md)。目前開發進度見[功能開發週期](docs/development-cycle.md#目前進度)。
