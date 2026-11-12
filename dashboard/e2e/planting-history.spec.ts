import { expect, test } from '@playwright/test';

const north = 'ad31b935-6b1c-4669-8c92-fb1c8489900f';
const south = 'ad31b935-6b1c-4669-8c92-fb1c84899001';
const plantingId = '9d2cf07b-d8d2-4308-9d36-f0ce10504731';
const historyUrl = '/dashboard/planting-history';
const northField = { id: north, name: '北側田', areaSquareMeters: 1000 };
const record = {
  id: plantingId, fieldId: north, fieldName: '北側田', cropName: '番茄',
  variety: '玉女', actualPlantingDate: '2026-03-12', areaSquareMeters: 240,
};

test('栽培歷史可依條件查詢、重試、看詳情及返回原結果', async ({ page }) => {
  const queries: string[][] = [];
  let failNextFilteredQuery = true;

  await page.route('**/api/v1/**', async route => {
    const { pathname, searchParams } = new URL(route.request().url());
    switch (pathname) {
      case '/api/v1/auth/csrf':
        return route.fulfill({ status: 204 });
      case '/api/v1/auth/refresh':
        return route.fulfill({ json: { accessToken: 'test-token', sessionId: 'test-session' } });
      case '/api/v1/auth/session':
        return route.fulfill({ status: 204 });
      case '/api/v1/fields':
        return route.fulfill({ json: { fields: [northField, { id: south, name: '南側田', areaSquareMeters: 800 }] } });
      case '/api/v1/planting-history/crops':
        return route.fulfill({
          json: searchParams.get('fieldId') === north ? ['番茄'] : ['番茄', '玉米'],
        });
      case '/api/v1/planting-history': {
        const query = [...searchParams.entries()].map(([key, value]) => `${key}=${value}`).sort();
        queries.push(query);
        if (searchParams.has('cropName') && failNextFilteredQuery) {
          failNextFilteredQuery = false;
          return route.fulfill({ status: 500 });
        }
        return route.fulfill({ json: searchParams.get('fieldId') === south ? [] : [record] });
      }
      case `/api/v1/fields/${north}`:
        return route.fulfill({ json: northField });
      case `/api/v1/plantings/${plantingId}`:
        return route.fulfill({ json: { ...record, status: 'completed', plannedPlantingDate: '2026-03-10', notes: '第二期採收完成' } });
      default:
        return route.fulfill({ status: 404 });
    }
  });

  await page.goto(historyUrl);
  const fields = page.getByLabel('田區', { exact: true });
  const crops = page.getByLabel('作物', { exact: true });
  const search = page.getByRole('button', { name: '查詢', exact: true });
  const table = page.getByRole('table', { name: '栽培歷史紀錄' });
  await expect(fields.getByRole('option')).toHaveText(['全部田區', '北側田', '南側田']);
  await expect(crops.getByRole('option')).toHaveText(['全部作物', '番茄', '玉米']);
  await expect(table).toBeHidden();
  expect(queries).toEqual([]);
  await search.click();
  await expect(table).toContainText('北側田');
  expect(queries).toEqual([[]]);

  await crops.selectOption('玉米');
  await fields.selectOption(north);
  await expect(crops.getByRole('option')).toHaveText(['全部作物', '番茄']);
  await expect(crops).toHaveValue('');
  await crops.selectOption('番茄');
  await search.click();
  await expect(page.getByRole('alert')).toHaveText('無法查詢栽培歷史，請再試一次。');
  await expect(fields).toHaveValue(north);
  await expect(crops).toHaveValue('番茄');
  await page.getByRole('button', { name: '重試查詢' }).click();
  await expect(table).toContainText('玉女');
  const filtered = ['cropName=番茄', `fieldId=${north}`];
  expect(queries).toEqual([[], filtered, filtered]);

  await table.getByRole('link', { name: /查看詳情/ }).click();
  await expect(page).toHaveURL(`${historyUrl}/${north}/${plantingId}`);
  await expect(table).toBeHidden();
  const detail = page.getByRole('region', { name: '栽培紀錄資料' });
  await expect(detail).toContainText('已結束');
  await expect(detail).toContainText('2026-03-10');
  await expect(detail).toContainText('第二期採收完成');
  await page.getByRole('link', { name: '返回栽培歷史' }).click();
  await expect(page).toHaveURL(historyUrl);
  await expect(fields).toHaveValue(north);
  await expect(crops).toHaveValue('番茄');
  await expect(table).toContainText('240 m²');
  expect(queries).toEqual([[], filtered, filtered]);

  await fields.selectOption(south);
  await search.click();
  await expect(page.getByRole('status').filter({ hasText: '查無符合條件' })).toBeVisible();
});
