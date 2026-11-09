# CropPilot 協作指引

- 新對話接續專案時，先讀 [功能開發週期](docs/development-cycle.md)的「目前進度」。以專案文件中的確認事項作為接續依據。
- 進行需求或 UX 討論時，讀 [原型規劃](docs/prototype-plan.md)的「UX 草案審查記錄」。該處保存設計稿連結、審查狀態、待決事項與歷史 HTML 原型。
- 新功能先做需求訪談，一次問一個影響方向的問題。確認首版範圍與驗收條件後，以 Figma 做桌機畫面和可點選流程，設計審查後再接續資料庫與 API 設計及 Angular 實作。
- API 採 contract-first。變更契約時先更新 Stoplight 匯出的 `openapi.yaml`，再從同一份規格生成 ASP.NET Core 抽象 Controller、請求與回應 DTO，以及 Angular client。業務實作放在生成目錄之外，以便重新生成。[OpenAPI 契約](docs/openapi/openapi.yaml)
- 後端用 `dotnet` 建置。OpenAPI Generator CLI 由 `java -jar` 執行，負責規格驗證及程式碼生成。
- PostgreSQL 的結構版本由 EF Core Migrations 管理，搭配 Npgsql。套用 migration 前先匯出 SQL，依設計核對外鍵、刪除行為及欄位約束，再套用到測試資料庫驗證。[資料庫與 API 設計](docs/design/first-desktop-slice-data-api.md)
- PostgreSQL 資料表與欄位一律使用小寫 `snake_case`，包含 ASP.NET Core Identity 的資料表與欄位。ER 圖使用實際的資料表與欄位名稱，API JSON 欄位依 OpenAPI 契約命名。
- 登入採 ASP.NET Core Identity 與官方 OpenID Connect 中介軟體。Google 首次登入以已驗證的電子郵件建立本地帳號，並以 Google 的 `sub` 綁定身分。電子郵件與既有帳號衝突時，提示使用原帳密。[Google 登入設計](docs/design/first-desktop-slice-data-api.md#google-登入)
