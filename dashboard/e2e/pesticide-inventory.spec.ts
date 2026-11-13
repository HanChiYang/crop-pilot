import { expect, test } from '@playwright/test';
import type { PesticideLotRsDto } from '../src/app/generated/model/pesticideLotRsDto';
import type { PesticideMaterialRsDto } from '../src/app/generated/model/pesticideMaterialRsDto';

const fieldId = 'ad31b935-6b1c-4669-8c92-fb1c8489900f';
const plantingId = '9d2cf07b-d8d2-4308-9d36-f0ce10504731';
const materialId = '9d2cf07b-d8d2-4308-9d36-f0ce10504732';
const openingLotId = '9d2cf07b-d8d2-4308-9d36-f0ce10504733';
const purchaseLotId = '9d2cf07b-d8d2-4308-9d36-f0ce10504734';
const plantingApi = '/api/v1/plantings/' + plantingId;
const pesticidesApi = '/api/v1/pesticides';
const planting = { id: plantingId, fieldId, cropName: '青江菜', status: 'growing', areaSquareMeters: 240 };

test('新增農藥與兩種批次，施用後回查批次及主檔餘量', async ({ page }) => {
  let material: PesticideMaterialRsDto | undefined;
  const lots: PesticideLotRsDto[] = [];
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
        if (request.method() === 'GET') return route.fulfill({ json: material ? [material] : [] });
        const { name, stockUnit } = request.postDataJSON();
        if (name === '已使用的名稱') {
          return route.fulfill({ status: 409, json: { detail: '已有同名農藥。' } });
        }
        expect({ name, stockUnit }).toEqual({ name: '示例農藥', stockUnit: 'milliliter' });
        material = { id: materialId, name, stockUnit, remainingQuantity: 0 };
        return route.fulfill({ status: 201, json: material });
      }
      case pesticidesApi + '/' + materialId + '/lots': {
        if (request.method() === 'GET') return route.fulfill({ json: lots });
        const body = request.postDataJSON();
        const purchase = body.source === 'purchase';
        expect(body).toEqual(purchase
          ? { source: 'purchase', purchasedOn: '2026-01-03', receivedQuantity: 500, unitPrice: 2.5 }
          : { source: 'openingStock', purchasedOn: null, availableFrom: '2026-01-01', receivedQuantity: 300, unitPrice: null });
        const lot: PesticideLotRsDto = {
          ...body,
          id: purchase ? purchaseLotId : openingLotId,
          materialId,
          availableFrom: purchase ? body.purchasedOn : body.availableFrom,
          remainingQuantity: body.receivedQuantity,
          totalCost: purchase ? 1250 : null,
          currencyCode: 'TWD',
        };
        lots.push(lot);
        material!.remainingQuantity += lot.remainingQuantity;
        return route.fulfill({ status: 201, json: lot });
      }
      case plantingApi + '/material-applications': {
        if (request.method() === 'GET') return route.fulfill({ json: operations });
        const body = request.postDataJSON();
        expect(body.details).toEqual([{ materialPurchaseLotId: openingLotId, applicationMethod: 'broadcast', quantity: 10 }]);
        const lot = lots.find(lot => lot.id === openingLotId)!;
        lot.remainingQuantity -= 10;
        material!.remainingQuantity -= 10;
        const operation = {
          id: 'operation',
          operationDate: body.operationDate,
          details: [{ ...body.details[0], id: 'detail', materialId, materialType: 'pesticide',
            materialName: material!.name, unit: '毫升', unitPrice: null, currencyCode: 'TWD' }],
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
  await page.getByRole('button', { name: '新增農藥', exact: true }).click();
  await page.getByLabel('農藥名稱').fill('已使用的名稱');
  await page.getByLabel('庫存單位').selectOption('milliliter');
  const save = page.getByRole('button', { name: '儲存', exact: true });
  await save.click();
  await expect(page.getByRole('alert')).toContainText('已有同名農藥');
  await expect(page.getByLabel('農藥名稱')).toHaveValue('已使用的名稱');
  await expect(page.getByLabel('庫存單位')).toHaveValue('milliliter');
  await page.getByLabel('農藥名稱').fill('示例農藥');
  await save.click();

  await page.getByRole('button', { name: '登記進貨' }).click();
  await page.getByLabel('購買日期').fill('2026-01-03');
  await page.getByLabel('進貨量').fill('500');
  await save.click();
  await expect(page.getByRole('alert')).toHaveText('進貨須填寫購買日期及單價。');
  await page.getByLabel('單價').fill('2.5');
  await save.click();
  const purchaseRow = page.getByRole('row', { name: /^進貨 / });
  await expect(purchaseRow).toContainText('NT$1,250');

  await page.getByRole('button', { name: '登記期初庫存' }).click();
  await page.getByLabel('盤點日期').fill('2026-01-01');
  await page.getByLabel('盤點剩餘量').fill('300');
  await save.click();
  const openingRow = page.getByRole('row', { name: /^期初庫存 / });
  await expect(openingRow).toContainText('未填');
  const materialRow = page.getByRole('row', { name: /^示例農藥 / });
  await expect(materialRow.getByRole('cell', { name: '800', exact: true })).toBeVisible();

  await page.getByRole('link', { name: '作物栽培管理', exact: true }).click();
  await page.getByLabel('田區', { exact: true }).selectOption(fieldId);
  await page.getByLabel('田間作業 青江菜').click();
  await page.getByRole('link', { name: '資材施用紀錄', exact: true }).click();
  await page.getByRole('button', { name: '新增施用', exact: true }).click();
  await page.getByLabel('施用日期').fill('2026-01-02');
  await page.getByLabel('資材類別').selectOption('pesticide');
  await page.getByLabel('資材 1', { exact: true }).selectOption(materialId);
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
  await expect(materialRow.getByRole('cell', { name: '790', exact: true })).toBeVisible();
  await materialRow.getByRole('button', { name: '查看批次' }).click();
  await expect(openingRow.getByRole('cell', { name: '290 毫升', exact: true })).toBeVisible();
  await expect(purchaseRow.getByRole('cell', { name: '500 毫升', exact: true })).toHaveCount(2);
  await page.getByRole('link', { name: '肥料庫存', exact: true }).click();
  await expect(page.getByRole('heading', { name: '肥料庫存', exact: true })).toBeVisible();
});
