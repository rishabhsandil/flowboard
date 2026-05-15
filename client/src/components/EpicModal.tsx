import { useState, useEffect } from 'react';
import { api } from '../lib/api';
import { useEscapeKey } from '../lib/useEscapeKey';
import { confirmDialog } from '../lib/confirmDialog';
import { getApiError } from '../types/api';
import type { EpicWithProgress } from '../types';

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
    <div
      className="fixed inset-0 bg-black/60 flex items-center justify-center z-50 p-4"
      onClick={onClose}
    >
      <div className="panel w-full max-w-lg p-6" onClick={(e) => e.stopPropagation()}>
        <div className="flex items-center justify-between mb-5">
          <div>
            <p className="mono text-xs uppercase tracking-widest text-text-dim">
              // {isEdit ? 'edit epic' : 'new epic'}
            </p>
            <h3 className="heading text-lg flex items-center gap-2 mt-1">
              <span className="w-2.5 h-2.5 rounded-full" style={{ background: color }} />
              {title || 'untitled'}
            </h3>
          </div>
          <button onClick={onClose} className="btn-ghost text-xl px-2">
            ×
          </button>
        </div>

        <form onSubmit={submit} className="space-y-4">
          <Field label="title">
            <input
              className="input mono"
              autoFocus
              required
              maxLength={120}
              value={title}
              onChange={(e) => setTitle(e.target.value)}
            />
          </Field>

          <Field label="description">
            <textarea
              className="input mono min-h-[80px]"
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              placeholder="what does this epic cover?"
            />
          </Field>

          <Field label="color">
            <div className="flex gap-2 flex-wrap">
              {PALETTE.map((c) => (
                <button
                  type="button"
                  key={c}
                  onClick={() => setColor(c)}
                  className={`w-7 h-7 border-2 transition-all ${
                    color === c
                      ? 'border-text scale-110'
                      : 'border-border hover:border-border-strong'
                  }`}
                  style={{ background: c }}
                  aria-label={`color ${c}`}
                />
              ))}
            </div>
          </Field>

          <div className="grid grid-cols-2 gap-3">
            <Field label="start date">
              <input
                type="date"
                className="input mono"
                value={startDate}
                onChange={(e) => setStartDate(e.target.value)}
              />
            </Field>
            <Field label="due date">
              <input
                type="date"
                className="input mono"
                value={dueDate}
                onChange={(e) => setDueDate(e.target.value)}
              />
            </Field>
          </div>

          {error && <p className="mono text-xs text-priority-critical">{error}</p>}

          <div className="flex justify-between items-center pt-3 border-t border-border">
            {isEdit ? (
              <button
                type="button"
                onClick={remove}
                disabled={busy}
                className="btn-ghost text-priority-critical text-xs"
              >
                delete
              </button>
            ) : (
              <span />
            )}
            <div className="flex gap-2">
              <button type="button" onClick={onClose} className="btn-ghost">
                cancel
              </button>
              <button className="btn-primary" disabled={busy}>
                {busy ? '…' : isEdit ? 'save' : 'create →'}
              </button>
            </div>
          </div>
        </form>
      </div>
    </div>
  );
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div>
      <label className="mono text-xs uppercase tracking-widest text-text-dim mb-1.5 block">
        {label}
      </label>
      {children}
    </div>
  );
}

function toDateInput(d?: string | null): string {
  if (!d) return '';
  // API may return either "2026-05-01" or "2026-05-01T00:00:00".
  return d.length >= 10 ? d.slice(0, 10) : '';
}
