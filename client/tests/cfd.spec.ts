import { test, expect } from '@playwright/test';
import { getToken, createProject, createIssue } from './helpers';

// Auth state is loaded from playwright/.auth/user.json (set up by auth.setup.ts).

test.describe('Cumulative Flow Diagram page', () => {
  test('CFD page shows heading and loading resolves', async ({ page }) => {
    const token = await getToken(page);
    const { slug, id: projectId } = await createProject(page, token, `CFD-${Date.now()}`);
    await createIssue(page, token, projectId, 'An Issue');

    await page.goto(`/p/${slug}/reports/cfd`);
    await expect(page.getByText('Cumulative Flow', { exact: false })).toBeVisible();

    // Wait for the CFD API call to complete
    await page.waitForResponse((r) => r.url().includes('/reports/cfd') && r.status() === 200);

    // After snapshot is taken, loading disappears and either chart or empty state appears.
    await expect(page.getByText('loading…')).not.toBeVisible({ timeout: 5000 });
    const hasChart = await page.locator('.recharts-wrapper').count() > 0;
    const hasEmpty = await page.getByText('no data yet').count() > 0;
    expect(hasChart || hasEmpty).toBeTruthy();
  });

  test('CFD page shows chart after visiting (auto-snapshot taken)', async ({ page }) => {
    const token = await getToken(page);
    const { slug, id: projectId } = await createProject(page, token, `CFD2-${Date.now()}`);
    await createIssue(page, token, projectId, 'An Issue');

    await page.goto(`/p/${slug}/reports/cfd`);
    // Wait for the API call to complete — snapshot is taken server-side on visit
    await page.waitForResponse((r) => r.url().includes('/reports/cfd') && r.status() === 200);

    const chart = page.locator('.recharts-wrapper');
    await expect(chart).toBeVisible({ timeout: 8000 });
  });

  test('Day range selector is present with all options', async ({ page }) => {
    const token = await getToken(page);
    const { slug } = await createProject(page, token, `CFD3-${Date.now()}`);

    await page.goto(`/p/${slug}/reports/cfd`);
    const select = page.locator('select');
    await expect(select).toBeVisible();

    const options = await select.locator('option').allTextContents();
    expect(options.some((o) => o.includes('30'))).toBeTruthy();
    expect(options.length).toBeGreaterThanOrEqual(3);
  });
});
