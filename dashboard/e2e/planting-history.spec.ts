import { expect, type Page, test } from '@playwright/test';
import type { CompletionDateLowerBoundSource } from '../src/app/generated/model/completionDateLowerBoundSource';

const north = 'ad31b935-6b1c-4669-8c92-fb1c8489900f';
const south = 'ad31b935-6b1c-4669-8c92-fb1c84899001';
const plantingId = '9d2cf07b-d8d2-4308-9d36-f0ce10504731';
const historyUrl = '/dashboard/planting-history';
const northField = { id: north, name: '北側田', areaSquareMeters: 1000 };
const record = {
  id: plantingId, fieldId: north, fieldName: '北側田', cropName: '番茄',
  variety: '玉女', actualPlantingDate: '2026-03-12', completionDate: '2026-07-20', areaSquareMeters: 240,
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
      case `/api/v1/plantings/${plantingId}/harvests`:
        return route.fulfill({ json: { totalWeightKg: 0, harvests: [] } });
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
  await expect(table.getByRole('row').nth(1).getByRole('cell').nth(3)).toHaveText('2026-07-20');
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
  await expect(page.getByRole('heading', { name: '番茄' })).toBeVisible();
  await expect(page.getByText('已結束', { exact: true })).toBeVisible();
  const detail = page.getByRole('region', { name: '栽培紀錄資料' });
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

async function setupCorrectionMock(page: Page) {
  let completionDate = '2026-10-20';
  let earliestDate = '2026-10-08';
  let earliestDateSource: CompletionDateLowerBoundSource = 'harvest';
  let failRangeOnce = false;
  let nextPutStatus: 200 | 409 | 500 = 200;
  const putDates: string[] = [];
  const historyQueries: string[][] = [];

  await page.route('**/api/v1/**', async route => {
    const request = route.request();
    const { pathname, searchParams } = new URL(request.url());
    const method = request.method();

    switch (pathname) {
      case '/api/v1/auth/csrf':
      case '/api/v1/auth/session':
        return route.fulfill({ status: 204 });
      case '/api/v1/auth/refresh':
        return route.fulfill({ json: { accessToken: 'test-token', sessionId: 'test-session' } });
      case '/api/v1/fields':
        return route.fulfill({ json: { fields: [northField, { id: south, name: '南側田', areaSquareMeters: 800 }] } });
      case '/api/v1/planting-history/crops':
        return route.fulfill({ json: ['番茄'] });
      case '/api/v1/planting-history': {
        historyQueries.push([...searchParams.entries()].map(([key, value]) => `${key}=${value}`).sort());
        return route.fulfill({ json: [{
          id: plantingId, fieldId: north, fieldName: '北側田', cropName: '番茄', variety: '玉女',
          actualPlantingDate: '2026-03-01', completionDate, areaSquareMeters: 240,
        }] });
      }
      case `/api/v1/fields/${north}`:
        return route.fulfill({ json: northField });
      case `/api/v1/plantings/${plantingId}/harvests`:
        return route.fulfill({ json: { totalWeightKg: 100, harvests: [] } });
      case `/api/v1/plantings/${plantingId}/completion-date-range`:
        if (failRangeOnce) {
          failRangeOnce = false;
          return route.fulfill({ status: 500 });
        }
        return route.fulfill({ json: {
          completionDate,
          earliestDate,
          earliestDateSource,
          latestDate: '2026-11-18',
        } });
      case `/api/v1/plantings/${plantingId}/completion-date`: {
        const body = request.postDataJSON() as { completionDate: string };
        putDates.push(body.completionDate);
        if (nextPutStatus === 409) {
          nextPutStatus = 200;
          earliestDate = '2026-10-15';
          earliestDateSource = 'irrigation';
          return route.fulfill({
            status: 409,
            json: { status: 409, errors: { completionDate: ['結束日不得早於最後一筆採收或作業日 2026-10-15。'] } },
          });
        }
        if (nextPutStatus === 500) {
          nextPutStatus = 200;
          return route.fulfill({ status: 500 });
        }
        completionDate = body.completionDate;
        return route.fulfill({ json: {
          id: plantingId, fieldId: north, cropName: '番茄', variety: '玉女',
          plannedPlantingDate: '2026-02-20', actualPlantingDate: '2026-03-01', completionDate,
          areaSquareMeters: 240, status: 'completed', notes: '第二期採收完成',
        } });
      }
      case `/api/v1/plantings/${plantingId}`:
        return route.fulfill({ json: {
          id: plantingId, fieldId: north, cropName: '番茄', variety: '玉女',
          plannedPlantingDate: '2026-02-20', actualPlantingDate: '2026-03-01', completionDate,
          areaSquareMeters: 240, status: 'completed', notes: '第二期採收完成',
        } });
      default:
        if (method) return route.fulfill({ status: 404 });
        return route.fulfill({ status: 404 });
    }
  });

  return {
    putDates,
    historyQueries,
    setFailRangeOnce: (value: boolean) => { failRangeOnce = value; },
    setNextPutStatus: (value: 200 | 409 | 500) => { nextPutStatus = value; },
  };
}

test('已結束詳情可修正結束日、驗證邊界、更新補登上限並同步歷史清單', async ({ page }) => {
  const api = await setupCorrectionMock(page);
  await page.clock.setFixedTime(new Date('2026-11-18T06:00:00Z'));
  await page.goto(historyUrl);

  const fields = page.getByLabel('田區', { exact: true });
  const crops = page.getByLabel('作物', { exact: true });
  await fields.selectOption(north);
  await crops.selectOption('番茄');
  await page.getByRole('button', { name: '查詢', exact: true }).click();
  const table = page.getByRole('table', { name: '栽培歷史紀錄' });
  await table.getByRole('link', { name: /查看詳情/ }).click();

  const detail = page.getByRole('region', { name: '栽培紀錄資料' });
  await expect(detail).toContainText('2026-10-20');
  await page.getByRole('button', { name: '修正', exact: true }).click();

  const dialog = page.getByRole('dialog', { name: '修正結束日' });
  await expect(dialog).toContainText('北側田・番茄・已結束');
  await expect(dialog).toContainText('2026/03/01');
  await expect(dialog).toContainText('2026/10/20');
  const dateInput = dialog.getByLabel(/結束日/);
  await expect(dateInput).toHaveValue('2026-10-20');
  await expect(dateInput).toHaveAttribute('min', '2026-10-08');
  await expect(dateInput).toHaveAttribute('max', '2026-11-18');
  await expect(dialog).toContainText('可填範圍：2026/10/08（最後一筆採收）至 2026/11/18（今天）');
  await expect(dialog).toContainText('修正後，補登的採收與田間作業日期不得晚於新的結束日。');

  const save = dialog.getByRole('button', { name: '儲存', exact: true });
  await dateInput.fill('');
  await save.click();
  await expect(dialog.getByRole('alert')).toHaveText('請填寫結束日。');
  await expect(dateInput).toHaveValue('');

  await dateInput.fill('2026-11-25');
  await save.click();
  await expect(dialog.getByRole('alert')).toHaveText('結束日不得晚於今天。');
  await expect(dateInput).toHaveValue('2026-11-25');

  await dateInput.fill('2026-10-05');
  await save.click();
  await expect(dialog.getByRole('alert')).toHaveText('結束日不得早於 2026/10/08（最後一筆採收）。');
  await expect(dateInput).toHaveValue('2026-10-05');
  expect(api.putDates).toEqual([]);

  await page.keyboard.press('Escape');
  await expect(dialog).toBeHidden();
  await expect(detail).toContainText('2026-10-20');

  await page.getByRole('button', { name: '修正', exact: true }).click();
  await dateInput.fill('2026-10-12');
  await dialog.getByRole('button', { name: '取消', exact: true }).click();
  await expect(dialog).toBeHidden();
  await expect(detail).toContainText('2026-10-20');
  expect(api.putDates).toEqual([]);

  await page.getByRole('button', { name: '修正', exact: true }).click();
  await dateInput.fill('2026-10-12');
  await save.click();
  await expect(dialog).toBeHidden();
  await expect(detail).toContainText('2026-10-12');
  await expect(page.getByText('結束日已更新為 2026/10/12。')).toBeVisible();
  expect(api.putDates).toEqual(['2026-10-12']);

  await page.getByRole('button', { name: '新增採收', exact: true }).click();
  const harvestDialog = page.getByRole('dialog', { name: '新增採收' });
  await expect(harvestDialog.getByLabel('採收日期')).toHaveAttribute('max', '2026-10-12');
  await expect(harvestDialog.getByLabel('採收日期')).toHaveValue('2026-10-12');
  await harvestDialog.getByRole('button', { name: '取消', exact: true }).click();

  // 未變更日期仍可送出並顯示更新成功提示。
  await page.getByRole('button', { name: '修正', exact: true }).click();
  await expect(dateInput).toHaveValue('2026-10-12');
  await save.click();
  await expect(dialog).toBeHidden();
  await expect(page.getByText('結束日已更新為 2026/10/12。')).toBeVisible();
  expect(api.putDates).toEqual(['2026-10-12', '2026-10-12']);

  await page.getByRole('link', { name: '返回栽培歷史' }).click();
  await expect(page).toHaveURL(historyUrl);
  await expect(fields).toHaveValue(north);
  await expect(crops).toHaveValue('番茄');
  await expect(table.getByRole('row').nth(1).getByRole('cell').nth(3)).toHaveText('2026-10-12');
  expect(api.historyQueries.length).toBe(3);
});

test('修正結束日處理載入範圍失敗、下限變更衝突與儲存失敗重試', async ({ page }) => {
  const api = await setupCorrectionMock(page);
  await page.clock.setFixedTime(new Date('2026-11-18T06:00:00Z'));
  api.setFailRangeOnce(true);

  await page.goto(`${historyUrl}/${north}/${plantingId}`);
  await page.getByRole('button', { name: '修正', exact: true }).click();

  const dialog = page.getByRole('dialog', { name: '修正結束日' });
  const dateInput = dialog.getByLabel(/結束日/);
  const save = dialog.getByRole('button', { name: '儲存', exact: true });
  await expect(dateInput).toBeDisabled();
  await expect(dateInput).toHaveValue('2026-10-20');
  await expect(save).toBeDisabled();
  await expect(dialog.getByText(/可填範圍：/)).toBeHidden();
  await expect(dialog.getByRole('alert')).toHaveText('無法載入可填範圍，請再試一次。');
  await dialog.getByRole('button', { name: '重新載入', exact: true }).click();

  await expect(dateInput).toBeEnabled();
  await expect(save).toBeEnabled();
  await expect(dialog).toContainText('可填範圍：2026/10/08（最後一筆採收）至 2026/11/18（今天）');

  api.setNextPutStatus(409);
  await dateInput.fill('2026-10-12');
  await save.click();
  await expect(dialog).toBeVisible();
  await expect(dateInput).toHaveValue('2026-10-12');
  await expect(dateInput).toHaveAttribute('min', '2026-10-15');
  await expect(dialog).toContainText('可填範圍：2026/10/15（最後一筆灌溉）至 2026/11/18（今天）');
  await expect(dialog.getByRole('alert')).toHaveText('已有 2026/10/15 的灌溉紀錄，結束日不得早於這天。');

  api.setNextPutStatus(500);
  await dateInput.fill('2026-10-16');
  await save.click();
  await expect(dialog).toBeVisible();
  await expect(dateInput).toHaveValue('2026-10-16');
  await expect(dialog.getByRole('alert')).toHaveText('無法儲存結束日，請再試一次。');

  await save.click();
  await expect(dialog).toBeHidden();
  await expect(page.getByRole('region', { name: '栽培紀錄資料' })).toContainText('2026-10-16');
  await expect(page.getByText('結束日已更新為 2026/10/16。')).toBeVisible();
});
