import { expect, test } from '@playwright/test';
import type { PesticideLotRsDto } from '../src/app/generated/model/pesticideLotRsDto';
import type { PesticideMaterialRsDto } from '../src/app/generated/model/pesticideMaterialRsDto';

const fieldId = 'ad31b935-6b1c-4669-8c92-fb1c8489900f';
const plantingId = '9d2cf07b-d8d2-4308-9d36-f0ce10504731';
const materialId = '9d2cf07b-d8d2-4308-9d36-f0ce10504732';
const largerMaterialId = '9d2cf07b-d8d2-4308-9d36-f0ce10504735';
const openingLotId = '9d2cf07b-d8d2-4308-9d36-f0ce10504733';
const purchaseLotId = '9d2cf07b-d8d2-4308-9d36-f0ce10504734';
const plantingApi = '/api/v1/plantings/' + plantingId;
const pesticidesApi = '/api/v1/pesticides';
const planting = { id: plantingId, fieldId, cropName: '青江菜', status: 'growing', areaSquareMeters: 240 };

test('同名農藥依包裝分開登記進貨與扣量，既有期初庫存仍可使用', async ({ page }) => {
  const materials: PesticideMaterialRsDto[] = [{
    id: materialId, name: '示例農藥', stockUnit: 'milliliter', remainingQuantity: 300,
    package: { quantity: 500, unit: 'milliliter' },
  }];
  const lots: PesticideLotRsDto[] = [];
  lots.push({
    id: openingLotId, materialId, source: 'openingStock', availableFrom: '2026-01-01',
    purchasedOn: null, receivedQuantity: 300, remainingQuantity: 300, unitPrice: null, totalCost: null,
    currencyCode: 'TWD',
  });
  const operations: object[] = [];

  await page.route('**/api/v1/**', async route => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    switch (path) {
      case '/api/v1/auth/csrf':
      case '/api/v1/auth/session':
        return route.fulfill({ status: 204 });
      case '/api/v1/auth/refresh':
        return route.fulfill({ json: { accessToken: 'test-token', sessionId: 'test-session' } });
      case '/api/v1/fields':
        return route.fulfill({ json: { fields: [{ id: fieldId, name: '北側田', areaSquareMeters: 1000 }] } });
      case '/api/v1/overview':
        return route.fulfill({ json: [{ id: fieldId, name: '北側田', areaSquareMeters: 1000, plantings: [planting] }] });
      case '/api/v1/fields/' + fieldId:
        return route.fulfill({ json: { id: fieldId, name: '北側田', areaSquareMeters: 1000 } });
      case '/api/v1/fields/' + fieldId + '/plantings':
        return route.fulfill({ json: {
          areaSquareMeters: 1000, allocatedAreaSquareMeters: 240, remainingAreaSquareMeters: 760,
          plantings: [planting],
        } });
      case plantingApi:
        return route.fulfill({ json: planting });
      case '/api/v1/fertilizers':
        return route.fulfill({ json: [] });
      case pesticidesApi: {
        if (request.method() === 'GET') return route.fulfill({ json: materials });
        const body = request.postDataJSON();
        if (body.package.quantity === 500) {
          return route.fulfill({ status: 409, json: { detail: '已有相同的包裝規格。' } });
        }
        expect(body).toEqual({ name: '示例農藥', stockUnit: 'milliliter', package: { quantity: 1, unit: 'liter' } });
        const material = { ...body, id: largerMaterialId, remainingQuantity: 0 };
        materials.push(material);
        return route.fulfill({ status: 201, json: material });
      }
      case pesticidesApi + '/' + materialId + '/lots':
        return route.fulfill({ json: lots.filter(lot => lot.materialId === materialId) });
      case pesticidesApi + '/' + largerMaterialId + '/lots': {
        if (request.method() === 'GET') return route.fulfill({ json: lots.filter(lot => lot.materialId === largerMaterialId) });
        const body = request.postDataJSON();
        expect(body).toEqual({ source: 'purchase', purchasedOn: '2026-01-03', receivedQuantity: 3, unitPrice: 33.333333 });
        const lot: PesticideLotRsDto = {
          ...body,
          id: purchaseLotId,
          materialId: largerMaterialId,
          availableFrom: body.purchasedOn,
          remainingQuantity: body.receivedQuantity,
          totalCost: 99.999999,
          currencyCode: 'TWD',
        };
        lots.push(lot);
        materials[1].remainingQuantity += lot.remainingQuantity;
        return route.fulfill({ status: 201, json: lot });
      }
      case pesticidesApi + '/' + largerMaterialId:
        expect(request.method()).toBe('DELETE');
        materials.splice(1, 1);
        return route.fulfill({ status: 204 });
      case '/api/v1/pesticide-lots/' + purchaseLotId: {
        if (request.method() === 'PUT') {
          const body = request.postDataJSON();
          expect(body).toEqual({ purchasedOn: '2026-01-03', receivedQuantity: 4, unitPrice: 34 });
          const lot = lots.find(lot => lot.id === purchaseLotId)!;
          lot.unitPrice = body.unitPrice;
          lot.receivedQuantity = lot.remainingQuantity = body.receivedQuantity;
          lot.totalCost = 136;
          materials[1].remainingQuantity = lot.remainingQuantity;
          return route.fulfill({ json: lot });
        }
        expect(request.method()).toBe('DELETE');
        lots.splice(lots.findIndex(lot => lot.id === purchaseLotId), 1);
        materials[1].remainingQuantity = 0;
        return route.fulfill({ status: 204 });
      }
      case plantingApi + '/material-applications': {
        if (request.method() === 'GET') return route.fulfill({ json: operations });
        const body = request.postDataJSON();
        expect(body.details).toEqual([{ materialPurchaseLotId: openingLotId, applicationMethod: 'broadcast', quantity: 10 }]);
        const lot = lots.find(lot => lot.id === openingLotId)!;
        lot.remainingQuantity -= 10;
        materials[0].remainingQuantity -= 10;
        const operation = {
          id: 'operation',
          operationDate: body.operationDate,
          details: [{ ...body.details[0], id: 'detail', materialId, materialType: 'pesticide',
            materialName: materials[0].name, unit: '毫升', unitPrice: null, currencyCode: 'TWD' }],
        };
        operations.push(operation);
        return route.fulfill({ status: 201, json: operation });
      }
      default:
        return route.fulfill({ status: 404 });
    }
  });

  await page.goto('/dashboard');
  await page.getByRole('link', { name: '資材管理', exact: true }).click();
  await page.getByRole('button', { name: '新增農藥規格' }).click();
  await page.getByLabel('農藥名稱').fill('示例農藥');
  await page.getByLabel('庫存單位').selectOption('milliliter');
  await page.getByLabel('每件容量或重量').fill('500');
  const packageUnit = page.getByRole('combobox', { name: '規格單位', exact: true });
  await packageUnit.selectOption('milliliter');
  const saveSpec = page.getByRole('button', { name: '儲存規格', exact: true });
  const save = page.getByRole('button', { name: '儲存', exact: true });
  await saveSpec.click();
  await expect(page.getByRole('alert')).toContainText('已有相同的包裝規格');
  await expect(page.getByLabel('每件容量或重量')).toHaveValue('500');
  await expect(page.getByLabel('庫存單位')).toHaveValue('milliliter');
  await page.getByLabel('每件容量或重量').fill('1');
  await packageUnit.selectOption('liter');
  await saveSpec.click();

  const largerSpec = page.getByRole('row').filter({ hasText: '1 公升' });
  const smallerSpec = page.getByRole('row').filter({ hasText: '500 毫升' });
  await expect(smallerSpec).toContainText('可使用');
  await expect(largerSpec).toContainText('待進貨');
  const specRows = page.getByRole('region', { name: '農藥規格清單' }).locator('tbody tr');
  const statusSort = page.getByRole('button', { name: '使用狀態', exact: true });
  await statusSort.click();
  await expect(specRows.first()).toContainText('待進貨');
  await statusSort.click();
  await expect(specRows.first()).toContainText('可使用');
  await expect(page.getByRole('columnheader', { name: '使用狀態' })).toHaveAttribute('aria-sort', 'descending');
  await largerSpec.getByRole('button', { name: /^操作/ }).click();
  await expect(largerSpec.locator('[popover]').getByRole('button')).toHaveText(['登記進貨', '刪除規格']);
  await page.getByRole('button', { name: '登記進貨' }).click();
  await expect(page.getByLabel('資材規格')).toHaveValue(largerMaterialId);
  await page.getByLabel('購買日期').fill('2026-01-03');
  await page.getByLabel('進貨量').fill('3');
  await save.click();
  await expect(page.getByRole('alert')).toContainText('請填寫有效的進貨總價');
  await page.getByLabel('進貨總價').fill('100');
  await expect(page.locator('#unit-price-preview')).toHaveText('NT$33.333333');
  await save.click();
  const purchaseRow = page.getByRole('row').filter({ hasText: '2026/01/03' });
  await expect(purchaseRow).toContainText('NT$99.999999');
  await expect(page.getByRole('button', { name: '登記期初庫存' })).toHaveCount(0);
  const openingRow = page.getByRole('row').filter({ hasText: '期初庫存' });
  await expect(openingRow).toContainText('未填');

  await page.getByRole('link', { name: '作物栽培', exact: true }).click();
  await page.getByRole('button', { name: '操作 青江菜（北側田）' }).click();
  await page.getByRole('link', { name: '資材施用紀錄', exact: true }).click();
  await page.getByRole('button', { name: '新增施用', exact: true }).click();
  await page.getByLabel('施用日期').fill('2026-01-02');
  await page.getByLabel('資材類別').selectOption('pesticide');
  const selection = page.getByLabel('資材 1', { exact: true });
  await expect(selection.getByRole('option', { name: '示例農藥 · 500 毫升' })).toBeAttached();
  await expect(selection.getByRole('option', { name: '示例農藥 · 1 公升' })).toHaveCount(0);
  await selection.selectOption(materialId);
  const batches = page.getByLabel('庫存批次 1');
  await expect(batches.getByRole('option')).toHaveCount(2);
  await batches.selectOption(openingLotId);
  await page.getByLabel('施用方式').selectOption('broadcast');
  await page.getByLabel('用量').fill('10');
  await save.click();
  await expect(page.getByRole('region', { name: '資材施用紀錄清單' })).toContainText('10 毫升');
  await page.getByRole('button', { name: '編輯', exact: true }).click();
  await expect(batches).toHaveValue(openingLotId);
  await expect(page.getByLabel('用量')).toHaveValue('10');

  await page.getByRole('link', { name: '資材管理', exact: true }).click();
  await page.getByRole('button', { name: '進貨與庫存', exact: true }).click();
  await expect(openingRow.getByRole('cell', { name: '290 毫升', exact: true })).toBeVisible();
  await expect(purchaseRow.getByRole('cell', { name: '3 毫升', exact: true })).toHaveCount(2);
  await expect(purchaseRow.getByRole('button')).toHaveText(['編輯', '刪除']);
  await purchaseRow.getByRole('button', { name: '編輯', exact: true }).click();
  await expect(page.getByLabel('進貨量')).toHaveValue('3');
  await page.getByLabel('進貨量').fill('4');
  await page.getByLabel('單價').fill('34');
  await save.click();
  await expect(purchaseRow).toContainText('NT$136');
  await expect(openingRow.getByRole('button', { name: '刪除', exact: true })).toBeDisabled();
  const deletePurchase = purchaseRow.getByRole('button', { name: '刪除', exact: true });
  await page.getByRole('searchbox', { name: '搜尋農藥名稱' }).fill('示例');
  await deletePurchase.click();
  const confirmation = page.getByRole('region', { name: '確認刪除批次' });
  await expect(confirmation).toContainText('示例農藥 · 1 公升');
  await expect(confirmation).toContainText('進貨日期 2026/01/03');
  await confirmation.getByRole('button', { name: '取消' }).click();
  await deletePurchase.click();
  await confirmation.getByRole('button', { name: '確認刪除' }).click();
  await expect(confirmation).toHaveCount(0);
  await expect(page.getByRole('status').filter({ hasText: '已刪除批次' })).toBeVisible();
  await expect(purchaseRow).toHaveCount(0);
  await expect(openingRow).toBeVisible();
  await page.getByRole('button', { name: '規格管理', exact: true }).click();
  await largerSpec.getByRole('button', { name: /^操作/ }).click();
  await page.getByRole('button', { name: '刪除規格', exact: true }).click();
  await page.getByRole('button', { name: '確認刪除', exact: true }).click();
  await expect(page.getByRole('status').filter({ hasText: '已刪除規格' })).toBeVisible();
  await expect(largerSpec).toHaveCount(0);
  await expect(smallerSpec).toBeVisible();
  await page.getByRole('link', { name: '肥料', exact: true }).click();
  await expect(page.getByRole('region', { name: '肥料資材管理' })).toBeVisible();
});

