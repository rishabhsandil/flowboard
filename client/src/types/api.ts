/**
 * Shape of an error thrown by the `api` axios instance when the server
 * returns a 4xx/5xx with a `{ error: string }` body. Use `getApiError(err)`
 * instead of `err: any` so we never blindly access `.response.data` on
 * non-axios errors (e.g. network failures, code errors in the catch block).
 */
export interface ApiError {
  response?: {
    status?: number;
    data?: { error?: string; message?: string };
  };
  message?: string;
}

/** Extract a human-readable error code/message from an unknown thrown value. */
export function getApiError(err: unknown, fallback = 'unknown_error'): string {
  if (typeof err === 'object' && err !== null) {
    const e = err as ApiError;
    return e.response?.data?.error ?? e.response?.data?.message ?? e.message ?? fallback;
  }
  return fallback;
}

/** Server pagination envelope returned by paginated list endpoints. */
export interface Paged<T> {
  items: T[];
  total: number;
  skip: number;
  take: number;
}
