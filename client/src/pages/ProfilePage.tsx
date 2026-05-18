import { useEffect, useState, type FormEvent } from 'react';
import { Link } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../lib/api';
import { errorService } from '../lib/errorService';
import { useAuthStore } from '../lib/auth';
import { toast } from '../lib/toast';
import { useThemeStore } from '../lib/theme';
import type { User } from '../types';

interface MeResponse {
  user: User;
}

/**
 * Standalone profile page. Reachable from the dashboard header. Splits into
 * two cards: profile (name + avatar URL) and password (current + new).
 *
 * Both forms post errors inline via {silent: true} so we don't double up
 * with the global toast — the password form especially needs a clear
 * "wrong current password" message right next to the input.
 */
export default function ProfilePage() {
  const qc = useQueryClient();
  const setUser = useAuthStore((s) => s.setUser);

  const { data, isLoading } = useQuery({
    queryKey: ['auth-me'],
    queryFn: async () => (await api.get<MeResponse>('/auth/me')).data,
  });

  const me = data?.user;

  // ----- profile form -----
  const [name, setName] = useState('');
  const [avatarUrl, setAvatarUrl] = useState('');
  const [profileError, setProfileError] = useState<string | null>(null);

  // Hydrate the form once the GET returns. We don't reset on every render
  // because that would clobber what the user is typing if a refetch fires.
  useEffect(() => {
    if (me) {
      setName(me.name);
      setAvatarUrl(me.avatarUrl ?? '');
    }
  }, [me?.id]); // eslint-disable-line react-hooks/exhaustive-deps

  const profileMut = useMutation({
    mutationFn: async () => {
      const r = await api.patch<MeResponse>(
        '/auth/me',
        {
          name: name.trim() || null,
          // Empty string clears, undefined leaves alone — pick the right one.
          avatarUrl:
            avatarUrl.trim() === (me?.avatarUrl ?? '').trim()
              ? undefined
              : avatarUrl.trim() || null,
        },
        { silent: true } as Parameters<typeof api.patch>[2],
      );
      return r.data;
    },
    onSuccess: (resp) => {
      setProfileError(null);
      setUser(resp.user);
      qc.setQueryData(['auth-me'], resp);
      toast.success('profile updated');
    },
    onError: (e) => setProfileError(humanizeApiError(getApiError(e) ?? 'internal_error')),
  });

  // ----- password form -----
  const [currentPassword, setCurrentPassword] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [passwordError, setPasswordError] = useState<string | null>(null);

  const passwordMut = useMutation({
    mutationFn: async () => {
      await api.post('/auth/me/password', { currentPassword, newPassword }, {
        silent: true,
      } as Parameters<typeof api.post>[2]);
    },
    onSuccess: () => {
      setPasswordError(null);
      setCurrentPassword('');
      setNewPassword('');
      setConfirmPassword('');
      toast.success('password changed — other devices have been signed out');
    },
    onError: (e) => setPasswordError(errorService.getMessage(e, 'internal_error')),
  });

  function submitProfile(e: FormEvent) {
    e.preventDefault();
    setProfileError(null);
    if (!name.trim()) {
      setProfileError('name is required');
      return;
    }
    profileMut.mutate();
  }

  function submitPassword(e: FormEvent) {
    e.preventDefault();
    setPasswordError(null);
    if (newPassword.length < 8) {
      setPasswordError('new password must be at least 8 characters');
      return;
    }
    if (newPassword !== confirmPassword) {
      setPasswordError("new password and confirmation don't match");
      return;
    }
    passwordMut.mutate();
  }

  return (
    <div className="min-h-screen bg-bg">
      <header className="border-b border-border">
        <div className="max-w-3xl mx-auto px-8 py-4 flex items-center justify-between">
          <Link to="/dashboard" className="mono uppercase tracking-widest text-accent text-sm">
            ← FlowBoard
          </Link>
          <p className="mono text-xs uppercase tracking-widest text-text-dim">// profile</p>
        </div>
      </header>

      <main className="max-w-3xl mx-auto px-8 py-10 space-y-10">
        {isLoading || !me ? (
          <p className="mono text-xs text-text-dim">loading…</p>
        ) : (
          <>
            {/* Profile card */}
            <section className="border border-border bg-bg-panel p-6">
              <h2 className="mono text-lg mb-1">profile</h2>
              <p className="mono text-xs text-text-dim mb-6">
                {me.email} · joined {new Date(me.createdAt).toLocaleDateString()}
              </p>

              <form onSubmit={submitProfile} className="space-y-4">
                <Field label="name">
                  <input
                    className="input"
                    value={name}
                    onChange={(e) => setName(e.target.value)}
                    maxLength={80}
                    required
                  />
                </Field>

                <Field label="avatar url" hint="paste a public image URL, or leave blank">
                  <input
                    className="input"
                    type="url"
                    value={avatarUrl}
                    onChange={(e) => setAvatarUrl(e.target.value)}
                    maxLength={500}
                    placeholder="https://…"
                  />
                </Field>

                {profileError && (
                  <p className="mono text-xs text-priority-critical">{profileError}</p>
                )}

                <button
                  type="submit"
                  className="btn-primary text-xs"
                  disabled={profileMut.isPending}
                >
                  {profileMut.isPending ? 'saving…' : 'save profile'}
                </button>
              </form>
            </section>

            {/* Password card */}
            <section className="border border-border bg-bg-panel p-6">
              <h2 className="mono text-lg mb-1">password</h2>
              <p className="mono text-xs text-text-dim mb-6">
                changing your password signs out your other devices.
              </p>

              <form onSubmit={submitPassword} className="space-y-4">
                <Field label="current password">
                  <input
                    className="input"
                    type="password"
                    value={currentPassword}
                    onChange={(e) => setCurrentPassword(e.target.value)}
                    autoComplete="current-password"
                    required
                  />
                </Field>

                <Field label="new password" hint="minimum 8 characters">
                  <input
                    className="input"
                    type="password"
                    value={newPassword}
                    onChange={(e) => setNewPassword(e.target.value)}
                    minLength={8}
                    maxLength={100}
                    autoComplete="new-password"
                    required
                  />
                </Field>

                <Field label="confirm new password">
                  <input
                    className="input"
                    type="password"
                    value={confirmPassword}
                    onChange={(e) => setConfirmPassword(e.target.value)}
                    autoComplete="new-password"
                    required
                  />
                </Field>

                {passwordError && (
                  <p className="mono text-xs text-priority-critical">{passwordError}</p>
                )}

                <button
                  type="submit"
                  className="btn-primary text-xs"
                  disabled={passwordMut.isPending}
                >
                  {passwordMut.isPending ? 'changing…' : 'change password'}
                </button>
              </form>
            </section>

            <ThemeCard />
          </>
        )}
      </main>
    </div>
  );
}

function ThemeCard() {
  const theme = useThemeStore((s) => s.theme);
  const setTheme = useThemeStore((s) => s.setTheme);
  return (
    <section className="border border-border bg-bg-panel p-6">
      <h2 className="mono text-lg mb-1">appearance</h2>
      <p className="mono text-xs text-text-dim mb-6">
        choose how flowboard looks. saved per browser.
      </p>
      <div className="flex gap-2">
        {(['dark', 'light'] as const).map((t) => {
          const active = theme === t;
          return (
            <button
              key={t}
              type="button"
              onClick={() => setTheme(t)}
              className={
                'btn text-xs ' + (active ? 'border-accent text-accent' : 'text-text-muted')
              }
              aria-pressed={active}
            >
              {t}
            </button>
          );
        })}
      </div>
    </section>
  );
}

function Field({
  label,
  hint,
  children,
}: {
  label: string;
  hint?: string;
  children: React.ReactNode;
}) {
  return (
    <label className="block">
      <span className="mono text-xs text-text-dim uppercase tracking-widest block mb-1">
        {label}
      </span>
      {children}
      {hint && <span className="mono text-[10px] text-text-dim block mt-1">{hint}</span>}
    </label>
  );
}
