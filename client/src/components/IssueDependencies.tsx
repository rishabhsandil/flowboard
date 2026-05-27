import { useEffect, useMemo, useRef, useState } from 'react';
import { api } from '../lib/api';
import type { IssueDependency, IssuePickerRow } from '../types';

interface Props {
  issueId: string;
  onChange?: () => void;
}

// Jira-style link types. Internally these collapse onto two backend kinds
// ('blocks' / 'relates') plus a direction flip for "is blocked by".
type LinkType = 'blocks' | 'is_blocked_by' | 'relates';

const LINK_OPTIONS: { value: LinkType; label: string }[] = [
  { value: 'blocks', label: 'blocks' },
  { value: 'is_blocked_by', label: 'is blocked by' },
  { value: 'relates', label: 'relates to' },
];

/**
 * Linked-issues panel in the IssueModal's main column (below Sub-issues),
 * modelled on Jira's UX:
 *   • single "+ Add link" button opens a picker
 *   • picker has a link-type dropdown ("blocks" / "is blocked by" / "relates to")
 *     and a debounced issue title search scoped to the same project
 *   • existing links render as a single flat list grouped by relationship label
 */
export function IssueDependencies({ issueId, onChange }: Props) {
  const [deps, setDeps] = useState<{
    outgoing: IssueDependency[];
    incoming: IssueDependency[];
  } | null>(null);
  const [opening, setOpening] = useState(false);
  const [linkType, setLinkType] = useState<LinkType>('blocks');
  const [query, setQuery] = useState('');
  const [results, setResults] = useState<IssuePickerRow[] | null>(null);
  const [busy, setBusy] = useState(false);
  const wrapRef = useRef<HTMLDivElement>(null);

  async function load() {
    const r = await api.get<{ outgoing: IssueDependency[]; incoming: IssueDependency[] }>(
      `/issues/${issueId}/dependencies`,
    );
    setDeps(r.data);
  }

  useEffect(() => {
    let cancelled = false;
    api
      .get<{
        outgoing: IssueDependency[];
        incoming: IssueDependency[];
      }>(`/issues/${issueId}/dependencies`)
      .then((r) => {
        if (!cancelled) setDeps(r.data);
      });
    return () => {
      cancelled = true;
    };
  }, [issueId]);

  // Debounced search against the picker endpoint while the dropdown is open.
  useEffect(() => {
    if (!opening) {
      setResults(null);
      return;
    }
    const handle = setTimeout(async () => {
      const r = await api.get<{ items: IssuePickerRow[] }>(
        `/issues/${issueId}/dependencies/search`,
        { params: { q: query || undefined, take: 10 } },
      );
      setResults(r.data.items);
    }, 150);
    return () => clearTimeout(handle);
  }, [issueId, opening, query]);

  // Click-outside closes the picker.
  useEffect(() => {
    if (!opening) return;
    function onDoc(e: MouseEvent) {
      if (wrapRef.current && !wrapRef.current.contains(e.target as Node)) {
        setOpening(false);
        setQuery('');
      }
    }
    document.addEventListener('mousedown', onDoc);
    return () => document.removeEventListener('mousedown', onDoc);
  }, [opening]);

  // Flatten outgoing + incoming into a single list, deduped. For 'relates'
  // we only keep one row per pair (it shows up on both sides but is the
  // same edge). Each entry carries the user-facing label.
  type Linked = {
    key: string;
    label: string;
    otherId: string;
    otherTitle: string;
    otherClosedAt: string | null;
    // The (sourceId, targetId) the DELETE endpoint expects:
    sourceId: string;
    targetId: string;
  };
  const linked = useMemo<Linked[]>(() => {
    if (!deps) return [];
    const rows: Linked[] = [];
    const seenRelates = new Set<string>();
    for (const d of deps.outgoing) {
      if (d.kind === 'blocks') {
        rows.push({
          key: `out:blocks:${d.otherId}`,
          label: 'blocks',
          otherId: d.otherId,
          otherTitle: d.otherTitle,
          otherClosedAt: d.otherClosedAt,
          sourceId: issueId,
          targetId: d.otherId,
        });
      } else if (!seenRelates.has(d.otherId)) {
        seenRelates.add(d.otherId);
        rows.push({
          key: `relates:${d.otherId}`,
          label: 'relates to',
          otherId: d.otherId,
          otherTitle: d.otherTitle,
          otherClosedAt: d.otherClosedAt,
          sourceId: issueId,
          targetId: d.otherId,
        });
      }
    }
    for (const d of deps.incoming) {
      if (d.kind === 'blocks') {
        rows.push({
          key: `in:blocks:${d.otherId}`,
          label: 'is blocked by',
          otherId: d.otherId,
          otherTitle: d.otherTitle,
          otherClosedAt: d.otherClosedAt,
          sourceId: d.otherId,
          targetId: issueId,
        });
      } else if (!seenRelates.has(d.otherId)) {
        seenRelates.add(d.otherId);
        rows.push({
          key: `relates:${d.otherId}`,
          label: 'relates to',
          otherId: d.otherId,
          otherTitle: d.otherTitle,
          otherClosedAt: d.otherClosedAt,
          sourceId: d.otherId,
          targetId: issueId,
        });
      }
    }
    return rows;
  }, [deps, issueId]);

  // Skip issues already linked (in either direction) from the picker.
  const alreadyLinkedIds = useMemo(() => {
    const ids = new Set<string>();
    (deps?.outgoing ?? []).forEach((d) => ids.add(d.otherId));
    (deps?.incoming ?? []).forEach((d) => ids.add(d.otherId));
    return ids;
  }, [deps]);

  async function add(target: IssuePickerRow) {
    if (!opening) return;
    setBusy(true);
    try {
      // "is_blocked_by" is stored as a normal blocks edge pointing the
      // OTHER way, so the backend stays direction-aware.
      const sourceId = linkType === 'is_blocked_by' ? target.id : issueId;
      const dependsOnId = linkType === 'is_blocked_by' ? issueId : target.id;
      const kind = linkType === 'relates' ? 'relates' : 'blocks';
      await api.post(`/issues/${sourceId}/dependencies`, { dependsOnId, kind });
      await load();
      onChange?.();
    } finally {
      setBusy(false);
      setQuery('');
      setOpening(false);
    }
  }

  async function remove(row: Linked) {
    setBusy(true);
    try {
      await api.delete(`/issues/${row.sourceId}/dependencies/${row.targetId}`);
      await load();
      onChange?.();
    } finally {
      setBusy(false);
    }
  }

  return (
    <div ref={wrapRef}>
      <div className="flex items-center justify-between mb-3">
        <label className="mono text-xs uppercase tracking-widest text-text-dim">
          Linked issues {linked.length > 0 && `(${linked.length})`}
        </label>
        <button
          className="mono text-[10px] uppercase tracking-widest text-accent hover:underline flex items-center gap-1 disabled:opacity-50"
          onClick={() => setOpening(true)}
          disabled={busy || opening}
        >
          <span className="text-sm leading-none">+</span> Add link
        </button>
      </div>

      {linked.length > 0 ? (
        <div className="border border-border rounded divide-y divide-border" data-testid="dep-list">
          {linked.map((row) => (
            <div
              key={row.key}
              className="flex items-center gap-3 px-4 py-2 hover:bg-bg-soft/40 transition-colors"
            >
              <span className="mono text-[10px] uppercase tracking-widest text-text-dim w-24 flex-shrink-0">
                {row.label}
              </span>
              <span
                className={`w-2 h-2 rounded-full flex-shrink-0 ${
                  row.otherClosedAt ? 'bg-priority-low' : 'bg-accent'
                }`}
              />
              <span
                className={`text-sm flex-1 truncate ${
                  row.otherClosedAt ? 'line-through text-text-muted' : ''
                }`}
                title={row.otherTitle}
              >
                {row.otherTitle}
              </span>
              <button
                onClick={() => remove(row)}
                disabled={busy}
                className="mono text-[10px] text-text-dim hover:text-priority-critical px-2 py-1 rounded disabled:opacity-50"
                aria-label={`Remove ${row.otherTitle}`}
              >
                ×
              </button>
            </div>
          ))}
        </div>
      ) : (
        !opening && (
          <div className="border border-dashed border-border rounded p-6 text-center">
            <p className="mono text-xs text-text-dim">No linked issues yet.</p>
          </div>
        )
      )}

      {opening && (
        <div
          data-testid="dep-picker"
          className="mt-3 border border-border rounded p-3 bg-bg-soft/30 space-y-2"
        >
          <div className="flex items-center gap-2">
            <label className="mono text-[10px] uppercase tracking-widest text-text-dim shrink-0">
              this issue
            </label>
            <select
              data-testid="dep-link-type"
              className="input mono text-xs bg-bg py-1.5"
              value={linkType}
              onChange={(e) => setLinkType(e.target.value as LinkType)}
            >
              {LINK_OPTIONS.map((o) => (
                <option key={o.value} value={o.value}>
                  {o.label}
                </option>
              ))}
            </select>
          </div>
          <input
            className="input mono text-xs"
            placeholder="search issues in this project…"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            autoFocus
          />
          {results === null ? (
            <p className="mono text-xs text-text-dim">loading…</p>
          ) : results.length === 0 ? (
            <p className="mono text-xs text-text-dim">no matches.</p>
          ) : (
            <ul className="space-y-1 max-h-48 overflow-y-auto">
              {results.map((row) => {
                const taken = alreadyLinkedIds.has(row.id);
                return (
                  <li key={row.id}>
                    <button
                      onClick={() => !taken && add(row)}
                      disabled={taken || busy}
                      className={`w-full flex items-center gap-2 px-2 py-1 rounded text-left mono text-xs ${
                        taken ? 'text-text-dim cursor-not-allowed' : 'hover:bg-bg-soft'
                      }`}
                    >
                      <span
                        className={`w-2 h-2 rounded-full flex-shrink-0 ${
                          row.closedAt ? 'bg-priority-low' : 'bg-accent'
                        }`}
                      />
                      <span className="flex-1 truncate">{row.title}</span>
                      {taken && <span className="text-text-dim text-[10px]">linked</span>}
                    </button>
                  </li>
                );
              })}
            </ul>
          )}
          <div className="flex justify-end pt-1">
            <button
              className="mono text-[10px] text-text-dim hover:text-text"
              onClick={() => {
                setOpening(false);
                setQuery('');
              }}
            >
              cancel
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
