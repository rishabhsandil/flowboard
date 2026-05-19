import { test, expect } from '@playwright/test';
import { createProject, getToken, API } from './helpers';

// Auth state is loaded from playwright/.auth/user.json (set up by auth.setup.ts).

test.describe('Delete project (settings → general → danger zone)', () => {
  test('Owner sees the danger zone and the type-to-confirm modal gate works', async ({ page }) => {
    const token = await getToken(page);
    const { slug, id } = await createProject(page, token, `Delete-${Date.now()}`);

    await page.goto(`/p/${slug}/settings/general`);

    // Danger zone visible to the owner.
    await expect(page.getByText('// danger zone', { exact: false })).toBeVisible();
    await page.getByRole('button', { name: 'delete project' }).click();

    // Modal opens with the project's name in the heading.
    const heading = page.getByRole('heading', { name: /Delete "Delete-/ });
    await expect(heading).toBeVisible();

    // Confirm button is disabled until the user types "delete".
    const confirm = page.getByRole('button', { name: 'delete project' }).last();
    await expect(confirm).toBeDisabled();

    // Typing the wrong phrase keeps it disabled.
    await page.getByLabel('type delete to confirm').fill('nope');
    await expect(confirm).toBeDisabled();

    // Typing "delete" (case-insensitive, trimmed) enables it.
    await page.getByLabel('type delete to confirm').fill(' Delete ');
    await expect(confirm).toBeEnabled();

    // Confirm: project is deleted, user is redirected to /dashboard.
    await confirm.click();
    await page.waitForURL('**/dashboard');

    // Backend agrees the project is gone.
    const res = await page.request.get(`${API}/projects/${slug}`, {
      headers: { Authorization: `Bearer ${token}` },
    });
    expect(res.status()).toBe(404);

    // Cleanup: id reference avoids unused-var lint.
    expect(typeof id).toBe('string');
  });

  test('Escape closes the delete modal without deleting', async ({ page }) => {
    const token = await getToken(page);
    const { slug } = await createProject(page, token, `Keep-${Date.now()}`);

    await page.goto(`/p/${slug}/settings/general`);
    await page.getByRole('button', { name: 'delete project' }).click();
    await expect(page.getByRole('heading', { name: /Delete "Keep-/ })).toBeVisible();

    await page.keyboard.press('Escape');
    await expect(page.getByRole('heading', { name: /Delete "Keep-/ })).toBeHidden();

    // Project still resolves.
    const res = await page.request.get(`${API}/projects/${slug}`, {
      headers: { Authorization: `Bearer ${token}` },
    });
    expect(res.status()).toBe(200);
  });
});
