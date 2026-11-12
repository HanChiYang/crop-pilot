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
      case materialsApi:
        return route.fulfill({ json: [{ ...material, remainingQuantity: remaining }] });
      case materialsApi + '/' + materialId + '/lots':
        return route.fulfill({ json: [{ ...lot, remainingQuantity: remaining, unitPrice: operations.length ? 20 : null },
          { ...lot, id: depletedId, availableFrom: '2026-01-02', remainingQuantity: 0 }] });
      case plantingApi + '/fertilizations':
      case '/api/v1/fertilizations/' + operationId: {
        if (request.method() === 'GET') return route.fulfill({ json: operations });
        const { operationDate, details } = request.postDataJSON();
        if (conflict) {
          conflict = false;
          return route.fulfill({ status: 409, json: { errors: { 'details[0].quantity': ['此批次的庫存餘量不足。'] } } });
        }
        const editing = request.method() === 'PUT';
        expect(details).toEqual([{ ...(editing ? { id: detailId } : {}), materialPurchaseLotId: lotId,
          quantity: editing ? 2 : 3 }]);
        remaining = 8 - details[0].quantity;
        const operation = { id: operationId, operationDate, details: [{ ...details[0], id: detailId,
          materialId, fertilizerName: material.name, unit: material.stockUnit, unitPrice: null, currencyCode: 'TWD' }] };
        operations = [operation];
        return route.fulfill({ status: editing ? 200 : 201, json: operation });
      }
      default:
        return route.fulfill({ status: 404 });
    }
  });

  await page.goto(historyPath);
  await page.getByRole('link', { name: '施肥紀錄', exact: true }).click();
  await expect(page.getByText('目前沒有施肥紀錄。')).toBeVisible();
  await page.getByRole('button', { name: '新增施肥', exact: true }).click();
  await page.getByLabel('施肥日期').fill('2026-01-03');
  await page.getByLabel('肥料 1', { exact: true }).selectOption(materialId);
  const batches = page.getByLabel('肥料批次 1');
  await expect(batches.getByRole('option')).toHaveCount(2);
  await batches.selectOption(lotId);
  await page.getByLabel('用量').fill('3');
  await page.getByRole('button', { name: '儲存', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('庫存餘量不足');
  await expect(page.getByLabel('用量')).toHaveValue('3');
  await page.getByRole('button', { name: '儲存', exact: true }).click();
  const list = page.getByRole('region', { name: '施肥紀錄清單' });
  await expect(list).toContainText('3 公斤');
  await expect(list).toContainText('成本待補');

  await page.getByRole('button', { name: '編輯', exact: true }).click();
  await expect(page.getByLabel('施肥日期')).toHaveValue('2026-01-03');
  await expect(batches).toHaveValue(lotId);
  await expect(batches.getByRole('option').nth(1)).toContainText('可用 8 公斤');
  await expect(batches.getByRole('option').nth(1)).toContainText('原單價 成本待補');
  await page.getByLabel('用量').fill('2');
  await page.getByRole('button', { name: '儲存', exact: true }).click();
  await expect(list).toContainText('2 公斤');
  await page.getByRole('link', { name: '返回栽培詳情' }).click();
  await expect(page).toHaveURL(historyPath);
  await expect(page.getByRole('region', { name: '栽培紀錄資料' })).toContainText('已結束');
});

test('肥料庫存可精確登記六位小數的期初餘量及未知單價', async ({ page }) => {
  const maximum = 999999999.999999;
  const openingStock = { ...lot, receivedQuantity: maximum, remainingQuantity: maximum };
  let materials: (typeof material)[] = [];
  let batches: (typeof lot)[] = [];
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
        expect(request.postDataJSON()).toEqual({ name: '有機肥', stockUnit: '公斤' });
        materials = [{ ...material, remainingQuantity: 0 }];
        return route.fulfill({ status: 201, json: materials[0] });
      case materialsApi + '/' + materialId + '/lots':
        if (request.method() === 'GET') return route.fulfill({ json: batches });
        expect(request.postDataJSON()).toMatchObject({
          source: 'openingStock', receivedQuantity: maximum, purchasedOn: null, unitPrice: null,
        });
        materials = [{ ...material, remainingQuantity: maximum }];
        batches = [openingStock];
        return route.fulfill({ status: 201, json: openingStock });
      default:
        return route.fulfill({ status: 404 });
    }
  });

  await page.goto('/dashboard/fertilizers');
  await page.getByRole('button', { name: '新增肥料資材' }).click();
  await page.getByLabel('肥料名稱').fill('有機肥');
  await page.getByLabel('庫存單位').fill('公斤');
  await page.getByRole('button', { name: '儲存', exact: true }).click();
  await page.getByRole('button', { name: '新增批次' }).click();
  await page.getByLabel('批次來源').selectOption('openingStock');
  await page.getByLabel(/盤點剩餘量/).fill('999999999.9999991');
  await page.getByRole('button', { name: '儲存', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('最多六位小數');
  await page.getByLabel(/盤點剩餘量/).fill('999999999.999999');
  await page.getByRole('button', { name: '儲存', exact: true }).click();
  await expect(page.getByRole('region', { name: '肥料資材清單' })).toContainText('999,999,999.999999 公斤');
  const stock = page.getByRole('region', { name: '有機肥的庫存批次' });
  await expect(stock).toContainText('期初庫存');
  await expect(stock).toContainText('成本待補');
  await expect(stock.getByRole('button', { name: '刪除資材' })).toBeDisabled();
  await stock.getByRole('button', { name: '編輯', exact: true }).click();
  await expect(page.getByLabel(/盤點剩餘量/)).toHaveValue('999999999.999999');
});
