import { randomUUID } from 'node:crypto';
import { expect, Page, test } from '@playwright/test';

const fieldsUrl = '**/api/v1/fields';

async function signIn(page: Page): Promise<void> {
  const username = process.env['CROPPILOT_E2E_USERNAME'];
  const password = process.env['CROPPILOT_E2E_PASSWORD'];
  if (!username || !password) {
    throw new Error(
      '請設定 CROPPILOT_E2E_USERNAME 和 CROPPILOT_E2E_PASSWORD。',
    );
  }

  await page.goto('/login');
  await page.getByLabel('帳號', { exact: true }).fill(username);
  await page.getByLabel('密碼', { exact: true }).fill(password);
  await page.getByRole('button', { name: '登入', exact: true }).click();
  await expect(
    page.getByRole('heading', { name: '田區總覽', exact: true }),
  ).toBeVisible({ timeout: 15_000 });
}

test('未登入直接開啟田區頁面時轉至登入', async ({ page }) => {
  for (const path of [
    '/dashboard',
    '/dashboard/fields',
    '/dashboard/fields/new',
    `/dashboard/fields/${randomUUID()}/edit`,
    '/dashboard/plantings',
    `/dashboard/plantings/${randomUUID()}/new`,
  ]) {
    await page.goto(path);
    await expect(page).toHaveURL('/login');
  }
});

test('田區管理可搜尋名稱並切換名稱和面積的排序', async ({ page }) => {
  let listRequests = 0;
  await page.route('**/api/v1/**', (route) => {
    switch (new URL(route.request().url()).pathname) {
      case '/api/v1/auth/csrf':
      case '/api/v1/auth/session':
        return route.fulfill({ status: 204 });
      case '/api/v1/auth/refresh':
        return route.fulfill({
          json: { accessToken: 'test-token', sessionId: 'test-session' },
        });
      case '/api/v1/fields':
        listRequests++;
        return route.fulfill({
          json: {
            fields: [
              { id: randomUUID(), name: '北側田 B', areaSquareMeters: 80 },
              { id: randomUUID(), name: '南側田', areaSquareMeters: 100 },
              { id: randomUUID(), name: '北側田 A', areaSquareMeters: 1200 },
            ],
          },
        });
      default:
        return route.fulfill({ status: 404 });
    }
  });

  await page.goto('/dashboard/fields');
  const table = page.getByRole('table', { name: '田區管理列表' });
  const rows = table.getByRole('rowheader');
  const nameHeader = table.getByRole('columnheader', { name: '田區名稱' });
  const areaHeader = table.getByRole('columnheader', { name: '面積' });
  const search = page.getByRole('searchbox', { name: '搜尋田區名稱' });

  await expect(nameHeader).toHaveAttribute('aria-sort', 'ascending');
  const ascendingNames = ['北側田 A', '北側田 B', '南側田'];
  await expect(rows).toHaveText(ascendingNames);
  await nameHeader.getByRole('button').click();
  await expect(nameHeader).toHaveAttribute('aria-sort', 'descending');
  await expect(rows).toHaveText(ascendingNames.toReversed());

  await areaHeader.getByRole('button').click();
  await expect(areaHeader).toHaveAttribute('aria-sort', 'ascending');
  await expect(rows).toHaveText(['北側田 B', '南側田', '北側田 A']);
  await search.fill(' 北側田 ');
  await expect(page.getByRole('status')).toHaveText('2 筆結果');
  await expect(rows).toHaveText(['北側田 B', '北側田 A']);
  await areaHeader.getByRole('button').click();
  await expect(areaHeader).toHaveAttribute('aria-sort', 'descending');
  await expect(rows).toHaveText(['北側田 A', '北側田 B']);

  await search.fill('東側田');
  await expect(table.getByText('找不到符合的田區。')).toBeVisible();
  await page.getByRole('button', { name: '清除搜尋' }).click();
  await expect(search).toBeFocused();
  await expect(rows).toHaveText(['北側田 A', '南側田', '北側田 B']);
  expect(listRequests).toBe(1);
});

