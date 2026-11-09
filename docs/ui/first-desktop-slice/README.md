# CropPilot｜首個桌機切片 UI 草案 v0.1

2026-11-09 確認田區名稱和面積必填，座標選填，Figma 已同步更新。登入頁已新增「以 Google 帳號登入」，新增流程待審查。

- [Figma 設計稿 v0.1](https://www.figma.com/design/BwXm9ifVg4hZyteraSpuvc/CropPilot%EF%BD%9C%E9%A6%96%E5%80%8B%E6%A1%8C%E6%A9%9F%E5%88%87%E7%89%87-v0.1?node-id=0-1)
- [可點選原型，從首頁開始](https://www.figma.com/proto/BwXm9ifVg4hZyteraSpuvc/CropPilot-%E9%A6%96%E5%80%8B%E6%A1%8C%E6%A9%9F%E5%88%87%E7%89%87-v0.1?node-id=8-217&scaling=min-zoom&content-scaling=fixed&starting-point-node-id=8%3A217)
- [可點選原型，登入失敗與重試](https://www.figma.com/proto/BwXm9ifVg4hZyteraSpuvc/CropPilot-%E9%A6%96%E5%80%8B%E6%A1%8C%E6%A9%9F%E5%88%87%E7%89%87-v0.1?node-id=1-188&scaling=min-zoom&content-scaling=fixed&starting-point-node-id=1%3A188&show-proto-sidebar=1)
- [可點選原型，直接開啟 Google 登入入口](https://www.figma.com/proto/BwXm9ifVg4hZyteraSpuvc/CropPilot%EF%BD%9C%E9%A6%96%E5%80%8B%E6%A1%8C%E6%A9%9F%E5%88%87%E7%89%87-v0.1?node-id=1-2&scaling=min-zoom&content-scaling=fixed&starting-point-node-id=8%3A217&show-proto-sidebar=1)

已在 Figma 播放模式確認首頁的兩個登入入口都能進到登入畫面，登入後進入空的田區總覽。也已走查新增田區、新增栽培與返回總覽、兩個取消按鈕，以及登入失敗後的重試。田區管理顯示北側田的面積和座標，總覽顯示規劃中的番茄。Figma 原型用預填示例模擬流程。

![初版六個桌機畫面](overview.png)

上圖保留審查前的流程快照。最新表單見 [SVG 畫面](frames/03-field-new.svg)。

範圍為從首頁進入登入、建立田區、建立一筆規劃中的栽培，再回總覽確認。規則與驗收條件見[桌機版需求草案](../../requirements/desktop-field-and-planting.md)。

## 畫面與 Figma 連線對照

| 畫面 | 主要操作與目的地 |
| --- | --- |
| [00 首頁](frames/00-landing.svg) | 點右上角「登入」或主區塊「登入 CropPilot」，皆進入 01。 |
| [01 登入](frames/01-login.svg) | 點「登入」或「以 Google 帳號登入」，原型皆進入 02。 |
| [01b 登入失敗](frames/01b-login-error.svg) | 顯示帳密錯誤，可修正帳密重試或改用 Google。原型皆模擬成功進入 02。 |
| [02 空的田區總覽](frames/02-overview-empty.svg) | 點「新增田區」，進入 03。 |
| [03 新增田區](frames/03-field-new.svg) | 點「儲存田區」，進入 04；取消回 02。 |
| [04 田區管理](frames/04-field-saved.svg) | 確認田區、面積與座標已顯示。點「前往作物栽培管理」，進入 05。 |
| [05 新增栽培紀錄](frames/05-planting-new.svg) | 選田區並填作物。預定栽種日可留空。點「儲存並回總覽」，進入 06；取消回 04。 |
| [06 回到田區總覽](frames/06-overview-filled.svg) | 看到「北側田」和「規劃中」的番茄。日期留空時顯示「尚未設定」。 |

SVG 已匯入 Figma Design 並保留圖層，繁體中文文字已改用 Noto Sans TC。Figma 以 00 作為主要流程的起點，01b 作為登入錯誤的起點。專案中的 SVG 是匯入來源，可供對照。

已在 Figma 播放模式試按 01 和 01b 的 Google 按鈕，兩者都進入 02。原型直接模擬登入成功。實作時會整頁跳到 Google，取消時顯示「已取消 Google 登入」，失敗時顯示「無法以 Google 帳號登入，請再試一次，或使用原帳號密碼登入」。[登入畫面截圖](figma-google-login-verified.jpg)

## 田區表單的審查結果

- 已確認田區名稱及面積（平方公尺）必填，座標及備註選填。
- Figma 以十進位的緯度和經度表示單一定位點。資料驗證規則見[資料庫與 API 草案](../../design/first-desktop-slice-data-api.md)。
- 畫面中的田區、座標、作物及帳號皆為示意。
