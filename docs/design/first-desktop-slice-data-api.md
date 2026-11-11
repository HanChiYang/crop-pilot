# CropPilot｜首個桌機切片的 API 設計

範圍為「首頁 → 登入 → 新增田區 → 田區管理 → 新增栽培紀錄 → 回總覽」。欄位和操作流程依照已審查的 Figma 與驗收條件。資料表、欄位、ER 圖與外鍵另見資料庫設計。[資料庫設計](first-desktop-slice-database.md) · [Figma 審查記錄](../ui/first-desktop-slice/README.md) · [驗收條件](../requirements/desktop-field-and-planting.md#驗收切片) · [OpenAPI 契約](../openapi/openapi.yaml)

## API 約定

採同源部署。資料 API 依登入身分限制讀寫範圍，建立田區時由伺服器將登入者的主帳號 ID 寫入 `fields.main_account_id`。其他帳號的田區或栽培紀錄視為找不到。

### Bearer 驗證方式

OpenAPI 全域預設為 `BearerAuth`。田區、栽培、總覽與工作階段查詢使用 `Authorization: Bearer <access_token>`。續期與登出要求 `RenewalCookie` 和 `CsrfToken`。帳密登入也要求 `CsrfToken`。Angular 在這三項操作前先取得防偽權杖，再於 `X-XSRF-TOKEN` 標頭帶上權杖。取得防偽權杖及開始 Google 登入皆可匿名呼叫。

帳密由 ASP.NET Core Identity 驗證，成功後 CropPilot 回傳已簽章的 JWT Access Token 並設定 `CropPilot.Renewal` Cookie。JWT 的 `sub` 是 `main_account.id`，`role` 是簽發當下的角色陣列，`iat` 與 `exp` 分別是簽發及到期時間。Bearer 驗證在簽章、`iss`、`aud` 與效期通過後，要求 `sub` 為帶連字號的非零 UUID，並將解析出的主帳號 ID 提供給端點。`sub` 缺漏或不符格式時，受保護的端點回傳 `401`。權杖效期最多五分鐘，並受工作階段到期時間限制。

Angular 只在記憶體保存權杖，重新整理後用續期 Cookie 取得新權杖。當資料 API 回傳 Bearer 驗證失敗的 `401`，Angular 可續期並重試一次，續期回應中的 `sessionId` 須符合原工作階段才會重試原請求。

續期 Cookie 採 `HttpOnly`、`SameSite=Lax`，HTTPS 環境設定 `Secure`，路徑限 `/api/v1/auth`。Cookie 隨瀏覽器工作階段保存，後端期限為登入後八小時，續期不延長期限。續期時會檢查工作階段、帳號狀態與 Identity 安全戳記，並以最新角色簽發 JWT。登出撤銷資料庫工作階段並清除續期 Cookie。已簽發的 JWT 在原定時間到期，期間仍保有簽發時的角色與帳號授權。重新登入會撤銷登入請求所帶續期 Cookie 對應的工作階段，Google 登入以啟動時收到的 Cookie 為準。若另一個分頁已切換登入，舊分頁的登出請求會收到 `409`，新工作階段保持有效。[工作階段資料表](first-desktop-slice-database.md#auth_sessions)

### 端點與回應

| 操作 | 請求 | 成功結果 |
| --- | --- | --- |
| 取得防偽權杖 | `GET /api/v1/auth/csrf`，可在登入前呼叫 | `204`，設定供 Angular 讀取的 `XSRF-TOKEN` Cookie。 |
| 登入 | `POST /api/v1/auth/login`，傳 `LoginRqDto` 的 `username`、`password` | `200`，回傳 `AccessTokenRsDto` 並設定續期 Cookie。 |
| 確認登入狀態 | `GET /api/v1/auth/session`，由 Angular 進入總覽前呼叫 | 有效權杖回 `204`，失效時回 `401`。 |
| 開始 Google 登入 | `GET /api/v1/auth/google`，由瀏覽器整頁導覽 | `302`，透過 `Location` 跳轉到 Google。 |
| 續期 | `POST /api/v1/auth/refresh` | `200`，回傳新的 `AccessTokenRsDto`。 |
| 登出 | `POST /api/v1/auth/logout`，傳 `LogoutRqDto` 的 `sessionId` | `204`，撤銷工作階段並清除續期 Cookie。 |
| 田區管理與田區選單 | `GET /api/v1/fields` | `200`，回傳 `FieldRsDto`，其中 `fields` 是自己的田區陣列。 |
| 新增田區 | `POST /api/v1/fields`，傳 `CreateFieldRqDto` | `201`，回傳 `FieldRsDto`，其中 `fields` 只含新田區。`Location` 指向 `GET /api/v1/fields/{fieldId}`。 |
| 查詢田區 | `GET /api/v1/fields/{fieldId}` | `200`，回傳單筆 `FieldDto`。 |
| 編輯田區 | `PUT /api/v1/fields/{fieldId}`，傳 `EditFieldRqDto` | `200`，回傳 `FieldRsDto`，其中 `fields` 只含更新後的田區。 |
| 刪除單筆田區 | `DELETE /api/v1/fields/{fieldId}`，由路徑指定田區 | `204`，請求與成功回應皆無 body。 |
| 批次刪除田區 | `DELETE /api/v1/fields/bulk-delete`，傳 `DeleteFieldRqDto` 的 `fieldIds` | `204`，成功回應無 body。 |
| 新增栽培紀錄 | `POST /api/v1/fields/{fieldId}/plantings`，傳 `CreatePlantingRqDto` | `201`，回傳 `PlantingRsDto`，並以 `Location` 指向該紀錄的查詢端點。 |
| 查詢田區的栽培列表 | `GET /api/v1/fields/{fieldId}/plantings` | `200`，回傳 `PlantingRsDto` 陣列。自己的田區沒有目前的紀錄時回傳 `[]`。 |
| 查詢栽培紀錄 | `GET /api/v1/fields/{fieldId}/plantings/{plantingId}` | `200`，回傳 `PlantingRsDto`。 |
| 田區總覽 | `GET /api/v1/overview` | `200`，回傳自己的 `FieldOverviewRsDto` 陣列，每筆加上 `plantings`。田區沒有目前的栽培紀錄時，`plantings` 為空陣列。 |

### 新增時選擇栽培狀態

新增請求可傳 `status`，省略時預設為 `planned`。允許值為 `planned`、`growing`、`harvesting`。`growing` 要求 `actualPlantingDate`，缺省或傳 `null` 時回 `400`。補登 `harvesting` 時，`actualPlantingDate` 可省略或傳 `null`，成功回 `201`，回應含 `status: harvesting` 和 `actualPlantingDate: null`。管理列表與總覽均可查到該筆採收中的紀錄。

驗收時，以自己的田區補登一筆採收中紀錄，實際栽種日留空。重新查詢管理列表與總覽，確認狀態為「採收中」，預定與實際日期均無值時顯示「尚未設定」。

### Schema 命名

請求物件以 `RqDto` 結尾，回應物件以 `RsDto` 結尾，錯誤回應的 body 也採同一規則。可重用的資料項目使用 `Dto`，例如單筆田區使用 `FieldDto`，田區集合由 `FieldRsDto` 包裝。列舉以其代表的值命名，例如 `PlantingStatus`。

### 端點標題與田區 Operation ID

端點標題使用「清單」表示多筆查詢，使用「詳細資料」表示單筆查詢。資料寫入的標題以「單筆」或「批次」標示操作範圍。

OpenAPI 的 `operationId` 使用 lower camel case，由動詞加上資源名稱組成。單筆使用 `Field`，多筆使用 `Fields`。

| 操作 | 單筆 | 多筆 |
| --- | --- | --- |
| 查詢 | `getField` | `getFields` |
| 新增 | `createField` | `createFields` |
| 編輯 | `editField` | `editFields` |
| 刪除 | `deleteField` | `deleteFields` |

`createFields` 和 `editFields` 保留給未來的批次新增與編輯端點。

### 田區與栽培資料

田區回應含 `id`、`name`、`areaSquareMeters`、`latitude`、`longitude`、`notes`。栽培紀錄回應含 `id`、`fieldId`、`cropName`、`variety`、`plannedPlantingDate`、`actualPlantingDate`、`status`、`notes`。田區和栽培紀錄請求與回應的 `notes` 分別對應資料庫的 `fields.description` 和 `plantings.description`。選填欄位缺省或為 `null` 時存為 `null`，回應也使用 `null`。新增、管理列表與總覽涵蓋 `planned`、`growing` 和 `harvesting`。後續切片處理編輯、刪除與狀態變更。

編輯以完整表單更新，省略座標或備註會清除原值。同名檢查排除正在編輯的田區。批次刪除須提供至少一個且互不重複的田區 ID。若任一田區不存在或屬於其他帳號，整批回傳 `404`。有田區含栽培紀錄時回傳 `409`，整批保留。單筆刪除也適用相同的擁有權與栽培紀錄限制。

田區、栽培及總覽端點皆要求 `USER` 角色。刪除由資料庫交易檢查整批擁有權並執行，栽培衝突由 `plantings.field_id` 的限制刪除外鍵保護。只要田區有栽培紀錄，即使全部已結束，單筆與批次刪除均回傳 `409`，並保留原資料。

API 的小寫 `status` 值對應資料庫 `plantings.status` 的大寫值，例如 `planned` 對應 `PLANNED`。

#### 田區栽培列表

管理頁在選取或切換田區時呼叫列表端點。成功新增後回到管理頁，沿用原田區選擇並重新讀取列表，顯示已儲存的紀錄。取消新增也回到原田區的列表。

列表沿用總覽的目前栽培範圍，包含 `planned`、`growing` 與 `harvesting`。`completed` 保留供後續歷史列表使用。端點使用 Bearer 驗證並要求 `USER` 角色。無效的田區 ID 格式回 `400`，未登入回 `401`，缺少角色回 `403`，田區不存在或屬於其他帳號回 `404`。

列表驗收涵蓋自己的空田區回傳 `[]`、新增後查得到同一筆紀錄、其他田區的紀錄不混入、已結束的紀錄從目前列表收起，以及其他帳號查詢回傳 `404`。

只填必填欄位即可建立田區。以下請求送往 `POST /api/v1/fields`，兩個座標欄位會存為 `null`。

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
| 登入帳號缺少田區功能所需的 `USER` 角色 | `403`。 | 提示沒有使用權限。 |
| 田區或栽培紀錄不存在，或屬於其他帳號 | `404`。 | 提示找不到資料，提供返回列表的入口。 |
| 同帳號已有同名田區 | `409`，以 `errors.name` 回傳欄位錯誤。 | 在名稱欄位旁顯示「此名稱已使用」，保留輸入。 |
| 待刪除田區已有栽培紀錄 | `409`，回傳問題說明。 | 顯示無法刪除的原因。 |
| 伺服器儲存失敗 | `500`，通用錯誤訊息。 | 留在表單並保留輸入，讓使用者重試。 |

建立田區前，API 去除名稱頭尾空白，並保留大小寫。若寫入違反 `(main_account_id, name)` 唯一索引，即使兩筆請求同時送出，也應回傳 `409` 和 `errors.name`。

### 本地登入

本地建帳和登入時，後端都先去除 `username` 的頭尾空白，並拒絕去除後的空字串。建帳時保留輸入的大小寫，並忽略大小寫檢查帳號是否已使用。若已有 `Farmer01`，便會拒絕建立 `farmer01`。

`POST /api/v1/auth/login` 以區分大小寫的方式查找 `local_credentials.username`。若儲存的是 `Farmer01`，輸入 ` Farmer01 ` 也能找到帳號，輸入 `farmer01` 則會收到通用的登入失敗回應。找到帳密後由 Identity 驗證密碼，再透過 `main_account_id` 取得主帳號。

新建本地帳號時先設為 `PENDING`，通過 OTP 後轉為 `ACTIVE`。OTP 暫定以 Email 寄送，發送與驗證端點待建帳流程設計時定義。本地與 Google 登入都要求 `main_account.status` 為 `ACTIVE`，才能以主帳號 ID 建立 CropPilot 工作階段。

### 角色授權

後端以 `main_account_id` 讀取 `user_roles`，將角色加到登入身分供 API 授權使用。本地建帳和 Google 首次自動建帳都由後端在建帳交易中指派 `USER`。

`USER` 可使用農務功能，田區與栽培 API 仍須檢查登入者是否擁有該筆資料。`ADMIN` 用於後臺管理，角色指派與管理操作待後臺功能需求審查時定義。

API 使用的身分 Claim 由後端依目前的主帳號與角色產生。Google 權杖用於登入時的身分驗證。

### Google 登入

Google 提供 OpenID Connect（OIDC）身分驗證，建立在 OAuth 2.0 上。OAuth 2.1 仍是 IETF 草案。CropPilot 使用 ASP.NET Core 的官方 `AddOpenIdConnect` 接 Google，以授權碼流程搭配 PKCE。CropPilot 是 OIDC 用戶端，Google 提供授權端點與換發權杖的端點。[Google OpenID Connect](https://developers.google.com/identity/openid-connect/openid-connect) · [ASP.NET Core OIDC 設定](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-oidc-web-authentication?view=aspnetcore-10.0) · [OAuth 2.1 草案](https://datatracker.ietf.org/doc/draft-ietf-oauth-v2-1/)

1. Angular 的「以 Google 帳號登入」以連結整頁導覽到 `GET /api/v1/auth/google`。CropPilot 的入口呼叫 `Challenge`，中介軟體回傳 Google 授權網址。帳密登入由 `POST /api/v1/auth/login` 呼叫 Identity 的帳密驗證。
2. Google 導回註冊的 `/signin-google`，此路徑由 OIDC 中介軟體接收。中介軟體驗證 `state`、換取並驗證 ID token（含 `nonce`），再交由 CropPilot 完成帳號查找與工作階段建立。設定採 `response_type=code`、`response_mode=query` 及 `openid email` 範圍。`/signin-google` 的回呼路徑需轉送到 ASP.NET Core，並以各環境完整 HTTPS 網址註冊於 Google。用戶端密鑰放在伺服器端設定。[Google 回呼與 ID token 驗證](https://developers.google.com/identity/openid-connect/openid-connect)
3. 身分對應成功後，後端設定 `CropPilot.Renewal` Cookie，將瀏覽器導向 `/dashboard`。Angular 透過 CropPilot 的續期端點取得 Access Token。Google 權杖留在伺服器端處理。取消時導回 `/login?error=google_cancelled`，其他失敗導回 `/login?error=google_sign_in_failed`，由 Angular 顯示提示並讓使用者重試。

OIDC 中介軟體驗證 ID token 時會檢查發行者 `iss`。驗證通過後，後端以 `identity_provider = GOOGLE` 和 `provider_user_id = sub` 查找 `federated_identities`。找到綁定就透過 `main_account_id` 取得原帳號。首次遇到尚未綁定的 Google 身分時，後端將已驗證的 Email 去除頭尾空白並轉成小寫，再與 `user_profiles.email` 比對。[Google 的身分識別欄位](https://developers.google.com/identity/openid-connect/reference#id-tokens)

| Email 比對結果 | 處理方式 |
| --- | --- |
| 尚未使用 | 在同一筆交易建立 `main_account`、`user_profiles`、`federated_identities` 和 `USER` 角色指派。個人資料使用獨立的 `id` 並透過 `main_account_id` 關聯主帳號。主帳號直接設為 `ACTIVE`，個人資料保存已驗證的小寫 Email。 |
| 已有個人資料 | 以 `user_profiles.main_account_id` 取得主帳號 ID，回查 `local_credentials` 與 `federated_identities`。有本地帳密就提示用原帳密登入，有外部身分就提示使用原先綁定的身分提供者，兩者皆有時可用任一既有方式。 |

Email 相符時，後端導回登入頁顯示提示。既有本地帳號若為 `PENDING`，則提示先完成原帳號的 OTP 驗證。帳號綁定需要另行驗證既有帳號，列入後續帳號連結流程。

`MapIdentityApi` 可提供內建的帳密 `/login` 及其他帳號管理路由。CropPilot 的 `POST /api/v1/auth/login` 採本專案契約的欄位與回應，由 Identity 驗證帳密。Google 的瀏覽器入口寫入 OpenAPI，供雙方對照，Angular 用整頁導覽啟動。Google 回呼由中介軟體接收，因此實作文件另列 `/signin-google`。[ASP.NET Core Identity 內建 API](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-api-authorization?view=aspnetcore-10.0)

## OpenAPI 契約與程式碼生成

已確認採 contract-first。Stoplight 匯出的原檔沿用 OpenAPI 3.1.0，已填入首個切片的操作、請求及回應模型、Bearer 驗證、續期 Cookie、防偽權杖與錯誤格式。每個操作有獨立的 `operationId`。JSON API 的錯誤回應使用 `application/problem+json`，Google 登入入口則使用瀏覽器跳轉。[openapi.yaml](../openapi/openapi.yaml)

使用 OpenAPI Generator 7.27.0 驗證規格並試產兩端程式碼。C# 使用 `aspnetcore` 生成抽象 Controller 與 DTO，Angular 使用 `typescript-angular` 生成服務與型別。已檢查新增田區的名稱、面積及可為 `null` 的座標型別。實作時再以 `POST /api/v1/fields` 驗證兩端編譯、HTTP 回應及重複生成。OpenAPI Generator 將 3.1 列為 beta。[C# generator](https://openapi-generator.tech/docs/generators/aspnetcore/) · [Angular generator](https://openapi-generator.tech/docs/generators/typescript-angular/) · [OpenAPI 版本相容性](https://github.com/OpenAPITools/openapi-generator#1---installation)

串接時將 Angular client 的 base path 設為空字串，讓請求走同源的 `/api/v1/...`，並由 Angular 傳送防偽標頭。C# 端接上登入與防偽驗證，將錯誤轉為契約中的狀態碼與格式。新增田區另外驗證面積嚴格大於零、小數位數及座標是否成對。

## 第一輪驗收重點

1. 只傳名稱和正數面積可新增田區。收到 `201` 後進入田區管理，列表透過 `GET /api/v1/fields` 讀出新田區，重新整理仍取得相同資料，座標為 `null`。缺名稱、缺面積或面積小於等於零時，應拒絕請求且沒有新增資料。
   同帳號重送相同名稱或在名稱頭尾加空白，應回傳 `409` 和 `errors.name`。同帳號同時送出兩筆同名請求時，只建立一筆。不同帳號可使用相同名稱，同帳號也可使用僅大小寫不同的名稱。
2. 緯度與經度都留空可儲存，填齊合法值也可儲存。只填其一或超出範圍時，應回傳欄位錯誤。
3. 使用自己新增的田區建立一筆栽培紀錄，預定栽種日留空。回到總覽可查到該紀錄，狀態顯示「規劃中」，日期顯示「尚未設定」。
4. 使用另一個測試帳號，確認無法查詢他人的田區，也無法在他人田區新增栽培紀錄。登出後查詢總覽應回傳 `401`。
5. Google 登入成功後導回總覽，同一個 Google 帳號再次登入會取得原本的田區。取消登入或回呼驗證失敗時回到登入頁，顯示提示且可重試。驗收同一個 Google 帳號只會建立一個 CropPilot 帳號，且 Email 已有個人資料時會提示使用既有的登入方式。
