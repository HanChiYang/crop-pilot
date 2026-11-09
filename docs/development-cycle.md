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

## 需求訪談方式

1. 先問「第一版最想幫誰完成哪一件農務？」請專案負責人描述實際情境，也可先看參考產品來找候選情境。
2. 一次問一個會影響方向的問題，依回答追問。優先理解目前的做法、最卡的一步，以及完成後應能看見什麼結果。需要取捨時，再提出少量選項供比較。
3. 將使用情境、首版範圍、驗收條件及待釐清事項整理成短需求，由專案負責人確認後進入 UI／UX。已確認事項、原型假設與參考產品的做法分開記錄。

## 如何參考「農務 e 把抓」

使用[官方功能介紹](https://agrepm.moa.gov.tw/Front)和[官方問答](https://agrepm.moa.gov.tw/Front/Home/QA)理解耕地、栽培、作業排程和田間紀錄如何串在一起。選定需求後，比較相關操作的入口、所需的前置資料和作業結果，再由專案負責人決定 CropPilot 要支援的範圍。參考產品的功能可以列為訪談選項。

## 目前進度

登入資料模型回到設計審查。主帳號採 `main_account`，外部身分與本地帳密分別採 `federated_identities`、`local_credentials`。兩張登入表和 `user_profiles` 都各自以 `id` 為主鍵，並透過 `main_account_id` 關聯主帳號。`user_profiles` 保存小寫 Email 與名稱資料。本地帳號通過 Email OTP 後，主帳號從 `PENDING` 轉為 `ACTIVE`。未綁定的 Google 身分遇到相同 Email 時，依主帳號回查並提示既有的登入方式。待設計審查後再接續 migration、後端調整與驗證。[資料表與 ER 圖](design/first-desktop-slice-database.md)

角色設計採 `user_roles`。一般農民使用 `USER`，後臺管理者使用 `ADMIN`。現行 Identity 輔助表的移除會與儲存層調整一併實作。[角色授權設計](design/first-desktop-slice-data-api.md#角色授權)

首個驗收切片涵蓋「首頁 → 登入 → 新增田區 → 新增規劃中的栽培 → 回總覽」。田區名稱和面積必填，座標選填。[桌機畫面及 Figma 原型](ui/first-desktop-slice/README.md)已加入「以 Google 帳號登入」。登入部分已確認使用官方套件，首次用 Google 登入時自動建立 CropPilot 帳號，並進入後端實作。[桌機版需求](requirements/desktop-field-and-planting.md)

首個桌機切片的 API 契約已通過 OpenAPI Generator 7.27.0 驗證，也已在暫存目錄試產 C# 抽象 Controller、DTO 和 Angular client，並核對新增田區的必填欄位及可為 `null` 的座標。PostgreSQL 欄位、ERD、外鍵與刪除行為仍在第 3 階段審查。[OpenAPI 契約](openapi/openapi.yaml)

登入後端的本機設定和 migration 指令見[登入後端操作說明](../backend/CropPilot.Api/README.md)。田區與栽培的資料庫設計仍待審查。進入田區實作後，用 `POST /api/fields` 驗證兩端編譯、HTTP 回應和重新生成，再擴及其他端點。[資料庫設計](design/first-desktop-slice-database.md) · [API 設計](design/first-desktop-slice-data-api.md)

登入後端已通過編譯及 HTTP 測試。測試以暫存 SQLite 和模擬 Google OIDC 驗證帳密登入、防偽權杖、登出、首登建帳、重複登入和失敗處理。初始 Identity migration 已匯出 PostgreSQL SQL，核對了命名、外鍵和唯一索引。實際 Google 帳號與 PostgreSQL 的驗收在設定開發環境後進行。

新對話可由專案根目錄的 [AGENTS.md](../AGENTS.md)接續。先用問答跑一輪，待問法與需求文件格式穩定後，再評估是否整理成可重複使用的 Codex Skill。
