import { expect, Page, test } from '@playwright/test';
import type { IrrigationRsDto } from '../src/app/generated/model/irrigationRsDto';

const fieldId = 'ad31b935-6b1c-4669-8c92-fb1c8489900f';
const plantingId = '9d2cf07b-d8d2-4308-9d36-f0ce10504731';
const irrigationId = '9d2cf07b-d8d2-4308-9d36-f0ce10504735';
const field = { id: fieldId, name: '北側田', areaSquareMeters: 1000 };
const planting = { id: plantingId, fieldId, cropName: '番茄', areaSquareMeters: 240 };
const fieldApi = '/api/v1/fields/' + fieldId;
const collectionApi = '/api/v1/plantings/' + plantingId + '/irrigations';
const itemApi = '/api/v1/irrigations/' + irrigationId;
const historyPath = '/dashboard/planting-history/' + fieldId + '/' + plantingId;
const managementPath = '/dashboard/plantings?fieldId=' + fieldId;

async function mockApi(page: Page, status: 'completed' | 'growing' | 'planned') {
  const record = { ...planting, status };
  const writes: { method: string; path: string; body: unknown }[] = [];
  let operation: IrrigationRsDto | null = null;
  await page.route('**/api/v1/**', async route => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    const method = request.method();
    switch (path) {
      case '/api/v1/auth/csrf':
      case '/api/v1/auth/session':
        return route.fulfill({ status: 204 });
      case '/api/v1/auth/refresh':
        return route.fulfill({ json: { accessToken: 'test-token', sessionId: 'test-session' } });
      case '/api/v1/fields':
        return route.fulfill({ json: { fields: [field] } });
      case '/api/v1/overview':
        return route.fulfill({ json: [{ ...field, plantings: [record] }] });
      case fieldApi:
        return route.fulfill({ json: field });
      case fieldApi + '/plantings':
        return route.fulfill({ json: {
          areaSquareMeters: 1000, allocatedAreaSquareMeters: 240, remainingAreaSquareMeters: 760,
          plantings: [record],
        } });
      case '/api/v1/plantings/' + plantingId + '/harvests':
        return route.fulfill({ json: { totalWeightKg: 0, harvests: [] } });
      case '/api/v1/plantings/' + plantingId:
        return route.fulfill({ json: record });
      case '/api/v1/planting-history/crops':
        return route.fulfill({ json: ['番茄'] });
      case collectionApi:
        if (method === 'GET') return route.fulfill({ json: operation ? [operation] : [] });
        break;
      case itemApi:
        if (method === 'DELETE') {
          writes.push({ method, path, body: null });
          operation = null;
          return route.fulfill({ status: 204 });
        }
        break;
    }
    if ((path === collectionApi && method === 'POST') || (path === itemApi && method === 'PUT')) {
      const body = request.postDataJSON() as Omit<IrrigationRsDto, 'id'>;
      writes.push({ method, path, body });
      operation = { ...body, id: irrigationId };
      return route.fulfill({ status: method === 'POST' ? 201 : 200, json: operation });
    }
    return route.fulfill({ status: 404 });
  });
  return writes;
}

test('從栽培歷史補登灌溉、清空用水量並刪除紀錄', async ({ page }) => {
  const writes = await mockApi(page, 'completed');
  await page.clock.setFixedTime(new Date('2026-01-03T15:59:00Z'));
  await page.goto(historyPath);
  await page.getByRole('link', { name: '灌溉紀錄' }).click();
  await expect(page.getByText('尚無灌溉紀錄')).toBeVisible();
  await page.getByRole('button', { name: '新增灌溉' }).click();

  const date = page.getByLabel('灌溉日期');
  const method = page.getByLabel('灌溉方式');
  const volume = page.getByLabel('用水量（公升，選填）');
  const description = page.getByLabel('備註（選填）');
  const save = page.getByRole('button', { name: '儲存', exact: true });
  await expect(date).toHaveAttribute('max', '2026-01-03');
  await page.clock.setFixedTime(new Date('2026-01-03T16:01:00Z'));
  await date.focus();
  await expect(date).toHaveAttribute('max', '2026-01-04');
  await date.fill('2026-01-03');
  await method.selectOption('drip');
  await description.fill('北側畦');
  await volume.fill('0.0000001');
  await save.click();
  await expect(page.getByRole('alert')).toContainText('最多六位小數');
  expect(writes).toHaveLength(0);
  await volume.fill('0.000001');
  await save.click();

  const list = page.getByRole('table', { name: '灌溉紀錄清單' });
  await expect(list).toContainText('0.000001 公升');
  await expect(list).toContainText('滴灌');
  await expect(list).toContainText('北側畦');

  await list.getByRole('button', { name: /操作.*灌溉紀錄/ }).click();
  await list.getByRole('button', { name: '編輯' }).click();
  await expect(date).toHaveValue('2026-01-03');
  await expect(volume).toHaveValue('0.000001');
  await volume.clear();
  await method.selectOption('manual');
  await save.click();
  await expect(list).toContainText('未記錄');
  await expect(list).toContainText('人工澆灌');

  await list.getByRole('button', { name: /操作.*灌溉紀錄/ }).click();
  await list.getByRole('button', { name: '刪除' }).click();
  const dialog = page.getByRole('dialog', { name: '刪除灌溉紀錄？' });
  await dialog.getByRole('button', { name: '取消' }).click();
  await expect(list).toBeVisible();
  await list.getByRole('button', { name: /操作.*灌溉紀錄/ }).click();
  await list.getByRole('button', { name: '刪除' }).click();
  await dialog.getByRole('button', { name: '確認刪除' }).click();
  await expect(page.getByText('尚無灌溉紀錄')).toBeVisible();

  const details = { operationDate: '2026-01-03', description: '北側畦' };
  expect(writes).toEqual([
    { method: 'POST', path: collectionApi, body: { ...details, method: 'drip', waterVolumeLiters: 0.000001 } },
    { method: 'PUT', path: itemApi, body: { ...details, method: 'manual', waterVolumeLiters: null } },
    { method: 'DELETE', path: itemApi, body: null },
  ]);
  await page.getByRole('link', { name: '返回栽培詳情' }).click();
  await expect(page).toHaveURL(historyPath);
});

