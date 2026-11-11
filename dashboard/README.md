# CropPilot Dashboard

這個 Angular 專案提供首頁、帳密與 Google 登入，以及登入後的田區總覽、田區管理列表與新增表單。進入田區頁面前會向後端確認登入工作階段。

先啟動 ASP.NET Core 後端於 `http://localhost:7081`，再在 `dashboard/` 執行以下指令。瀏覽器開啟 `http://localhost:4200`。[後端操作說明](../backend/CropPilot.Api/README.md)

```powershell
npm ci
npm start
```

本機開發伺服器使用 HTTP，並將 `/api` 和 `/signin-google` 轉送到後端。本機 Google OAuth 用戶端需註冊 `http://localhost:4200/signin-google`。部署時使用 HTTPS，並註冊部署網域的 HTTPS 回呼網址。

用 `npm run build` 建置前端。登入與田區 client 由 OpenAPI 契約生成，重新生成時使用後端工具目錄的 `Generate-ApiContract.ps1`。[共用 OpenAPI 契約](../docs/openapi/openapi.yaml) · [桌機設計與 Figma 原型](../docs/ui/first-desktop-slice/README.md)

## 執行田區瀏覽器測試

依後端操作說明啟動 API，使用獨立的測試資料庫及已啟用的 `USER` 帳號。測試會登入並在該資料庫新增田區。從 `dashboard/` 設定帳號、密碼後執行下列指令。Playwright 會在需要時啟動 Angular 開發伺服器。

```powershell
npx playwright install chromium
$env:CROPPILOT_E2E_USERNAME = '<測試帳號>'
$env:CROPPILOT_E2E_PASSWORD = '<測試密碼>'
npm run test:e2e
```

若本機已安裝 Edge，可設定 `$env:CROPPILOT_E2E_BROWSER = 'msedge'` 後執行測試。
