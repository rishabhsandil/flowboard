import { useEffect, useMemo, useRef, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { api } from '../lib/api';
import type { Label } from '../types';

interface Props {
  issueId: string;
  projectId: string;
  onChange?: () => void;
}

/**
 * Issue-scoped label picker. Labels themselves are managed at the project
 * level (see /p/:slug/labels) — this component only attaches/detaches
 * existing labels, mirroring ZenHub. Click "edit" to open the dropdown,
 * toggle entries on/off, click outside or "done" to close.
 */
export function IssueLabels({ issueId, projectId, onChange }: Props) {
  const [catalogue, setCatalogue] = useState<Label[] | null>(null);
  const [attached, setAttached] = useState<Label[] | null>(null);
  const [picking, setPicking] = useState(false);
  const [filter, setFilter] = useState('');
  const wrapRef = useRef<HTMLDivElement>(null);
  // Modal is mounted under /p/:slug/* so the slug is always available here.
  const { slug: projectSlug } = useParams<{ slug: string }>();

  async function loadAll() {
    const [cat, mine] = await Promise.all([
      api.get<{ items: Label[] }>(`/projects/${projectId}/labels`),
      api.get<{ items: Label[] }>(`/issues/${issueId}/labels`),
    ]);
    setCatalogue(cat.data.items);
    setAttached(mine.data.items);
  }

  useEffect(() => {
    let cancelled = false;
    Promise.all([
      api.get<{ items: Label[] }>(`/projects/${projectId}/labels`),
      api.get<{ items: Label[] }>(`/issues/${issueId}/labels`),
    ]).then(([cat, mine]) => {
      if (cancelled) return;
      setCatalogue(cat.data.items);
      setAttached(mine.data.items);
    });
    return () => {
      cancelled = true;
    };
  }, [issueId, projectId]);

  // Close picker when clicking outside the labels block.
  useEffect(() => {
    if (!picking) return;
    function onDoc(e: MouseEvent) {
      if (wrapRef.current && !wrapRef.current.contains(e.target as Node)) {
        setPicking(false);
      }
    }
    document.addEventListener('mousedown', onDoc);
    return () => document.removeEventListener('mousedown', onDoc);
  }, [picking]);

  const attachedIds = useMemo(() => new Set((attached ?? []).map((l) => l.id)), [attached]);

  const filtered = useMemo(() => {
    const q = filter.trim().toLowerCase();
    if (!q) return catalogue ?? [];
    return (catalogue ?? []).filter((l) => l.name.toLowerCase().includes(q));
  }, [catalogue, filter]);

  async function toggle(label: Label) {
    if (attachedIds.has(label.id)) {
      await api.delete(`/issues/${issueId}/labels/${label.id}`);
    } else {
      await api.post(`/issues/${issueId}/labels`, { labelId: label.id });
    }
    await loadAll();
    onChange?.();
  }

  return (
    <div ref={wrapRef}>
      <div className="flex items-center justify-between mb-1.5">
        <label className="mono text-xs uppercase tracking-widest text-text-dim">labels</label>
        <button
          className="mono text-[11px] text-text-dim hover:text-accent"
          onClick={() => setPicking((v) => !v)}
        >
          {picking ? 'done' : 'edit'}
        </button>
      </div>

      <div className="flex flex-wrap gap-1.5 min-h-[26px]">
        {(attached ?? []).length === 0 && !picking ? (
          <span className="mono text-xs text-text-dim">no labels</span>
        ) : (
          (attached ?? []).map((l) => <LabelPill key={l.id} label={l} />)
        )}
      </div>

      {picking && (
        <div className="mt-3 border border-border rounded p-2 bg-bg-soft/30">
          <input
            className="input mono text-xs mb-2"
            placeholder="filter labels…"
            value={filter}
            onChange={(e) => setFilter(e.target.value)}
            autoFocus
          />
          {catalogue === null ? (
            <p className="mono text-xs text-text-dim">loading…</p>
          ) : catalogue.length === 0 ? (
            <p className="mono text-xs text-text-dim">
              no labels in this project yet
              {projectSlug && (
                <>
                  {' — '}
                  <Link
                    to={`/p/${projectSlug}/settings/labels`}
                    className="text-accent hover:underline"
                  >
                    manage labels
                  </Link>
                </>
              )}
              .
            </p>
          ) : filtered.length === 0 ? (
            <p className="mono text-xs text-text-dim">no matches.</p>
          ) : (
            <ul className="space-y-1 max-h-48 overflow-y-auto">
              {filtered.map((l) => {
                const on = attachedIds.has(l.id);
                return (
                  <li key={l.id}>
                    <button
                      onClick={() => toggle(l)}
                      className={`w-full flex items-center gap-2 px-2 py-1 rounded text-left mono text-xs ${
                        on ? 'bg-accent/10' : 'hover:bg-bg-soft'
                      }`}
                    >
                      <span className="w-3 h-3 rounded-full" style={{ background: l.color }} />
                      <span className="flex-1">{l.name}</span>
                      <span className="text-text-dim">{on ? '✓' : ''}</span>
                    </button>
                  </li>
                );
              })}
            </ul>
          )}
          {projectSlug && catalogue && catalogue.length > 0 && (
            <div className="border-t border-border mt-2 pt-2 text-right">
              <Link
                to={`/p/${projectSlug}/settings/labels`}
                className="mono text-[10px] text-text-dim hover:text-accent"
              >
                manage labels →
              </Link>
            </div>
          )}
        </div>
      )}
    </div>
  );
}

function LabelPill({ label }: { label: Label }) {
  return (
    <span
      className="mono text-[10px] uppercase tracking-wider px-2 py-0.5 rounded-full border"
      style={{
        borderColor: label.color,
        color: label.color,
        background: `${label.color}1a`,
      }}
    >
      {label.name}
    </span>
  );
}
