import { useState } from 'react';
import { NavLink, Outlet, useOutletContext, useNavigate } from 'react-router-dom';
import { useQueryClient } from '@tanstack/react-query';
import { api } from '../lib/api';
import { toast } from '../lib/toast';
import { useEscapeKey } from '../lib/useEscapeKey';
import type { Project } from '../types';

interface Ctx {
  project?: Project;
}

/**
 * Settings shell with horizontal tab nav. Forwards the parent's project
 * context down to the active tab so children (LabelsPage, future tabs)
 * keep using <code>useOutletContext</code> as before.
 */
export default function SettingsLayout() {
  const { project } = useOutletContext<Ctx>();

  return (
    <div className="flex flex-col h-full">
      <header className="px-8 pt-8 pb-0">
        <p className="mono text-xs uppercase tracking-widest text-text-dim">// settings</p>
        <h1 className="mono text-2xl">{project?.name ?? '…'}</h1>
        <p className="mono text-xs text-text-dim mt-1">configure labels and project preferences.</p>
      </header>

      <nav className="px-8 mt-6 border-b border-border flex gap-1">
        <TabLink to="members">Members</TabLink>
        <TabLink to="labels">Labels</TabLink>
        <TabLink to="general">General</TabLink>
      </nav>

      <div className="flex-1 overflow-y-auto">
        <Outlet context={{ project }} />
      </div>
    </div>
  );
}

function TabLink({ to, children }: { to: string; children: React.ReactNode }) {
  return (
    <NavLink
      to={to}
      className={({ isActive }) =>
        `mono text-xs uppercase tracking-widest px-4 py-2 -mb-px border-b-2 transition-colors ${
          isActive
            ? 'border-accent text-accent'
            : 'border-transparent text-text-dim hover:text-text'
        }`
      }
    >
      {children}
    </NavLink>
  );
}

/**
 * General tab body. Renders read-only project metadata, plus an
 * owner-only "danger zone" with a destructive type-to-confirm
 * delete-project action.
 */
export function SettingsGeneralPage() {
  const { project } = useOutletContext<Ctx>();
  const [showDelete, setShowDelete] = useState(false);
  const isOwner = project?.role === 'owner';

  return (
    <div className="p-8 max-w-3xl space-y-3">
      <p className="mono text-xs uppercase tracking-widest text-text-dim">// general</p>
      <div className="panel border rounded p-4">
        <p className="mono text-xs text-text-dim mb-1">slug</p>
        <p className="mono text-sm">{project?.slug ?? '—'}</p>
      </div>
      <div className="panel border rounded p-4">
        <p className="mono text-xs text-text-dim mb-1">id</p>
        <p className="mono text-sm">{project?.id ?? '—'}</p>
      </div>

      {isOwner && project && (
        <div className="panel border border-priority-critical/40 rounded p-4 mt-6">
          <p className="mono text-xs uppercase tracking-widest text-priority-critical mb-2">
            // danger zone
          </p>
          <p className="mono text-sm mb-1">Delete this project</p>
          <p className="mono text-xs text-text-dim mb-3">
            Permanently removes the project and every issue, epic, sprint, label, comment, and
            activity row inside it. This cannot be undone.
          </p>
          <button
            type="button"
            onClick={() => setShowDelete(true)}
            className="btn border-priority-critical text-priority-critical hover:bg-priority-critical/10"
          >
            delete project
          </button>
        </div>
      )}

      <p className="mono text-xs text-text-dim">
        more project-level settings will appear here soon.
      </p>

      {showDelete && project && (
        <DeleteProjectModal project={project} onClose={() => setShowDelete(false)} />
      )}
    </div>
  );
}

/**
 * Type-to-confirm modal for the destructive delete-project action. The
 * confirm button stays disabled until the user types the literal word
 * "delete" (case-insensitive, trimmed) into the input. This matches the
 * pattern GitHub / Linear use for repo / workspace deletion and prevents
 * accidental clicks on a destructive button.
 *
 * On success, invalidate the dashboard projects list and navigate the
 * user back to /dashboard — the project they were on no longer exists.
 */
function DeleteProjectModal({ project, onClose }: { project: Project; onClose: () => void }) {
  const nav = useNavigate();
  const qc = useQueryClient();
  const [phrase, setPhrase] = useState('');
  const [busy, setBusy] = useState(false);

  useEscapeKey(() => {
    if (!busy) onClose();
  });

  const canConfirm = phrase.trim().toLowerCase() === 'delete' && !busy;

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    if (!canConfirm) return;
    setBusy(true);
    try {
      await api.delete(`/projects/${project.id}`);
      // The current project is gone — drop every query keyed off it so
      // unmounting children don't refetch and 403.
      qc.removeQueries({ queryKey: ['project', project.slug] });
      qc.invalidateQueries({ queryKey: ['projects'] });
      toast.success('project deleted');
      nav('/dashboard');
    } catch {
      // The axios interceptor toasts the error; keep the modal open so
      // the user can retry or cancel.
      setBusy(false);
    }
  }

  return (
    <div
      className="fixed inset-0 bg-black/60 flex items-center justify-center z-50 p-4"
      role="dialog"
      aria-modal="true"
      aria-labelledby="delete-project-title"
      onClick={() => {
        if (!busy) onClose();
      }}
    >
      <div className="panel w-full max-w-md p-6" onClick={(e) => e.stopPropagation()}>
        <p className="mono text-xs uppercase tracking-widest text-priority-critical">
          // danger zone
        </p>
        <h3 id="delete-project-title" className="heading text-lg mt-1">
          Delete "{project.name}"?
        </h3>
        <p className="text-sm text-text-dim mt-3">
          This permanently removes the project, its board, every issue, epic, sprint, label,
          comment, and activity row. <span className="text-text">This cannot be undone.</span>
        </p>

        <form onSubmit={submit} className="mt-5 space-y-3">
          <label className="block">
            <span className="mono text-xs text-text-dim">
              Type <span className="text-priority-critical">delete</span> to confirm
            </span>
            <input
              className="input mono mt-2"
              value={phrase}
              onChange={(e) => setPhrase(e.target.value)}
              placeholder="delete"
              autoFocus
              disabled={busy}
              aria-label="type delete to confirm"
            />
          </label>

          <div className="flex justify-end gap-2 pt-1">
            <button type="button" className="btn-ghost" onClick={onClose} disabled={busy}>
              cancel
            </button>
            <button
              type="submit"
              disabled={!canConfirm}
              className="btn border-priority-critical text-priority-critical hover:bg-priority-critical/10 disabled:opacity-40 disabled:hover:bg-transparent"
            >
              {busy ? 'deleting…' : 'delete project'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
