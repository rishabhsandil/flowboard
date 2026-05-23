import { useEffect, useMemo, useState } from 'react';
import { useOutletContext, useNavigate, useParams } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { api } from '../lib/api';
import type { ActivityRow, Project } from '../types';
import type { Paged } from '../types/api';

interface Ctx {
  project?: Project;
}

const PAGE_SIZE = 50;

const KNOWN_TYPES = [
  'issue_created',
  'issue_updated',
  'issue_moved',
  'issue_closed',
  'issue_reopened',
  'issue_deleted',
  'comment_added',
  'comment_edited',
  'comment_deleted',
  'mention',
  'label_added',
  'label_removed',
  'member_added',
  'member_removed',
  'member_left',
  'member_role_changed',
].sort();

/**
 * Project-wide activity feed. Shows every logged event newest-first with
 * pagination + type, actor, and date filters. Clicking a row that's tied to an issue
 * deep-links into the issues page with that issue's modal pre-opened.
 */
export default function ActivityPage() {
  const { project } = useOutletContext<Ctx>();
  const { slug } = useParams<{ slug: string }>();
  const projectId = project?.id;
  const nav = useNavigate();

  const [skip, setSkip] = useState(0);
  const [typeFilter, setTypeFilter] = useState<string>('');
  const [actorId, setActorId] = useState<string>('');
  const [startDate, setStartDate] = useState<string>('');
  const [endDate, setEndDate] = useState<string>('');

  // Reset to first page whenever any filter changes
  useEffect(() => {
    setSkip(0);
  }, [typeFilter, actorId, startDate, endDate]);

  const { data: members } = useQuery({
    queryKey: ['project-members', projectId],
    queryFn: async () => {
      const r = await api.get<{ items: { id: string; name: string }[] }>(
        `/projects/${projectId}/members`,
      );
      return r.data.items;
    },
    enabled: !!projectId,
  });

  const { data: epics } = useQuery({
    queryKey: ['epics', projectId],
    queryFn: async () =>
      (await api.get<{ items: { id: string; title: string }[] }>(`/projects/${projectId}/epics`))
        .data.items,
    enabled: !!projectId,
  });

  const { data: sprints } = useQuery({
    queryKey: ['sprints', projectId],
    queryFn: async () =>
      (await api.get<{ items: { id: string; name: string }[] }>(`/projects/${projectId}/sprints`))
        .data.items,
    enabled: !!projectId,
  });

  const { data: board } = useQuery({
    queryKey: ['board', projectId],
    queryFn: async () =>
      (await api.get<{ columns: { id: string; name: string }[] }>(`/projects/${projectId}/board`))
        .data,
    enabled: !!projectId,
    retry: false, // In case board is missing
  });

  const idToName = useMemo(() => {
    const map = new Map<string, string>();
    members?.forEach((m) => map.set(m.id, m.name));
    epics?.forEach((e) => map.set(e.id, e.title));
    sprints?.forEach((s) => map.set(s.id, s.name));
    board?.columns?.forEach((c) => map.set(c.id, c.name));
    return map;
  }, [members, epics, sprints, board]);

  const { data, isLoading } = useQuery({
    queryKey: ['project-activity', projectId, skip, typeFilter, actorId, startDate, endDate],
    queryFn: async () => {
      const params: Record<string, any> = { skip, take: PAGE_SIZE };
      if (typeFilter) params.type = typeFilter;
      if (actorId) params.actorId = actorId;
      if (startDate) params.startDate = new Date(startDate).toISOString();
      if (endDate) {
        const ed = new Date(endDate);
        ed.setHours(23, 59, 59, 999);
        params.endDate = ed.toISOString();
      }
      const r = await api.get<Paged<ActivityRow>>(`/projects/${projectId}/activity`, { params });
      return r.data;
    },
    enabled: !!projectId,
  });

  const items = useMemo(() => data?.items ?? [], [data]);
  const total = data?.total ?? 0;

  function open(row: ActivityRow) {
    if (!row.issueId) return;
    nav(`/p/${slug}/issues?issue=${row.issueId}`);
  }

  function clearFilters() {
    setTypeFilter('');
    setActorId('');
    setStartDate('');
    setEndDate('');
  }

  const hasFilters = typeFilter || actorId || startDate || endDate;

  return (
    <div className="flex flex-col h-full">
      <header className="px-8 pt-8 pb-4 border-b border-border">
        <p className="mono text-xs uppercase tracking-widest text-text-dim">// activity</p>
        <h1 className="mono text-2xl mt-1">{project?.name ?? '…'}</h1>
        <p className="mono text-xs text-text-dim mt-1">
          Newest events across the project. {total > 0 && `${total} matching events.`}
        </p>

        <div className="mt-4 flex flex-wrap items-center gap-3">
          <label className="mono text-xs text-text-dim uppercase tracking-widest">actor</label>
          <select
            className="input mono text-xs w-48"
            value={actorId}
            onChange={(e) => setActorId(e.target.value)}
          >
            <option value="">all</option>
            {members?.map((m) => (
              <option key={m.id} value={m.id}>
                {m.name}
              </option>
            ))}
          </select>

          <label className="mono text-xs text-text-dim uppercase tracking-widest ml-2">type</label>
          <select
            className="input mono text-xs w-48"
            value={typeFilter}
            onChange={(e) => setTypeFilter(e.target.value)}
          >
            <option value="">all</option>
            {KNOWN_TYPES.map((t) => (
              <option key={t} value={t}>
                {t.replace(/_/g, ' ')}
              </option>
            ))}
          </select>

          <label className="mono text-xs text-text-dim uppercase tracking-widest ml-2">from</label>
          <input
            type="date"
            className="input mono text-xs w-32"
            value={startDate}
            onChange={(e) => setStartDate(e.target.value)}
          />

          <label className="mono text-xs text-text-dim uppercase tracking-widest ml-2">to</label>
          <input
            type="date"
            className="input mono text-xs w-32"
            value={endDate}
            onChange={(e) => setEndDate(e.target.value)}
          />

          {hasFilters && (
            <button
              type="button"
              className="mono text-xs text-text-dim hover:text-accent ml-2"
              onClick={clearFilters}
            >
              clear filters
            </button>
          )}
        </div>
      </header>

      <div className="flex-1 overflow-y-auto px-8 py-6">
        {isLoading && items.length === 0 ? (
          <p className="mono text-xs text-text-dim">loading…</p>
        ) : items.length === 0 ? (
          <p className="mono text-xs text-text-dim">no activity to show.</p>
        ) : (
          <ol className="space-y-3 border-l border-border pl-4 max-w-3xl">
            {items.map((r) => (
              <li key={r.id} className="relative group">
                <span className="absolute -left-[21px] top-1.5 w-2 h-2 rounded-full bg-accent" />
                <button
                  type="button"
                  onClick={() => open(r)}
                  disabled={!r.issueId}
                  className={`block text-left w-full ${r.issueId ? 'hover:opacity-80 cursor-pointer' : 'cursor-default'}`}
                >
                  <div className="mono text-xs text-text flex items-center gap-1 flex-wrap">
                    <span>{r.actorName ?? 'system'}</span>{' '}
                    <span className="text-text-dim flex items-center">
                      {(() => {
                        const desc = describe(r, idToName);
                        return (
                          <>
                            {desc.text}
                            {desc.details && (
                              <span
                                className="relative group/tooltip inline-block ml-1 cursor-help"
                                onClick={(e) => {
                                  e.preventDefault();
                                  e.stopPropagation();
                                }}
                              >
                                <svg
                                  className="w-3 h-3 text-text-dim hover:text-accent"
                                  fill="none"
                                  viewBox="0 0 24 24"
                                  stroke="currentColor"
                                >
                                  <path
                                    strokeLinecap="round"
                                    strokeLinejoin="round"
                                    strokeWidth={2}
                                    d="M13 16h-1v-4h-1m1-4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z"
                                  />
                                </svg>
                                <span className="absolute bottom-full mb-1 left-1/2 -translate-x-1/2 w-max max-w-xs bg-bg-panel border border-border p-2 rounded shadow-lg opacity-0 group-hover/tooltip:opacity-100 pointer-events-none transition-opacity z-10 text-left whitespace-pre-wrap text-text">
                                  {desc.details}
                                </span>
                              </span>
                            )}
                          </>
                        );
                      })()}
                    </span>
                  </div>
                  <div className="mono text-[10px] text-text-dim mt-0.5">
                    {new Date(r.createdAt).toLocaleString()}
                    {r.issueId && (
                      <span className="ml-2 opacity-0 group-hover:opacity-100 transition-opacity text-accent">
                        open →
                      </span>
                    )}
                  </div>
                </button>
              </li>
            ))}
          </ol>
        )}
      </div>

      {total > PAGE_SIZE && (
        <footer className="px-8 py-3 border-t border-border flex items-center justify-between">
          <p className="mono text-xs text-text-dim">
            showing {skip + 1}–{Math.min(skip + PAGE_SIZE, total)} of {total}
          </p>
          <div className="flex gap-2">
            <button
              type="button"
              className="btn-ghost text-xs"
              disabled={skip === 0}
              onClick={() => setSkip((s) => Math.max(0, s - PAGE_SIZE))}
            >
              ← prev
            </button>
            <button
              type="button"
              className="btn-ghost text-xs"
              disabled={skip + PAGE_SIZE >= total}
              onClick={() => setSkip((s) => s + PAGE_SIZE)}
            >
              next →
            </button>
          </div>
        </footer>
      )}
    </div>
  );
}

