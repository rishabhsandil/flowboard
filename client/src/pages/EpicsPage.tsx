import { useState } from 'react';
import { useNavigate, useOutletContext, useParams } from 'react-router-dom';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../lib/api';
import type { EpicWithProgress, Project } from '../types';
import type { Paged } from '../types/api';
import { EpicModal } from '../components/EpicModal';

interface Ctx {
  project?: Project;
}

export default function EpicsPage() {
  const { project } = useOutletContext<Ctx>();
  const { slug } = useParams<{ slug: string }>();
  const nav = useNavigate();
  const qc = useQueryClient();
  const [openCreate, setOpenCreate] = useState(false);
  const [editing, setEditing] = useState<EpicWithProgress | null>(null);

  const queryKey = ['epics', project?.id];
  const { data, isLoading } = useQuery({
    queryKey,
    queryFn: async () =>
      (await api.get<Paged<EpicWithProgress>>(`/projects/${project!.id}/epics`)).data.items,
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
          <p className="mono text-xs uppercase tracking-widest text-text-dim">// epics</p>
          <h1 className="mono text-2xl">{project?.name}</h1>
        </div>
        <button onClick={() => setOpenCreate(true)} disabled={!project} className="btn-primary">
          + new epic
        </button>
      </div>

      {isLoading ? (
        <p className="mono text-text-muted">loading…</p>
      ) : !data?.length ? (
        <div className="panel p-12 text-center">
          <p className="text-text-muted mb-4">no epics yet.</p>
          <button onClick={() => setOpenCreate(true)} className="btn-primary">
            + create your first epic
          </button>
        </div>
      ) : (
        <div className="space-y-px bg-border">
          {data.map((e) => (
            <EpicRow
              key={e.id}
              epic={e}
              onOpen={() => nav(`/p/${slug}/epics/${e.id}`)}
              onEdit={() => setEditing(e)}
            />
          ))}
        </div>
      )}

      {openCreate && project && (
        <EpicModal projectId={project.id} onClose={() => setOpenCreate(false)} onSaved={refetch} />
      )}
      {editing && project && (
        <EpicModal
          projectId={project.id}
          epic={editing}
          onClose={() => setEditing(null)}
          onSaved={refetch}
        />
      )}
    </div>
  );
}

function EpicRow({
  epic,
  onOpen,
  onEdit,
}: {
  epic: EpicWithProgress;
  onOpen: () => void;
  onEdit: () => void;
}) {
  const pct = epic.percentComplete ?? 0;
  return (
    <div
      onClick={onOpen}
      className="w-full bg-bg-panel p-5 grid grid-cols-12 gap-4 items-center text-left hover:bg-bg-subtle transition-colors cursor-pointer"
    >
      <div className="col-span-4 flex items-center gap-2 min-w-0">
        <span className="w-2 h-2 rounded-full flex-shrink-0" style={{ background: epic.color }} />
        <p className="mono truncate">{epic.title}</p>
      </div>
      <div className="col-span-4">
        <div className="h-1.5 bg-bg-subtle border border-border overflow-hidden">
          <div
            className="h-full transition-all"
            style={{ width: `${pct}%`, background: epic.color }}
          />
        </div>
      </div>
      <div className="col-span-1 mono text-sm text-right">
        {epic.percentComplete === null ? '—' : `${pct}%`}
      </div>
      <div className="col-span-2 mono text-xs text-text-muted text-right">
        {epic.closedIssues}/{epic.totalIssues} · {epic.completedPoints}/{epic.totalPoints}pt
      </div>
      <div className="col-span-1 text-right">
        <button
          onClick={(e) => {
            e.stopPropagation();
            onEdit();
          }}
          className="mono text-xs text-text-dim hover:text-accent"
        >
          edit
        </button>
      </div>
    </div>
  );
}
