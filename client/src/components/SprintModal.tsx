import { useState, useEffect } from 'react';
import { api } from '../lib/api';
import { useEscapeKey } from '../lib/useEscapeKey';
import { confirmDialog } from '../lib/confirmDialog';
import { getApiError } from '../types/api';
import type { Sprint } from '../types';

interface Props {
  projectId: string;
  sprint?: Sprint | null; // when set → edit mode
  onClose: () => void;
  onSaved: () => void;
}

const STATUSES: Sprint['status'][] = ['planned', 'active', 'completed'];

const statusHint: Record<Sprint['status'], string> = {
  planned: 'not started yet',
  active: 'in progress — shows on board filter',
  completed: 'closed — counted in velocity chart',
};

export function SprintModal({ projectId, sprint, onClose, onSaved }: Props) {
  const isEdit = !!sprint;
  const [name, setName] = useState(sprint?.name ?? '');
  const [goal, setGoal] = useState(sprint?.goal ?? '');
  const [startDate, setStartDate] = useState(toDateInput(sprint?.startDate) || defaultStart());
  const [endDate, setEndDate] = useState(toDateInput(sprint?.endDate) || defaultEnd());
  const [status, setStatus] = useState<Sprint['status']>(sprint?.status ?? 'planned');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  useEscapeKey(onClose);

  useEffect(() => {
    if (!sprint) return;
    setName(sprint.name);
    setGoal(sprint.goal ?? '');
    setStartDate(toDateInput(sprint.startDate));
    setEndDate(toDateInput(sprint.endDate));
    setStatus(sprint.status);
  }, [sprint]);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    if (!name.trim() || !startDate || !endDate) return;
    if (endDate < startDate) {
      setError('end date must be on or after start date');
      return;
    }
    setBusy(true);
    setError(null);
    try {
      const body = {
        name: name.trim(),
        goal: goal.trim() || null,
        startDate,
        endDate,
        status,
      };
      if (isEdit) {
        await api.patch(`/sprints/${sprint!.id}`, body);
      } else {
        await api.post(`/projects/${projectId}/sprints`, body);
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
    if (!sprint) return;
    const ok = await confirmDialog({
      title: `Delete sprint "${sprint.name}"?`,
      description: 'Issues currently in this sprint will be unassigned but not deleted.',
      confirmLabel: 'Delete sprint',
      destructive: true,
    });
    if (!ok) return;
    setBusy(true);
    try {
      await api.delete(`/sprints/${sprint.id}`);
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
              // {isEdit ? 'edit sprint' : 'new sprint'}
            </p>
            <h3 className="heading text-lg mt-1">{name || 'untitled'}</h3>
          </div>
          <button onClick={onClose} className="btn-ghost text-xl px-2">
            ×
          </button>
        </div>

        <form onSubmit={submit} className="space-y-4">
          <Field label="name">
            <input
              className="input mono"
              autoFocus
              required
              maxLength={80}
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="e.g. Sprint 12 — Q2 kickoff"
            />
          </Field>

          <Field label="goal">
            <textarea
              className="input mono min-h-[60px]"
              value={goal}
              onChange={(e) => setGoal(e.target.value)}
              placeholder="what does this sprint aim to deliver?"
            />
          </Field>

          <div className="grid grid-cols-2 gap-3">
            <Field label="start date">
              <input
                type="date"
                className="input mono"
                required
                value={startDate}
                onChange={(e) => setStartDate(e.target.value)}
              />
            </Field>
            <Field label="end date">
              <input
                type="date"
                className="input mono"
                required
                value={endDate}
                onChange={(e) => setEndDate(e.target.value)}
              />
            </Field>
          </div>

          <Field label="status">
            <div className="grid grid-cols-3 gap-2">
              {STATUSES.map((s) => (
                <button
                  type="button"
                  key={s}
                  onClick={() => setStatus(s)}
                  className={`mono text-xs uppercase tracking-widest py-2 border transition-colors ${
                    status === s
                      ? 'border-accent text-accent bg-bg-subtle'
                      : 'border-border text-text-muted hover:text-text'
                  }`}
                >
                  {s}
                </button>
              ))}
            </div>
            <p className="mono text-[10px] uppercase tracking-widest text-text-dim mt-1.5">
              {statusHint[status]}
            </p>
          </Field>

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
  return d.length >= 10 ? d.slice(0, 10) : '';
}

function defaultStart(): string {
  return new Date().toISOString().slice(0, 10);
}

function defaultEnd(): string {
  const d = new Date();
  d.setDate(d.getDate() + 14);
  return d.toISOString().slice(0, 10);
}
