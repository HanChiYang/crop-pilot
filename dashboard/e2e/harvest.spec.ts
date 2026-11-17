import { expect, type Page, test } from '@playwright/test';
import type { HarvestRsDto } from '../src/app/generated/model/harvestRsDto';
import type { PlantingRsDto } from '../src/app/generated/model/plantingRsDto';
import type { SaveHarvestRqDto } from '../src/app/generated/model/saveHarvestRqDto';

const field = { id: 'ad31b935-6b1c-4669-8c92-fb1c8489900f', name: '北側田', areaSquareMeters: 1000 };
const plantingId = '9d2cf07b-d8d2-4308-9d36-f0ce10504731';
const plantingRoute = '/api/v1/plantings/' + plantingId;
const harvestsRoute = plantingRoute + '/harvests';
const fieldsRoute = '/api/v1/fields/' + field.id;
const managementPage = '/dashboard/plantings';
const historyPage = '/dashboard/planting-history';
const first = { id: 'harvest-1', plantingId, harvestDate: '2026-11-14', weightKg: 40, description: '第一次採收' };
const second = { id: 'harvest-2', plantingId, harvestDate: '2026-11-17', weightKg: 60, description: '第二次採收' };

async function mockApi(page: Page, status: PlantingRsDto['status'], initial: HarvestRsDto[] = []) {
  const record = {
    id: plantingId, fieldId: field.id, cropName: '番茄', variety: '黑柿',
    areaSquareMeters: 300, actualPlantingDate: '2026-09-18', status,
  };
  let harvests = [...initial];
  const queries: URLSearchParams[] = [];
  const writes: { method: string; path: string; body?: unknown }[] = [];
  const faults = { load: false, edit: false };
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
        return route.fulfill({ json: { fields: [field] } });
      case '/api/v1/overview':
        return route.fulfill({ json: [{ ...field, plantings: [record] }] });
      case '/api/v1/planting-history/crops':
        return route.fulfill({ json: ['番茄'] });
      case '/api/v1/planting-history':
        return route.fulfill({ json: [{ ...record, fieldName: field.name }] });
      case fieldsRoute:
        return route.fulfill({ json: field });
      case plantingRoute:
        return route.fulfill({ json: record });
      case harvestsRoute:
        if (method === 'POST') {
          const body = request.postDataJSON() as SaveHarvestRqDto;
          writes.push({ method, path: pathname, body });
          const created = { ...body, id: 'harvest-new', plantingId, description: body.description ?? null };
          harvests = [...harvests, created];
          return route.fulfill({ status: 201, json: created });
        }
        queries.push(searchParams);
        if (faults.load) {
          faults.load = false;
          return route.fulfill({ status: 500 });
        }
        const from = searchParams.get('startDate');
        const to = searchParams.get('endDate');
        const ascending = searchParams.get('sortDirection') === 'asc';
        const byWeight = searchParams.get('sortBy') === 'weightKg';
        return route.fulfill({ json: {
          totalWeightKg: harvests.reduce((sum, row) => sum + row.weightKg, 0),
          harvests: harvests
            .filter(row => (!from || row.harvestDate >= from) && (!to || row.harvestDate <= to))
            .sort((a, b) => (ascending ? 1 : -1) *
              (byWeight ? a.weightKg - b.weightKg : a.harvestDate.localeCompare(b.harvestDate))),
        } });
    }

    const selected = harvests.find(row => pathname === '/api/v1/harvests/' + row.id);
    if (selected && method === 'PUT') {
      const body = request.postDataJSON() as SaveHarvestRqDto;
      writes.push({ method, path: pathname, body });
      if (faults.edit) {
        faults.edit = false;
        return route.fulfill({ status: 500 });
      }
      const updated = { ...selected, ...body };
      harvests = harvests.map(row => row.id === selected.id ? updated : row);
      return route.fulfill({ status: 200, json: updated });
    }
    if (selected && method === 'DELETE') {
      writes.push({ method, path: pathname });
      harvests = harvests.filter(row => row.id !== selected.id);
      return route.fulfill({ status: 204 });
    }
    return route.fulfill({ status: 404 });
  });
  return { queries, writes, faults };
}

