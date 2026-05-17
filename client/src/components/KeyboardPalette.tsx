import { useEffect, useMemo, useRef, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { motion, AnimatePresence } from 'framer-motion';
import { useQuery } from '@tanstack/react-query';
import { api } from '../lib/api';
import type { Project } from '../types';
import type { Paged } from '../types/api';

/**
 * Global keyboard command palette. Open with Cmd/Ctrl+K or "?" anywhere
 * in the app (except when the user is typing in an input/textarea/
 * contenteditable). Filters across navigation actions and your projects
 * so jumping to a different board is one keystroke + a few letters.
 */
export function KeyboardPalette() {
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState('');
  const [highlight, setHighlight] = useState(0);
  const inputRef = useRef<HTMLInputElement>(null);
  const nav = useNavigate();
  const { slug } = useParams<{ slug: string }>();

  // Fetch the project list lazily — only when the palette opens.
  const { data: projects } = useQuery({
    queryKey: ['palette-projects'],
    queryFn: async () => (await api.get<Paged<Project>>('/projects')).data.items,
    enabled: open,
    staleTime: 30_000,
  });

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

  return (
    <AnimatePresence>
      {open && (
        <motion.div
          className="fixed inset-0 z-[100] flex items-start justify-center pt-24 px-4 bg-black/60 backdrop-blur-[2px]"
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          exit={{ opacity: 0 }}
          onClick={() => setOpen(false)}
        >
          <motion.div
            className="w-full max-w-2xl bg-bg shadow-2xl overflow-hidden border border-border"
            initial={{ y: -20, scale: 0.98, opacity: 0 }}
            animate={{ y: 0, scale: 1, opacity: 1 }}
            exit={{ y: -20, scale: 0.98, opacity: 0 }}
            transition={{ type: 'spring', damping: 25, stiffness: 300 }}
            onClick={(e) => e.stopPropagation()}
          >
            <div className="relative">
              <input
                ref={inputRef}
                type="text"
                value={query}
                onChange={(e) => setQuery(e.target.value)}
                onKeyDown={handleKey}
                placeholder="Type to search...  (↑↓ to move, ↵ to run)"
                className="w-full bg-transparent border-0 border-b border-border px-6 py-4 mono text-sm focus:outline-none focus:ring-0 placeholder:text-text-dim/50"
              />
            </div>

            <div className="max-h-[60vh] overflow-y-auto py-2">
              {filtered.length === 0 ? (
                <div className="px-6 py-8 text-center">
                  <p className="mono text-xs text-text-dim uppercase tracking-widest">
                    No matching commands
                  </p>
                </div>
              ) : (
                filtered.map((c, i) => (
                  <div
                    key={c.id}
                    onMouseEnter={() => setHighlight(i)}
                    onClick={() => {
                      c.run();
                      setOpen(false);
                    }}
                    className={`px-6 py-3 mono text-xs cursor-pointer flex items-center justify-between transition-colors ${
                      i === highlight
                        ? 'bg-accent/10 text-accent border-l-2 border-accent'
                        : 'text-text-muted hover:bg-bg-soft/40 border-l-2 border-transparent'
                    }`}
                  >
                    <span className={i === highlight ? 'font-semibold' : ''}>{c.label}</span>
                    {c.hint && (
                      <span
                        className={`text-[10px] px-2 py-0.5 rounded ${
                          i === highlight ? 'bg-accent/20 text-accent' : 'bg-bg-soft text-text-dim'
                        }`}
                      >
                        {c.hint}
                      </span>
                    )}
                  </div>
                ))
              )}
            </div>

            <div className="px-6 py-3 border-t border-border bg-bg-soft/30 flex items-center justify-between mono text-[10px] uppercase tracking-widest text-text-dim">
              <div className="flex gap-4">
                <span>
                  <kbd className="px-1.5 py-0.5 rounded bg-bg-panel border border-border mr-1">
                    ↑↓
                  </kbd>{' '}
                  Navigate
                </span>
                <span>
                  <kbd className="px-1.5 py-0.5 rounded bg-bg-panel border border-border mr-1">
                    ↵
                  </kbd>{' '}
                  Select
                </span>
              </div>
              <div>
                Press{' '}
                <kbd className="px-1.5 py-0.5 rounded bg-bg-panel border border-border mx-1">
                  ESC
                </kbd>{' '}
                to close
              </div>
            </div>
          </motion.div>
        </motion.div>
      )}
    </AnimatePresence>
  );
}

function isTypingTarget(t: EventTarget | null): boolean {
  if (!t || !(t instanceof HTMLElement)) return false;
  const tag = t.tagName;
  if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT') return true;
  if (t.isContentEditable) return true;
  return false;
}
