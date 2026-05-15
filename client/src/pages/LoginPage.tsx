import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { api, humanizeApiError } from '../lib/api';
import { useAuthStore } from '../lib/auth';
import { getApiError } from '../types/api';

export default function LoginPage() {
  const nav = useNavigate();
  const setSession = useAuthStore((s) => s.setSession);
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setLoading(true);
    try {
      // silent: this form renders the error inline below the button.
      const res = await api.post('/auth/login', { email, password }, { silent: true });
      setSession(res.data.user, res.data.accessToken, res.data.refreshToken);
      nav('/dashboard');
    } catch (err) {
      setError(humanizeApiError(getApiError(err, 'login_failed')));
    } finally {
      setLoading(false);
    }
  }

  return (
    <AuthShell>
      <h1 className="heading text-2xl mb-1">login</h1>
      <p className="text-text-muted text-sm mb-6">welcome back.</p>
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
        <input
          className="input mono"
          type="password"
          placeholder="password"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          required
        />
        {error && <div className="mono text-priority-critical text-sm">{error}</div>}
        <button className="btn-primary w-full py-2.5" disabled={loading}>
          {loading ? 'signing in…' : 'sign in →'}
        </button>
      </form>
      <p className="mt-6 text-sm text-text-muted">
        no account?{' '}
        <Link to="/register" className="text-accent hover:underline">
          register
        </Link>
      </p>
    </AuthShell>
  );
}

export function AuthShell({ children }: { children: React.ReactNode }) {
  return (
    <div className="min-h-screen flex items-center justify-center px-4">
      <div className="panel w-full max-w-sm p-8">
        <Link to="/" className="mono text-accent text-xs uppercase tracking-widest">
          ← FlowBoard
        </Link>
        <div className="mt-6">{children}</div>
      </div>
    </div>
  );
}
