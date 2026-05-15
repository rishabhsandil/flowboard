import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { api } from '../lib/api';
import { useAuthStore } from '../lib/auth';
import type { Project } from '../types';
import type { Paged } from '../types/api';

export default function DashboardPage() {
  const nav = useNavigate();
  const qc = useQueryClient();
  const { user, logout } = useAuthStore();
  const [showCreate, setShowCreate] = useState(false);

  const { data, isLoading } = useQuery({
    queryKey: ['projects'],
    queryFn: async () => (await api.get<Paged<Project>>('/projects')).data.items,
  });

  const create = useMutation({
    mutationFn: async (vars: { name: string; description: string }) =>
      (await api.post('/projects', vars)).data.project as Project,
    onSuccess: (project) => {
      qc.invalidateQueries({ queryKey: ['projects'] });
      nav(`/p/${project.slug}/board`);
    },
  });

  return (
    <div className="min-h-screen">
      <header className="border-b border-border px-8 py-4 flex items-center justify-between">
        <Link to="/" className="mono uppercase tracking-widest text-accent">
          FlowBoard
        </Link>
        <div className="flex items-center gap-4">
          <Link
            to="/profile"
            className="mono text-sm text-text-muted hover:text-accent transition-colors"
          >
            {user?.email}
          </Link>
          <button
            onClick={() => {
              logout();
              nav('/');
            }}
            className="btn-ghost text-sm"
          >
            sign out
          </button>
        </div>
      </header>

      <main className="max-w-5xl mx-auto px-8 py-12">
        <div className="flex items-end justify-between mb-8">
          <div>
            <p className="mono text-xs uppercase tracking-widest text-text-dim mb-2">// projects</p>
            <h1 className="mono text-3xl">your workspace</h1>
          </div>
          <button onClick={() => setShowCreate(true)} className="btn-primary">
            + new project
          </button>
        </div>

        {isLoading ? (
          <div className="mono text-text-muted">loading…</div>
        ) : data?.length ? (
          <div className="grid grid-cols-1 md:grid-cols-2 gap-px bg-border">
            {data.map((p) => (
              <Link
                key={p.id}
                to={`/p/${p.slug}/board`}
                className="bg-bg-panel p-6 hover:bg-bg-subtle transition-colors group"
              >
                <div className="flex items-center justify-between">
                  <h2 className="mono text-lg group-hover:text-accent transition-colors">
                    {p.name}
                  </h2>
                  <span className="mono text-xs text-text-dim">{p.role}</span>
                </div>
                {p.description && (
                  <p className="text-sm text-text-muted mt-2 line-clamp-2">{p.description}</p>
                )}
                <p className="mono text-xs text-text-dim mt-4">/p/{p.slug}</p>
              </Link>
            ))}
          </div>
        ) : (
          <div className="panel p-12 text-center">
            <p className="text-text-muted mb-4">no projects yet.</p>
            <button onClick={() => setShowCreate(true)} className="btn-primary">
              create your first project
            </button>
          </div>
        )}
      </main>

      {showCreate && (
        <CreateProjectModal
          onClose={() => setShowCreate(false)}
          onSubmit={(vars) => create.mutate(vars)}
          loading={create.isPending}
        />
      )}
    </div>
  );
}

function CreateProjectModal({
  onClose,
  onSubmit,
  loading,
}: {
  onClose: () => void;
  onSubmit: (v: { name: string; description: string }) => void;
  loading: boolean;
}) {
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');

  return (
    <div
      className="fixed inset-0 bg-black/60 flex items-center justify-center z-50 p-4"
      onClick={onClose}
    >
      <div className="panel w-full max-w-md p-6" onClick={(e) => e.stopPropagation()}>
        <h3 className="heading text-lg mb-4">new project</h3>
        <form
          onSubmit={(e) => {
            e.preventDefault();
            if (name.trim()) onSubmit({ name, description });
          }}
          className="space-y-3"
        >
          <input
            className="input mono"
            placeholder="project name"
            value={name}
            onChange={(e) => setName(e.target.value)}
            required
            autoFocus
          />
          <textarea
            className="input mono"
            placeholder="description (optional)"
            rows={3}
            value={description}
            onChange={(e) => setDescription(e.target.value)}
          />
          <div className="flex gap-2 justify-end">
            <button type="button" onClick={onClose} className="btn-ghost">
              cancel
            </button>
            <button className="btn-primary" disabled={loading}>
              {loading ? 'creating…' : 'create →'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