test('作物栽培列選單可進詳情，使用 API 排序、篩選及新增採收', async ({ page }) => {
  const { queries, writes, faults } = await mockApi(page, 'harvesting', [first, second]);
  faults.load = true;
  await page.clock.setFixedTime(new Date('2026-11-17T06:00:00Z'));
  await page.goto(managementPage + '?fieldId=' + field.id);
  await page.getByRole('button', { name: '操作 番茄（北側田）' }).click();
  await page.getByRole('link', { name: '查看詳情' }).click();
  await expect(page.getByRole('heading', { name: '番茄' })).toBeVisible();
  await expect(page.getByText('無法載入採收紀錄，請再試一次。')).toBeVisible();
  await page.getByRole('button', { name: '重新載入' }).click();

  const list = page.getByRole('table', { name: '採收紀錄清單' });
  const rows = list.locator('tbody tr');
  await expect(rows.first()).toContainText('60 公斤');
  await expect(list.getByRole('columnheader', { name: '採收日期' })).toHaveAttribute('aria-sort', 'descending');
  await list.getByRole('button', { name: '採收重量' }).click();
  await expect(rows.first()).toContainText('40 公斤');
  expect(queries.at(-1)?.get('sortBy')).toBe('weightKg');
  expect(queries.at(-1)?.get('sortDirection')).toBe('asc');

  await page.getByLabel('開始日期', { exact: true }).fill('2026-11-17');
  await page.getByLabel('結束日期', { exact: true }).fill('2026-11-17');
  await page.getByRole('button', { name: '套用篩選' }).click();
  await expect(rows).toHaveCount(1);
  await expect(list).toContainText('60 公斤');
  await expect(page.getByText('100 公斤', { exact: true })).toBeVisible();
  expect(queries.at(-1)?.get('startDate')).toBe('2026-11-17');
  expect(queries.at(-1)?.get('endDate')).toBe('2026-11-17');

  const tabs = page.getByRole('tablist', { name: '栽培詳情' });
  const harvestTab = tabs.getByRole('tab', { name: '採收紀錄' });
  await expect(tabs.getByRole('tab')).toHaveText(['採收紀錄', '販售紀錄', '成本紀錄', '累計損益']);
  await expect(harvestTab).toHaveAttribute('aria-selected', 'true');
  await tabs.getByRole('tab', { name: '販售紀錄' }).click();
  await expect(page.getByRole('tabpanel', { name: '販售紀錄' })).toHaveText('販售紀錄開發中。');
  await expect(list).toBeHidden();
  await tabs.getByRole('tab', { name: '販售紀錄' }).press('ArrowRight');
  const costsTab = tabs.getByRole('tab', { name: '成本紀錄' });
  await expect(costsTab).toBeFocused();
  await expect(page.getByRole('tabpanel', { name: '成本紀錄' })).toHaveText('成本紀錄開發中。');
  await costsTab.press('End');
  await expect(page.getByRole('tabpanel', { name: '累計損益' })).toHaveText('累計損益開發中。');
  await tabs.getByRole('tab', { name: '累計損益' }).press('Home');
  await expect(harvestTab).toBeFocused();
  await expect(list).toContainText('60 公斤');
  await expect(list).not.toContainText('40 公斤');
  await expect(page.getByLabel('開始日期', { exact: true })).toHaveValue('2026-11-17');

  await page.getByRole('button', { name: '新增採收', exact: true }).click();
  const editor = page.getByRole('dialog', { name: '新增採收' });
  await expect(editor.getByLabel('採收日期')).toHaveValue('2026-11-17');
  await editor.getByLabel('採收重量（公斤）').fill('0.0000001');
  await editor.getByRole('button', { name: '儲存採收' }).click();
  await expect(editor.getByRole('alert')).toContainText('最多六位小數');
  expect(writes).toHaveLength(0);
  await editor.getByLabel('採收重量（公斤）').fill('2.125');
  await editor.getByLabel('備註（選填）').fill('補登');
  await editor.getByRole('button', { name: '儲存採收' }).click();
  await expect(list).toContainText('補登');
  await expect(page.getByText('102.125 公斤', { exact: true })).toBeVisible();
  expect(writes).toEqual([{
    method: 'POST', path: harvestsRoute,
    body: { harvestDate: '2026-11-17', weightKg: 2.125, description: '補登' },
  }]);
  expect(queries.at(-1)?.get('sortBy')).toBe('weightKg');
  expect(queries.at(-1)?.get('startDate')).toBe('2026-11-17');

  await page.getByRole('link', { name: '返回作物栽培' }).click();
  await expect(page).toHaveURL(managementPage + '?fieldId=' + field.id);
});

