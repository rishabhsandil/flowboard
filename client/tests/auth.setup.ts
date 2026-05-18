import { test as setup } from '@playwright/test';

const API = 'http://localhost:8080/api';
const STORAGE_FILE = 'playwright/.auth/user.json';

/**
 * Creates (or reuses) a single e2e test account and saves the auth state to
 * disk so all spec files can reuse it without re-authenticating per test.
 * This avoids hitting the registration rate limit (10/5min).
 */
setup('create e2e test account', async ({ page }) => {
  const email    = 'e2e-playwright@test.local';
  const password = 'E2eTestPw123!';
  const name     = 'E2E Test User';

  // Register (best-effort — 409 = already exists, which is fine).
  await page.request.post(`${API}/auth/register`, {
    data: { email, name, password },
    headers: { 'Content-Type': 'application/json', 'X-Requested-With': 'XMLHttpRequest' },
  });

  // Log in via the UI so browser storage (localStorage auth store) is populated.
  await page.goto('/login');
  const emailInput    = page.locator('input[type="email"]');
  const passwordInput = page.locator('input[type="password"]');
  await emailInput.waitFor({ state: 'visible' });
  await emailInput.fill(email);
  await passwordInput.waitFor({ state: 'visible' });
  await passwordInput.fill(password);
  await page.locator('button').first().click();
  await page.waitForURL('**/dashboard', { timeout: 15_000 });

  await page.context().storageState({ path: STORAGE_FILE });
});
