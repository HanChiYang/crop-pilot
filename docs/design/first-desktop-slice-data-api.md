# CropPilot｜首個桌機切片的 API 設計

範圍為「首頁 → 登入 → 新增田區 → 新增規劃中的栽培 → 回總覽」。欄位和操作流程依照已審查的 Figma 與驗收條件。資料表、欄位、ER 圖與外鍵另見資料庫設計。[資料庫設計](first-desktop-slice-database.md) · [Figma 審查記錄](../ui/first-desktop-slice/README.md) · [驗收條件](../requirements/desktop-field-and-planting.md#驗收切片) · [OpenAPI 契約](../openapi/openapi.yaml)

## API 約定

提案採同源部署及登入 Cookie。Angular 在首次寫入前及登入或登出後取得新的防偽權杖，寫入時在 `X-XSRF-TOKEN` 標頭帶上該權杖。資料 API 依登入身分限制讀寫範圍，建立田區時由伺服器將登入者的主帳號 ID 寫入 `fields.main_account_id`。其他帳號的田區或栽培紀錄視為找不到。

| 操作 | 請求 | 成功結果 |
| --- | --- | --- |
| 取得防偽權杖 | `GET /api/auth/csrf`，可在登入前呼叫 | `204`，設定供 Angular 讀取的 `XSRF-TOKEN` Cookie。 |
| 登入 | `POST /api/auth/login`，傳 `username`、`password` | `204`，設定登入 Cookie。 |
| 確認登入狀態 | `GET /api/auth/session`，由 Angular 進入總覽前呼叫 | 有效工作階段回 `204`，失效時回 `401`。 |
| 開始 Google 登入 | `GET /api/auth/google`，由瀏覽器整頁導覽 | `302`，透過 `Location` 跳轉到 Google。 |
| 登出 | `POST /api/auth/logout` | `204`，清除登入 Cookie。 |
| 田區管理與田區選單 | `GET /api/fields` | `200`，回傳自己的田區陣列。 |
| 新增田區 | `POST /api/fields`，傳名稱、面積及選填欄位 | `201`，回傳已建立的田區，並以 `Location` 指向 `GET /api/fields/{fieldId}`。 |
| 查詢田區 | `GET /api/fields/{fieldId}` | `200`，回傳田區。 |
| 新增規劃中的栽培 | `POST /api/fields/{fieldId}/plantings`，傳 `cropName` 及選填的 `variety`、`plannedPlantingDate`、`notes` | `201`，回傳已建立的栽培紀錄，並以 `Location` 指向該紀錄的查詢端點。 |
| 查詢栽培紀錄 | `GET /api/fields/{fieldId}/plantings/{plantingId}` | `200`，回傳指定田區的栽培紀錄。 |
| 田區總覽 | `GET /api/overview` | `200`，回傳自己的田區陣列，每筆加上 `plantings`。田區沒有目前的栽培紀錄時，`plantings` 為空陣列。 |

田區回應含 `id`、`name`、`areaSquareMeters`、`latitude`、`longitude`、`notes`。栽培紀錄回應含 `id`、`fieldId`、`cropName`、`variety`、`plannedPlantingDate`、`actualPlantingDate`、`status`、`notes`。田區和栽培紀錄請求與回應的 `notes` 分別對應資料庫的 `fields.description` 和 `plantings.description`。選填欄位缺省或為 `null` 時存為 `null`，回應也使用 `null`。總覽可顯示 `planned`、`growing`、`harvesting`，首切片只會新增 `planned`。後續切片處理編輯、刪除與狀態變更。

API 的小寫 `status` 值對應資料庫 `plantings.status` 的大寫值，例如 `planned` 對應 `PLANNED`。

只填必填欄位即可建立田區。以下請求送往 `POST /api/fields`，兩個座標欄位會存為 `null`。

```json
{
  "name": "北側田",
  "areaSquareMeters": 1200
}
```

| 情況 | HTTP 回應 | Angular 畫面 |
| --- | --- | --- |
| 必填欄位缺漏，或欄位、路徑參數格式錯誤 | `400`，回傳 `errors`，以 JSON 欄位或參數名對應訊息陣列。 | 在相應欄位旁顯示提示，保留輸入。 |
| 防偽權杖缺漏或驗證失敗 | `400`，回傳問題說明與空的 `errors`。 | 顯示請求失敗，保留輸入。 |
| 登入資料錯誤 | `401`，通用登入失敗訊息。 | 顯示帳號或密碼錯誤，可重試。 |
| 未登入或工作階段已失效 | `401`。 | 導回登入。 |
| 田區或栽培紀錄不存在，或屬於其他帳號 | `404`。 | 提示找不到資料，提供返回列表的入口。 |
| 伺服器儲存失敗 | `500`，通用錯誤訊息。 | 留在表單並保留輸入，讓使用者重試。 |

### 本地登入

本地建帳和登入時，後端都先去除 `username` 的頭尾空白，並拒絕去除後的空字串。建帳時保留輸入的大小寫，並忽略大小寫檢查帳號是否已使用。若已有 `Farmer01`，便會拒絕建立 `farmer01`。

`POST /api/auth/login` 以區分大小寫的方式查找 `local_credentials.username`。若儲存的是 `Farmer01`，輸入 ` Farmer01 ` 也能找到帳號，輸入 `farmer01` 則會收到通用的登入失敗回應。找到帳密後由 Identity 驗證密碼，再透過 `main_account_id` 取得主帳號。

新建本地帳號時先設為 `PENDING`，通過 OTP 後轉為 `ACTIVE`。OTP 暫定以 Email 寄送，發送與驗證端點待建帳流程設計時定義。本地與 Google 登入都要求 `main_account.status` 為 `ACTIVE`，才能以主帳號 ID 建立 CropPilot 工作階段。

### 角色授權

後端以 `main_account_id` 讀取 `user_roles`，將角色加到登入身分供 API 授權使用。本地建帳和 Google 首次自動建帳都由後端在建帳交易中指派 `USER`。

`USER` 可使用農務功能，田區與栽培 API 仍須檢查登入者是否擁有該筆資料。`ADMIN` 用於後臺管理，角色指派與管理操作待後臺功能需求審查時定義。

登入需要的身分 Claim 由後端從帳號、角色及個人資料產生。CropPilot 以 Cookie 維持登入，Google 權杖在登入過程中驗證後即用完。

### Google 登入

Google 提供 OpenID Connect（OIDC）身分驗證，建立在 OAuth 2.0 上。OAuth 2.1 仍是 IETF 草案。CropPilot 使用 ASP.NET Core 的官方 `AddOpenIdConnect` 接 Google，以授權碼流程搭配 PKCE。CropPilot 是 OIDC 用戶端，Google 提供授權端點與換發權杖的端點。[Google OpenID Connect](https://developers.google.com/identity/openid-connect/openid-connect) · [ASP.NET Core OIDC 設定](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-oidc-web-authentication?view=aspnetcore-10.0) · [OAuth 2.1 草案](https://datatracker.ietf.org/doc/draft-ietf-oauth-v2-1/)

1. Angular 的「以 Google 帳號登入」以連結整頁導覽到 `GET /api/auth/google`。CropPilot 的入口呼叫 `Challenge`，中介軟體回傳 Google 授權網址。帳密登入由 `POST /api/auth/login` 呼叫 Identity 的帳密驗證。
2. Google 導回註冊的 `/signin-google`，此路徑由 OIDC 中介軟體接收。中介軟體驗證 `state`、換取並驗證 ID token（含 `nonce`），再交由 CropPilot 完成帳號查找與工作階段建立。設定採 `response_type=code`、`response_mode=query` 及 `openid email` 範圍。`/signin-google` 的回呼路徑需轉送到 ASP.NET Core，並以各環境完整 HTTPS 網址註冊於 Google。用戶端密鑰放在伺服器端設定。[Google 回呼與 ID token 驗證](https://developers.google.com/identity/openid-connect/openid-connect)
3. 身分對應成功後，後端設定 `CropPilot.Session` Cookie，將瀏覽器導向 `/dashboard`。Angular 照常用 Cookie 呼叫 API，首次寫入前取得新的防偽權杖。Google 權杖留在伺服器端處理。取消時導回 `/login?error=google_cancelled`，其他失敗導回 `/login?error=google_sign_in_failed`，由 Angular 顯示提示並讓使用者重試。

OIDC 中介軟體驗證 ID token 時會檢查發行者 `iss`。驗證通過後，後端以 `identity_provider = GOOGLE` 和 `provider_user_id = sub` 查找 `federated_identities`。找到綁定就透過 `main_account_id` 取得原帳號。首次遇到尚未綁定的 Google 身分時，後端將已驗證的 Email 去除頭尾空白並轉成小寫，再與 `user_profiles.email` 比對。[Google 的身分識別欄位](https://developers.google.com/identity/openid-connect/reference#id-tokens)

| Email 比對結果 | 處理方式 |
| --- | --- |
| 尚未使用 | 在同一筆交易建立 `main_account`、`user_profiles`、`federated_identities` 和 `USER` 角色指派。個人資料使用獨立的 `id` 並透過 `main_account_id` 關聯主帳號。主帳號直接設為 `ACTIVE`，個人資料保存已驗證的小寫 Email。 |
| 已有個人資料 | 以 `user_profiles.main_account_id` 取得主帳號 ID，回查 `local_credentials` 與 `federated_identities`。有本地帳密就提示用原帳密登入，有外部身分就提示使用原先綁定的身分提供者，兩者皆有時可用任一既有方式。 |

Email 相符時，後端導回登入頁顯示提示。既有本地帳號若為 `PENDING`，則提示先完成原帳號的 OTP 驗證。帳號綁定需要另行驗證既有帳號，列入後續帳號連結流程。

`MapIdentityApi` 可提供內建的帳密 `/login` 及其他帳號管理路由。CropPilot 的 `POST /api/auth/login` 採本專案契約的欄位與回應，由 Identity 驗證帳密。Google 的瀏覽器入口寫入 OpenAPI，供雙方對照，Angular 用整頁導覽啟動。Google 回呼由中介軟體接收，因此實作文件另列 `/signin-google`。[ASP.NET Core Identity 內建 API](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-api-authorization?view=aspnetcore-10.0)

## OpenAPI 契約與程式碼生成

已確認採 contract-first。Stoplight 匯出的原檔沿用 OpenAPI 3.1.0，已填入首個切片的操作、請求及回應模型、Cookie 認證、防偽權杖與錯誤格式。每個操作有獨立的 `operationId`。JSON API 的錯誤回應使用 `application/problem+json`，Google 登入入口則使用瀏覽器跳轉。[openapi.yaml](../openapi/openapi.yaml)

使用 OpenAPI Generator 7.27.0 驗證規格並試產兩端程式碼。C# 使用 `aspnetcore` 生成抽象 Controller 與 DTO，Angular 使用 `typescript-angular` 生成服務與型別。已檢查新增田區的名稱、面積及可為 `null` 的座標型別。實作時再以 `POST /api/fields` 驗證兩端編譯、HTTP 回應及重複生成。OpenAPI Generator 將 3.1 列為 beta。[C# generator](https://openapi-generator.tech/docs/generators/aspnetcore/) · [Angular generator](https://openapi-generator.tech/docs/generators/typescript-angular/) · [OpenAPI 版本相容性](https://github.com/OpenAPITools/openapi-generator#1---installation)

串接時將 Angular client 的 base path 設為空字串，讓請求走同源的 `/api/...`，並由 Angular 傳送防偽標頭。C# 端接上登入與防偽驗證，將錯誤轉為契約中的狀態碼與格式。新增田區另外驗證面積嚴格大於零、小數位數及座標是否成對。

## 第一輪驗收重點

1. 只傳名稱和正數面積可新增田區，重新查詢仍取得相同資料，座標為 `null`。缺名稱、缺面積或面積小於等於零時，應拒絕請求且沒有新增資料。
2. 緯度與經度都留空可儲存，填齊合法值也可儲存。只填其一或超出範圍時，應回傳欄位錯誤。
3. 使用自己新增的田區建立一筆栽培紀錄，預定栽種日留空。回到總覽可查到該紀錄，狀態顯示「規劃中」，日期顯示「尚未設定」。
4. 使用另一個測試帳號，確認無法查詢他人的田區，也無法在他人田區新增栽培紀錄。登出後查詢總覽應回傳 `401`。
5. Google 登入成功後導回總覽，同一個 Google 帳號再次登入會取得原本的田區。取消登入或回呼驗證失敗時回到登入頁，顯示提示且可重試。驗收同一個 Google 帳號只會建立一個 CropPilot 帳號，且 Email 已有個人資料時會提示使用既有的登入方式。
