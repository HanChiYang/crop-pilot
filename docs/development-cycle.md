# CropPilot｜功能開發週期

CropPilot 的學習目標是完整練習一個功能的開發流程，並保留日後能說明的設計決策與驗收證據。每次選一個小功能，從使用情境一路做到驗收。後續階段若發現需求或操作流程有缺漏，可以回到前一階段修正，再更新受影響的決策。

| 階段 | 要做的事 | 階段產出 |
| --- | --- | --- |
| 1. 需求討論 | 透過問答了解誰在什麼情境遇到什麼問題、現在怎麼處理，以及最希望改善哪一步。一起確認第一版要包含的操作、留待後續的項目和怎樣算完成。 | 一份經確認的短需求，包含使用情境、範圍、驗收條件與待釐清事項。 |
| 2. UI／UX | 以 Figma 作為主要設計稿。把相關桌機畫面並排，連成可點選的流程，確認入口、操作順序、欄位、回饋和錯誤處理。有設計回饋時先更新 Figma。 | Figma 畫面總覽與可操作流程，以及需要回到需求討論的問題。設計稿連結與審查決定存回專案。 |
| 3. 資料庫與 API 設計 | 依已確認的需求與流程，定義資料欄位、關聯與驗證規則。前後端先約定端點、請求、成功回應和錯誤回應。 | 足夠讓前後端各自開工的資料模型及 API 約定。 |
| 4. 實作與串接 | 以 C#／ASP.NET Core 實作後端 API 與資料存取。Angular 可先按約定格式用示意資料實作，待 API 就緒後串接。 | 可在本機走完的前後端流程。 |
| 5. 驗收與修正 | 依第 1 階段的條件，從本輪支援的裝置操作 UI，檢查正常操作、輸入錯誤及儲存失敗，修正後複驗。 | 驗收結果與後續項目。 |

首輪以桌機版為優先，UI 草案和功能驗收先走桌機流程。開發時保留基本的版面彈性，例如欄位可換行、表單不綁固定寬度。桌機流程穩定後，再依田間使用情境決定哪些操作需要優先做手機版。

整合測試優先涵蓋關鍵驗收條件和可能回歸的行為。從正式入口操作，驗證對外結果及與案例相關的資料庫狀態，確保行為出錯時測試會失敗。使用 Testcontainers 啟動 PostgreSQL 時，套用專案的 EF Core migrations，並走實際 API 的驗證和授權流程。若案例只差輸入值或欄位，且操作流程與斷言相同，用 xUnit `[Theory]` 集中成同一個參數化測試，保留各組輸入的測試結果。不同的業務行為各自命名，讓失敗原因容易辨識。

## 需求訪談方式

1. 先問「第一版最想幫誰完成哪一件農務？」請專案負責人描述實際情境，也可先看參考產品來找候選情境。
2. 一次問一個會影響方向的問題，依回答追問。優先理解目前的做法、最卡的一步，以及完成後應能看見什麼結果。需要取捨時，再提出少量選項供比較。
3. 將使用情境、首版範圍、驗收條件及待釐清事項整理成短需求，由專案負責人確認後進入 UI／UX。已確認事項、原型假設與參考產品的做法分開記錄。

## 如何參考「農務 e 把抓」

