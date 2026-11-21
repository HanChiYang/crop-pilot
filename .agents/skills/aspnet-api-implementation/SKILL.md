---
name: aspnet-api-implementation
description: 依已確認的 OpenAPI 契約實作、修改與驗證 ASP.NET Core API。適用於串接生成的 Controller 和 DTO、實作授權與資料存取，以及加入 HTTP 整合測試。純需求討論或契約設計審查由各專案的設計流程處理。
---

# ASP.NET Core API 實作

1. 讀取專案的 `AGENTS.md`、已確認的需求、OpenAPI 契約及相近端點。釐清這次要實作的路由、成功與錯誤回應，以及身分、角色、資源擁有權的要求。涉及尚未確認且會改變對外行為的設計決定時，依專案的設計流程處理。
2. 沿用專案的契約生成流程。需要調整契約時，先更新並驗證規格，再從同一份規格重新生成受影響的後端與 client 檔案。把業務實作放在生成目錄之外，檢查路由、HTTP 狀態、欄位命名及可空值是否符合契約。
3. 依專案的分層實作。沿用既有身分驗證取得呼叫者身分，把角色檢查和資源擁有權分開核對。資料存取依已驗證身分限制範圍。遇到依賴唯一性等資料庫約束的行為，驗證並行操作下仍能產生契約預期的結果。
4. 沿用既有錯誤管線。由框架處理模型繫結及認證授權，將可預期的業務結果轉成契約指定的狀態與錯誤內容，讓未預期的例外交給全域處理。依契約保留前端辨識錯誤所需的欄位。
5. 用專案既有方式建置與驗證。需要整合測試時，從正式 HTTP 路由走實際的驗證與授權管線，檢查對外回應和相關的資料庫狀態。依變更挑選正常流程、錯誤輸入、跨帳號存取或並行衝突。用測試資料庫執行 migrations，遵循專案的提交與 PR 核准規則。

## CropPilot

- 先讀 `AGENTS.md` 的「後端 API 實作慣例」。依專案規則使用 Controller → Service → Repository、生成的請求 DTO、回應 mapper、統一的錯誤訊息和 Bearer 帳號識別。
- 從 `docs/openapi/openapi.yaml` 核對契約。需要生成時，沿用 `backend/tools/Generate-ApiContract.ps1` 和 `backend/CropPilot.Api/README.md` 的指令。涉及資料庫結構時，先依設計核對匯出的 migration SQL，再套用到測試庫。
- 沿用 `backend/CropPilot.Api.Tests` 的 xUnit、Testcontainers 與實際 JWT 驗證。從 repo 根目錄執行 `dotnet test backend/CropPilot.Api.Tests/CropPilot.Api.Tests.csproj`。實作和測試可以分成小 commit，開 PR 前依 `AGENTS.md` 一起推送。
