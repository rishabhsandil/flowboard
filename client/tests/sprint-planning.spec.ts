import { test, expect } from '@playwright/test';
import { getToken, createProject, createIssue, API } from './helpers';

// Auth state is loaded from playwright/.auth/user.json (set up by auth.setup.ts).

test.describe('Sprint planning page', () => {
  test('Plan page loads with header and sprint selector', async ({ page }) => {
    const token = await getToken(page);
    const { slug } = await createProject(page, token, `Plan-${Date.now()}`);

    await page.goto(`/p/${slug}/plan`);
    await expect(page.getByText('// sprint planning', { exact: false })).toBeVisible();
    // Sprint selector is always visible
    await expect(page.locator('select')).toBeVisible();
  });

  test('No sprint selected shows prompt', async ({ page }) => {
    const token = await getToken(page);
    const { slug } = await createProject(page, token, `Plan2-${Date.now()}`);

    await page.goto(`/p/${slug}/plan`);
    await expect(page.getByText('select a sprint to start planning')).toBeVisible();
  });

  test('Selecting a sprint shows backlog and sprint columns', async ({ page }) => {
    const token = await getToken(page);
    const { slug, id: projectId } = await createProject(page, token, `Plan3-${Date.now()}`);
    await createIssue(page, token, projectId, 'My Backlog Card');

    // Create a sprint
    const sprintRes = await page.request.post(`${API}/projects/${projectId}/sprints`, {
      data: { name: 'S1', startDate: '2026-06-01', endDate: '2026-06-14' },
      headers: {
        Authorization: `Bearer ${token}`,
        'Content-Type': 'application/json',
        'X-Requested-With': 'XMLHttpRequest',
      },
    });
    expect(sprintRes.ok()).toBeTruthy();

    await page.goto(`/p/${slug}/plan`);
    // Select the sprint — Playwright selectOption with text filter
    await page.locator('select').selectOption({ label: 'S1 (planned)' });

    // Both columns are now visible
    await expect(page.getByText('// backlog', { exact: false })).toBeVisible();
    await expect(page.getByText('My Backlog Card')).toBeVisible();
  });
});
