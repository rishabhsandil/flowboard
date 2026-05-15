import { useState } from 'react';
import { Link, useOutletContext } from 'react-router-dom';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../lib/api';
import type { Project, Sprint } from '../types';
import type { Paged } from '../types/api';
import { SprintModal } from '../components/SprintModal';

interface Ctx {
  project?: Project;
}

const statusColor: Record<Sprint['status'], string> = {
  planned: 'text-text-muted border-border',
  active: 'text-accent border-accent',
  completed: 'text-priority-low border-border',
};

export default function SprintsPage() {
  const { project } = useOutletContext<Ctx>();
  const qc = useQueryClient();
  const [openCreate, setOpenCreate] = useState(false);
  const [editing, setEditing] = useState<Sprint | null>(null);

  const queryKey = ['sprints', project?.id];
  const { data, isLoading } = useQuery({
    queryKey,
    queryFn: async () =>
      (await api.get<Paged<Sprint>>(`/projects/${project!.id}/sprints`)).data.items,
    enabled: !!project,
  });

  function refetch() {
    qc.invalidateQueries({ queryKey });
    qc.invalidateQueries({ queryKey: ['board', project?.id] });
  }

  return (
    <div className="p-8 max-w-5xl">
      <div className="mb-8 flex items-end justify-between">
        <div>
          <p className="mono text-xs uppercase tracking-widest text-text-dim">// sprints</p>
          <h1 className="mono text-2xl">{project?.name}</h1>
        </div>
        <button onClick={() => setOpenCreate(true)} disabled={!project} className="btn-primary">
          + new sprint
        </button>
      </div>

      {isLoading ? (
        <p className="mono text-text-muted">loading…</p>
      ) : !data?.length ? (
        <div className="panel p-12 text-center">
          <p className="text-text-muted mb-4">no sprints yet.</p>
          <button onClick={() => setOpenCreate(true)} className="btn-primary">
            + create your first sprint
          </button>
        </div>
      ) : (
        <div className="border border-border">
          <div className="grid grid-cols-12 px-4 py-2 mono text-xs uppercase tracking-widest text-text-dim border-b border-border bg-bg-subtle">
            <div className="col-span-4">sprint</div>
            <div className="col-span-3">dates</div>
            <div className="col-span-2">status</div>
            <div className="col-span-3 text-right">actions</div>
          </div>
          {data.map((s) => (
            <div
              key={s.id}
              className="grid grid-cols-12 px-4 py-3 items-center border-t border-border first:border-t-0 hover:bg-bg-subtle transition-colors"
            >
              <div className="col-span-4 min-w-0">
                <p className="mono truncate">{s.name}</p>
                {s.goal && <p className="text-xs text-text-muted truncate mt-0.5">{s.goal}</p>}
              </div>
              <div className="col-span-3 mono text-xs text-text-muted">
                {fmt(s.startDate)} → {fmt(s.endDate)}
              </div>
              <div className="col-span-2">
                <span
                  className={`mono text-[10px] uppercase tracking-widest border px-1.5 py-0.5 ${statusColor[s.status]}`}
                >
                  {s.status}
                </span>
              </div>
              <div className="col-span-3 text-right space-x-3">
                <button
                  onClick={() => setEditing(s)}
                  className="mono text-xs text-text-muted hover:text-text"
                >
                  edit
                </button>
                <Link to={s.id} className="mono text-xs text-accent hover:underline">
                  view →
                </Link>
              </div>
            </div>
          ))}
        </div>
      )}

      {openCreate && project && (
        <SprintModal
          projectId={project.id}
          onClose={() => setOpenCreate(false)}
          onSaved={refetch}
        />
      )}
      {editing && project && (
        <SprintModal
          projectId={project.id}
          sprint={editing}
          onClose={() => setEditing(null)}
          onSaved={refetch}
        />
      )}
    </div>
  );
}

function fmt(d: string) {
  return new Date(d).toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
}