test('已結束批次可修改與刪除採收，失敗時保留輸入並可重試', async ({ page }) => {
  const { faults, writes } = await mockApi(page, 'completed', [first, second]);
  await page.clock.setFixedTime(new Date('2026-11-17T06:00:00Z'));
  await page.goto(historyPage);
  await page.getByRole('button', { name: '查詢', exact: true }).click();
  await page.getByRole('link', { name: /查看詳情/ }).click();
  await expect(page.getByRole('region', { name: '栽培紀錄資料' })).toContainText('已結束');
  await expect(page.getByRole('tab', { name: '採收紀錄', selected: true })).toBeVisible();
  await expect(page.getByRole('tab', { name: '累計損益' })).toBeVisible();
  const list = page.getByRole('table', { name: '採收紀錄清單' });
  const row = list.getByRole('row').filter({ hasText: '第二次採收' });
  await row.getByRole('button', { name: /操作/ }).click();
  await row.getByRole('button', { name: '編輯' }).click();
  const editor = page.getByRole('dialog', { name: '編輯採收' });
  await expect(editor.getByLabel('採收重量（公斤）')).toHaveValue('60');
  await expect(editor.getByLabel('備註（選填）')).toHaveValue('第二次採收');
  await editor.getByLabel('採收重量（公斤）').fill('50');
  await editor.getByLabel('備註（選填）').clear();
  faults.edit = true;
  await editor.getByRole('button', { name: '儲存變更' }).click();
  await expect(editor.getByRole('alert')).toContainText('無法儲存');
  await expect(editor.getByLabel('採收重量（公斤）')).toHaveValue('50');
  await editor.getByRole('button', { name: '儲存變更' }).click();
  await expect(page.getByText('90 公斤', { exact: true })).toBeVisible();

  const updated = list.getByRole('row').filter({ hasText: '50 公斤' });
  const openDelete = async () => {
    await updated.getByRole('button', { name: /操作/ }).click();
    await updated.getByRole('button', { name: '刪除' }).click();
  };
  await openDelete();
  const confirmation = page.getByRole('dialog', { name: '刪除採收紀錄？' });
  await expect(confirmation).toContainText('90 公斤調整為 40 公斤');
  await confirmation.getByRole('button', { name: '取消' }).click();
  await expect(updated).toBeVisible();
  await openDelete();
  await confirmation.getByRole('button', { name: '確認刪除' }).click();
  await expect(updated).toBeHidden();
  await expect(page.getByText('40 公斤', { exact: true })).toHaveCount(2);
  expect(writes.slice(1)).toEqual([
    { method: 'PUT', path: '/api/v1/harvests/harvest-2',
      body: { harvestDate: '2026-11-17', weightKg: 50, description: null } },
    { method: 'DELETE', path: '/api/v1/harvests/harvest-2' },
  ]);
});

test('規劃中的栽培可查採收清單', async ({ page }) => {
  await mockApi(page, 'planned');
  await page.goto(managementPage + '/' + field.id + '/' + plantingId);
  await expect(page.getByText('規劃中的栽培無法登記採收。')).toBeVisible();
  await expect(page.getByText('尚無採收紀錄。')).toBeVisible();
  await expect(page.getByRole('button', { name: '新增採收', exact: true })).toBeHidden();
});

test('有採收的栽培拒絕整批刪除並顯示原因', async ({ page }) => {
  await mockApi(page, 'harvesting', [first]);
  await page.route('**' + plantingRoute + '/delete-preview', route => route.fulfill({
    status: 409,
    json: { status: 409, detail: '這批栽培已有採收紀錄，無法整批刪除。' },
  }));
  await page.goto(managementPage);
  await page.getByRole('button', { name: '操作 番茄（北側田）' }).click();
  await page.getByRole('button', { name: '刪除 番茄' }).click();
  const dialog = page.getByRole('dialog', { name: '刪除栽培紀錄？' });
  await expect(dialog.getByRole('alert')).toHaveText('這批栽培已有採收紀錄，無法整批刪除。');
  await expect(dialog.getByRole('button', { name: '刪除栽培紀錄', exact: true })).toBeDisabled();
  await dialog.getByRole('button', { name: '取消' }).click();
  await expect(page.getByRole('rowheader', { name: '番茄' })).toBeVisible();
});
