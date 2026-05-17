import { useState, useEffect } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
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
              <p className="mono text-xs uppercase tracking-widest text-text-dim">
                // {isEdit ? 'edit sprint' : 'new sprint'}
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
                  <Field label="Sprint Name">
                    <input
                      className="input mono text-2xl font-bold border-transparent px-0 hover:border-border focus:border-accent transition-colors w-full bg-transparent"
                      autoFocus
                      required
                      maxLength={80}
                      value={name}
                      onChange={(e) => setName(e.target.value)}
                      placeholder="e.g. Sprint 12 — Q2 kickoff"
                    />
                  </Field>

                  <Field label="Sprint Goal">
                    <textarea
                      className="input mono min-h-[160px] w-full resize-y bg-bg-soft/10 focus:bg-transparent"
                      value={goal}
                      onChange={(e) => setGoal(e.target.value)}
                      placeholder="What does this sprint aim to deliver?"
                    />
                  </Field>
                </div>

                {/* Metadata Column */}
                <div className="w-full md:w-64 flex-shrink-0 space-y-6">
                  <div className="p-4 border border-border rounded bg-bg-soft/20 space-y-5">
                    <Field label="Status">
                      <div className="flex flex-col gap-1.5">
                        {STATUSES.map((s) => (
                          <button
                            type="button"
                            key={s}
                            onClick={() => setStatus(s)}
                            className={`mono text-[10px] uppercase tracking-widest py-2 px-3 border transition-colors text-left rounded ${
                              status === s
                                ? 'border-accent text-accent bg-accent/5'
                                : 'border-border text-text-muted hover:text-text hover:border-border-strong'
                            }`}
                          >
                            <span className="flex justify-between items-center">
                              {s}
                              {status === s && (
                                <span className="w-1.5 h-1.5 rounded-full bg-accent" />
                              )}
                            </span>
                          </button>
                        ))}
                      </div>
                      <p className="mono text-[9px] uppercase tracking-tighter text-text-dim mt-2 leading-relaxed">
                        {statusHint[status]}
                      </p>
                    </Field>
                  </div>

                  <div className="space-y-4 pt-2 border-t border-border/50">
                    <Field label="Start Date">
                      <input
                        type="date"
                        className="input mono text-sm w-full bg-bg-soft/20 py-2"
                        required
                        value={startDate}
                        onChange={(e) => setStartDate(e.target.value)}
                      />
                    </Field>
                    <Field label="End Date">
                      <input
                        type="date"
                        className="input mono text-sm w-full bg-bg-soft/20 py-2"
                        required
                        value={endDate}
                        onChange={(e) => setEndDate(e.target.value)}
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
                        Delete Sprint
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
                {busy ? '...' : isEdit ? 'Save Changes' : 'Create Sprint →'}
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

function defaultStart(): string {
  return new Date().toISOString().slice(0, 10);
}

function defaultEnd(): string {
  const d = new Date();
  d.setDate(d.getDate() + 14);
  return d.toISOString().slice(0, 10);
}
