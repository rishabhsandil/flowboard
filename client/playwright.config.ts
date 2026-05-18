import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
  testDir: './tests',
  fullyParallel: false,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  workers: 1,
  reporter: 'list',

  use: {
    baseURL: 'http://localhost:5173',
    trace: 'on-first-retry',
    // CSRF header required by the backend middleware
    extraHTTPHeaders: { 'X-Requested-With': 'XMLHttpRequest' },
    actionTimeout: 15_000,
    navigationTimeout: 30_000,
  },

  projects: [
    // 1. Create one shared test account and save auth state to disk.
    {
      name: 'setup',
      testMatch: '**/auth.setup.ts',
    },

    // 2. All e2e specs run with the saved auth state.
    {
      name: 'chromium',
      use: {
        ...devices['Desktop Chrome'],
        storageState: 'playwright/.auth/user.json',
      },
      dependencies: ['setup'],
      testIgnore: '**/auth.setup.ts',
    },
  ],

  // Start the Vite dev server before the tests.
  webServer: {
    command: 'npm run dev -- --host 0.0.0.0',
    url: 'http://localhost:5173',
    reuseExistingServer: true,
    timeout: 30_000,
  },
});
