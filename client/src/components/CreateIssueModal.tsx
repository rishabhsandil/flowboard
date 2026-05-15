import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { api } from '../lib/api';
import { useEscapeKey } from '../lib/useEscapeKey';
import type { EpicWithProgress, Priority, Sprint } from '../types';
import type { Paged } from '../types/api';

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
  const [priority, setPriority] = useState<Priority>('medium');
  const [points, setPoints] = useState(0);
  const [epicId, setEpicId] = useState<string>(defaultEpicId ?? '');
  const [sprintId, setSprintId] = useState<string>(defaultSprintId ?? '');
  const [loading, setLoading] = useState(false);
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
    <div
      className="fixed inset-0 bg-black/60 flex items-center justify-center z-50 p-4"
      onClick={onClose}
    >
      <div className="panel w-full max-w-md p-6" onClick={(e) => e.stopPropagation()}>
        <h3 className="heading text-lg mb-1">new issue</h3>
        {parentId && <p className="mono text-xs text-text-muted mb-4">// creating as sub-issue</p>}
        <form onSubmit={submit} className="space-y-3">
          <input
            className="input mono"
            placeholder="title"
            autoFocus
            required
            value={title}
            onChange={(e) => setTitle(e.target.value)}
          />
          <div className="grid grid-cols-2 gap-3">
            <select
              className="input mono"
              value={priority}
              onChange={(e) => setPriority(e.target.value as Priority)}
            >
              {priorities.map((p) => (
                <option key={p} value={p}>
                  {p}
                </option>
              ))}
            </select>
            <input
              className="input mono"
              type="number"
              min={0}
              max={100}
              placeholder="points"
              value={points}
              onChange={(e) => setPoints(Math.max(0, parseInt(e.target.value || '0')))}
            />
          </div>
          <div className="grid grid-cols-2 gap-3">
            <select
              className="input mono"
              value={epicId}
              onChange={(e) => setEpicId(e.target.value)}
            >
              <option value="">epic — none</option>
              {(epics ?? []).map((ep) => (
                <option key={ep.id} value={ep.id}>
                  {ep.title}
                </option>
              ))}
            </select>
            <select
              className="input mono"
              value={sprintId}
              onChange={(e) => setSprintId(e.target.value)}
            >
              <option value="">sprint — none</option>
              {(sprints ?? []).map((s) => (
                <option key={s.id} value={s.id}>
                  {s.name}
                  {s.status !== 'planned' ? ` · ${s.status}` : ''}
                </option>
              ))}
            </select>
          </div>
          <div className="flex gap-2 justify-end pt-2">
            <button type="button" onClick={onClose} className="btn-ghost">
              cancel
            </button>
            <button className="btn-primary" disabled={loading}>
              {loading ? 'creating…' : 'create →'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
