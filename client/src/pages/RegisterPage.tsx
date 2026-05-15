import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { api, humanizeApiError } from '../lib/api';
import { useAuthStore } from '../lib/auth';
import { AuthShell } from './LoginPage';
import { getApiError } from '../types/api';

export default function RegisterPage() {
  const nav = useNavigate();
  const setSession = useAuthStore((s) => s.setSession);
  const [name, setName] = useState('');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setLoading(true);
    try {
      const res = await api.post('/auth/register', { name, email, password }, { silent: true });
      setSession(res.data.user, res.data.accessToken, res.data.refreshToken);
      nav('/dashboard');
    } catch (err) {
      setError(humanizeApiError(getApiError(err, 'register_failed')));
    } finally {
      setLoading(false);
    }
  }

  return (
    <AuthShell>
      <h1 className="heading text-2xl mb-1">register</h1>
      <p className="text-text-muted text-sm mb-6">create an account.</p>
      <form onSubmit={submit} className="space-y-3">
        <input
          className="input mono"
          placeholder="full name"
          value={name}
          onChange={(e) => setName(e.target.value)}
          required
          autoFocus
        />
        <input
          className="input mono"
          type="email"
          placeholder="email"
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          required
        />
        <input
          className="input mono"
          type="password"
          placeholder="password (8+ chars)"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          required
          minLength={8}
        />
        {error && <div className="mono text-priority-critical text-sm">{error}</div>}
        <button className="btn-primary w-full py-2.5" disabled={loading}>
          {loading ? 'creating…' : 'create account →'}
        </button>
      </form>
      <p className="mt-6 text-sm text-text-muted">
        already have one?{' '}
        <Link to="/login" className="text-accent hover:underline">
          log in
        </Link>
      </p>
    </AuthShell>
  );
}