test('栽培中的列操作可開啟灌溉清單並返回原田區', async ({ page }) => {
    await mockApi(page, 'growing');
    await page.goto(managementPath);
    await page.getByRole('button', { name: '操作 番茄（北側田）' }).click();
    const row = page.getByRole('row').filter({ has: page.getByRole('rowheader', { name: '番茄' }) });
    await expect(row.getByRole('link')).toHaveText(['查看詳情', '編輯', '資材施用', '灌溉紀錄']);
    await page.getByRole('link', { name: '灌溉紀錄' }).click();
    await expect(page.getByRole('heading', { name: '灌溉紀錄' })).toBeVisible();
    const add = page.getByRole('button', { name: '新增灌溉' });
    await expect(add).toBeVisible();
    await page.getByRole('link', { name: '返回作物栽培管理' }).click();
    await expect(page).toHaveURL(managementPath);
    await expect(page.getByLabel('田區', { exact: true })).toHaveValue(fieldId);
});

test('規劃中可查看詳情、操作列選單及新增灌溉', async ({ page }) => {
  const writes = await mockApi(page, 'planned');
  await page.goto(managementPath);
  await page.getByRole('button', { name: '操作 番茄（北側田）' }).click();
  const row = page.getByRole('row').filter({ has: page.getByRole('rowheader', { name: '番茄' }) });
  await expect(row.getByRole('link')).toHaveText(['查看詳情', '編輯', '資材施用', '灌溉紀錄']);
  await expect(row.getByRole('button', { name: '刪除 番茄' })).toBeVisible();
  await row.getByRole('link', { name: '灌溉紀錄' }).click();
  await expect(page.getByText('北側田・番茄・規劃中')).toBeVisible();
  await page.getByRole('button', { name: '新增灌溉' }).click();
  await page.getByLabel('灌溉日期').fill('2026-01-03');
  await page.getByLabel('灌溉方式').selectOption('drip');
  await page.getByRole('button', { name: '儲存', exact: true }).click();
  await expect(page.getByRole('table', { name: '灌溉紀錄清單' })).toContainText('滴灌');
  expect(writes).toHaveLength(1);
});

test('栽培刪除摘要變更時顯示新影響，重新確認後才刪除', async ({ page }) => {
  await mockApi(page, 'growing');
  const plantingApi = '/api/v1/plantings/' + plantingId;
  const firstVersion = 'A'.repeat(64);
  const secondVersion = 'B'.repeat(64);
  let currentVersion = firstVersion;
  let deleted = false;
  await page.route('**/api/v1/overview', route => route.fulfill({
    json: [{ ...field, plantings: deleted ? [] : [{ ...planting, status: 'growing' }] }],
  }));
  await page.route('**' + plantingApi + '/delete-preview', route => route.fulfill({ json: {
    plantingId, fieldName: field.name, cropName: planting.cropName, status: 'growing',
    releasedAreaSquareMeters: 240, materialApplicationCount: 1,
    irrigationCount: currentVersion === firstVersion ? 0 : 1,
    inventoryReturns: [{ lotId: 'batch', materialName: '有機肥', source: 'openingStock',
      availableFrom: '2026-01-01', quantity: 2, unit: '公斤' }],
    previewVersion: currentVersion,
  } }));
  await page.route(url => url.pathname === plantingApi, async route => {
    const confirmed = new URL(route.request().url()).searchParams.get('previewVersion');
    if (currentVersion === firstVersion) {
      expect(confirmed).toBe(firstVersion);
      currentVersion = secondVersion;
      return route.fulfill({ status: 409 });
    }
    expect(confirmed).toBe(secondVersion);
    deleted = true;
    return route.fulfill({ status: 204 });
  });
  await page.goto(managementPath);
  await page.getByRole('button', { name: '操作 番茄（北側田）' }).click();
  await page.getByRole('button', { name: '刪除 番茄' }).click();
  const dialog = page.getByRole('dialog', { name: '刪除栽培紀錄？' });
  await expect(dialog).toContainText('有機肥');
  await expect(dialog).toContainText('2 公斤');
  await expect(dialog).toContainText('灌溉紀錄 0 筆');
  const confirm = dialog.getByRole('button', { name: '刪除栽培及作業紀錄' });
  await confirm.click();
  await expect(dialog.getByRole('alert')).toContainText('請重新確認');
  await expect(dialog).toContainText('灌溉紀錄 1 筆');
  expect(deleted).toBe(false);
  await confirm.click();
  await expect(dialog).not.toBeVisible();
  await expect(page.getByText('已刪除栽培紀錄')).toBeVisible();
});
