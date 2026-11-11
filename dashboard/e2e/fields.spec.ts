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
  ).toBeVisible();
}

test('未登入直接開啟田區頁面時轉至登入', async ({ page }) => {
  for (const path of [
    '/dashboard',
    '/dashboard/fields',
    '/dashboard/fields/new',
  ]) {
    await page.goto(path);
    await expect(page).toHaveURL('/login');
  }
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
    fieldsUrl,
    (route) => route.fulfill({ json: { fields: [] } }),
    { times: 1 },
  );
  await page.getByRole('link', { name: '田區總覽', exact: true }).click();
  await page.getByRole('link', { name: '＋ 新增田區' }).click();
  await expect(page.getByRole('heading', { name: '新增田區' })).toBeVisible();
  await page.getByRole('button', { name: '取消' }).click();
  await expect(page).toHaveURL('/dashboard');
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
