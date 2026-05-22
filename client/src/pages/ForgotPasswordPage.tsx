import { useState } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../lib/api';
import { AuthShell } from './LoginPage';
import { errorService } from '../lib/errorService';

export default function ForgotPasswordPage() {
  const [email, setEmail] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [sent, setSent] = useState(false);
  // Dev-only: the API echoes the raw token in Development so the flow is
  // testable without an SMTP integration. Hidden in production.
  const [devLink, setDevLink] = useState<string | null>(null);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setLoading(true);
    try {
      const res = await api.post<{ ok: boolean; devToken?: string }>(
        '/auth/forgot',
        { email },
        { silent: true },
      );
      setSent(true);
      if (res.data.devToken) {
        setDevLink(`${window.location.origin}/reset?token=${res.data.devToken}`);
      }
    } catch (err) {
      setError(errorService.getMessage(err, 'request_failed'));
    } finally {
      setLoading(false);
    }
  }

  if (sent) {
    return (
      <AuthShell>
        <h1 className="heading text-2xl mb-1">check your inbox</h1>
        <p className="text-text-muted text-sm mb-6">
          if an account exists for <span className="mono">{email}</span> you'll get a reset link
          shortly. the link is valid for 1 hour.
        </p>
        {devLink && (
          <div className="panel p-3 mb-4 text-xs">
            <div className="text-text-dim uppercase tracking-widest mono mb-1">dev link</div>
            <a className="mono text-accent break-all" href={devLink}>
              {devLink}
            </a>
          </div>
        )}
        <Link to="/login" className="text-accent hover:underline text-sm">
          ← back to login
        </Link>
      </AuthShell>
    );
  }

  return (
    <AuthShell>
      <h1 className="heading text-2xl mb-1">forgot password</h1>
      <p className="text-text-muted text-sm mb-6">
        enter your email and we'll send you a reset link.
      </p>
      <form onSubmit={submit} className="space-y-3">
        <input
          className="input mono"
          type="email"
          placeholder="email"
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          required
          autoFocus
        />
        {error && <div className="mono text-priority-critical text-sm">{error}</div>}
        <button className="btn-primary w-full py-2.5" disabled={loading}>
          {loading ? 'sending…' : 'send reset link →'}
        </button>
      </form>
      <p className="mt-6 text-sm text-text-muted">
        remembered it?{' '}
        <Link to="/login" className="text-accent hover:underline">
          log in
        </Link>
      </p>
    </AuthShell>
  );
}