test('田區可編輯、取消刪除，確認後才刪除', async ({ page }) => {
  const original = `編輯測試 ${randomUUID()}`;
  const updated = `${original} 已更新`;
  await signIn(page);
  await page.getByRole('link', { name: '田區管理', exact: true }).click();
  await page.getByRole('link', { name: '＋ 新增田區' }).click();
  const name = page.getByLabel('田區名稱（必填）');
  const area = page.getByLabel('面積（平方公尺，必填）');
  const notes = page.getByLabel('備註（選填）');
  const save = page.getByRole('button', { name: '儲存田區' });
  await name.fill(original);
  await area.fill('1200.125');
  await notes.fill('舊備註');
  await save.click();

  await page
    .getByRole('link', { name: `編輯田區：${original}`, exact: true })
    .click();
  await expect(name).toHaveValue(original);
  await expect(area).toHaveValue('1200.125');
  await expect(notes).toHaveValue('舊備註');
  await name.fill(updated);
  await area.fill('2000');
  await notes.clear();
  await save.click();
  await expect(
    page.getByRole('status').filter({ hasText: '已更新田區' }),
  ).toContainText(updated);
  const row = page.getByRole('row').filter({ hasText: updated });
  await expect(row).toContainText('2,000 m²');
  await expect(row).not.toContainText('舊備註');
  await page.reload();
  await expect(row).toBeVisible();

  const remove = row.getByRole('button', { name: `刪除田區：${updated}` });
  const dialog = page.getByRole('dialog', { name: '刪除田區' });
  await remove.click();
  await expect(dialog).toContainText(updated);
  await dialog.getByRole('button', { name: '取消' }).click();
  await expect(dialog).toBeHidden();
  await expect(row).toBeVisible();

  // 攔截一次回應以驗證田區編輯流程中的刪除衝突提示。
  await page.route(
    '**/api/v1/fields/*',
    (route) => route.fulfill({ status: 409 }),
    { times: 1 },
  );
  await remove.click();
  await dialog.getByRole('button', { name: '確認刪除' }).click();
  await expect(dialog.getByRole('alert')).toHaveText(
    '有栽培紀錄的田區不能刪除。',
  );
  await dialog.getByRole('button', { name: '取消' }).click();
  await expect(row).toBeVisible();

  await remove.click();
  await dialog.getByRole('button', { name: '確認刪除' }).click();
  await expect(dialog).toBeHidden();
  await expect(
    page.getByRole('status').filter({ hasText: '已刪除田區' }),
  ).toContainText(updated);
  const reloaded = page.waitForResponse(
    (response) =>
      new URL(response.url()).pathname === '/api/v1/fields' && response.ok(),
  );
  await page.reload();
  await reloaded;
  await expect(page.getByText('正在載入田區…')).toBeHidden();
  await expect(row).toHaveCount(0);
});

test('田區列表載入失敗後可重試，空列表提供新增入口', async ({ page }) => {
  await signIn(page);
  const firstResponse = Promise.withResolvers<void>();
  await page.route(
    fieldsUrl,
    async (route) => {
      await firstResponse.promise;
      await route.fulfill({ status: 503 });
    },
    { times: 1 },
  );
  try {
    await page.getByRole('link', { name: '田區管理', exact: true }).click();
    await expect(page.getByText('正在載入田區…')).toBeVisible();
  } finally {
    firstResponse.resolve();
  }
  await expect(page.getByRole('alert')).toHaveText(
    '目前無法載入田區，請再試一次。',
  );

  await page.route(
    fieldsUrl,
    (route) => route.fulfill({ json: { fields: [] } }),
    { times: 1 },
  );
  await page.getByRole('button', { name: '重新載入' }).click();
  await expect(
    page.getByRole('heading', { name: '目前還沒有田區' }),
  ).toBeVisible();
  await page.route(
    '**/api/v1/overview',
    (route) => route.fulfill({ json: [] }),
    { times: 1 },
  );
  await page.getByRole('link', { name: '田區總覽', exact: true }).click();
  await page.getByRole('link', { name: '＋ 新增田區' }).click();
  await expect(page.getByRole('heading', { name: '新增田區' })).toBeVisible();
  await page.getByRole('button', { name: '取消' }).click();
  await expect(page).toHaveURL('/dashboard');
});

