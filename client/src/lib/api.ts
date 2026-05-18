import axios, { type AxiosRequestConfig } from 'axios';
import { useAuthStore } from './auth';
import { errorService } from './errorService';

// Module augmentation: lets any axios call (including api.post/patch/delete)
// accept a `silent: true` flag without TS errors. We read it in the response
// interceptor below to decide whether to pop a toast.
declare module 'axios' {
  // eslint-disable-next-line @typescript-eslint/no-empty-object-type
  export interface AxiosRequestConfig {
    silent?: boolean;
  }
}

export const api = axios.create({
  baseURL: import.meta.env.VITE_API_URL ?? 'http://localhost:8080/api',
  // CSRF defense-in-depth: the backend CsrfProtectionMiddleware requires this
  // header on all state-mutating requests. Browsers cannot send custom headers
  // cross-site without a CORS preflight, so this header proves the request
  // originated from JavaScript running on an allowed origin.
  headers: { 'X-Requested-With': 'XMLHttpRequest' },
});

api.interceptors.request.use((config) => {
  const token = useAuthStore.getState().accessToken;
  if (token) config.headers.Authorization = `Bearer ${token}`;
  return config;
});

let refreshing: Promise<string | null> | null = null;

api.interceptors.response.use(
  (r) => r,
  async (error) => {
    const original = error.config as AxiosRequestConfig | undefined;
    if (error.response?.status === 401 && original && !(original as { _retry?: boolean })._retry) {
      (original as { _retry?: boolean })._retry = true;
      refreshing ??= refresh();
      const newToken = await refreshing;
      refreshing = null;
      if (newToken) {
        if (original.headers) {
          (original.headers as Record<string, string>).Authorization = `Bearer ${newToken}`;
        }
        return api(original);
      }
      useAuthStore.getState().logout();
      // Don't toast the auth redirect — the login page will speak for itself.
      return Promise.reject(error);
    }

    // Surface server errors via a toast unless the caller opted out. We
    // skip 401s (handled above), and skip when a request explicitly says
    // it'll render the error itself.
    if (!original?.silent && error.response && error.response.status !== 401) {
      errorService.toast(error, 'request_failed');
    } else if (!original?.silent && !error.response) {
      // Network failure — no response object at all.
      errorService.toastCode('network error — check your connection');
    }
    return Promise.reject(error);
  },
);

async function refresh(): Promise<string | null> {
  const rt = useAuthStore.getState().refreshToken;
  if (!rt) return null;
  try {
    const res = await axios.post(`${api.defaults.baseURL}/auth/refresh`, {
      refreshToken: rt,
    });
    // Server rotates refresh tokens — persist the new one alongside the
    // access token. If the server omits it we keep the existing rt.
    const access = res.data.accessToken as string;
    const newRt = res.data.refreshToken as string | undefined;
    const store = useAuthStore.getState();
    if (newRt && store.user) {
      store.setSession(store.user, access, newRt);
    } else {
      store.setAccessToken(access);
    }
    return access;
  } catch {
    return null;
  }
}
