import { useMemo, useState, useEffect } from 'react';
import { useOutletContext, useSearchParams } from 'react-router-dom';
import { useQuery, useQueryClient, keepPreviousData } from '@tanstack/react-query';
import { Layers } from 'lucide-react';
import { api } from '../lib/api';
import { toast } from '../lib/toast';
import { confirmDialog } from '../lib/confirmDialog';
import type {
  EpicWithProgress,
  IssueListRow,
  Label,
  Priority,
  Project,
  ProjectMember,
  Sprint,
} from '../types';
import type { Paged } from '../types/api';
import { IssueModal } from '../components/IssueModal';
import { CreateIssueModal } from '../components/CreateIssueModal';
import { Avatar } from '../components/Avatar';

interface Ctx {
  project?: Project;
}

// Sentinel for "no <thing> attached" — matches the empty-Guid contract on
// the backend (see IssueQueries.ListByProject + UpdateIssueRequest).
const NONE_GUID = '00000000-0000-0000-0000-000000000000';

type StatusFilter = 'all' | 'open' | 'closed';
type PriorityFilter = 'all' | Priority;

interface Filters {
  status: StatusFilter;
  priority: PriorityFilter;
  epicId: string; // '', NONE_GUID, or a real id
  sprintId: string;
  assigneeId: string;
  labelId: string;
  search: string;
  showSubIssues: boolean;
}

const DEFAULT_FILTERS: Filters = {
  status: 'open',
  priority: 'all',
  epicId: '',
  sprintId: '',
  assigneeId: '',
  labelId: '',
  search: '',
  showSubIssues: false,
};

interface IssuesResponse {
  items: IssueListRow[];
  total: number;
  skip: number;
  take: number;
}

/**
 * Project-wide issue listing. The board is great for kanban flow but
 * doesn't help when you want to ask "what's still open assigned to me with
 * the 'blocked' label?" — that's what this page is for. All filters live in
 * component state (intentionally not in the URL yet; can promote to query
 * params later if sharing links matters).
 */