test('栽培超額保留輸入，編輯、刪除與結束後更新面積及總覽', async ({
  page,
}) => {
  const fieldName = `栽培測試 ${randomUUID()}`;
  await signIn(page);
  await page.getByRole('link', { name: '田區管理', exact: true }).click();
  await page.getByRole('link', { name: '＋ 新增田區' }).click();
  await page.getByLabel('田區名稱（必填）').fill(fieldName);
  await page.getByLabel('面積（平方公尺，必填）').fill('100');
  await page.getByRole('button', { name: '儲存田區' }).click();
  await expect(
    page.getByRole('row').filter({ hasText: fieldName }),
  ).toBeVisible();
  await page.getByRole('link', { name: '作物栽培管理', exact: true }).click();
  const field = page.getByLabel('田區', { exact: true });
  const add = page.getByRole('link', { name: '新增栽培紀錄', exact: true });
  await expect(field).toHaveValue('');
  await expect(add).toBeHidden();
  await page.route(
    '**/api/v1/fields/*/plantings',
    (route) => route.fulfill({ status: 503 }),
    { times: 1 },
  );
  const fieldId = (await field.selectOption({ label: fieldName }))[0];
  await expect(page.getByRole('alert')).toHaveText('目前無法載入栽培紀錄。');
  await expect(field).toHaveValue(fieldId);
  await page.getByRole('button', { name: '重新載入' }).click();
  await expect(page.getByText('這個田區還沒有栽培紀錄')).toBeVisible();
  await add.click();
  await page.getByRole('button', { name: '取消' }).click();
  await expect(field).toHaveValue(fieldId);
  await expect(page.getByText('這個田區還沒有栽培紀錄')).toBeVisible();
  await add.click();

  const crop = page.getByLabel('作物名稱（必填）');
  const status = page.getByLabel('狀態', { exact: true });
  const save = page.getByRole('button', { name: '儲存', exact: true });
  await expect(status).toHaveValue('planned');
  await crop.fill('番茄');
  const area = page.getByLabel('占用面積（m²，必填）');
  await area.fill('70');
  await status.selectOption('growing');
  await save.click();
  await expect(
    page.getByLabel('實際栽種日（必填）'),
  ).toHaveAccessibleDescription('請填寫實際栽種日。');
  await status.selectOption('harvesting');
  await expect(page.getByLabel('實際栽種日（選填）')).toHaveValue('');
  await page.route(
    '**/api/v1/fields/*/plantings',
    (route) => route.fulfill({ status: 503 }),
    { times: 1 },
  );
  await save.click();
  await expect(page.getByRole('alert')).toHaveText(
    '目前無法儲存栽培紀錄，請再試一次。',
  );
  await expect(crop).toHaveValue('番茄');
  await expect(status).toHaveValue('harvesting');
  await save.click();
  const planting = page.getByRole('row').filter({ hasText: '番茄' });
  await expect(field).toHaveValue(fieldId);
  await expect(planting).toContainText('採收中');
  await expect(planting).toContainText('尚未設定');
  await page.reload();
  await expect(planting).toBeVisible();

  await add.click();
  await crop.fill('小白菜');
  await status.selectOption('growing');
  await page.getByLabel('實際栽種日（必填）').fill('2026-11-01');
  await status.selectOption('planned');
  await area.fill('35');
  await save.click();
  await expect(area).toHaveAccessibleDescription('占用面積超過可分配的 30 m²。');
  await expect(area).toHaveValue('35');
  await expect(crop).toHaveValue('小白菜');
  await area.fill('30');
  await save.click();
  const planned = page.getByRole('row').filter({ hasText: '小白菜' });
  await expect(planned).toContainText('規劃中');
  await expect(planned).toContainText('尚未設定');

  await page.getByRole('link', { name: '田區總覽', exact: true }).click();
  const overview = page.getByRole('row').filter({ hasText: fieldName });
  await expect(overview).toContainText('番茄');
  await expect(overview).toContainText('採收中');
  await expect(overview).toContainText('小白菜');
  await expect(overview).toContainText('規劃中');
  await expect(overview).toContainText('尚未設定');

  await page.goto(`/dashboard/plantings?fieldId=${fieldId}`);
  const remaining = page.locator('dl div').filter({ hasText: '剩餘面積' }).locator('dd');
  await expect(remaining).toHaveText('0 m²');
  await planned.getByRole('link', { name: '編輯 小白菜' }).click();
  await expect(area).toHaveValue('30');
  await page.getByLabel('品種（選填）').fill('示意品種');
  await save.click();
  await expect(planned).toContainText('示意品種');
  const remove = planned.getByRole('button', { name: '刪除 小白菜' });
  const confirmation = page.getByRole('dialog', { name: '刪除栽培紀錄' });
  await remove.click();
  await expect(confirmation).toContainText('小白菜');
  await confirmation.getByRole('button', { name: '取消' }).click();
  await expect(planned).toBeVisible();
  await remove.click();
  await confirmation.getByRole('button', { name: '確認刪除' }).click();
  await expect(planned).toBeHidden();
  await expect(remaining).toHaveText('30 m²');

  await planting.getByRole('link', { name: '編輯 番茄' }).click();
  await status.selectOption('completed');
  await page.getByLabel('實際栽種日（必填）').fill('2026-11-01');
  await save.click();
  await expect(remaining).toHaveText('100 m²');
  await page.getByRole('link', { name: '田區總覽', exact: true }).click();
  await expect(overview).toBeVisible();
  await expect(overview).not.toContainText('番茄');
});

