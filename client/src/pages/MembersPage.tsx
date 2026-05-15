import { useState } from 'react';
import { useOutletContext } from 'react-router-dom';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { api, humanizeApiError } from '../lib/api';
import { confirmDialog } from '../lib/confirmDialog';
import { useAuthStore } from '../lib/auth';
import { toast } from '../lib/toast';
import { getApiError } from '../types/api';
import type { Project, ProjectMember } from '../types';

interface Ctx {
  project?: Project;
}

/**
 * Members tab inside Project Settings. Owners can invite by email, change
 * a member's role, and remove members. Non-owners see a read-only roster
 * plus a "leave project" button for themselves.
 *
 * Rules enforced (server is the source of truth — UI mirrors them):
 * - Last owner cannot be demoted or removed.
 * - Self-remove is allowed for any member; everything else requires owner.
 */
export default function MembersPage() {
  const { project } = useOutletContext<Ctx>();
  const projectId = project?.id;
  const currentUserId = useAuthStore((s) => s.user?.id) ?? null;
  const isOwner = project?.role === 'owner';
  const qc = useQueryClient();

  const [email, setEmail] = useState('');
  const [role, setRole] = useState<'member' | 'owner'>('member');
  const [busy, setBusy] = useState(false);
  const [inviteError, setInviteError] = useState<string | null>(null);

  const { data: members = [], isLoading } = useQuery({
    queryKey: ['members', projectId],
    queryFn: async () => {
      const r = await api.get<{ items: ProjectMember[] }>(`/projects/${projectId}/members`);
      return r.data.items;
    },
    enabled: !!projectId,
  });

  function refresh() {
    qc.invalidateQueries({ queryKey: ['members', projectId] });
  }

  // The "owners count" gate is duplicated server-side; we only use it to
  // disable the buttons so the user gets immediate feedback.
  const ownerCount = members.filter((m) => m.role === 'owner').length;

  async function invite(e: React.FormEvent) {
    e.preventDefault();
    if (!projectId || !email.trim()) return;
    setBusy(true);
    setInviteError(null);
    try {
      await api.post(
        `/projects/${projectId}/members`,
        { email: email.trim(), role },
        // The form renders the error inline; suppress the global toast so
        // the user doesn't see the same message twice.
        { silent: true },
      );
      setEmail('');
      setRole('member');
      refresh();
      toast.success('member added');
    } catch (err) {
      setInviteError(humanizeApiError(getApiError(err, 'request_failed')));
    } finally {
      setBusy(false);
    }
  }

  async function changeRole(m: ProjectMember, next: 'owner' | 'member') {
    if (m.role === next) return;
    if (next === 'member' && m.role === 'owner' && ownerCount <= 1) {
      toast.error(humanizeApiError('last_owner'));
      return;
    }
    try {
      await api.patch(`/projects/${projectId}/members/${m.id}`, { role: next });
      refresh();
    } catch {
      // the global error interceptor handles this
    }
  }

  async function remove(m: ProjectMember) {
    const isSelf = m.id === currentUserId;
    if (!isSelf && m.role === 'owner' && ownerCount <= 1) {
      toast.error(humanizeApiError('last_owner'));
      return;
    }
    const ok = await confirmDialog({
      title: isSelf ? `Leave "${project?.name}"?` : `Remove ${m.name} from this project?`,
      description: isSelf
        ? "You'll lose access to the project's board, issues, and history."
        : "They'll lose access immediately. Issues they created or were assigned to are kept.",
      confirmLabel: isSelf ? 'Leave project' : 'Remove member',
      destructive: true,
    });
    if (!ok) return;
    try {
      await api.delete(`/projects/${projectId}/members/${m.id}`);
      refresh();
      toast.success(isSelf ? 'left project' : 'member removed');
    } catch {
      /* interceptor toast */
    }
  }

  return (
    <div className="p-8 max-w-3xl space-y-6">
      <p className="mono text-xs uppercase tracking-widest text-text-dim">// members</p>

      {isOwner && (
        <form
          onSubmit={invite}
          className="panel border rounded p-4 space-y-3"
          aria-label="add member"
        >
          <h3 className="mono text-sm">Add member</h3>
          <div className="flex flex-col sm:flex-row gap-2">
            <input
              type="email"
              required
              placeholder="teammate@example.com"
              className="input mono flex-1"
              value={email}
              onChange={(e) => {
                setEmail(e.target.value);
                setInviteError(null);
              }}
              disabled={busy}
            />
            <select
              className="input mono sm:w-40"
              value={role}
              onChange={(e) => setRole(e.target.value as 'owner' | 'member')}
              disabled={busy}
            >
              <option value="member">member</option>
              <option value="owner">owner</option>
            </select>
            <button
              type="submit"
              className="btn-primary"
              disabled={busy || email.trim().length === 0}
            >
              {busy ? 'adding…' : 'add'}
            </button>
          </div>
          {inviteError && <p className="mono text-xs text-priority-critical">{inviteError}</p>}
          <p className="mono text-[11px] text-text-dim">
            The user must already have a FlowBoard account. Email-based invites for unregistered
            users are coming soon.
          </p>
        </form>
      )}

      <div className="panel border rounded">
        <div className="px-4 py-3 border-b border-border flex items-baseline justify-between">
          <h3 className="mono text-sm">Roster</h3>
          <span className="mono text-xs text-text-dim">
            {isLoading ? 'loading…' : `${members.length} member${members.length === 1 ? '' : 's'}`}
          </span>
        </div>
        <ul>
          {members.map((m) => {
            const isSelf = m.id === currentUserId;
            const isLastOwner = m.role === 'owner' && ownerCount <= 1;
            return (
              <li
                key={m.id}
                className="px-4 py-3 border-b border-border last:border-b-0 flex items-center gap-3"
              >
                <div className="flex-1 min-w-0">
                  <p className="mono text-sm truncate">
                    {m.name}
                    {isSelf && <span className="text-text-dim"> (you)</span>}
                  </p>
                  <p className="mono text-xs text-text-dim truncate">{m.email}</p>
                </div>
                {isOwner && !isSelf ? (
                  <select
                    className="input mono text-xs w-28"
                    value={m.role}
                    disabled={isLastOwner}
                    onChange={(e) => changeRole(m, e.target.value as 'owner' | 'member')}
                  >
                    <option value="member">member</option>
                    <option value="owner">owner</option>
                  </select>
                ) : (
                  <span className="mono text-xs text-text-dim uppercase tracking-widest">
                    {m.role}
                  </span>
                )}
                {(isOwner || isSelf) && (
                  <button
                    type="button"
                    onClick={() => remove(m)}
                    disabled={isLastOwner && !isSelf}
                    className="mono text-[11px] text-text-dim hover:text-priority-critical disabled:opacity-40 disabled:hover:text-text-dim"
                    title={
                      isLastOwner && !isSelf
                        ? 'Promote another owner before removing the last one.'
                        : isSelf
                          ? 'Leave this project'
                          : 'Remove from project'
                    }
                  >
                    {isSelf ? 'leave' : 'remove'}
                  </button>
                )}
              </li>
            );
          })}
          {!isLoading && members.length === 0 && (
            <li className="px-4 py-6 mono text-xs text-text-dim text-center">no members yet.</li>
          )}
        </ul>
      </div>
    </div>
  );
}
