# CropPilot｜第一輪原型規劃

## UX 草案審查記錄

**2026-11-11 · 肥料庫存、灌溉與農藥管理 · 待審查**

作物栽培管理的列操作選單包含施肥、灌溉與用藥紀錄。肥料新增資材有獨立播放入口。施肥清單可查看多批庫存，編輯示範將已用的 3 公斤改為 2 公斤、退回 1 公斤，並可試走超過批次可用量的提示。[三類作業原型](https://www.figma.com/proto/BwXm9ifVg4hZyteraSpuvc/CropPilot?node-id=182-141&starting-point-node-id=182%3A141&scaling=scale-down) · [新增肥料資材](https://www.figma.com/proto/BwXm9ifVg4hZyteraSpuvc/CropPilot?node-id=182-306&starting-point-node-id=182%3A306&scaling=scale-down)

已走查灌溉新增和編輯。草案欄位包含實際灌溉日期、灌溉方式、選填用水量及備註。農藥管理以示例資材走查用藥新增、編輯、庫存不足後修正、查批次餘量及登記進貨。灌溉與農藥屬後續功能的 UX 草案。原型使用預填範例，灌溉與用藥的歷史補登、農藥主檔建立及期初庫存登記仍待接線。

**2026-11-11 · 肥料資材庫存與施肥扣量 · 待審查**

新增「資材庫存｜肥料」桌機頁面。庫存清單顯示資材的總剩餘量、各批登記量與剩餘量，以及已知單價。已在播放模式走查登記進貨 10 公斤與每公斤 NT$20，以及登記期初庫存 8 公斤、選該批施肥 3 公斤、回查剩 5 公斤。期初庫存範例省略購買日期與單價，施肥紀錄顯示成本待補。[庫存可點選原型](https://www.figma.com/proto/BwXm9ifVg4hZyteraSpuvc/CropPilot?node-id=174-27&starting-point-node-id=174%3A27&scaling=scale-down)

此稿以已有「有機肥」資材為起點。新增資材、同時有多批庫存、編輯施肥及庫存不足的互動分支待補。施肥表單使用預填範例示範選批次與扣量。

**2026-11-11 · 施肥新增與編輯 · 待審查**

從北側田番茄的施肥清單走查兩條路徑。新增從一項肥料開始，可點「新增肥料」查看兩項明細，再儲存回清單。編輯從第一筆作業帶入實際施肥日期、肥料、總量和單位，儲存後示範更新用量。原型使用預填範例模擬輸入。[可點選原型](https://www.figma.com/proto/BwXm9ifVg4hZyteraSpuvc/CropPilot?node-id=152-124&starting-point-node-id=152%3A124&scaling=scale-down)

**2026-11-11 · 作物栽培管理的施肥入口 · 待審查**

桌機原型以北側田的番茄示範從清單的「⋯」展開操作選單，選「施肥紀錄」進入該筆栽培的施肥列表，再返回清單。已在播放模式走查。此版以整張畫框接收點擊，示範上述路徑。[可點選原型](https://www.figma.com/proto/BwXm9ifVg4hZyteraSpuvc/CropPilot?node-id=152-124&starting-point-node-id=152%3A124&scaling=scale-down)

**2026-11-11 · 施肥用量驗證與歷史補登 · 待審查**

用量為零時，儲存會顯示欄位錯誤並保留日期與兩項明細。點選錯誤欄位可試走修正，再儲存回清單。[用量驗證原型](https://www.figma.com/proto/BwXm9ifVg4hZyteraSpuvc/CropPilot?node-id=168-191&starting-point-node-id=168%3A191&scaling=scale-down)

從栽培歷史的番茄批次進入詳情，補登 10 月 5 日施用的有機肥。儲存後回到清單，批次仍顯示「已結束」。[歷史補登原型](https://www.figma.com/proto/BwXm9ifVg4hZyteraSpuvc/CropPilot?node-id=168-21&starting-point-node-id=168%3A21&scaling=scale-down)

**2026-11-11 · 栽培歷史 · 已確認**

首版可依田區及作物查詢已結束的栽培紀錄，支援全部田區及全部作物。原型以北側田的番茄示範篩選、單筆詳情及返回原列表，並附上查無結果與查詢失敗畫面。[可點選原型](https://www.figma.com/proto/BwXm9ifVg4hZyteraSpuvc/CropPilot?node-id=145-3&page-id=145%3A2&starting-point-node-id=145%3A3&scaling=scale-down) · [需求與驗收條件](requirements/desktop-field-and-planting.md#栽培歷史)

**2026-11-11 · 作物栽培管理整合稿 · 範圍已確認**

本輪一併驗收占用面積、單筆編輯與狀態變更、規劃中刪除。已確認沒有既有栽培紀錄，無須設計補填流程。整合稿仍須走查超額提示及保留輸入。

同一頁呈現田區總面積、已分配面積、剩餘面積，以及每筆作物的狀態、日期和占用面積。原型從有 300 m² 剩餘面積的栽培列表開始，可直接新增。新增和編輯表單的占用面積皆標示必填。小白菜結束後，管理頁顯示剩餘 300 m²，田區總覽仍顯示番茄。舊流程圖中的待補面積分支已退出播放流程。[可點選原型](https://www.figma.com/proto/BwXm9ifVg4hZyteraSpuvc/CropPilot?node-id=87-2035&page-id=87%3A1916&starting-point-node-id=87%3A2035&scaling=scale-down&show-proto-sidebar=1)

整合稿補上規劃中紀錄的刪除分支。已在播放模式確認取消會保留小白菜，確認刪除後同一田區的剩餘面積從 0 m² 增至 300 m²。[從規劃中紀錄試走刪除](https://www.figma.com/proto/BwXm9ifVg4hZyteraSpuvc/CropPilot?node-id=87-2173&page-id=87%3A1916&starting-point-node-id=87%3A2173&scaling=scale-down)

單筆栽培改從列表的「編輯」開啟完整表單，載入作物名稱、品種、狀態、預定與實際栽種日、占用面積和備註。播放模式已走查小白菜維持規劃中儲存，以及在表單內改為栽培中、補填必填日期後儲存。其他欄位目前呈現於原型表單，逐欄輸入與儲存結果依需求驗收。[編輯表單原型](https://www.figma.com/proto/BwXm9ifVg4hZyteraSpuvc/CropPilot?node-id=103-2&page-id=87%3A1916&starting-point-node-id=87%3A2173&scaling=scale-down)

**v0.1 · 2026-11-09 · 初步檢視**

這次看過的互動稿涵蓋總覽、田區列表與新增、種植批次列表與新增。使用者初步認可整體操作方向，首輪以桌機版為優先。手機版保留作為後續設計參考。

早期 HTML 互動稿中的資料與流程供討論使用。首個桌機切片的 UI／UX 已確認，田區名稱和面積必填，座標選填。最新範圍與驗收條件見[桌機版需求草案](requirements/desktop-field-and-planting.md)。

**主要設計稿**　第一個桌機切片的 Figma v0.1 已建立，從未登入的首頁開始。[桌機切片設計記錄](ui/first-desktop-slice/README.md)保存設計稿、可點選原型、連線對照與驗證結果，供畫面審查。

登入頁及帳密失敗畫面已新增「以 Google 帳號登入」。兩個按鈕皆已在 Figma 播放模式驗證可進入田區總覽，模擬 Google 登入成功。[Google 登入原型](ui/first-desktop-slice/README.md)

**早期原型**　[CropPilot HTML 互動稿 v0.1](ui/prototypes/croppilot-v0.1.html)留作參考，也可用來試實際輸入、表單驗證及列表更新。瀏覽器可直接開啟，新增的示意資料會在重新整理後還原。

## 技術方向

使用者指定前端採 Angular，後端採 C#。專案的學習目標是[走完整個功能開發週期](development-cycle.md)，包含需求、UI／UX、資料庫與 API 設計、實作串接和驗收。Figma 設計確認後，前端進入 Angular 與 TypeScript 實作。介面元件建議用 Angular Material，版本在建立專案時鎖定。後端框架建議採 ASP.NET Core Web API，使用本機已安裝的 .NET 10 SDK（10.0.402）。本機 Node.js 為 24.19.0，Angular CLI 為 22.1.4。[Angular 版本相容表](https://angular.dev/reference/versions) · [.NET 支援政策](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)

首個桌機切片採 contract-first，以 Stoplight 匯出的 `openapi.yaml` 生成 ASP.NET Core 抽象 Controller、DTO 和 Angular client。先用 `POST /api/v1/fields` 驗證實作與串接。PostgreSQL 結構版本由 EF Core Migrations 管理。[OpenAPI 契約](openapi/openapi.yaml) · [API 設計](design/first-desktop-slice-data-api.md) · [資料庫設計](design/first-desktop-slice-database.md)

## 導覽與畫面草案

首輪以桌機版為主。以下手機畫面與操作是後續設計參考，開發順序見[功能開發週期](development-cycle.md)。

以個別農民或小型農場為示意情境，介面用繁體中文。桌面左側放「總覽、田區、種植批次、作物曆、肥培、農藥」。手機以頂欄選單切頁。作物曆、肥培、農藥先用簡短佔位頁說明規劃用途；農藥頁上的安全採收期區塊只說明用途。所有頁面顯示「原型示意資料｜重新整理後會回到預設資料」。

### 總覽 `/dashboard`

```text
總覽                                  示意基準日 2026/11/09
[ 田區 2 個 ]    [ 栽培中 1 批 ]    [ 近期預定採收 1 批 ]

近期預定採收
11/14   番茄・示意品種 A    北側田     栽培中

[查看田區]   [查看種植批次]
```

田區數與栽培中批次數取自列表資料。近期預定採收顯示狀態為「栽培中」且預定日期介於示意基準日與其後第 6 天（含）的批次，依日期排序。日期是使用者填寫的規劃資訊。手機版依序堆疊摘要、清單與入口。沒有符合的批次時顯示「這段期間沒有預定採收」。

### 田區 `/fields`

```text
田區管理                                                [新增田區]
名稱                 位置                 面積
北側田               農場北側             1,200 m²
南側田               農場南側               850 m²
```

新增表單含田區名稱、面積（m²）和位置。名稱去掉頭尾空白後必填，面積須為大於 0 的有限數字，可含小數，位置選填。儲存成功後回列表、顯示新項目與文字提示。手機列表改為卡片，面積與位置直接可見，新增表單採單欄。空列表顯示「目前沒有田區」與新增入口。

### 種植批次 `/crop-cycles`

```text
種植批次                                             [新增種植批次]
[全部田區 ▾]  [全部狀態 ▾]
作物／品種          田區      種植日期       預定採收日       狀態
番茄／示意品種 A    北側田    2026/09/05     2026/11/14       栽培中
小白菜              南側田    2026/11/12     —                規劃中
```

新增表單依序填田區、作物、品種、狀態、種植日期、預定採收日。田區、作物、狀態和種植日期必填，品種與預定採收日選填。先選作物才列出其品種，切換作物會清除原先選的品種。狀態預設為「規劃中」，日期欄標示「預定種植日」。選「栽培中」或「已結束」時改標示「實際種植日」。預定採收日若有填，須等於或晚於種植日期。

手機版以卡片顯示作物、田區、狀態和日期，篩選放在列表上方，表單採單欄。尚無田區時，表單顯示原因和「先新增田區」連結。篩選結果為空時提供清除篩選。表單驗證錯誤放在欄位旁，狀態同時以文字呈現。記憶體資料直接載入，串 API 時再加入載入中、讀取失敗重試與儲存失敗時保留表單內容。

## 示意資料與型別

單一農場是本輪假設。田區、作物與種植批次各有自己的識別碼。品種屬於一個作物，批次以 ID 連結田區、作物和選填的品種。日期是本地日曆日期 `YYYY-MM-DD`，表單須驗證日期有效性。型別是原型用的草案，正式 API 和資料庫結構可隨操作驗證調整。

```ts
type LocalDate = string;
type CropCycleStatus = 'planned' | 'growing' | 'closed';

interface Field {
  id: string;
  name: string;
  areaSquareMeters: number;
  location: string | null;
}

interface Crop {
  id: string;
  name: string;
}

interface Variety {
  id: string;
  cropId: string;
  name: string;
}

interface CropCycle {
  id: string;
  fieldId: string;
  cropId: string;
  varietyId: string | null;
  plantingDate: LocalDate;
  expectedHarvestDate: LocalDate | null;
  status: CropCycleStatus;
}

const statusLabels: Record<CropCycleStatus, string> = {
  planned: '規劃中',
  growing: '栽培中',
  closed: '已結束',
};

// 全部為虛構的畫面測試資料，日期僅用於驗證操作。
const demoDate: LocalDate = '2026-11-09';
const demoFields: Field[] = [
  { id: 'field-north', name: '北側田', areaSquareMeters: 1200, location: '農場北側' },
  { id: 'field-south', name: '南側田', areaSquareMeters: 850, location: '農場南側' },
];
const demoCrops: Crop[] = [
  { id: 'tomato', name: '番茄' },
  { id: 'bok-choy', name: '小白菜' },
];
const demoVarieties: Variety[] = [
  { id: 'tomato-a', cropId: 'tomato', name: '示意品種 A' },
];
const demoCropCycles: CropCycle[] = [
  {
    id: 'cycle-1', fieldId: 'field-north', cropId: 'tomato', varietyId: 'tomato-a',
    plantingDate: '2026-09-05', expectedHarvestDate: '2026-11-14', status: 'growing',
  },
  {
    id: 'cycle-2', fieldId: 'field-south', cropId: 'bok-choy', varietyId: null,
    plantingDate: '2026-11-12', expectedHarvestDate: null, status: 'planned',
  },
];
```

## 待決事項

| 決策 | 方案與取捨 | 原型建議 |
| --- | --- | --- |
| 首頁入口 | 總覽能掃描全場，但新增批次要先切頁。批次列表能直接操作，但全場摘要需另找。 | 先以總覽為首頁，保留明顯的批次入口。 |
| 一個批次能否跨田區 | 單一田區容易新增、查詢和回溯作業。多田區貼近同步種植，但要釐清部分田區何時結束與如何記錄作業。 | 原型先採一批次一田區，用實際作業情境評估多田區需求。 |
| 同一田區能否同時有多個批次 | 允許重疊可表達間作或分區種植，但可能接受誤填。限制重疊有助避免誤填，但需要可靠的實際結束日，也會擋住合法的間作情境。 | 原型允許重疊，先收集間作、補植和輪作案例。 |
| 批次狀態如何取得 | 手動選擇可反映提前採收、延後種植等現場情況，但需記得更新。依日期推算能減少操作，但需有完整的實際作業紀錄。 | 先由使用者選擇狀態。若現場需要同時追蹤計畫與實際種植日，正式模型再拆成兩欄。 |

## 可分開驗收的切片

1. **田區列表與新增**。可從桌機版導覽進入田區列表，驗證空白名稱與無效面積。填寫有效資料後看到新田區和成功提示；切頁回來資料仍在，重新整理則回到預設資料。
2. **種植批次列表與新增**。第一步新增的田區可在表單中選到。驗證必填欄、品種與作物的關係，以及採收日期早於種植日期時的提示。儲存後在列表中找到批次，並可依田區和狀態篩選。
3. **總覽與其他模組入口**。總覽的田區數、栽培中批次數與近期預定採收由同一份資料得出。新增符合條件的批次後，摘要和列表同步更新。作物曆、肥培和農藥可從導覽切入佔位頁。

建議先驗收切片 1。它能讓專案負責人在建立批次之前，先用熟悉的田區情境檢查欄位、單位及桌機操作。
