import { useState, useEffect, useRef } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { api } from '../lib/api';
import { useEscapeKey } from '../lib/useEscapeKey';
import { confirmDialog } from '../lib/confirmDialog';
import { getApiError } from '../types/api';
import type { EpicWithProgress } from '../types';
import { MarkdownToolbar } from './MarkdownToolbar';

interface Props {
  projectId: string;
  epic?: EpicWithProgress | null; // when set → edit mode
  onClose: () => void;
  onSaved: () => void;
}

const PALETTE = [
  '#22d3ee', // cyan
  '#a78bfa', // violet
  '#f472b6', // pink
  '#fbbf24', // amber
  '#4ade80', // green
  '#f87171', // red
  '#60a5fa', // blue
  '#94a3b8', // slate
];

export function EpicModal({ projectId, epic, onClose, onSaved }: Props) {
  const isEdit = !!epic;
  const [title, setTitle] = useState(epic?.title ?? '');
  const [description, setDescription] = useState(epic?.description ?? '');
  const [color, setColor] = useState(epic?.color ?? PALETTE[0]);
  const [startDate, setStartDate] = useState(toDateInput(epic?.startDate));
  const [dueDate, setDueDate] = useState(toDateInput(epic?.dueDate));
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const descRef = useRef<HTMLTextAreaElement>(null);
  useEscapeKey(onClose);

  useEffect(() => {
    if (!epic) return;
    setTitle(epic.title);
    setDescription(epic.description ?? '');
    setColor(epic.color);
    setStartDate(toDateInput(epic.startDate));
    setDueDate(toDateInput(epic.dueDate));
  }, [epic]);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    if (!title.trim()) return;
    setBusy(true);
    setError(null);
    try {
      const body = {
        title: title.trim(),
        description: description.trim() || null,
        color,
        startDate: startDate || null,
        dueDate: dueDate || null,
      };
      if (isEdit) {
        await api.patch(`/epics/${epic!.id}`, body);
      } else {
        await api.post(`/projects/${projectId}/epics`, body);
      }
      onSaved();
      onClose();
    } catch (err) {
      setError(getApiError(err, 'failed to save'));
    } finally {
      setBusy(false);
    }
  }

  async function remove() {
    if (!epic) return;
    const ok = await confirmDialog({
      title: `Delete epic "${epic.title}"?`,
      description: 'Issues currently in this epic will be unlinked but not deleted.',
      confirmLabel: 'Delete epic',
      destructive: true,
    });
    if (!ok) return;
    setBusy(true);
    try {
      await api.delete(`/epics/${epic.id}`);
      onSaved();
      onClose();
    } catch {
      setError('failed to delete');
    } finally {
      setBusy(false);
    }
  }

  return (
    <AnimatePresence>
      <motion.div
        className="fixed inset-0 bg-black/60 z-50 flex justify-end"
        initial={{ opacity: 0 }}
        animate={{ opacity: 1 }}
        exit={{ opacity: 0 }}
        onClick={onClose}
      >
        <motion.div
          className="h-full w-full max-w-2xl panel border-l shadow-2xl flex flex-col bg-bg overflow-hidden relative"
          initial={{ x: '100%' }}
          animate={{ x: 0 }}
          exit={{ x: '100%' }}
          transition={{ type: 'spring', damping: 25, stiffness: 200 }}
          onClick={(e) => e.stopPropagation()}
        >
          {/* Header */}
          <div className="flex items-center justify-between px-6 py-4 border-b border-border shrink-0 bg-bg-soft/30">
            <div className="flex items-center gap-3">
              <span className="w-3 h-3 rounded-full shadow-sm" style={{ background: color }} />
              <p className="mono text-xs uppercase tracking-widest text-text-dim">
                // {isEdit ? 'edit epic' : 'new epic'}
              </p>
            </div>
            <button
              onClick={onClose}
              className="btn-ghost text-xl px-2 hover:bg-bg-soft rounded transition-colors"
            >
              ×
            </button>
          </div>

          <form onSubmit={submit} className="flex-1 overflow-y-auto flex flex-col">
            <div className="flex-1 p-6 md:p-8 space-y-8">
              <div className="flex flex-col md:flex-row gap-8">
                {/* Main Content */}
                <div className="flex-1 space-y-6 min-w-0">
                  <Field label="Title">
                    <input
                      className="input mono text-2xl font-bold border-transparent px-0 hover:border-border focus:border-accent transition-colors w-full bg-transparent"
                      autoFocus
                      required
                      maxLength={120}
                      value={title}
                      onChange={(e) => setTitle(e.target.value)}
                      placeholder="Epic title"
                    />
                  </Field>

                  <Field label="Description">
                    <div className="border border-border focus-within:border-accent rounded">
                      <MarkdownToolbar
                        textareaRef={descRef}
                        value={description}
                        onChange={setDescription}
                      />
                      <textarea
                        ref={descRef}
                        className="input mono min-h-[200px] border-0 rounded-none rounded-b w-full resize-y bg-bg-soft/10 focus:bg-transparent focus:ring-0"
                        value={description}
                        onChange={(e) => setDescription(e.target.value)}
                        placeholder="What does this epic cover? (Supports markdown)"
                      />
                    </div>
                  </Field>
                </div>

                {/* Metadata Column */}
                <div className="w-full md:w-64 flex-shrink-0 space-y-6">
                  <Field label="Color">
                    <div className="grid grid-cols-4 gap-2">
                      {PALETTE.map((c) => (
                        <button
                          type="button"
                          key={c}
                          onClick={() => setColor(c)}
                          className={`aspect-square border-2 transition-all rounded ${
                            color === c
                              ? 'border-text scale-105 shadow-md'
                              : 'border-transparent hover:border-border-strong'
                          }`}
                          style={{ background: c }}
                          aria-label={`color ${c}`}
                        />
                      ))}
                    </div>
                  </Field>

                  <div className="space-y-4 pt-2 border-t border-border/50">
                    <Field label="Start Date">
                      <input
                        type="date"
                        className="input mono text-sm w-full bg-bg-soft/20 py-2"
                        value={startDate}
                        onChange={(e) => setStartDate(e.target.value)}
                      />
                    </Field>
                    <Field label="Due Date">
                      <input
                        type="date"
                        className="input mono text-sm w-full bg-bg-soft/20 py-2"
                        value={dueDate}
                        onChange={(e) => setDueDate(e.target.value)}
                      />
                    </Field>
                  </div>

                  {isEdit && (
                    <div className="pt-6 mt-6 border-t border-border">
                      <button
                        type="button"
                        onClick={remove}
                        disabled={busy}
                        className="btn-ghost text-priority-critical w-full flex justify-center py-2 hover:bg-priority-critical/10 transition-colors"
                      >
                        Delete Epic
                      </button>
                    </div>
                  )}
                </div>
              </div>

              {error && (
                <div className="p-3 bg-priority-critical/10 border border-priority-critical/20 rounded">
                  <p className="mono text-xs text-priority-critical">{error}</p>
                </div>
              )}
            </div>

            {/* Footer Actions */}
            <div className="px-6 py-4 border-t border-border bg-bg-soft/30 flex justify-end gap-3 shrink-0">
              <button type="button" onClick={onClose} className="btn-ghost px-6">
                Cancel
              </button>
              <button className="btn-primary px-8" disabled={busy}>
                {busy ? '...' : isEdit ? 'Save Changes' : 'Create Epic →'}
              </button>
            </div>
          </form>
        </motion.div>
      </motion.div>
    </AnimatePresence>
  );
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div>
      <label className="mono text-[10px] uppercase tracking-widest text-text-dim mb-2 block font-semibold">
        {label}
      </label>
      {children}
    </div>
  );
}

function toDateInput(d?: string | null): string {
  if (!d) return '';
  return d.length >= 10 ? d.slice(0, 10) : '';
}
