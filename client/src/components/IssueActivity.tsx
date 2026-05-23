import { useEffect, useState } from 'react';
import { api } from '../lib/api';
import type { ActivityRow } from '../types';
import type { Paged } from '../types/api';

interface Props {
  issueId: string;
  /** Bumped from the parent after any mutation so this view re-fetches. */
  refreshKey?: number;
}

/**
 * Activity feed for one issue. Reads /issues/:id/activity (newest first) and
 * renders a one-line summary per row. The server stores `payload` as JSONB
 * and returns it as a string here; we parse it lazily so a malformed row
 * never breaks the whole list.
 */
export function IssueActivity({ issueId, refreshKey }: Props) {
  const [rows, setRows] = useState<ActivityRow[] | null>(null);

  useEffect(() => {
    let cancelled = false;
    api
      .get<Paged<ActivityRow>>(`/issues/${issueId}/activity`, {
        params: { skip: 0, take: 50 },
      })
      .then((r) => {
        if (!cancelled) setRows(r.data.items);
      });
    return () => {
      cancelled = true;
    };
  }, [issueId, refreshKey]);

  if (rows === null) return <p className="mono text-xs text-text-dim">loading…</p>;
  if (rows.length === 0) return <p className="mono text-xs text-text-dim">no activity yet.</p>;

  return (
    <ol className="space-y-2 border-l border-border pl-4">
      {rows.map((r) => (
        <li key={r.id} className="relative">
          <span className="absolute -left-[21px] top-1.5 w-2 h-2 rounded-full bg-accent" />
          <div className="mono text-xs text-text">
            <span className="text-text">{r.actorName ?? 'system'}</span>{' '}
            <span className="text-text-dim">{describe(r)}</span>
          </div>
          <div className="mono text-[10px] text-text-dim">
            {new Date(r.createdAt).toLocaleString()}
          </div>
        </li>
      ))}
    </ol>
  );
}

function describe(row: ActivityRow): string {
  const p = safeParse(row.payload);
  switch (row.type) {
    case 'issue_created':
      return `created this issue${p?.title ? ` — "${p.title}"` : ''}`;
    case 'issue_updated': {
      const fieldLabels: Record<string, string> = {
        epicId: 'epic',
        sprintId: 'sprint',
        storyPoints: 'story points',
        assigneeId: 'assignee',
        parentId: 'parent issue',
      };
      const fields = (p?.changes as Array<{ field: string }> | undefined)
        ?.map((c) => fieldLabels[c.field] ?? c.field)
        .join(', ');
      return fields ? `updated ${fields}` : 'updated this issue';
    }
    case 'issue_moved': {
      const from =
        (p?.fromColumnName as string | undefined) ??
        (p?.fromColumnId ? String(p.fromColumnId).slice(0, 8) : 'none');
      const to =
        (p?.toColumnName as string | undefined) ??
        (p?.toColumnId ? String(p.toColumnId).slice(0, 8) : 'none');
      return `moved this issue: ${from} → ${to}`;
    }
    case 'issue_closed':
      return 'closed this issue';
    case 'issue_reopened':
      return 'reopened this issue';
    case 'issue_deleted':
      return 'deleted this issue';
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
    default:
      return row.type;
  }
}

function safeParse(s: string): Record<string, unknown> | null {
  try {
    return JSON.parse(s);
  } catch {
    return null;
  }
}
