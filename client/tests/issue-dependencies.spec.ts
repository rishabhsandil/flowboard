import { test, expect, type Page } from '@playwright/test';
import { getToken, createProject, createIssue, API } from './helpers';

// Auth state is loaded from playwright/.auth/user.json (set up by auth.setup.ts).

test.describe('Issue dependencies panel', () => {
  test('Add a "blocks" link via the Jira-style picker', async ({ page }) => {
    const token = await getToken(page);
    const { slug, id: projectId } = await createProject(page, token, `Deps-${Date.now()}`);
    await createIssue(page, token, projectId, 'Source-card-deps');
    await createIssue(page, token, projectId, 'Target-card-deps');

    await page.goto(`/p/${slug}/board`);
    await page.getByText('Source-card-deps').click();

    await page.getByRole('button', { name: /add link/i }).click();
    const picker = page.getByTestId('dep-picker');
    await picker.getByTestId('dep-link-type').selectOption('blocks');
    await picker.getByPlaceholder('search issues in this project…').fill('Target');
    await picker.getByRole('button', { name: /Target-card-deps/ }).click();

    // Chip lands in the flat list with the "blocks" label.
    const list = page.getByTestId('dep-list');
    await expect(list.getByText('Target-card-deps')).toBeVisible();
    await expect(list.getByText('blocks', { exact: true })).toBeVisible();
  });

  test('Add an "is blocked by" link reverses the edge direction', async ({ page }) => {
    const token = await getToken(page);
    const { slug, id: projectId } = await createProject(page, token, `DepsRev-${Date.now()}`);
    await createIssue(page, token, projectId, 'Focus-card');
    await createIssue(page, token, projectId, 'Blocker-card');

    await page.goto(`/p/${slug}/board`);
    await page.getByText('Focus-card').click();

    await page.getByRole('button', { name: /add link/i }).click();
    const picker = page.getByTestId('dep-picker');
    await picker.getByTestId('dep-link-type').selectOption('is_blocked_by');
    await picker.getByPlaceholder('search issues in this project…').fill('Blocker');
    await picker.getByRole('button', { name: /Blocker-card/ }).click();

    const list = page.getByTestId('dep-list');
    await expect(list.getByText('Blocker-card')).toBeVisible();
    await expect(list.getByText('is blocked by', { exact: true })).toBeVisible();
  });

  test('Removing a link clears it from the list', async ({ page }) => {
    const token = await getToken(page);
    const { slug, id: projectId } = await createProject(page, token, `DepsDel-${Date.now()}`);
    const sourceId = await createIssue(page, token, projectId, 'has-link-card');
    const targetId = await createIssue(page, token, projectId, 'gets-unlinked-card');

    const res = await preSeedDependency(page, token, sourceId, targetId);
    expect(res.ok()).toBeTruthy();

    await page.goto(`/p/${slug}/board`);
    await page.getByText('has-link-card').click();

    const list = page.getByTestId('dep-list');
    await expect(list.getByText('gets-unlinked-card')).toBeVisible();
    await list.getByRole('button', { name: /Remove gets-unlinked-card/ }).click();
    await expect(list.getByText('gets-unlinked-card')).toHaveCount(0);
  });
});

function preSeedDependency(page: Page, token: string, sourceId: string, targetId: string) {
  return page.request.post(`${API}/issues/${sourceId}/dependencies`, {
    data: { dependsOnId: targetId, kind: 'blocks' },
    headers: {
      Authorization: `Bearer ${token}`,
      'Content-Type': 'application/json',
      'X-Requested-With': 'XMLHttpRequest',
    },
  });
}
