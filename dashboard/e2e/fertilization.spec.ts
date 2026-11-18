import { expect, test } from '@playwright/test';

const fieldId = 'ad31b935-6b1c-4669-8c92-fb1c8489900f';
const plantingId = '9d2cf07b-d8d2-4308-9d36-f0ce10504731';
const materialId = '9d2cf07b-d8d2-4308-9d36-f0ce10504732';
const lotId = '9d2cf07b-d8d2-4308-9d36-f0ce10504733';
const depletedId = '9d2cf07b-d8d2-4308-9d36-f0ce10504734';
const operationId = '9d2cf07b-d8d2-4308-9d36-f0ce10504735';
const detailId = '9d2cf07b-d8d2-4308-9d36-f0ce10504736';
const field = { id: fieldId, name: '北側田', areaSquareMeters: 1000 };
const planting = { id: plantingId, fieldId, cropName: '番茄', areaSquareMeters: 240, status: 'completed' };
const material = { id: materialId, name: '有機肥', stockUnit: '公斤', remainingQuantity: 8 };
const lot = { id: lotId, materialId, source: 'openingStock', purchasedOn: null, availableFrom: '2026-01-01',
  receivedQuantity: 8, remainingQuantity: 8, unitPrice: null, currencyCode: 'TWD' };
const historyPath = '/dashboard/planting-history/' + fieldId + '/' + plantingId;
const plantingApi = '/api/v1/plantings/' + plantingId;
const materialsApi = '/api/v1/fertilizers';