test('新增田區後可重新載入，驗證錯誤和名稱衝突保留輸入', async ({ page }) => {
  const name = `瀏覽器測試 ${randomUUID()}`;
  await signIn(page);
  await page.getByRole('link', { name: '田區管理', exact: true }).click();
  await page.getByRole('link', { name: '＋ 新增田區' }).click();

  const save = page.getByRole('button', { name: '儲存田區' });
  const nameInput = page.getByLabel('田區名稱（必填）');
  const areaInput = page.getByLabel('面積（平方公尺，必填）');
  const latitude = page.getByLabel('緯度', { exact: true });
  const longitude = page.getByLabel('經度', { exact: true });
  const notes = page.getByLabel('備註（選填）');

  await save.click();
  await expect(nameInput).toHaveAccessibleDescription('請輸入田區名稱。');
  await expect(areaInput).toHaveAccessibleDescription('請輸入田區面積。');
  await nameInput.fill(name);
  await areaInput.fill('1200.125');
  await latitude.fill('0');
  await save.click();
  await expect(longitude).toHaveAccessibleDescription('請與緯度一起填寫。');
  await longitude.fill('120.5');
  await notes.fill('田'.repeat(1001));
  await save.click();
  await expect(notes).toHaveAccessibleDescription(/備註最多 1000 個字元/);
  await expect(nameInput).toHaveValue(name);

  await notes.fill('靠近灌溉入口');
  await save.click();
  await expect(page).toHaveURL('/dashboard/fields');
  await expect(
    page.getByRole('status').filter({ hasText: name }),
  ).toBeVisible();
  const row = page.getByRole('row').filter({ hasText: name });
  await expect(row).toContainText('1,200.125 m²');
  await expect(row).toContainText(/0\.000000,\s*120\.500000/);
  await expect(row).toContainText('靠近灌溉入口');
  await page.reload();
  await expect(row).toBeVisible();

  await page.getByRole('link', { name: '＋ 新增田區' }).click();
  await nameInput.fill(` ${name} `);
  await areaInput.fill('10');
  await save.click();
  await expect(nameInput).toHaveAccessibleDescription('此名稱已使用。');
  await expect(nameInput).toHaveValue(` ${name} `);
  await expect(areaInput).toHaveValue('10');

  const secondName = `${name} 只填必填`;
  await nameInput.fill(secondName);
  await save.click();
  await expect(
    page.getByRole('row').filter({ hasText: secondName }),
  ).toContainText('未設定');

  await page.getByRole('link', { name: '田區總覽' }).click();
  await expect(
    page.getByRole('row').filter({ hasText: secondName }),
  ).toBeVisible();
  await page.getByRole('button', { name: '登出' }).click();
  await expect(page).toHaveURL('/login');
  await page.goto('/dashboard/fields');
  await expect(page).toHaveURL('/login');
});
