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

/**
 * Project-wide activity feed. Shows every logged event newest-first with
 * pagination + a type filter. Clicking a row that's tied to an issue
 * deep-links into the issues page with that issue's modal pre-opened
 * (handled by the existing `?issue=:id` query param).
 *
 * Types are derived from whatever the server has emitted so the dropdown
 * stays accurate as new event kinds get added without UI churn.
 */
export default function ActivityPage() {
  const { project } = useOutletContext<Ctx>();
  const { slug } = useParams<{ slug: string }>();
  const projectId = project?.id;
  const nav = useNavigate();

  const [skip, setSkip] = useState(0);
  const [typeFilter, setTypeFilter] = useState<string>('');

  // Reset to first page whenever the filter changes — otherwise paginating
  // out of an empty filtered set leaves the user staring at "no activity".
  useEffect(() => {
    setSkip(0);
  }, [typeFilter]);

  const { data, isLoading } = useQuery({
    queryKey: ['project-activity', projectId, skip],
    queryFn: async () => {
      const r = await api.get<Paged<ActivityRow>>(`/projects/${projectId}/activity`, {
        params: { skip, take: PAGE_SIZE },
      });
      return r.data;
    },
    enabled: !!projectId,
  });

  const items = useMemo(() => data?.items ?? [], [data]);
  const total = data?.total ?? 0;

  const filtered = useMemo(
    () => (typeFilter ? items.filter((r) => r.type === typeFilter) : items),
    [items, typeFilter],
  );

  // Build the type dropdown from what's actually been logged in this page.
  const knownTypes = useMemo(() => {
    const set = new Set(items.map((r) => r.type));
    return Array.from(set).sort();
  }, [items]);

  function open(row: ActivityRow) {
    if (!row.issueId) return;
    nav(`/p/${slug}/issues?issue=${row.issueId}`);
  }

  return (
    <div className="flex flex-col h-full">
      <header className="px-8 pt-8 pb-4 border-b border-border">
        <p className="mono text-xs uppercase tracking-widest text-text-dim">// activity</p>
        <h1 className="mono text-2xl mt-1">{project?.name ?? '…'}</h1>
        <p className="mono text-xs text-text-dim mt-1">
          Newest events across the project. {total > 0 && `${total} total.`}
        </p>

        <div className="mt-4 flex items-center gap-3">
          <label className="mono text-xs text-text-dim uppercase tracking-widest">type</label>
          <select
            className="input mono text-xs w-56"
            value={typeFilter}
            onChange={(e) => setTypeFilter(e.target.value)}
          >
            <option value="">all</option>
            {knownTypes.map((t) => (
              <option key={t} value={t}>
                {t.replace(/_/g, ' ')}
              </option>
            ))}
          </select>
          {typeFilter && (
            <button
              type="button"
              className="mono text-xs text-text-dim hover:text-accent"
              onClick={() => setTypeFilter('')}
            >
              clear
            </button>
          )}
        </div>
      </header>

      <div className="flex-1 overflow-y-auto px-8 py-6">
        {isLoading && items.length === 0 ? (
          <p className="mono text-xs text-text-dim">loading…</p>
        ) : filtered.length === 0 ? (
          <p className="mono text-xs text-text-dim">no activity to show.</p>
        ) : (
          <ol className="space-y-3 border-l border-border pl-4 max-w-3xl">
            {filtered.map((r) => (
              <li key={r.id} className="relative group">
                <span className="absolute -left-[21px] top-1.5 w-2 h-2 rounded-full bg-accent" />
                <button
                  type="button"
                  onClick={() => open(r)}
                  disabled={!r.issueId}
                  className={`block text-left w-full ${r.issueId ? 'hover:opacity-80 cursor-pointer' : 'cursor-default'}`}
                >
                  <div className="mono text-xs text-text">
                    <span>{r.actorName ?? 'system'}</span>{' '}
                    <span className="text-text-dim">{describe(r)}</span>
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

      {/* Pagination — server returns {items,total,skip,take}. Disabled
          when the filter narrows the visible set; paging there would
          desync. Clearing the filter restores the buttons. */}
      {!typeFilter && total > PAGE_SIZE && (
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

function describe(row: ActivityRow): string {
  const p = safeParse(row.payload);
  switch (row.type) {
    case 'issue_created':
      return `created an issue${p?.title ? ` — "${p.title}"` : ''}`;
    case 'issue_updated': {
      const fieldLabels: Record<string, string> = {
        columnId: 'board column',
        epicId: 'epic',
        sprintId: 'sprint',
        storyPoints: 'story points',
        assigneeId: 'assignee',
        parentId: 'parent issue',
      };
      const fields = (p?.changes as Array<{ field: string }> | undefined)
        ?.map((c) => fieldLabels[c.field] ?? c.field)
        .join(', ');
      return fields ? `updated ${fields}` : 'updated an issue';
    }
    case 'issue_closed':
      return 'closed an issue';
    case 'issue_reopened':
      return 'reopened an issue';
    case 'issue_deleted':
      return p?.title ? `deleted issue "${p.title}"` : 'deleted an issue';
    case 'comment_added':
      return p?.snippet ? `commented: "${p.snippet}"` : 'added a comment';
    case 'comment_edited':
      return 'edited a comment';
    case 'comment_deleted':
      return 'deleted a comment';
    case 'mention':
      return 'mentioned a member';
    case 'label_added':
      return p?.name ? `added label "${p.name}"` : 'added a label';
    case 'label_removed':
      return p?.name ? `removed label "${p.name}"` : 'removed a label';
    case 'member_added':
      return p?.email ? `added ${p.email} as ${p.role ?? 'member'}` : 'added a member';
    case 'member_removed':
      return 'removed a member';
    case 'member_left':
      return 'left the project';
    case 'member_role_changed':
      return p?.from && p?.to ? `changed a role: ${p.from} → ${p.to}` : 'changed a role';
    default:
      return row.type.replace(/_/g, ' ');
  }
}

function safeParse(s: string): Record<string, unknown> | null {
  try {
    return JSON.parse(s);
  } catch {
    return null;
  }
}