test('資材清單依欄首升降冪排序，搜尋保留目前的排序', async ({ page }) => {
  const materials: PesticideMaterialRsDto[] = [
    { id: materialId, name: '農藥 10', stockUnit: 'milliliter', remainingQuantity: 5, package: { quantity: 500, unit: 'milliliter' } },
    { id: largerMaterialId, name: '農藥 2', stockUnit: 'milliliter', remainingQuantity: 100, package: { quantity: 1, unit: 'liter' } },
  ];
  const lots: PesticideLotRsDto[] = [
    { id: openingLotId, materialId, source: 'openingStock', availableFrom: '2026-01-01',
      purchasedOn: null, receivedQuantity: 500, remainingQuantity: 5, unitPrice: null, totalCost: null, currencyCode: 'TWD' },
    { id: purchaseLotId, materialId: largerMaterialId, source: 'purchase', availableFrom: '2026-01-02',
      purchasedOn: '2026-01-02', receivedQuantity: 100, remainingQuantity: 100, unitPrice: 1, totalCost: 100, currencyCode: 'TWD' },
    { id: 'last', materialId: largerMaterialId, source: 'purchase', availableFrom: '2026-01-03',
      purchasedOn: '2026-01-03', receivedQuantity: 20, remainingQuantity: 0, unitPrice: 8, totalCost: 160, currencyCode: 'TWD' },
  ];
  await page.route('**/api/v1/**', route => {
    const path = new URL(route.request().url()).pathname;
    if (path === '/api/v1/auth/refresh') {
      return route.fulfill({ json: { accessToken: 'test-token', sessionId: 'test-session' } });
    }
    if (path === pesticidesApi) return route.fulfill({ json: materials });
    const matchingMaterial = materials.find(item => path === pesticidesApi + '/' + item.id + '/lots');
    if (matchingMaterial) return route.fulfill({ json: lots.filter(lot => lot.materialId === matchingMaterial.id) });
    return route.fulfill({ status: 204 });
  });

  await page.goto('/dashboard/materials/pesticides');
  const specs = page.getByRole('region', { name: '農藥規格清單' }).getByRole('rowheader');
  await expect(specs).toHaveText(['農藥 2', '農藥 10']);
  await page.getByRole('button', { name: '名稱', exact: true }).press('Enter');
  await expect(specs).toHaveText(['農藥 10', '農藥 2']);

  await page.getByRole('button', { name: '進貨與庫存' }).click();
  const rows = page.getByRole('region', { name: '農藥進貨與庫存清單' }).locator('tbody tr');
  const dates = rows.locator('td:first-child');
  const newest = '2026/01/03';
  const middle = '2026/01/02';
  const oldest = /2026\/01\/01/;
  await expect(dates).toHaveText([newest, middle, oldest]);
  await page.getByRole('button', { name: '進貨日期', exact: true }).click();
  await expect(dates).toHaveText([oldest, middle, newest]);
  await page.getByRole('button', { name: '名稱／規格', exact: true }).click();
  await expect(dates).toHaveText([newest, middle, oldest]);
  await page.getByRole('button', { name: '進貨量', exact: true }).click();
  await expect(dates).toHaveText([oldest, middle, newest]);
  await page.getByRole('button', { name: '剩餘量', exact: true }).click();
  await expect(dates).toHaveText([middle, oldest, newest]);
  await page.getByRole('button', { name: '進貨總價', exact: true }).click();
  await expect(dates).toHaveText([newest, middle, oldest]);
  const unitPrice = page.getByRole('button', { name: '庫存單位單價', exact: true });
  await unitPrice.click();
  await expect(dates).toHaveText([newest, middle, oldest]);
  await unitPrice.click();
  await expect(dates).toHaveText([middle, newest, oldest]);
  await page.getByRole('searchbox', { name: '搜尋農藥名稱' }).fill('農藥 2');
  await expect(dates).toHaveText([middle, newest]);
  await expect(page.getByRole('columnheader', { name: '庫存單位單價' })).toHaveAttribute('aria-sort', 'ascending');
});
