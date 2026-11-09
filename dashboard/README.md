# CropPilot Dashboard

這個 Angular 專案提供首頁、帳密與 Google 登入，以及登入後的田區總覽空畫面。總覽在載入前會向後端確認登入工作階段。

先啟動 ASP.NET Core 後端於 `https://localhost:7081`，再在 `dashboard/` 執行以下指令。瀏覽器開啟 `https://localhost:4200`。[後端操作說明](../backend/CropPilot.Api/README.md)

```powershell
npm ci
npm start
```

開發伺服器使用 HTTPS，並將 `/api` 和 `/signin-google` 轉送到後端。Google OAuth 用戶端需註冊 `https://localhost:4200/signin-google`。首次使用本機憑證時，依瀏覽器提示確認開發伺服器的憑證。

用 `npm run build` 建置前端。登入 client 由 OpenAPI 契約生成，重新生成時使用後端工具目錄的 `Generate-AuthContract.ps1`。[共用 OpenAPI 契約](../docs/openapi/openapi.yaml) · [桌機設計與 Figma 原型](../docs/ui/first-desktop-slice/README.md)
