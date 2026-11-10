# CropPilot 協作指引

- 新對話接續專案時，先讀 [功能開發週期](docs/development-cycle.md)的「目前進度」。以專案文件中的確認事項作為接續依據。
- 進行需求或 UX 討論時，讀 [原型規劃](docs/prototype-plan.md)的「UX 草案審查記錄」。該處保存設計稿連結、審查狀態、待決事項與歷史 HTML 原型。
- 新功能先做需求訪談，一次問一個影響方向的問題。確認首版範圍與驗收條件後，以 Figma 做桌機畫面和可點選流程，設計審查後再接續資料庫與 API 設計及 Angular 實作。
- 開發前先取得最新的 `origin/develop`，以它為基礎建立本機 `feature/<主題>` 分支並在該分支修改與驗證。推送同名分支，以遠端 `develop` 為目標建立 PR，並透過該 PR 合併。
- 依影響範圍驗證。小改動核對差異即可，行為變更驗證直接受影響的流程，跨模組或高風險變更依影響擴大驗證。
- 提請核准 commit 前，審查本次待提交的差異。若可使用 `$pre-commit-review`，依該 skill 執行。登入、權限、資料庫遷移或跨模組行為變更，須派全新上下文的 subagent 做唯讀獨立審查，只交付範圍、基準、必要需求與專案規則。
- 執行 commit 前，核對待提交的差異與已審查、已核准的版本。如果內容或範圍變動，重新審查並請使用者核准。
- 每次 commit、push、建立 PR（含草稿）與合併都須分別取得使用者明確核准。有選項卡時優先使用。核准 commit 時附上變更摘要、審查結果與 commit 訊息。核准 push 時提供遠端與分支。建立 PR 前提供來源分支、目標分支、標題與內文。
- API 採 contract-first。變更契約時先更新 Stoplight 匯出的 `openapi.yaml`，再從同一份規格生成 ASP.NET Core 抽象 Controller、請求與回應 DTO，以及 Angular client。業務實作放在生成目錄之外，以便重新生成。[OpenAPI 契約](docs/openapi/openapi.yaml)
- 後端用 `dotnet` 建置。OpenAPI Generator CLI 由 `java -jar` 執行，負責規格驗證及程式碼生成。
- PostgreSQL 的結構版本由 EF Core Migrations 管理，搭配 Npgsql。套用 migration 前先匯出 SQL，依設計核對外鍵、刪除行為及欄位約束，再套用到測試資料庫驗證。[資料庫設計](docs/design/first-desktop-slice-database.md)
- PostgreSQL 資料表與欄位一律使用小寫 `snake_case`，包含 ASP.NET Core Identity 的資料表與欄位。應用程式的資料表一律以 `uuid` 型別的單欄 `id` 作為主鍵。ER 圖使用實際的資料表與欄位名稱，API JSON 欄位依 OpenAPI 契約命名。
- 登入採 ASP.NET Core Identity。Google 登入使用官方 OpenID Connect 中介軟體，外部身分與 Email 比對依設計文件實作。[Google 登入設計](docs/design/first-desktop-slice-data-api.md#google-登入)
