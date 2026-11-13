# CropPilot 協作指引

- 新對話接續專案時，先讀 [功能開發週期](docs/development-cycle.md)的「目前進度」。以專案文件中的確認事項作為接續依據。
- 進行需求或 UX 討論時，讀 [原型規劃](docs/prototype-plan.md)的「UX 草案審查記錄」。該處保存設計稿連結、審查狀態、待決事項與歷史 HTML 原型。
- 新功能先做需求訪談，一次問一個影響方向的問題。確認首版範圍與驗收條件後，以 Figma 做桌機畫面和可點選流程，設計審查後再接續資料庫與 API 設計及 Angular 實作。
- 開發前先取得最新的 `origin/develop`，以它為基礎建立本機 `feature/<主題>` 分支並在該分支修改與驗證。推送同名分支，以遠端 `develop` 為目標建立 PR，並透過該 PR 合併。
- 依影響範圍驗證。小改動核對差異即可，行為變更驗證直接受影響的流程，跨模組或高風險變更依影響擴大驗證。
- 提請核准 commit 前，審查本次待提交的差異。若可使用 `$pre-commit-review`，依該 skill 執行。登入、權限、資料庫遷移或跨模組行為變更，須派全新上下文的 subagent 做唯讀獨立審查，只交付範圍、基準、必要需求與專案規則。
- 執行 commit 前，核對待提交的差異與已審查、已核准的版本。如果內容或範圍變動，重新審查並請使用者核准。
- 功能變更依可獨立審查的範圍拆成小 commit。實作與測試可分開提交。建立 PR 前，先提交並推送該切片的實作和對應的自動化測試。
- 使用者明確說「可發 PR」、「可以發 PR」或以同義措辭核准建立 PR（含草稿）時，視為同時核准本次 PR 所需的 commit、push 和建立 PR。完成提交前審查與驗證後，依序執行，無須再逐項詢問。其他情況依使用者明確核准的操作執行。提請核准時，commit 附上變更摘要、審查與驗證結果及 commit 訊息，push 附上遠端與分支，PR 附上來源分支、目標分支、標題與完整內文。已核准的內容或目標有變動時，重新取得受影響項目的核准。合併另行核准。
- API 採 contract-first。變更契約時先更新 Stoplight 匯出的 `openapi.yaml`，再從同一份規格生成 ASP.NET Core 抽象 Controller、請求與回應 DTO，以及 Angular client。業務實作放在生成目錄之外，以便重新生成。[OpenAPI 契約](docs/openapi/openapi.yaml)
- 後端用 `dotnet` 建置。OpenAPI Generator CLI 由 `java -jar` 執行，負責規格驗證及程式碼生成。
- PostgreSQL 的結構版本由 EF Core Migrations 管理，搭配 Npgsql。套用 migration 前先匯出 SQL，依設計核對外鍵、刪除行為及欄位約束，再套用到測試資料庫驗證。[資料庫設計](docs/design/first-desktop-slice-database.md)
- PostgreSQL 資料表與欄位一律使用小寫 `snake_case`，包含 ASP.NET Core Identity 的資料表與欄位。應用程式的資料表一律以 `uuid` 型別的單欄 `id` 作為主鍵。ER 圖使用實際的資料表與欄位名稱，API JSON 欄位依 OpenAPI 契約命名。
- 登入採 ASP.NET Core Identity。Google 登入使用官方 OpenID Connect 中介軟體，外部身分與 Email 比對依設計文件實作。[Google 登入設計](docs/design/first-desktop-slice-data-api.md#google-登入)

## 後端 API 實作慣例

- 依 Controller → Service → Repository 分層。Controller 處理 HTTP 請求和回應，Service 負責業務驗證與操作流程，Repository 負責 EF Core 查詢、寫入及辨識資料庫約束衝突。
- 請求欄位與 Service 所需資料一致時，直接傳入生成的請求 DTO。只有欄位或語意有實際差異時才新增輸入型別。實體到回應 DTO 的轉換放在 API 層的 mapper。
- 由 ASP.NET Core 處理模型繫結與授權錯誤。Service 用結果型別表達可預期的業務結果，例如欄位驗證失敗或名稱衝突，Controller 依 OpenAPI 契約回傳對應的 HTTP 狀態與 `ProblemDetails`。未預期的例外交給全域例外中介軟體。新增的後端錯誤訊息集中在 `Constants/ErrorMessages.cs`。
- Bearer API 由 JWT 驗證統一檢查 `sub`，端點使用驗證流程提供的主帳號 ID。查詢和寫入依該 ID 限制資源的擁有範圍。[Bearer 驗證方式](docs/design/first-desktop-slice-data-api.md#bearer-驗證方式)
- 新增或修改的手寫 C# 類別與方法使用繁體中文 XML 註解，說明用途及有意義的參數、回傳值或失敗條件。一般程式碼中的空字串值使用 `string.Empty`，需要編譯期常數時使用字串常值。
