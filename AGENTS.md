# CropPilot 協作指引

- 新對話接續專案時，先讀 [功能開發週期](docs/development-cycle.md)的「目前進度」。以專案文件中的確認事項作為接續依據。
- 進行需求或 UX 討論時，讀 [原型規劃](docs/prototype-plan.md)的「UX 草案審查記錄」。該處保存設計稿連結、審查狀態、待決事項與歷史 HTML 原型。
- 新功能先做需求訪談，一次問一個影響方向的問題。確認首版範圍與驗收條件後，以 Figma 做桌機畫面和可點選流程，設計審查後再接續資料庫與 API 設計及 Angular 實作。
- 開始開發前，先從遠端取得最新的 `origin/develop`，再以它為基礎建立本機 `feature/<主題>` 分支。
- 在本機 `feature/<主題>` 分支開發和提交，完成後推送同名分支到遠端。
- 以遠端 `feature/<主題>` 為來源、遠端 `develop` 為目標建立 Pull Request，並透過該 PR 合併。
- API 採 contract-first。變更契約時先更新 Stoplight 匯出的 `openapi.yaml`，再從同一份規格生成 ASP.NET Core 抽象 Controller、請求與回應 DTO，以及 Angular client。業務實作放在生成目錄之外，以便重新生成。[OpenAPI 契約](docs/openapi/openapi.yaml)
- 後端用 `dotnet` 建置。OpenAPI Generator CLI 由 `java -jar` 執行，負責規格驗證及程式碼生成。
- PostgreSQL 的結構版本由 EF Core Migrations 管理，搭配 Npgsql。套用 migration 前先匯出 SQL，依設計核對外鍵、刪除行為及欄位約束，再套用到測試資料庫驗證。[資料庫設計](docs/design/first-desktop-slice-database.md)
- PostgreSQL 資料表與欄位一律使用小寫 `snake_case`，包含 ASP.NET Core Identity 的資料表與欄位。應用程式的資料表一律以 `uuid` 型別的單欄 `id` 作為主鍵。ER 圖使用實際的資料表與欄位名稱，API JSON 欄位依 OpenAPI 契約命名。
- 登入採 ASP.NET Core Identity 與官方 OpenID Connect 中介軟體。Google 登入通過 ID token 驗證後，以 `identity_provider = GOOGLE` 和 `provider_user_id = sub` 查找外部身分。首次登入比對已驗證的 Email 與 `user_profiles`。若已有相同 Email，依主帳號回查並提示既有登入方式。[Google 登入設計](docs/design/first-desktop-slice-data-api.md#google-登入)
