import { NavLink, Outlet, useParams, Link, useNavigate } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { api } from '../lib/api';
import { useAuthStore } from '../lib/auth';
import { useBoardSocket } from '../lib/useBoardSocket';
import type { Project } from '../types';

export default function ProjectLayout() {
  const { slug } = useParams<{ slug: string }>();
  const nav = useNavigate();
  const logout = useAuthStore((s) => s.logout);

  const { data: project, isLoading } = useQuery({
    queryKey: ['project', slug],
    queryFn: async () => (await api.get<{ project: Project }>(`/projects/${slug}`)).data.project,
    enabled: !!slug,
  });

  // One SignalR subscription per visited project. The hook is a no-op until
  // the project query resolves, and tears down when the user navigates to a
  // different project (or leaves the project routes entirely).
  useBoardSocket(project?.id);

  return (
    <div className="min-h-screen flex">
      <aside className="w-60 border-r border-border bg-bg-subtle flex flex-col">
        <div className="px-5 py-4 border-b border-border">
          <Link to="/dashboard" className="mono uppercase tracking-widest text-accent text-sm">
            ← FlowBoard
          </Link>
        </div>

        <div className="px-5 py-4 border-b border-border">
          <p className="mono text-xs uppercase tracking-widest text-text-dim mb-1">// project</p>
          <p className="mono text-sm truncate">{project?.name ?? '…'}</p>
        </div>

        <nav className="flex-1 py-2">
          <SidebarLink to={`/p/${slug}/board`}>Board</SidebarLink>
          <SidebarLink to={`/p/${slug}/issues`}>Issues</SidebarLink>
          <SidebarLink to={`/p/${slug}/epics`}>Epics</SidebarLink>
          <SidebarLink to={`/p/${slug}/sprints`}>Sprints</SidebarLink>
          <SidebarLink to={`/p/${slug}/plan`}>Plan</SidebarLink>
          <SidebarLink to={`/p/${slug}/reports/cfd`}>CFD</SidebarLink>
          <SidebarLink to={`/p/${slug}/activity`}>Activity</SidebarLink>
          <SidebarLink to={`/p/${slug}/settings`}>Settings</SidebarLink>
        </nav>

        <div className="px-5 py-4 border-t border-border">
          <button
            onClick={() => {
              logout();
              nav('/');
            }}
            className="mono text-xs text-text-muted hover:text-text"
          >
            sign out
          </button>
        </div>
      </aside>

      <main className="flex-1 overflow-hidden">
        {isLoading ? (
          <div className="p-8 mono text-text-muted">loading project…</div>
        ) : (
          <Outlet context={{ project }} />
        )}
      </main>
    </div>
  );
}

function SidebarLink({ to, children }: { to: string; children: React.ReactNode }) {
  return (
    <NavLink
      to={to}
      className={({ isActive }) =>
        `block px-5 py-2 mono text-sm border-l-2 transition-colors ${
          isActive
            ? 'border-accent text-accent bg-bg-panel'
            : 'border-transparent text-text-muted hover:text-text hover:bg-bg-panel'
        }`
      }
    >
      {children}
    </NavLink>
  );
}
