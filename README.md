# CropPilot

| 目錄 | 用途 |
| --- | --- |
| [dashboard/](dashboard/) | Angular 桌機前端，包含首頁、登入與田區總覽空畫面。 |
| [backend/](backend/) | ASP.NET Core API、EF Core migrations 和後端工具。 |
| [docs/](docs/) | 前後端共用的需求、設計、原型和 [OpenAPI 契約](docs/openapi/openapi.yaml)。 |

從專案根目錄建置後端：

```powershell
dotnet build backend/CropPilot.Api/CropPilot.Api.csproj
```

本機登入切片的啟動方式見[前端操作說明](dashboard/README.md)和[後端操作說明](backend/CropPilot.Api/README.md)。目前開發進度見[功能開發週期](docs/development-cycle.md#目前進度)。
