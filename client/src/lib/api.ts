import axios, { type AxiosRequestConfig } from 'axios';
import { useAuthStore } from './auth';
import { toast } from './toast';
import { getApiError } from '../types/api';

// Module augmentation: lets any axios call (including api.post/patch/delete)
// accept a `silent: true` flag without TS errors. We read it in the response
// interceptor below to decide whether to pop a toast.
declare module 'axios' {
  // eslint-disable-next-line @typescript-eslint/no-empty-object-type
  export interface AxiosRequestConfig {
    silent?: boolean;
  }
}

/**
 * Friendlier strings for known backend error codes. Anything not listed
 * falls back to the raw code, which is fine for diagnostics — devs see the
 * exact contract while users see something readable for the common cases.
 */
const ERROR_MESSAGES: Record<string, string> = {
  email_in_use: 'that email is already registered',
  invalid_credentials: 'email or password is incorrect',
  label_name_exists: 'a label with that name already exists',
  label_project_mismatch: 'that label belongs to a different project',
  user_not_found: 'no FlowBoard account found for that email',
  member_not_found: 'that user is not a member of this project',
  last_owner: "can't remove the last owner — promote someone else first",
  internal_error: 'something went wrong on the server — please try again',
};

export function humanizeApiError(code: string): string {
  return ERROR_MESSAGES[code] ?? code;
}

export const api = axios.create({
  baseURL: import.meta.env.VITE_API_URL ?? 'http://localhost:8080/api',
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
      const code = getApiError(error, 'request_failed');
      toast.error(humanizeApiError(code));
    } else if (!original?.silent && !error.response) {
      // Network failure — no response object at all.
      toast.error('network error — check your connection');
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
