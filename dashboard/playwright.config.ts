import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: './e2e',
  use: {
    baseURL: 'http://localhost:4200',
    channel: process.env['CROPPILOT_E2E_BROWSER'],
    trace: process.env['CI'] ? 'retain-on-failure' : 'off',
    screenshot: 'only-on-failure',
  },
  webServer: {
    command: 'npm start',
    url: 'http://localhost:4200',
    reuseExistingServer: true,
  },
});
