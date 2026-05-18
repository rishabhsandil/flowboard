import { toast } from './toast';
import { getApiError } from '../types/api';

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

export function humanizeError(code: string): string {
  return ERROR_MESSAGES[code] ?? code;
}

export const errorService = {
  /** Gets the human-readable string for an unknown error object */
  getMessage(err: unknown, fallback = 'unknown_error'): string {
    const code = getApiError(err, fallback);
    return humanizeError(code);
  },

  /** Parses the error and pops a toast with the human-readable message */
  toast(err: unknown, fallback = 'request_failed') {
    toast.error(this.getMessage(err, fallback));
  },

  /** Toast a specific known string code directly */
  toastCode(code: string) {
    toast.error(humanizeError(code));
  },
};
