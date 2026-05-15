import { NavLink, Outlet, useOutletContext } from 'react-router-dom';
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
 * Placeholder body for the General tab. Fills the slot so the tabs feel
 * meaningful from day one; real settings (rename project, archive, etc.)
 * will land here as the API grows.
 */
export function SettingsGeneralPage() {
  const { project } = useOutletContext<Ctx>();
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
      <p className="mono text-xs text-text-dim">
        more project-level settings will appear here soon.
      </p>
    </div>
  );
}
