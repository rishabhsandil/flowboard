import { useEffect, useMemo, useRef, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { api } from '../lib/api';
import type { Project } from '../types';
import type { Paged } from '../types/api';

/**
 * Global keyboard command palette. Open with Cmd/Ctrl+K or "?" anywhere
 * in the app (except when the user is typing in an input/textarea/
 * contenteditable). Filters across navigation actions and your projects
 * so jumping to a different board is one keystroke + a few letters.
 *
 * Mounted once in App.tsx alongside ToastHost / ConfirmDialogHost.
 */
export function KeyboardPalette() {
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState('');
  const [highlight, setHighlight] = useState(0);
  const inputRef = useRef<HTMLInputElement>(null);
  const nav = useNavigate();
  const { slug } = useParams<{ slug: string }>();

  // Fetch the project list lazily — only when the palette opens. Stays in
  // the React Query cache so reopening is instant.
  const { data: projects } = useQuery({
    queryKey: ['palette-projects'],
    queryFn: async () => (await api.get<Paged<Project>>('/projects')).data.items,
    enabled: open,
    staleTime: 30_000,
  });

  // Global hotkey listener. We treat focus inside form fields as "user is
  // typing, don't hijack" — except we still allow Cmd/Ctrl+K because that's
  // the universal palette shortcut (matches VS Code, Linear, etc.).
  useEffect(() => {
    function handler(e: KeyboardEvent) {
      const inField = isTypingTarget(e.target);
      if ((e.metaKey || e.ctrlKey) && e.key.toLowerCase() === 'k') {
        e.preventDefault();
        setOpen((o) => !o);
        return;
      }
      if (e.key === '?' && !inField) {
        e.preventDefault();
        setOpen(true);
      }
      if (e.key === 'Escape' && open) {
        setOpen(false);
      }
    }
    window.addEventListener('keydown', handler);
    return () => window.removeEventListener('keydown', handler);
  }, [open]);

  // Reset state on open and focus the input next tick so the autoFocus
  // doesn't get stolen by whatever element had focus before.
  useEffect(() => {
    if (open) {
      setQuery('');
      setHighlight(0);
      requestAnimationFrame(() => inputRef.current?.focus());
    }
  }, [open]);

  type Cmd = { id: string; label: string; hint?: string; run: () => void };

  const commands = useMemo<Cmd[]>(() => {
    const c: Cmd[] = [
      {
        id: 'nav-dash',
        label: 'Dashboard',
        hint: 'go to all projects',
        run: () => nav('/dashboard'),
      },
      {
        id: 'nav-profile',
        label: 'Profile',
        hint: 'edit name, password',
        run: () => nav('/profile'),
      },
    ];
    if (slug) {
      c.push(
        {
          id: 'nav-board',
          label: 'Board',
          hint: 'kanban view',
          run: () => nav(`/p/${slug}/board`),
        },
        {
          id: 'nav-issues',
          label: 'Issues',
          hint: 'searchable list',
          run: () => nav(`/p/${slug}/issues`),
        },
        { id: 'nav-epics', label: 'Epics', run: () => nav(`/p/${slug}/epics`) },
        { id: 'nav-sprints', label: 'Sprints', run: () => nav(`/p/${slug}/sprints`) },
        { id: 'nav-activity', label: 'Activity', run: () => nav(`/p/${slug}/activity`) },
        { id: 'nav-members', label: 'Members', run: () => nav(`/p/${slug}/settings/members`) },
        { id: 'nav-labels', label: 'Labels', run: () => nav(`/p/${slug}/settings/labels`) },
      );
    }
    for (const p of projects ?? []) {
      c.push({
        id: `proj-${p.id}`,
        label: `Open: ${p.name}`,
        hint: `/p/${p.slug}`,
        run: () => nav(`/p/${p.slug}/board`),
      });
    }
    return c;
  }, [nav, slug, projects]);

  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase();
    if (!q) return commands;
    return commands.filter(
      (c) => c.label.toLowerCase().includes(q) || (c.hint && c.hint.toLowerCase().includes(q)),
    );
  }, [commands, query]);

  // Keep highlight in range when the filtered list shrinks.
  useEffect(() => {
    if (highlight >= filtered.length) setHighlight(0);
  }, [filtered.length, highlight]);

  function handleKey(e: React.KeyboardEvent) {
    if (e.key === 'ArrowDown') {
      e.preventDefault();
      setHighlight((h) => Math.min(h + 1, filtered.length - 1));
    } else if (e.key === 'ArrowUp') {
      e.preventDefault();
      setHighlight((h) => Math.max(h - 1, 0));
    } else if (e.key === 'Enter') {
      e.preventDefault();
      const cmd = filtered[highlight];
      if (cmd) {
        cmd.run();
        setOpen(false);
      }
    }
  }

  if (!open) return null;

  return (
    <div
      className="fixed inset-0 z-50 flex items-start justify-center pt-24 px-4 bg-black/60"
      onClick={() => setOpen(false)}
    >
      <div
        className="w-full max-w-xl bg-bg-panel border border-border shadow-2xl"
        onClick={(e) => e.stopPropagation()}
      >
        <input
          ref={inputRef}
          type="text"
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          onKeyDown={handleKey}
          placeholder="type to search…  (↑↓ to move, ↵ to run, esc to close)"
          className="w-full bg-transparent border-0 border-b border-border px-4 py-3 mono text-sm focus:outline-none focus:ring-0"
        />
        <ul className="max-h-80 overflow-y-auto">
          {filtered.length === 0 ? (
            <li className="px-4 py-3 mono text-xs text-text-dim">no commands match</li>
          ) : (
            filtered.map((c, i) => (
              <li
                key={c.id}
                onMouseEnter={() => setHighlight(i)}
                onClick={() => {
                  c.run();
                  setOpen(false);
                }}
                className={`px-4 py-2 mono text-xs cursor-pointer flex items-center justify-between ${
                  i === highlight ? 'bg-accent/10 text-accent' : 'text-text hover:bg-bg-soft/50'
                }`}
              >
                <span>{c.label}</span>
                {c.hint && <span className="text-text-dim text-[10px]">{c.hint}</span>}
              </li>
            ))
          )}
        </ul>
        <div className="px-4 py-2 border-t border-border mono text-[10px] text-text-dim">
          tip: press <kbd className="px-1 border border-border">?</kbd> or{' '}
          <kbd className="px-1 border border-border">⌘K</kbd> anywhere to open this
        </div>
      </div>
    </div>
  );
}

function isTypingTarget(t: EventTarget | null): boolean {
  if (!t || !(t instanceof HTMLElement)) return false;
  const tag = t.tagName;
  if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT') return true;
  if (t.isContentEditable) return true;
  return false;
}