使用[官方功能介紹](https://agrepm.moa.gov.tw/Front)和[官方問答](https://agrepm.moa.gov.tw/Front/Home/QA)理解耕地、栽培、作業排程和田間紀錄如何串在一起。選定需求後，比較相關操作的入口、所需的前置資料和作業結果，再由專案負責人決定 CropPilot 要支援的範圍。參考產品的功能可以列為訪談選項。

## 目前進度

「首頁 → 登入 → 田區總覽空畫面」已完成前後端串接。本機 Docker PostgreSQL 已套用帳號 migration，Angular 以登入 API 建立工作階段，再用 `GET /api/v1/auth/session` 保護總覽。用 Edge 實際驗證兩個首頁登入入口、帳密錯誤提示、登入、重新整理及登出。Google 首次與重複登入、取消與 Email 衝突已用模擬 OIDC 測試，真實 Google 帳號仍待設定 OAuth 用戶端後驗收。[前端操作說明](../dashboard/README.md) · [API 設計](design/first-desktop-slice-data-api.md)

帳號 migration 已用舊版測試資料驗證升級，保留主帳號 UUID、密碼雜湊及 Google 綁定。帳號、登入方式、個人資料及角色分表，舊帳號遷移為 `ACTIVE` 並取得 `USER`。後臺授權的角色指派流程，以及本地帳號註冊與 Email OTP，待各自功能審查後實作。[資料表與 ER 圖](design/first-desktop-slice-database.md)

「空的田區總覽 → 新增田區 → 儲存後進入田區管理並看見新田區」已完成前後端串接，田區 API 與 Angular 串接分別經 PR #9、#10 合併至 `develop`。田區名稱和面積必填，座標和備註選填，總覽與管理列表皆查詢登入帳號的田區。PR #9 記錄 migration 驗證及 32 個 API 整合測試通過，涵蓋帳號隔離、權杖格式、並行重名與欄位驗證。PR #10 記錄 Angular 正式建置及 3 個 Playwright 案例通過，涵蓋未登入轉址、列表載入失敗重試、空列表入口、取消、新增後重新整理、欄位驗證與名稱衝突保留輸入。列表失敗與空列表使用攔截模擬。[田區 API PR](https://github.com/HanChiYang/crop-pilot/pull/9) · [前端串接 PR](https://github.com/HanChiYang/crop-pilot/pull/10) · [瀏覽器測試操作說明](../dashboard/README.md#執行田區瀏覽器測試)

田區編輯及刪除已經 PR #13 合併至 `develop`。編輯沿用新增表單並載入既有資料，刪除前顯示田區名稱供確認。批次刪除由 API 提供，任一田區不存在、不屬於登入帳號或有栽培紀錄時，整批保留。[田區與栽培資料](design/first-desktop-slice-data-api.md#田區與栽培資料)

作物栽培新增與查詢已在 `feature/crop-planting` 串接後端與 Angular。管理頁先選田區，再手動輸入作物名稱與選填品種。新增預設為「規劃中」，可改為「栽培中」或「採收中」。栽培中必填實際栽種日，採收中可留空。儲存或取消都保留田區選擇，田區總覽顯示各田區目前的作物、狀態及日期。[桌機畫面及 Figma 原型](ui/first-desktop-slice/README.md) · [桌機版需求](requirements/desktop-field-and-planting.md#驗收切片)

Angular 正式建置及 5 個 Edge 瀏覽器測試通過。栽培流程驗證初次進入、選田區、空列表、取消、新增後重新整理，以及總覽同時顯示「規劃中」「採收中」與「尚未設定」。列表和儲存失敗以攔截回應模擬，分別驗證保留田區並重試、留在表單並保留輸入。

OpenAPI 契約已通過 OpenAPI Generator 7.27.0 驗證，Controller、DTO 和 Angular client 由同一契約生成。`CreatePlantings` migration 的 SQL 已核對並套用至獨立 PostgreSQL 測試庫，52 個 API 整合測試通過，包含跨帳號隔離、日期驗證、已結束紀錄保留、單筆與批次刪除 `409`，以及並行新增與刪除的一致性。後續切片處理栽培紀錄編輯、刪除與狀態變更。[OpenAPI 契約](openapi/openapi.yaml) · [資料庫設計](design/first-desktop-slice-database.md)

後端 API 分層、DTO、錯誤處理、Bearer 擁有權驗證及 XML 註解慣例已整理至 `AGENTS.md`，並經 PR #11 合併至 `develop`。[協作指引](../AGENTS.md#後端-api-實作慣例) · [慣例更新 PR](https://github.com/HanChiYang/crop-pilot/pull/11)

受保護的 API 直接驗證 CropPilot 簽發的短效 JWT，並使用簽發時的角色。Angular 只在記憶體保存權杖，重新整理時透過續期 Cookie 恢復登入。帳密登入、續期和登出要求防偽權杖。登出後停止續期，已簽發的 JWT 在原定時間到期。[Bearer 驗證方式](design/first-desktop-slice-data-api.md#bearer-驗證方式)

新對話可由專案根目錄的 [AGENTS.md](../AGENTS.md)接續。需求訪談與資料庫及 API 審查可使用 `feature-discovery` 和 `data-api-design-review` Skills。
