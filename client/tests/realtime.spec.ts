import { test, expect, type Page } from '@playwright/test';
import { getToken, createProject, createIssue, API } from './helpers';

const AUTH_STATE = 'playwright/.auth/user.json';

test.describe('Real-time board updates', () => {
  test('Issue created in one tab appears on the other tab without reload', async ({ browser }) => {
    // Two independent browser contexts, both authed as the same e2e user.
    // Using two contexts (rather than two pages in one context) ensures the
    // SignalR connections are fully independent — closer to "two people".
    const ctxA = await browser.newContext({ storageState: AUTH_STATE });
    const ctxB = await browser.newContext({ storageState: AUTH_STATE });
    try {
      const pageA = await ctxA.newPage();
      const pageB = await ctxB.newPage();

      const token = await getToken(pageA);
      const { slug, id: projectId } = await createProject(
        pageA,
        token,
        `Realtime-${Date.now()}`,
      );

      // Both tabs land on the board, opening the SignalR connection.
      await Promise.all([
        pageA.goto(`/p/${slug}/board`),
        pageB.goto(`/p/${slug}/board`),
      ]);

      // Give each socket a beat to connect before we mutate.
      await pageA.waitForTimeout(750);
      await pageB.waitForTimeout(750);

      // pageA creates the issue via the REST API (not the UI) — this isolates
      // the test from form interactions and asserts the realtime path purely.
      const title = `Live-event-${Date.now()}`;
      await createIssue(pageA, token, projectId, title);

      // pageB should pick it up via the SignalR event → board query
      // invalidation, with no manual refresh.
      await expect(pageB.getByText(title)).toBeVisible({ timeout: 10_000 });
    } finally {
      await ctxA.close();
      await ctxB.close();
    }
  });

  test('Issue deletion in one tab removes it from the other tab', async ({ browser }) => {
    const ctxA = await browser.newContext({ storageState: AUTH_STATE });
    const ctxB = await browser.newContext({ storageState: AUTH_STATE });
    try {
      const pageA = await ctxA.newPage();
      const pageB = await ctxB.newPage();
      const token = await getToken(pageA);
      const { slug, id: projectId } = await createProject(
        pageA,
        token,
        `RealtimeDel-${Date.now()}`,
      );
      const title = `to-be-removed-${Date.now()}`;
      const issueId = await createIssue(pageA, token, projectId, title);

      await Promise.all([
        pageA.goto(`/p/${slug}/board`),
        pageB.goto(`/p/${slug}/board`),
      ]);
      await expect(pageB.getByText(title)).toBeVisible();

      await pageA.waitForTimeout(500);
      await deleteIssue(pageA, token, issueId);

      await expect(pageB.getByText(title)).toHaveCount(0, { timeout: 10_000 });
    } finally {
      await ctxA.close();
      await ctxB.close();
    }
  });
});

function deleteIssue(page: Page, token: string, issueId: string) {
  return page.request.delete(`${API}/issues/${issueId}`, {
    headers: {
      Authorization: `Bearer ${token}`,
      'X-Requested-With': 'XMLHttpRequest',
    },
  });
}
