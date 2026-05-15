import { useMemo, useState } from 'react';
import { Link, useNavigate, useOutletContext, useParams } from 'react-router-dom';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../lib/api';
import { toast } from '../lib/toast';
import { confirmDialog } from '../lib/confirmDialog';
import { EpicModal } from '../components/EpicModal';
import { CreateIssueModal } from '../components/CreateIssueModal';
import { IssueModal } from '../components/IssueModal';
import type { EpicWithProgress, IssueListRow, Priority, Project } from '../types';
import type { Paged } from '../types/api';

interface Ctx {
  project?: Project;
}

const PRIORITY_BADGE: Record<Priority, string> = {
  critical: 'text-priority-critical',
  high: 'text-priority-high',
  medium: 'text-text',
  low: 'text-text-dim',
};

/**
 * Detail view for a single epic. Header shows the epic's metadata + a
 * progress bar; the body lists every issue tagged with this epic.
 *
 * The list piggybacks on the existing /projects/{id}/issues endpoint with
 * the `epicId` filter — we don't need a separate backend route for this.
 */
export default function EpicDetailPage() {
  const { project } = useOutletContext<Ctx>();
  const { slug, id } = useParams<{ slug: string; id: string }>();
  const nav = useNavigate();
  const qc = useQueryClient();
  const [editing, setEditing] = useState(false);
  const [openCreate, setOpenCreate] = useState(false);
  const [openIssueId, setOpenIssueId] = useState<string | null>(null);

  const epicKey = ['epic', id];
  const { data: epicData, isLoading: epicLoading } = useQuery({
    queryKey: epicKey,
    queryFn: async () => (await api.get<{ epic: EpicWithProgress }>(`/epics/${id}`)).data.epic,
    enabled: !!id,
  });

  const issuesKey = ['issues-list', project?.id, { epicId: id }];
  const { data: issues, isLoading: issuesLoading } = useQuery({
    queryKey: issuesKey,
    queryFn: async () =>
      (
        await api.get<Paged<IssueListRow>>(`/projects/${project!.id}/issues`, {
          params: { epicId: id, take: 200 },
        })
      ).data,
    enabled: !!project && !!id,
  });

  function refetchAll() {
    qc.invalidateQueries({ queryKey: epicKey });
    qc.invalidateQueries({ queryKey: ['issues-list', project?.id] });
    qc.invalidateQueries({ queryKey: ['epics', project?.id] });
    qc.invalidateQueries({ queryKey: ['board', project?.id] });
  }

  async function handleDelete() {
    const ok = await confirmDialog({
      title: 'Delete this epic?',
      description: 'Issues attached to it will be unlinked but kept. This cannot be undone.',
      confirmLabel: 'delete epic',
      destructive: true,
    });
    if (!ok) return;
    try {
      await api.delete(`/epics/${id}`);
      toast.success('epic deleted');
      refetchAll();
      nav(`/p/${slug}/epics`);
    } catch {
      // global toast handles surface; nothing extra to do here.
    }
  }

  // Sort by closed-last so open work bubbles to the top of the list.
  const sortedIssues = useMemo(() => {
    if (!issues?.items) return [];
    return [...issues.items].sort((a, b) => {
      const ac = a.closedAt ? 1 : 0;
      const bc = b.closedAt ? 1 : 0;
      if (ac !== bc) return ac - bc;
      // Within open vs closed groups, keep server order (created_at asc).
      return 0;
    });
  }, [issues]);

  if (epicLoading || !epicData) {
    return <div className="p-8 mono text-text-dim">loading epic…</div>;
  }

  const e = epicData;
  const pct = e.percentComplete ?? 0;

  return (
    <div className="p-8 max-w-5xl">
      {/* Breadcrumb / header */}
      <div className="mb-2">
        <Link to={`/p/${slug}/epics`} className="mono text-xs text-text-dim hover:text-accent">
          ← all epics
        </Link>
      </div>

      <header className="mb-6 flex items-start justify-between gap-4">
        <div className="min-w-0">
          <p className="mono text-xs uppercase tracking-widest text-text-dim">// epic</p>
          <h1 className="mono text-2xl flex items-center gap-3 mt-1">
            <span className="w-3 h-3 rounded-full inline-block" style={{ background: e.color }} />
            <span className="truncate">{e.title}</span>
          </h1>
          {e.description && (
            <p className="mono text-sm text-text-muted mt-2 whitespace-pre-wrap">{e.description}</p>
          )}
        </div>
        <div className="flex gap-2 shrink-0">
          <button onClick={() => setEditing(true)} className="btn-ghost text-xs">
            edit
          </button>
          <button onClick={handleDelete} className="btn-ghost text-xs text-priority-critical">
            delete
          </button>
        </div>
      </header>

      {/* Stats grid */}
      <div className="grid grid-cols-2 md:grid-cols-4 gap-3 mb-6">
        <Stat label="progress" value={`${pct}%`} />
        <Stat label="issues" value={`${e.closedIssues}/${e.totalIssues}`} />
        <Stat label="points" value={`${e.completedPoints}/${e.totalPoints}`} />
        <Stat label="due" value={e.dueDate ? new Date(e.dueDate).toLocaleDateString() : '—'} />
      </div>

      <div className="h-1.5 bg-bg-subtle border border-border overflow-hidden mb-8">
        <div className="h-full transition-all" style={{ width: `${pct}%`, background: e.color }} />
      </div>

      {/* Issues list */}
      <div className="flex items-center justify-between mb-3">
        <h2 className="mono text-sm uppercase tracking-widest text-text-dim">// issues</h2>
        <button onClick={() => setOpenCreate(true)} className="btn-primary text-xs">
          + new issue in this epic
        </button>
      </div>

      <div className="panel border rounded overflow-hidden">
        {issuesLoading ? (
          <p className="mono text-text-muted p-6">loading issues…</p>
        ) : sortedIssues.length === 0 ? (
          <p className="mono text-text-dim p-6">no issues attached to this epic yet.</p>
        ) : (
          <table className="w-full mono text-xs">
            <thead className="bg-bg-soft/50 text-text-dim uppercase tracking-widest">
              <tr>
                <Th className="w-[6%]">prio</Th>
                <Th>title</Th>
                <Th className="w-[14%]">status</Th>
                <Th className="w-[14%]">sprint</Th>
                <Th className="w-[14%]">assignee</Th>
                <Th className="w-[6%] text-right">pts</Th>
              </tr>
            </thead>
            <tbody>
              {sortedIssues.map((row) => {
                const closed = row.closedAt !== null;
                return (
                  <tr
                    key={row.id}
                    onClick={() => setOpenIssueId(row.id)}
                    className="border-t border-border hover:bg-bg-soft/40 cursor-pointer"
                  >
                    <td className={`px-3 py-2 ${PRIORITY_BADGE[row.priority]}`}>
                      {row.priority[0].toUpperCase()}
                    </td>
                    <td className={`px-3 py-2 ${closed ? 'text-text-dim line-through' : ''}`}>
                      {row.title}
                    </td>
                    <td className="px-3 py-2 text-text-dim">
                      {closed ? 'closed' : (row.columnName ?? 'open')}
                    </td>
                    <td className="px-3 py-2">
                      {row.sprintName ?? <span className="text-text-dim">—</span>}
                    </td>
                    <td className="px-3 py-2">
                      {row.assigneeName ?? <span className="text-text-dim">unassigned</span>}
                    </td>
                    <td className="px-3 py-2 text-right">
                      {row.storyPoints > 0 ? (
                        row.storyPoints
                      ) : (
                        <span className="text-text-dim">—</span>
                      )}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        )}
      </div>

      {editing && project && (
        <EpicModal
          projectId={project.id}
          epic={e}
          onClose={() => setEditing(false)}
          onSaved={() => {
            setEditing(false);
            refetchAll();
          }}
        />
      )}

      {openCreate && project && (
        <CreateIssueModal
          projectId={project.id}
          defaultEpicId={e.id}
          defaultSprintId={null}
          onClose={() => setOpenCreate(false)}
          onCreated={refetchAll}
        />
      )}

      {openIssueId && (
        <IssueModal
          issueId={openIssueId}
          onClose={() => setOpenIssueId(null)}
          onChange={refetchAll}
        />
      )}
    </div>
  );
}

function Stat({ label, value }: { label: string; value: string }) {
  return (
    <div className="border border-border bg-bg-panel p-3">
      <p className="mono text-[10px] uppercase tracking-widest text-text-dim">{label}</p>
      <p className="mono text-lg mt-1">{value}</p>
    </div>
  );
}

function Th({ children, className = '' }: { children: React.ReactNode; className?: string }) {
  return <th className={`text-left px-3 py-2 font-normal ${className}`}>{children}</th>;
}