test('從栽培歷史補登施肥，衝突保留輸入，並可編輯原明細', async ({ page }) => {
  let remaining = 8;
  let operations: { id: string; operationDate: string; details: object[] }[] = [];
  let conflict = true;

  await page.route('**/api/v1/**', async route => {
    const request = route.request();
    const { pathname } = new URL(request.url());
    switch (pathname) {
      case '/api/v1/auth/csrf':
      case '/api/v1/auth/session':
        return route.fulfill({ status: 204 });
      case '/api/v1/auth/refresh':
        return route.fulfill({ json: { accessToken: 'test-token', sessionId: 'test-session' } });
      case '/api/v1/fields':
        return route.fulfill({ json: { fields: [field] } });
      case '/api/v1/fields/' + fieldId:
        return route.fulfill({ json: field });
      case '/api/v1/planting-history/crops':
        return route.fulfill({ json: ['番茄'] });
      case plantingApi:
        return route.fulfill({ json: planting });
      case '/api/v1/pesticides':
        return route.fulfill({ json: [] });
      case materialsApi:
        return route.fulfill({ json: [{ ...material, remainingQuantity: remaining }] });
      case materialsApi + '/' + materialId + '/lots':
        return route.fulfill({ json: [{ ...lot, remainingQuantity: remaining, unitPrice: operations.length ? 20 : null },
          { ...lot, id: depletedId, availableFrom: '2026-01-02', remainingQuantity: 0 }] });
      case plantingApi + '/material-applications':
      case '/api/v1/material-applications/' + operationId: {
        if (request.method() === 'GET') return route.fulfill({ json: operations });
        const { operationDate, applicationMethod, sprayWaterVolumeLiters, description, details } = request.postDataJSON();
        if (conflict) {
          conflict = false;
          return route.fulfill({ status: 409, json: { errors: { 'details[0].quantity': ['此批次的庫存餘量不足。'] } } });
        }
        const editing = request.method() === 'PUT';
        expect(applicationMethod).toBe('broadcast');
        expect(sprayWaterVolumeLiters).toBeNull();
        expect(description).toBe('整地');
        expect(details).toEqual([{ ...(editing ? { id: detailId } : {}), materialPurchaseLotId: lotId,
          quantity: editing ? 2 : 3 }]);
        remaining = 8 - details[0].quantity;
        const operation = { id: operationId, operationDate, applicationMethod, sprayWaterVolumeLiters, description, details: [{ ...details[0], id: detailId,
          materialId, materialType: 'fertilizer', materialName: material.name, unit: material.stockUnit, unitPrice: null, currencyCode: 'TWD' }] };
        operations = [operation];
        return route.fulfill({ status: editing ? 200 : 201, json: operation });
      }
      default:
        return route.fulfill({ status: 404 });
    }
  });

  await page.goto(historyPath);
  await page.getByRole('link', { name: '資材施用紀錄', exact: true }).click();
  await expect(page.getByText('尚無資材施用紀錄')).toBeVisible();
  await page.getByRole('button', { name: '新增施用', exact: true }).click();
  await page.getByLabel('施用日期').fill('2026-01-03');
  await page.getByLabel('資材 1', { exact: true }).selectOption(materialId);
  const batches = page.getByLabel('庫存批次 1');
  await expect(batches.getByRole('option')).toHaveCount(2);
  await batches.selectOption(lotId);
  await page.getByRole('radio', { name: '噴施' }).check();
  await page.getByLabel('噴施總水量（公升）').fill('20');
  await page.getByRole('radio', { name: '撒施' }).check();
  await expect(page.getByLabel('噴施總水量（公升）')).toHaveCount(0);
  await page.getByRole('radio', { name: '噴施' }).check();
  await expect(page.getByLabel('噴施總水量（公升）')).toHaveValue('');
  await page.getByRole('radio', { name: '撒施' }).check();
  await page.getByLabel('備註（選填）').fill('整地');
  await page.getByLabel('用量').fill('3');
  await page.getByRole('button', { name: '儲存', exact: true }).click();
  await expect(page.locator('.detail').getByRole('alert')).toContainText('庫存餘量不足');
  await expect(page.getByLabel('用量')).toHaveValue('3');
  await page.getByRole('button', { name: '儲存', exact: true }).click();
  const list = page.getByRole('table', { name: '資材施用紀錄清單' });
  await expect(list).toContainText('3 公斤');
  await expect(list).toContainText('成本待補');
  const keyword = page.getByRole('searchbox', { name: '關鍵字搜尋' });
  await keyword.fill('農藥');
  await expect(list).toContainText('查無符合');
  await keyword.fill('有機肥');
  await expect(list).toContainText('3 公斤');

  await list.getByRole('button', { name: /操作.*資材施用/ }).click();
  await page.getByRole('button', { name: '編輯', exact: true }).click();
  await expect(page.getByRole('radio', { name: '撒施' })).toBeChecked();
  await expect(page.getByLabel('備註（選填）')).toHaveValue('整地');
  await expect(page.getByLabel('施用日期')).toHaveValue('2026-01-03');
  await expect(batches).toHaveValue(lotId);
  await expect(batches.getByRole('option').nth(1)).toContainText('可用 8 公斤');
  await expect(batches.getByRole('option').nth(1)).toContainText('批次單價 NT$20');
  await page.getByLabel('用量').fill('2');
  await page.getByRole('button', { name: '儲存', exact: true }).click();
  await expect(list).toContainText('2 公斤');
  await page.getByRole('link', { name: '返回栽培詳情' }).click();
  await expect(page).toHaveURL(historyPath);
  await expect(page.getByRole('region', { name: '栽培紀錄資料' })).toContainText('已結束');
});

