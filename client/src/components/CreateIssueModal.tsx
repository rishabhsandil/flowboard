import { useState, useRef } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { useQuery } from '@tanstack/react-query';
import { api } from '../lib/api';
import { useEscapeKey } from '../lib/useEscapeKey';
import type { EpicWithProgress, Priority, Sprint } from '../types';
import type { Paged } from '../types/api';
import { MarkdownToolbar } from './MarkdownToolbar';

interface Props {
  projectId: string;
  firstColumnId?: string;
  defaultEpicId?: string | null;
  defaultSprintId?: string | null;
  /** When set, the created issue will be a sub-issue of this parent. */
  parentId?: string | null;
  onClose: () => void;
  onCreated: () => void;
}

const priorities: Priority[] = ['low', 'medium', 'high', 'critical'];

export function CreateIssueModal({
  projectId,
  firstColumnId,
  defaultEpicId,
  defaultSprintId,
  parentId,
  onClose,
  onCreated,
}: Props) {
  const [title, setTitle] = useState('');
  const [description, setDescription] = useState('');
  const [priority, setPriority] = useState<Priority>('medium');
  const [points, setPoints] = useState(0);
  const [epicId, setEpicId] = useState<string>(defaultEpicId ?? '');
  const [sprintId, setSprintId] = useState<string>(defaultSprintId ?? '');
  const [loading, setLoading] = useState(false);

  const descRef = useRef<HTMLTextAreaElement>(null);
  useEscapeKey(onClose);

  const { data: epics } = useQuery({
    queryKey: ['epics', projectId],
    queryFn: async () =>
      (await api.get<Paged<EpicWithProgress>>(`/projects/${projectId}/epics`)).data.items,
  });
  const { data: sprints } = useQuery({
    queryKey: ['sprints', projectId],
    queryFn: async () =>
      (await api.get<Paged<Sprint>>(`/projects/${projectId}/sprints`)).data.items,
  });

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    if (!title.trim()) return;
    setLoading(true);
    try {
      await api.post(`/projects/${projectId}/issues`, {
        title,
        description: description.trim() || null,
        priority,
        storyPoints: points,
        columnId: firstColumnId,
        epicId: epicId || null,
        sprintId: sprintId || null,
        parentId: parentId ?? null,
      });
      onCreated();
      onClose();
    } finally {
      setLoading(false);
    }
  }

  return (
    <AnimatePresence>
      <motion.div
        className="fixed inset-0 bg-black/60 flex items-center justify-center z-[60] p-4"
        initial={{ opacity: 0 }}
        animate={{ opacity: 1 }}
        exit={{ opacity: 0 }}
        onClick={onClose}
      >
        <motion.div
          className="panel w-full max-w-lg bg-bg shadow-2xl overflow-hidden border border-border"
          initial={{ scale: 0.95, opacity: 0 }}
          animate={{ scale: 1, opacity: 1 }}
          exit={{ scale: 0.95, opacity: 0 }}
          onClick={(e) => e.stopPropagation()}
        >
          {/* Header */}
          <div className="px-6 py-4 border-b border-border bg-bg-soft/30 flex items-center justify-between">
            <div>
              <h3 className="heading text-lg font-bold">New Issue</h3>
              {parentId && (
                <p className="mono text-[10px] uppercase tracking-widest text-accent mt-0.5">
                  // creating as sub-issue
                </p>
              )}
            </div>
            <button
              onClick={onClose}
              className="btn-ghost text-xl px-2 hover:bg-bg-soft rounded transition-colors"
            >
              ×
            </button>
          </div>

          <form onSubmit={submit} className="p-6 space-y-5 max-h-[85vh] overflow-y-auto">
            <Field label="Title">
              <input
                className="input mono w-full bg-bg-soft/10 focus:bg-transparent"
                placeholder="What needs to be done?"
                autoFocus
                required
                value={title}
                onChange={(e) => setTitle(e.target.value)}
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
                  className="input mono min-h-[120px] border-0 rounded-none rounded-b focus:ring-0 bg-bg-soft/10 focus:bg-transparent"
                  placeholder="Add details... (optional)"
                  value={description}
                  onChange={(e) => setDescription(e.target.value)}
                />
              </div>
            </Field>

            <div className="grid grid-cols-2 gap-4">
              <Field label="Priority">
                <select
                  className="input mono w-full bg-bg-soft/10"
                  value={priority}
                  onChange={(e) => setPriority(e.target.value as Priority)}
                >
                  {priorities.map((p) => (
                    <option key={p} value={p}>
                      {p}
                    </option>
                  ))}
                </select>
              </Field>
              <Field label="Story Points">
                <input
                  className="input mono w-full bg-bg-soft/10"
                  type="number"
                  min={0}
                  max={100}
                  placeholder="0"
                  value={points}
                  onChange={(e) => setPoints(Math.max(0, parseInt(e.target.value || '0')))}
                />
              </Field>
            </div>

            <div className="grid grid-cols-2 gap-4 pt-2">
              <Field label="Epic">
                <select
                  className="input mono w-full bg-bg-soft/10"
                  value={epicId}
                  onChange={(e) => setEpicId(e.target.value)}
                >
                  <option value="">— None —</option>
                  {(epics ?? []).map((ep) => (
                    <option key={ep.id} value={ep.id}>
                      {ep.title}
                    </option>
                  ))}
                </select>
              </Field>
              <Field label="Sprint">
                <select
                  className="input mono w-full bg-bg-soft/10"
                  value={sprintId}
                  onChange={(e) => setSprintId(e.target.value)}
                >
                  <option value="">— None —</option>
                  {(sprints ?? []).map((s) => (
                    <option key={s.id} value={s.id}>
                      {s.name}
                      {s.status !== 'planned' ? ` · ${s.status}` : ''}
                    </option>
                  ))}
                </select>
              </Field>
            </div>

            {/* Actions */}
            <div className="flex gap-3 justify-end pt-4 border-t border-border mt-6">
              <button type="button" onClick={onClose} className="btn-ghost px-6">
                Cancel
              </button>
              <button className="btn-primary px-8" disabled={loading}>
                {loading ? 'Creating...' : 'Create Issue →'}
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
      <label className="mono text-[10px] uppercase tracking-widest text-text-dim mb-1.5 block font-semibold">
        {label}
      </label>
      {children}
    </div>
  );
}