function describe(
  row: ActivityRow,
  idToName: Map<string, string>,
): { text: string; details?: string } {
  const p = safeParse(row.payload);
  const textOnly = (text: string) => ({ text });

  switch (row.type) {
    case 'issue_created':
      return textOnly(`created an issue${p?.title ? ` — "${p.title}"` : ''}`);
    case 'issue_moved': {
      const from =
        (p?.fromColumnName as string | undefined) ??
        (p?.fromColumnId ? String(p.fromColumnId).slice(0, 8) : 'none');
      const to =
        (p?.toColumnName as string | undefined) ??
        (p?.toColumnId ? String(p.toColumnId).slice(0, 8) : 'none');
      return { text: `moved an issue: ${from} → ${to}` };
    }
    case 'issue_updated': {
      const fieldLabels: Record<string, string> = {
        epicId: 'epic',
        sprintId: 'sprint',
        storyPoints: 'story points',
        assigneeId: 'assignee',
        parentId: 'parent issue',
      };

      const formatValue = (val: unknown) => {
        if (val === null || val === undefined) return 'none';
        const str = String(val);
        if (/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(str)) {
          return idToName.get(str) ?? str.slice(0, 8);
        }
        return str.length > 20 ? str.slice(0, 20) + '…' : str;
      };

      const changes = p?.changes as
        | Array<{ field: string; from?: unknown; to?: unknown }>
        | undefined;
      if (!changes || changes.length === 0) return textOnly('updated an issue');

      const fieldNames = changes.map((c) => fieldLabels[c.field] ?? c.field).join(', ');

      const details = changes
        .filter((c) => c.from !== undefined || c.to !== undefined)
        .map(
          (c) =>
            `${fieldLabels[c.field] ?? c.field}: ${formatValue(c.from)} → ${formatValue(c.to)}`,
        )
        .join('\n');

      return {
        text: fieldNames ? `updated ${fieldNames}` : 'updated an issue',
        details: details || undefined,
      };
    }
    case 'issue_closed':
      return textOnly('closed an issue');
    case 'issue_reopened':
      return textOnly('reopened an issue');
    case 'issue_deleted':
      return textOnly(p?.title ? `deleted issue "${p.title}"` : 'deleted an issue');
    case 'comment_added':
      return textOnly(p?.snippet ? `commented: "${p.snippet}"` : 'added a comment');
    case 'comment_edited':
      return textOnly('edited a comment');
    case 'comment_deleted':
      return textOnly('deleted a comment');
    case 'mention':
      return textOnly('mentioned a member');
    case 'label_added':
      return textOnly(p?.name ? `added label "${p.name}"` : 'added a label');
    case 'label_removed':
      return textOnly(p?.name ? `removed label "${p.name}"` : 'removed a label');
    case 'member_added':
      return textOnly(p?.email ? `added ${p.email} as ${p.role ?? 'member'}` : 'added a member');
    case 'member_removed':
      return textOnly('removed a member');
    case 'member_left':
      return textOnly('left the project');
    case 'member_role_changed':
      return textOnly(p?.from && p?.to ? `changed a role: ${p.from} → ${p.to}` : 'changed a role');
    default:
      return textOnly(row.type.replace(/_/g, ' '));
  }
}

function safeParse(s: string): Record<string, unknown> | null {
  try {
    return JSON.parse(s);
  } catch {
    return null;
  }
}
