import { useState } from 'react';
import { useOutletContext } from 'react-router-dom';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../lib/api';
import { confirmDialog } from '../lib/confirmDialog';
import { errorService } from '../lib/errorService';
import type { Label, Project } from '../types';

interface Ctx {
  project?: Project;
}

/**
 * Project-level label catalogue. Labels are owned by the project (ZenHub
 * pattern), so all CRUD lives here; the issue modal only picks from this
 * list. Names are unique per-project (case-insensitive) — duplicates surface
 * as a 409 from the API and are shown inline.
 */
export default function LabelsPage() {
  const { project } = useOutletContext<Ctx>();
  const qc = useQueryClient();
  const queryKey = ['labels', project?.id];

  const { data: labels, isLoading } = useQuery({
    queryKey,
    queryFn: async () =>
      (await api.get<{ items: Label[] }>(`/projects/${project!.id}/labels`)).data.items,
    enabled: !!project,
  });

  const [name, setName] = useState('');
  const [color, setColor] = useState('#64748b');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  function refetch() {
    qc.invalidateQueries({ queryKey });
  }

  async function create() {
    const trimmed = name.trim();
    if (!trimmed || !project) return;
    setSubmitting(true);
    setError(null);
    try {
      // silent: this section renders the error inline.
      await api.post(`/projects/${project.id}/labels`, { name: trimmed, color }, { silent: true });
      setName('');
      setColor('#64748b');
      refetch();
    } catch (e) {
      setError(errorService.getMessage(e));
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div className="p-8 max-w-3xl">
      <div className="mb-6">
        <p className="mono text-xs uppercase tracking-widest text-text-dim">// labels</p>
        <h1 className="mono text-2xl">{project?.name}</h1>
        <p className="mono text-xs text-text-dim mt-1">
          define the label catalogue for this project. Issues pick from this list.
        </p>
      </div>

      <section className="panel border rounded p-4 mb-6">
        <p className="mono text-xs uppercase tracking-widest text-text-dim mb-2">new label</p>
        <div className="flex gap-2">
          <input
            className="input mono flex-1"
            placeholder="name (e.g. bug, blocked, frontend)"
            value={name}
            onChange={(e) => setName(e.target.value)}
            maxLength={40}
          />
          <input
            type="color"
            className="w-10 h-10 rounded border border-border cursor-pointer bg-transparent"
            value={color}
            onChange={(e) => setColor(e.target.value)}
            aria-label="label color"
          />
          <button
            className="btn-primary"
            onClick={create}
            disabled={submitting || name.trim().length === 0 || !project}
          >
            create
          </button>
        </div>
        {error && <p className="mono text-xs text-priority-critical mt-2">{error}</p>}
      </section>

      <section>
        {isLoading ? (
          <p className="mono text-text-muted">loading…</p>
        ) : !labels || labels.length === 0 ? (
          <p className="mono text-text-dim">no labels yet — create one above.</p>
        ) : (
          <ul className="divide-y divide-border border border-border rounded">
            {labels.map((l) => (
              <LabelRow key={l.id} label={l} onChange={refetch} />
            ))}
          </ul>
        )}
      </section>
    </div>
  );
}

function LabelRow({ label, onChange }: { label: Label; onChange: () => void }) {
  const [editing, setEditing] = useState(false);
  const [name, setName] = useState(label.name);
  const [color, setColor] = useState(label.color);
  const [error, setError] = useState<string | null>(null);

  async function save() {
    const trimmed = name.trim();
    if (!trimmed) return;
    setError(null);
    try {
      await api.patch(`/labels/${label.id}`, { name: trimmed, color }, { silent: true });
      setEditing(false);
      onChange();
    } catch (e) {
      setError(errorService.getMessage(e));
    }
  }

  async function remove() {
    const ok = await confirmDialog({
      title: `Delete label "${label.name}"?`,
      description: 'This label will be removed from every issue that currently has it.',
      confirmLabel: 'Delete label',
      destructive: true,
    });
    if (!ok) return;
    await api.delete(`/labels/${label.id}`);
    onChange();
  }

  if (editing) {
    return (
      <li className="flex items-center gap-2 px-4 py-3">
        <input
          type="color"
          className="w-8 h-8 rounded border border-border cursor-pointer bg-transparent"
          value={color}
          onChange={(e) => setColor(e.target.value)}
        />
        <input
          className="input mono flex-1"
          value={name}
          onChange={(e) => setName(e.target.value)}
          maxLength={40}
          autoFocus
        />
        <button className="btn text-xs" onClick={save}>
          save
        </button>
        <button
          className="btn-ghost text-xs"
          onClick={() => {
            setEditing(false);
            setName(label.name);
            setColor(label.color);
            setError(null);
          }}
        >
          cancel
        </button>
        {error && <span className="mono text-[11px] text-priority-critical ml-2">{error}</span>}
      </li>
    );
  }

  return (
    <li className="flex items-center gap-3 px-4 py-3">
      <span
        className="mono text-[11px] uppercase tracking-wider px-2.5 py-0.5 rounded-full border"
        style={{
          borderColor: label.color,
          color: label.color,
          background: `${label.color}1a`,
        }}
      >
        {label.name}
      </span>
      <span className="mono text-[10px] text-text-dim">{label.color}</span>
      <span className="flex-1" />
      <button
        className="mono text-[11px] text-text-dim hover:text-accent"
        onClick={() => setEditing(true)}
      >
        edit
      </button>
      <button
        className="mono text-[11px] text-text-dim hover:text-priority-critical"
        onClick={remove}
      >
        delete
      </button>
    </li>
  );
}
