import { useMemo, useState } from 'react';
import { useParams, useOutletContext, Link } from 'react-router-dom';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../lib/api';
import type { BurndownPoint, Project, Sprint, SprintIssue, VelocityRow } from '../types';
import type { Paged } from '../types/api';
import { BurndownChart } from '../components/BurndownChart';
import { VelocityChart } from '../components/VelocityChart';
import { SprintModal } from '../components/SprintModal';
import { CreateIssueModal } from '../components/CreateIssueModal';
import { Avatar } from '../components/Avatar';

interface Ctx {
  project?: Project;
}

const STATUSES: Sprint['status'][] = ['planned', 'active', 'completed'];
const statusColor: Record<Sprint['status'], string> = {
  planned: 'text-text-muted border-border',
  active: 'text-accent border-accent',
  completed: 'text-priority-low border-border',
};

export default function SprintDetailPage() {
  const { id } = useParams<{ id: string }>();
  const { project } = useOutletContext<Ctx>();
  const qc = useQueryClient();
  const [editing, setEditing] = useState(false);
  const [adding, setAdding] = useState(false);

  // Hydrate the current sprint from the project's sprint list (no extra endpoint needed).
  const { data: sprints } = useQuery({
    queryKey: ['sprints', project?.id],
    queryFn: async () =>
      (await api.get<Paged<Sprint>>(`/projects/${project!.id}/sprints`)).data.items,
    enabled: !!project,
  });
  const sprint = useMemo(() => sprints?.find((s) => s.id === id), [sprints, id]);

  const { data: velocity } = useQuery({
    queryKey: ['velocity', id],
    queryFn: async () =>
      (await api.get<{ velocity: VelocityRow[] }>(`/sprints/${id}/velocity`)).data.velocity,
    enabled: !!id,
  });

  const issuesKey = ['sprint-issues', id];
  const { data: issues } = useQuery({
    queryKey: issuesKey,
    queryFn: async () =>
      (await api.get<{ issues: SprintIssue[] }>(`/sprints/${id}/issues`)).data.issues,
    enabled: !!id,
  });

  const { data: burndown } = useQuery({
    queryKey: ['burndown', id],
    queryFn: async () =>
      (await api.get<{ burndown: BurndownPoint[] }>(`/sprints/${id}/burndown`)).data.burndown,
    // Only meaningful for active or completed sprints with at least a start date
    enabled: !!id && sprint?.status !== 'planned',
  });

  async function setStatus(next: Sprint['status']) {
    if (!sprint || sprint.status === next) return;
    await api.patch(`/sprints/${sprint.id}`, { status: next });
    qc.invalidateQueries({ queryKey: ['sprints', project?.id] });
    qc.invalidateQueries({ queryKey: ['velocity', id] });
    qc.invalidateQueries({ queryKey: ['burndown', id] });
  }

  function refetchAll() {
    qc.invalidateQueries({ queryKey: ['sprints', project?.id] });
    qc.invalidateQueries({ queryKey: issuesKey });
    qc.invalidateQueries({ queryKey: ['velocity', id] });
    qc.invalidateQueries({ queryKey: ['burndown', id] });
    qc.invalidateQueries({ queryKey: ['board', project?.id] });
  }

  return (
    <div className="p-8 max-w-5xl space-y-8">
      <div>
        <p className="mono text-xs uppercase tracking-widest text-text-dim">// sprint</p>
        {sprint ? (
          <>
            <div className="flex items-end justify-between gap-4 flex-wrap">
              <div>
                <h1 className="mono text-2xl">{sprint.name}</h1>
                <p className="mono text-xs text-text-muted mt-1">
                  {fmt(sprint.startDate)} → {fmt(sprint.endDate)}
                  {' · '}
                  <span
                    className={`inline-block border px-1.5 py-0.5 ml-1 ${statusColor[sprint.status]}`}
                  >
                    {sprint.status}
                  </span>
                </p>
                {sprint.goal && (
                  <p className="text-sm text-text-muted mt-2 max-w-xl">{sprint.goal}</p>
                )}
              </div>
              <div className="flex gap-2">
                <button onClick={() => setAdding(true)} className="btn-primary">
                  + add issue
                </button>
                <button onClick={() => setEditing(true)} className="btn-ghost">
                  edit
                </button>
                <Link to=".." relative="path" className="btn-ghost">
                  ← all sprints
                </Link>
              </div>
            </div>

            <div className="mt-4 flex items-center gap-2">
              <span className="mono text-[10px] uppercase tracking-widest text-text-dim">
                set status:
              </span>
              {STATUSES.map((s) => (
                <button
                  key={s}
                  onClick={() => setStatus(s)}
                  className={`mono text-[10px] uppercase tracking-widest border px-1.5 py-0.5 transition-colors ${
                    sprint.status === s
                      ? statusColor[s]
                      : 'border-border text-text-dim hover:text-text'
                  }`}
                >
                  {s}
                </button>
              ))}
            </div>
          </>
        ) : (
          <h1 className="mono text-2xl">{project?.name}</h1>
        )}
      </div>

      {/* Burndown chart — shown for active and completed sprints */}
      {sprint && sprint.status !== 'planned' && (
        <section className="panel p-6">
          <h2 className="heading text-sm mb-1">burndown</h2>
          <p className="mono text-[10px] uppercase tracking-widest text-text-dim mb-4">
            // remaining story points · solid = actual · dashed = ideal
          </p>
          <BurndownChart data={burndown ?? []} />
        </section>
      )}

      <section className="panel p-6">
        <h2 className="heading text-sm mb-4">velocity</h2>
        <VelocityChart data={velocity ?? []} />
        {sprint?.status !== 'completed' && (
          <p className="mono text-[10px] uppercase tracking-widest text-text-dim mt-3">
            // chart only includes sprints with status = completed
          </p>
        )}
      </section>

      <section>
        <div className="flex items-center justify-between mb-3">
          <h2 className="heading text-sm">issues in sprint</h2>
          <span className="mono text-xs text-text-muted">{(issues ?? []).length} total</span>
        </div>
        <div className="border border-border">
          <div className="grid grid-cols-12 px-4 py-2 mono text-xs uppercase tracking-widest text-text-dim border-b border-border bg-bg-subtle">
            <div className="col-span-5">title</div>
            <div className="col-span-2">assignee</div>
            <div className="col-span-1">priority</div>
            <div className="col-span-2">column</div>
            <div className="col-span-1 text-right">pts</div>
            <div className="col-span-1 text-right">status</div>
          </div>
          {(issues ?? []).length === 0 ? (
            <div className="p-6 text-center text-text-muted text-sm">
              no issues assigned to this sprint yet.
            </div>
          ) : (
            (issues ?? []).map((i) => (
              <div
                key={i.id}
                className="grid grid-cols-12 px-4 py-2 items-center border-t border-border"
              >
                <div className="col-span-5 truncate flex items-center gap-2">
                  {i.epic_color && (
                    <span
                      className="w-1.5 h-1.5 rounded-full flex-shrink-0"
                      style={{ background: i.epic_color }}
                    />
                  )}
                  <span className="truncate">{i.title}</span>
                </div>
                <div className="col-span-2 flex items-center gap-2 min-w-0">
                  <Avatar
                    id={i.assignee_id}
                    name={i.assignee_name}
                    avatarUrl={i.assignee_avatar_url}
                    size="sm"
                  />
                  <span className="mono text-xs text-text-muted truncate">
                    {i.assignee_name ?? 'unassigned'}
                  </span>
                </div>
                <div className="col-span-1 mono text-xs uppercase">{i.priority}</div>
                <div className="col-span-2 mono text-xs text-text-muted">
                  {i.column_name ?? '—'}
                </div>
                <div className="col-span-1 mono text-xs text-right">{i.story_points}</div>
                <div className="col-span-1 mono text-xs text-right">
                  {i.closed_at ? (
                    <span className="text-priority-low">closed</span>
                  ) : (
                    <span className="text-accent">open</span>
                  )}
                </div>
              </div>
            ))
          )}
        </div>
      </section>

      {editing && project && sprint && (
        <SprintModal
          projectId={project.id}
          sprint={sprint}
          onClose={() => setEditing(false)}
          onSaved={refetchAll}
        />
      )}
      {adding && project && sprint && (
        <CreateIssueModal
          projectId={project.id}
          defaultSprintId={sprint.id}
          onClose={() => setAdding(false)}
          onCreated={refetchAll}
        />
      )}
    </div>
  );
}

function fmt(d: string) {
  return new Date(d).toLocaleDateString(undefined, {
    month: 'short',
    day: 'numeric',
    year: 'numeric',
  });
}
