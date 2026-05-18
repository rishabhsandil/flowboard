import { type Page } from '@playwright/test';

export const API = 'http://localhost:8080/api';

// Shared e2e test credentials — must match auth.setup.ts
export const TEST_EMAIL    = 'e2e-playwright@test.local';
export const TEST_PASSWORD = 'E2eTestPw123!';

/** Read the access token from the Zustand-persisted auth store in localStorage. */
export async function getToken(page: Page): Promise<string> {
  // Navigate to the app so the storage context is initialized (no-op if already there).
  if (!page.url().includes('localhost')) {
    await page.goto('/dashboard');
  }
  const token = await page.evaluate(() => {
    const raw = localStorage.getItem('flowboard-auth');
    if (!raw) return null;
    const parsed = JSON.parse(raw) as { state?: { accessToken?: string } };
    return parsed?.state?.accessToken ?? null;
  });
  if (!token) throw new Error('No access token in localStorage — auth setup may have failed');
  return token;
}

/** Create a project and return its slug and id via the API. */
export async function createProject(
  page: Page,
  token: string,
  name: string,
): Promise<{ id: string; slug: string }> {
  const res = await page.request.post(`${API}/projects`, {
    data: { name },
    headers: {
      Authorization: `Bearer ${token}`,
      'Content-Type': 'application/json',
      'X-Requested-With': 'XMLHttpRequest',
    },
  });
  const body = await res.json();
  return { id: body.project.id, slug: body.project.slug };
}

/** Create an issue in a project via the API. */
export async function createIssue(
  page: Page,
  token: string,
  projectId: string,
  title: string,
): Promise<string> {
  const res = await page.request.post(`${API}/projects/${projectId}/issues`, {
    data: { title },
    headers: {
      Authorization: `Bearer ${token}`,
      'Content-Type': 'application/json',
      'X-Requested-With': 'XMLHttpRequest',
    },
  });
  const body = await res.json();
  return body.issue.id as string;
}