export default function IssuesPage() {
  const { project } = useOutletContext<Ctx>();
  const qc = useQueryClient();
  const [filters, setFilters] = useState<Filters>(DEFAULT_FILTERS);
  const [page, setPage] = useState(0);
  const take = 50;
  const [openIssueId, setOpenIssueId] = useState<string | null>(null);
  const [openCreate, setOpenCreate] = useState(false);

  // Multi-select for bulk actions. Only rows currently visible on the page
  // can be selected (we deliberately don't carry selection across pages —
  // that gets surprising fast). Toggling a filter clears the set.
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [bulkBusy, setBulkBusy] = useState(false);
  const [bulkSprintId, setBulkSprintId] = useState('');
  const [bulkLabelId, setBulkLabelId] = useState('');
  const [bulkAssigneeId, setBulkAssigneeId] = useState('');

  // Deep-link support: `?issue=:id` opens the issue modal directly,
  // `?labelId=:id` pre-selects the label filter. Used by the activity feed
  // and label chips. Cleared from the URL on close so back-button works.
  const [searchParams, setSearchParams] = useSearchParams();
  useEffect(() => {
    const issueParam = searchParams.get('issue');
    if (issueParam && issueParam !== openIssueId) setOpenIssueId(issueParam);
    const labelParam = searchParams.get('labelId');
    if (labelParam) {
      setFilters((f) => (f.labelId === labelParam ? f : { ...f, labelId: labelParam }));
    }
    // Run once per URL change.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [searchParams]);

  function closeIssue() {
    setOpenIssueId(null);
    if (searchParams.has('issue')) {
      const next = new URLSearchParams(searchParams);
      next.delete('issue');
      setSearchParams(next, { replace: true });
    }
  }

  function update<K extends keyof Filters>(key: K, value: Filters[K]) {
    setFilters((f) => ({ ...f, [key]: value }));
    setPage(0); // any filter change resets to the first page
    setSelected(new Set()); // and clears any in-progress selection
  }

  // Build params for the API. Empty strings → omit; the empty-Guid sentinel
  // is forwarded as-is so the backend can pick the "no foo" branch.
  const queryParams = useMemo(() => {
    const p: Record<string, string | number> = { skip: page * take, take };
    if (filters.status !== 'all') p.status = filters.status;
    if (filters.priority !== 'all') p.priority = filters.priority;
    if (filters.epicId) p.epicId = filters.epicId;
    if (filters.sprintId) p.sprintId = filters.sprintId;
    if (filters.assigneeId) p.assigneeId = filters.assigneeId;
    if (filters.labelId) p.labelId = filters.labelId;
    if (filters.search.trim()) p.search = filters.search.trim();

    // If not showing sub-issues, filter for top-level only (parentId = NONE_GUID)
    if (!filters.showSubIssues) p.parentId = NONE_GUID;

    return p;
  }, [filters, page]);

  const queryKey = ['issues-list', project?.id, queryParams];
  const { data, isLoading, isFetching } = useQuery({
    queryKey,
    queryFn: async () =>
      (
        await api.get<IssuesResponse>(`/projects/${project!.id}/issues`, {
          params: queryParams,
        })
      ).data,
    enabled: !!project,
    placeholderData: keepPreviousData,
  });

  const { data: epics } = useQuery({
    queryKey: ['epics', project?.id],
    queryFn: async () =>
      (await api.get<Paged<EpicWithProgress>>(`/projects/${project!.id}/epics`)).data.items,
    enabled: !!project,
  });
  const { data: sprints } = useQuery({
    queryKey: ['sprints', project?.id],
    queryFn: async () =>
      (await api.get<Paged<Sprint>>(`/projects/${project!.id}/sprints`)).data.items,
    enabled: !!project,
  });
  const { data: labels } = useQuery({
    queryKey: ['labels', project?.id],
    queryFn: async () =>
      (await api.get<{ items: Label[] }>(`/projects/${project!.id}/labels`)).data.items,
    enabled: !!project,
  });
  const { data: members } = useQuery({
    queryKey: ['members', project?.id],
    queryFn: async () =>
      (await api.get<{ items: ProjectMember[] }>(`/projects/${project!.id}/members`)).data.items,
    enabled: !!project,
  });

  function refetch() {
    qc.invalidateQueries({ queryKey: ['issues-list', project?.id] });
    qc.invalidateQueries({ queryKey: ['board', project?.id] });
  }

  // ----- Bulk actions -----
  // We fan out one HTTP call per selected issue and Promise.allSettled the
  // results. Partial failures show a count rather than blocking the whole
  // batch — server-side authz failures shouldn't undo the others.
  const visibleIds = data?.items.map((i) => i.id) ?? [];
  const allVisibleSelected = visibleIds.length > 0 && visibleIds.every((id) => selected.has(id));

  function toggleRow(id: string) {
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  function toggleAllVisible() {
    setSelected((prev) => {
      if (allVisibleSelected) {
        const next = new Set(prev);
        for (const id of visibleIds) next.delete(id);
        return next;
      }
      const next = new Set(prev);
      for (const id of visibleIds) next.add(id);
      return next;
    });
  }

  async function runBulk(label: string, fn: (id: string) => Promise<unknown>) {
    setBulkBusy(true);
    try {
      const ids = Array.from(selected);
      const results = await Promise.allSettled(ids.map(fn));
      const ok = results.filter((r) => r.status === 'fulfilled').length;
      const fail = results.length - ok;
      if (fail === 0) toast.success(`${label}: ${ok} issue${ok === 1 ? '' : 's'}`);
      else toast.error(`${label}: ${ok} ok, ${fail} failed`);
      setSelected(new Set());
      refetch();
    } finally {
      setBulkBusy(false);
    }
  }

  async function bulkSetSprint() {
    if (!bulkSprintId) return;
    // Empty-Guid sentinel = clear sprint, anything else = assign.
    const sprintId = bulkSprintId === NONE_GUID ? null : bulkSprintId;
    await runBulk('moved', (id) => api.patch(`/issues/${id}`, { sprintId }));
    setBulkSprintId('');
  }

  async function bulkApplyLabel() {
    if (!bulkLabelId) return;
    await runBulk('labeled', (id) => api.post(`/issues/${id}/labels`, { labelId: bulkLabelId }));
    setBulkLabelId('');
  }

  async function bulkAssign() {
    if (!bulkAssigneeId) return;
    const assigneeId = bulkAssigneeId === NONE_GUID ? null : bulkAssigneeId;
    await runBulk('assigned', (id) => api.patch(`/issues/${id}`, { assigneeId }));
    setBulkAssigneeId('');
  }

  async function bulkClose(closed: boolean) {
    await runBulk(closed ? 'closed' : 'reopened', (id) => api.patch(`/issues/${id}`, { closed }));
  }

  async function bulkDelete() {
    const n = selected.size;
    const ok = await confirmDialog({
      title: `Delete ${n} issue${n === 1 ? '' : 's'}?`,
      description: 'This cannot be undone. Comments and label links will also be removed.',
      confirmLabel: 'delete',
      destructive: true,
    });
    if (!ok) return;
    await runBulk('deleted', (id) => api.delete(`/issues/${id}`));
  }

  const totalPages = data ? Math.max(1, Math.ceil(data.total / take)) : 1;

  return (
    <div className="p-8 max-w-7xl">
      <div className="mb-6 flex items-end justify-between">
        <div>
          <p className="mono text-xs uppercase tracking-widest text-text-dim">// issues</p>
          <h1 className="mono text-2xl">{project?.name}</h1>
          <p className="mono text-xs text-text-dim mt-1">
            search and filter every issue in the project.
          </p>
        </div>
        <button className="btn-primary" disabled={!project} onClick={() => setOpenCreate(true)}>
          + new issue
        </button>
      </div>

      {/* Filter bar */}
      <div className="panel border rounded p-3 mb-4 grid grid-cols-1 md:grid-cols-6 gap-2">
        <input
          className="input mono md:col-span-2"
          placeholder="search title…"
          value={filters.search}
          onChange={(e) => update('search', e.target.value)}
        />
        <Select
          value={filters.status}
          onChange={(v) => update('status', v as StatusFilter)}
          options={[
            ['open', 'open'],
            ['closed', 'closed'],
            ['all', 'all status'],
          ]}
        />
        <Select
          value={filters.priority}
          onChange={(v) => update('priority', v as PriorityFilter)}
          options={[
            ['all', 'any priority'],
            ['critical', 'critical'],
            ['high', 'high'],
            ['medium', 'medium'],
            ['low', 'low'],
          ]}
        />
        <Select
          value={filters.epicId}
          onChange={(v) => update('epicId', v)}
          options={[
            ['', 'any epic'],
            [NONE_GUID, '— no epic'],
            ...(epics ?? []).map((e) => [e.id, e.title] as [string, string]),
          ]}
        />
        <Select
          value={filters.sprintId}
          onChange={(v) => update('sprintId', v)}
          options={[
            ['', 'any sprint'],
            [NONE_GUID, '— no sprint'],
            ...(sprints ?? []).map((s) => [s.id, s.name] as [string, string]),
          ]}
        />
        <Select
          value={filters.assigneeId}
          onChange={(v) => update('assigneeId', v)}
          options={[
            ['', 'anyone'],
            [NONE_GUID, '— unassigned'],
            ...(members ?? []).map((m) => [m.id, m.name] as [string, string]),
          ]}
        />
        <Select
          value={filters.labelId}
          onChange={(v) => update('labelId', v)}
          options={[
            ['', 'any label'],
            ...(labels ?? []).map((l) => [l.id, l.name] as [string, string]),
          ]}
        />
        <div className="flex items-center gap-2 px-2">
          <input
            type="checkbox"
            id="showSubIssues"
            checked={filters.showSubIssues}
            onChange={(e) => update('showSubIssues', e.target.checked)}
          />
          <label htmlFor="showSubIssues" className="mono text-[10px] uppercase cursor-pointer">
            show sub-issues
          </label>
        </div>
        <button
          className="btn-ghost text-xs mono"
          onClick={() => {
            setFilters(DEFAULT_FILTERS);
            setPage(0);
          }}
        >
          reset
        </button>
      </div>

      {/* Result count + pagination */}
      <div className="flex items-center justify-between mb-2">
        <p className="mono text-xs text-text-dim">
          {data
            ? `${data.total} issue${data.total === 1 ? '' : 's'}${
                isFetching ? ' (refreshing…)' : ''
              }`
            : 'loading…'}
        </p>
        <div className="flex items-center gap-2">
          <button
            className="btn-ghost text-xs mono"
            disabled={page === 0}
            onClick={() => setPage((p) => Math.max(0, p - 1))}
          >
            ← prev
          </button>
          <span className="mono text-[11px] text-text-dim">
            page {page + 1} / {totalPages}
          </span>
          <button
            className="btn-ghost text-xs mono"
            disabled={!data || page + 1 >= totalPages}
            onClick={() => setPage((p) => p + 1)}
          >
            next →
          </button>
        </div>
      </div>

      {/* Bulk action bar — only visible when at least one row is checked. */}
      {selected.size > 0 && (
        <div className="panel border rounded p-3 mb-2 flex flex-wrap items-center gap-2 bg-bg-soft/40">
          <span className="mono text-xs text-text">{selected.size} selected</span>

          <select
            className="input mono text-xs py-1.5"
            value={bulkSprintId}
            onChange={(e) => setBulkSprintId(e.target.value)}
            disabled={bulkBusy}
          >
            <option value="">move to sprint…</option>
            <option value={NONE_GUID}>— no sprint</option>
            {(sprints ?? []).map((s) => (
              <option key={s.id} value={s.id}>
                {s.name}
              </option>
            ))}
          </select>
          <button
            className="btn-ghost text-xs mono"
            disabled={!bulkSprintId || bulkBusy}
            onClick={bulkSetSprint}
          >
            apply
          </button>

          <select
            className="input mono text-xs py-1.5"
            value={bulkLabelId}
            onChange={(e) => setBulkLabelId(e.target.value)}
            disabled={bulkBusy}
          >
            <option value="">add label…</option>
            {(labels ?? []).map((l) => (
              <option key={l.id} value={l.id}>
                {l.name}
              </option>
            ))}
          </select>
          <button
            className="btn-ghost text-xs mono"
            disabled={!bulkLabelId || bulkBusy}
            onClick={bulkApplyLabel}
          >
            apply
          </button>

          <select
            className="input mono text-xs py-1.5"
            value={bulkAssigneeId}
            onChange={(e) => setBulkAssigneeId(e.target.value)}
            disabled={bulkBusy}
          >
            <option value="">assign to…</option>
            <option value={NONE_GUID}>— unassigned</option>
            {(members ?? []).map((m) => (
              <option key={m.id} value={m.id}>
                {m.name}
              </option>
            ))}
          </select>
          <button
            className="btn-ghost text-xs mono"
            disabled={!bulkAssigneeId || bulkBusy}
            onClick={bulkAssign}
          >
            apply
          </button>

          <button
            className="btn-ghost text-xs mono"
            disabled={bulkBusy}
            onClick={() => bulkClose(true)}
          >
            close
          </button>
          <button
            className="btn-ghost text-xs mono"
            disabled={bulkBusy}
            onClick={() => bulkClose(false)}
          >
            reopen
          </button>
          <button
            className="btn-ghost text-xs mono text-priority-critical"
            disabled={bulkBusy}
            onClick={bulkDelete}
          >
            delete
          </button>

          <button
            className="btn-ghost text-xs mono ml-auto"
            disabled={bulkBusy}
            onClick={() => setSelected(new Set())}
          >
            clear selection
          </button>
        </div>
      )}

      {/* Table */}
      <div className="panel border rounded overflow-hidden">
        {isLoading ? (
          <p className="mono text-text-muted p-6">loading…</p>
        ) : !data || data.items.length === 0 ? (
          <p className="mono text-text-dim p-6">no issues match these filters.</p>
        ) : (
          <table className="w-full mono text-xs">
            <thead className="bg-bg-soft/50 text-text-dim uppercase tracking-widest">
              <tr>
                <Th className="w-[3%]">
                  <input
                    type="checkbox"
                    checked={allVisibleSelected}
                    onChange={toggleAllVisible}
                    aria-label="select all visible"
                  />
                </Th>
                <Th className="w-[6%]">prio</Th>
                <Th>title</Th>
                <Th className="w-[12%]">status</Th>
                <Th className="w-[12%]">epic</Th>
                <Th className="w-[12%]">sprint</Th>
                <Th className="w-[12%]">assignee</Th>
                <Th className="w-[6%] text-right">pts</Th>
              </tr>
            </thead>
            <tbody>
              {data.items.map((row) => (
                <IssueRow
                  key={row.id}
                  row={row}
                  selected={selected.has(row.id)}
                  onToggleSelect={() => toggleRow(row.id)}
                  onClick={() => setOpenIssueId(row.id)}
                />
              ))}
            </tbody>
          </table>
        )}
      </div>

      {openIssueId && <IssueModal issueId={openIssueId} onClose={closeIssue} onChange={refetch} />}
      {openCreate && project && (
        <CreateIssueModal
          projectId={project.id}
          defaultEpicId={filters.epicId && filters.epicId !== NONE_GUID ? filters.epicId : null}
          defaultSprintId={
            filters.sprintId && filters.sprintId !== NONE_GUID ? filters.sprintId : null
          }
          onClose={() => setOpenCreate(false)}
          onCreated={refetch}
        />
      )}
    </div>
  );
}

// ---------- Sub-components ----------

function Select({
  value,
  onChange,
  options,
}: {
  value: string;
  onChange: (v: string) => void;
  options: [string, string][];
}) {
  return (
    <select className="input mono" value={value} onChange={(e) => onChange(e.target.value)}>
      {options.map(([v, label]) => (
        <option key={v} value={v}>
          {label}
        </option>
      ))}
    </select>
  );
}

function Th({ children, className = '' }: { children: React.ReactNode; className?: string }) {
  return <th className={`text-left px-3 py-2 font-normal ${className}`}>{children}</th>;
}

const PRIORITY_BADGE: Record<Priority, string> = {
  critical: 'text-priority-critical',
  high: 'text-priority-high',
  medium: 'text-text',
  low: 'text-text-dim',
};

function IssueRow({
  row,
  selected,
  onToggleSelect,
  onClick,
}: {
  row: IssueListRow;
  selected: boolean;
  onToggleSelect: () => void;
  onClick: () => void;
}) {
  const labels = parseLabels(row.labelsJson);
  const closed = row.closedAt !== null;
  const [, setSearchParams] = useSearchParams();

  // Click on a label chip should filter the table to that label rather than
  // open the issue. We stop propagation so the row's onClick doesn't fire.
  function applyLabelFilter(e: React.MouseEvent, labelId: string) {
    e.stopPropagation();
    setSearchParams((prev) => {
      const next = new URLSearchParams(prev);
      next.set('labelId', labelId);
      return next;
    });
  }

  // Checkbox cell stops propagation too — clicking the box should NOT
  // open the issue modal.
  function onCheckboxClick(e: React.MouseEvent) {
    e.stopPropagation();
  }

  return (
    <tr
      onClick={onClick}
      className={`border-t border-border hover:bg-bg-soft/40 cursor-pointer ${
        selected ? 'bg-accent/5' : ''
      }`}
    >
      <td className="px-3 py-2" onClick={onCheckboxClick}>
        <input
          type="checkbox"
          checked={selected}
          onChange={onToggleSelect}
          aria-label="select issue"
        />
      </td>
      <td className={`px-3 py-2 ${PRIORITY_BADGE[row.priority]}`}>
        {row.priority[0].toUpperCase()}
      </td>
      <td className="px-3 py-2">
        <div className={`flex items-center gap-2 ${closed ? 'text-text-dim line-through' : ''}`}>
          {row.parentId && <Layers size={10} className="text-accent shrink-0" />}
          <span className="mono text-[10px] text-text-dim shrink-0">#{row.number}</span>
          <span className="truncate">{row.title}</span>
          {labels.length > 0 && (
            <span className="flex gap-1 shrink-0">
              {labels.slice(0, 3).map((l) => (
                <button
                  key={l.id}
                  type="button"
                  onClick={(e) => applyLabelFilter(e, l.id)}
                  title={`filter by ${l.name}`}
                  className="text-[10px] uppercase tracking-wider px-1.5 py-0.5 rounded-full border hover:opacity-80 transition-opacity"
                  style={{
                    borderColor: l.color,
                    color: l.color,
                    background: `${l.color}1a`,
                  }}
                >
                  {l.name}
                </button>
              ))}
              {labels.length > 3 && (
                <span className="text-[10px] text-text-dim">+{labels.length - 3}</span>
              )}
            </span>
          )}
        </div>
      </td>
      <td className="px-3 py-2 text-text-dim">{closed ? 'closed' : (row.columnName ?? 'open')}</td>
      <td className="px-3 py-2">
        {row.epicTitle ? (
          <span className="inline-flex items-center gap-1.5">
            <span
              className="w-2 h-2 rounded-full inline-block"
              style={{ background: row.epicColor ?? '#64748b' }}
            />
            <span className="truncate">{row.epicTitle}</span>
          </span>
        ) : (
          <span className="text-text-dim">—</span>
        )}
      </td>
      <td className="px-3 py-2">{row.sprintName ?? <span className="text-text-dim">—</span>}</td>
      <td className="px-3 py-2">
        {row.assigneeId ? (
          <span className="inline-flex items-center gap-1.5">
            <Avatar
              id={row.assigneeId}
              name={row.assigneeName}
              avatarUrl={row.assigneeAvatarUrl}
              size="sm"
            />
            <span className="truncate">{row.assigneeName ?? '—'}</span>
          </span>
        ) : (
          <span className="text-text-dim">unassigned</span>
        )}
      </td>
      <td className="px-3 py-2 text-right">{row.storyPoints || ''}</td>
    </tr>
  );
}

function parseLabels(json: string): { id: string; name: string; color: string }[] {
  try {
    const parsed = JSON.parse(json);
    return Array.isArray(parsed) ? parsed : [];
  } catch {
    return [];
  }
}