test('肥料進貨支援六位小數及未知價格，既有期初批次仍可維護', async ({ page }) => {
  const maximum = 999999999.999999;
  const legacyId = '9d2cf07b-d8d2-4308-9d36-f0ce10504737';
  const legacyMaterial = { ...material, id: legacyId, stockUnit: '袋', remainingQuantity: 0 };
  const openingStock = { ...lot, materialId: legacyId, remainingQuantity: 0 };
  let materials = [legacyMaterial];
  let batches: (typeof lot)[] = [];
  let failLoading = true;
  await page.route('**/api/v1/**', async route => {
    const request = route.request();
    switch (new URL(request.url()).pathname) {
      case '/api/v1/auth/csrf':
      case '/api/v1/auth/session':
        return route.fulfill({ status: 204 });
      case '/api/v1/auth/refresh':
        return route.fulfill({ json: { accessToken: 'test-token', sessionId: 'test-session' } });
      case materialsApi:
        if (request.method() === 'GET') return route.fulfill({ json: materials });
        expect(request.postDataJSON()).toEqual({
          name: '有機肥', stockUnit: '公斤', package: { quantity: 20, unit: 'kilogram' },
        });
        const created = { ...material, ...request.postDataJSON(), remainingQuantity: 0 };
        materials.push(created);
        return route.fulfill({ status: 201, json: created });
      case materialsApi + '/' + legacyId + '/lots':
        if (failLoading) {
          failLoading = false;
          return route.fulfill({ status: 503 });
        }
        return route.fulfill({ json: [openingStock] });
      case materialsApi + '/' + materialId + '/lots':
        if (request.method() === 'GET') return route.fulfill({ json: batches });
        expect(request.postDataJSON()).toEqual({
          source: 'purchase', receivedQuantity: maximum, purchasedOn: '2026-01-03', unitPrice: null,
        });
        materials[1].remainingQuantity = maximum;
        batches = [{ ...lot, ...request.postDataJSON(), id: depletedId, availableFrom: '2026-01-03', remainingQuantity: maximum }];
        return route.fulfill({ status: 201, json: batches[0] });
      case '/api/v1/fertilizer-lots/' + depletedId:
        expect(request.method()).toBe('DELETE');
        materials[1].remainingQuantity = 0;
        batches = [];
        return route.fulfill({ status: 204 });
      default:
        return route.fulfill({ status: 404 });
    }
  });

  await page.goto('/dashboard/fertilizers');
  await expect(page.getByRole('alert')).toContainText('無法載入肥料規格及庫存');
  await page.getByRole('button', { name: '重新載入' }).click();
  const legacyRow = page.getByRole('row').filter({ hasText: '未設定包裝' });
  await expect(legacyRow).toContainText('已用完');
  await expect(legacyRow.getByRole('cell', { name: '袋', exact: true })).toBeVisible();
  await legacyRow.getByRole('button', { name: /^操作/ }).click();
  await expect(legacyRow.locator('[popover]').getByRole('button')).toHaveText(['登記進貨', '刪除規格']);
  await expect(page.getByRole('button', { name: '刪除規格' })).toBeDisabled();
  await page.keyboard.press('Escape');
  await page.getByRole('button', { name: '新增肥料規格' }).click();
  const stockUnit = page.getByRole('combobox', { name: '庫存單位', exact: true });
  await expect(stockUnit.getByRole('option')).toHaveText(['請選擇庫存單位', '公升', '毫升', '公斤', '公克']);
  await stockUnit.selectOption({ label: '公斤' });
  await page.getByLabel('每件容量或重量').fill('20');
  await page.getByRole('combobox', { name: '規格單位', exact: true }).selectOption('kilogram');
  await page.getByRole('textbox', { name: '肥料名稱', exact: true }).fill('有機肥');
  await page.getByRole('button', { name: '儲存規格', exact: true }).click();
  await expect(page.getByRole('row').filter({ hasText: '20 公斤' })).toContainText('待進貨');
  await page.getByRole('button', { name: '進貨與庫存' }).click();
  await expect(page.getByRole('button', { name: '登記期初庫存' })).toHaveCount(0);
  await page.getByRole('button', { name: '登記進貨' }).click();
  await page.getByLabel('資材規格').selectOption({ label: '有機肥 · 20 公斤' });
  await page.getByLabel('購買日期').fill('2026-01-03');
  await page.getByLabel(/進貨量/).fill('999999999.9999991');
  await page.getByRole('button', { name: '儲存', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('最多六位小數');
  await page.getByLabel(/進貨量/).fill('999999999.999999');
  await page.getByRole('button', { name: '儲存', exact: true }).click();
  const purchaseRow = page.getByRole('row').filter({ hasText: '2026/01/03' });
  await expect(purchaseRow.getByRole('button')).toHaveText(['編輯', '刪除']);
  await expect(purchaseRow.getByRole('cell', { name: '999,999,999.999999 公斤' })).toHaveCount(2);
  await expect(purchaseRow.getByRole('cell', { name: '未填', exact: true })).toHaveCount(2);
  const openingRow = page.getByRole('row').filter({ hasText: '期初庫存' });
  await expect(openingRow.getByRole('cell', { name: '0 袋', exact: true })).toBeVisible();
  await expect(openingRow.getByRole('button', { name: '刪除', exact: true })).toBeDisabled();
  await purchaseRow.getByRole('button', { name: '刪除', exact: true }).click();
  await page.getByRole('button', { name: '確認刪除', exact: true }).click();
  await expect(page.getByRole('status').filter({ hasText: '已刪除批次' })).toBeVisible();
  await expect(purchaseRow).toHaveCount(0);
  await openingRow.getByRole('button', { name: '編輯', exact: true }).click();
  await expect(page.getByLabel(/盤點剩餘量/)).toHaveValue('8');
});
