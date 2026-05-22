import { useEffect, useMemo, useState } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { api } from '../lib/api';
import { AuthShell } from './LoginPage';
import { errorService } from '../lib/errorService';

export default function ResetPasswordPage() {
  const nav = useNavigate();
  const [params] = useSearchParams();
  // Snapshot the token on first render then strip it from the address bar
  // (audit #4). Leaving `?token=...` in `location.href` would leak the secret
  // via `document.referrer` if any link on this page navigated off-site, and
  // via browser history sync if the user is signed in to a sync profile.
  // Lazy initializer reads useSearchParams once on mount; subsequent renders
  // use the captured value rather than re-reading the (now-scrubbed) URL.
  const [token] = useState(() => params.get('token') ?? '');

  const [password, setPassword] = useState('');
  const [confirm, setConfirm] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [done, setDone] = useState(false);

  useEffect(() => {
    // Replace the URL (no history entry) so the token vanishes from the bar.
    if (token && window.location.search) {
      window.history.replaceState({}, '', window.location.pathname);
    }

    // Defense-in-depth: install <meta name="referrer" content="no-referrer">
    // while this page is mounted so any inadvertent subresource request does
    // not carry the original Referer that may still be cached in-process.
    const meta = document.createElement('meta');
    meta.name = 'referrer';
    meta.content = 'no-referrer';
    document.head.appendChild(meta);
    return () => {
      document.head.removeChild(meta);
    };
  }, [token]);

  // Mirror the backend StringLength(MinimumLength = 8) rule. Confirm field
  // must also match, and the token must be present.
  const passwordOk = password.length >= 8;
  const matches = password === confirm;
  const canSubmit = useMemo(
    () => !!token && passwordOk && matches && !loading,
    [token, passwordOk, matches, loading],
  );

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    if (!canSubmit) return;
    setError(null);
    setLoading(true);
    try {
      await api.post('/auth/reset', { token, newPassword: password }, { silent: true });
      setDone(true);
      setTimeout(() => nav('/login'), 1500);
    } catch (err) {
      setError(errorService.getMessage(err, 'invalid_reset_token'));
    } finally {
      setLoading(false);
    }
  }

  if (!token) {
    return (
      <AuthShell>
        <h1 className="heading text-2xl mb-1">missing token</h1>
        <p className="text-text-muted text-sm mb-6">
          this reset link is incomplete. request a new one from the forgot-password page.
        </p>
        <Link to="/forgot" className="text-accent hover:underline text-sm">
          ← request a new link
        </Link>
      </AuthShell>
    );
  }

  if (done) {
    return (
      <AuthShell>
        <h1 className="heading text-2xl mb-1">password updated</h1>
        <p className="text-text-muted text-sm">redirecting to login…</p>
      </AuthShell>
    );
  }

  return (
    <AuthShell>
      <h1 className="heading text-2xl mb-1">reset password</h1>
      <p className="text-text-muted text-sm mb-6">choose a new password to sign in with.</p>
      <form onSubmit={submit} className="space-y-3">
        <input
          className="input mono"
          type="password"
          placeholder="new password (8+ chars)"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          required
          minLength={8}
          autoFocus
        />
        <input
          className="input mono"
          type="password"
          placeholder="confirm new password"
          value={confirm}
          onChange={(e) => setConfirm(e.target.value)}
          required
          minLength={8}
        />
        {password.length > 0 && !passwordOk && (
          <div className="mono text-priority-critical text-sm">
            password must be at least 8 characters
          </div>
        )}
        {confirm.length > 0 && !matches && (
          <div className="mono text-priority-critical text-sm">passwords do not match</div>
        )}
        {error && <div className="mono text-priority-critical text-sm">{error}</div>}
        <button className="btn-primary w-full py-2.5" disabled={!canSubmit}>
          {loading ? 'updating…' : 'update password →'}
        </button>
      </form>
      <p className="mt-6 text-sm text-text-muted">
        <Link to="/login" className="text-accent hover:underline">
          ← back to login
        </Link>
      </p>
    </AuthShell>
  );
}
