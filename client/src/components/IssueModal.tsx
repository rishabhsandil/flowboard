import { useEffect, useRef, useState } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import ReactMarkdown from 'react-markdown';
import rehypeSanitize from 'rehype-sanitize';
import remarkGfm from 'remark-gfm';
import { api } from '../lib/api';
import { useAuthStore } from '../lib/auth';
import { useEscapeKey } from '../lib/useEscapeKey';
import { confirmDialog } from '../lib/confirmDialog';
import type { EpicWithProgress, Issue, Priority, Sprint } from '../types';
import type { Paged } from '../types/api';
import { IssueComments } from './IssueComments';
import { IssueActivity } from './IssueActivity';
import { IssueLabels } from './IssueLabels';
import { CreateIssueModal } from './CreateIssueModal';
import { MarkdownToolbar } from './MarkdownToolbar';

interface Props {
  issueId: string;
  onClose: () => void;
  onChange: () => void;
}

// Patch payload mirrors the backend's UpdateIssueRequest. Empty-Guid sentinel
// is used to clear epic/sprint/assignee; null/undefined leaves them unchanged.
type IssuePatch = Partial<{
  title: string;
  description: string | null;
  priority: Priority;
  storyPoints: number;
  columnId: string;
  epicId: string | null;
  sprintId: string | null;
  assigneeId: string | null;
  parentId: string | null;
  closed: boolean;
}>;

const priorities: Priority[] = ['low', 'medium', 'high', 'critical'];
const EMPTY_GUID = '00000000-0000-0000-0000-000000000000';

export function IssueModal({ issueId, onClose, onChange }: Props) {
  const [issue, setIssue] = useState<Issue | null>(null);
  const [saving, setSaving] = useState(false);
  const [tab, setTab] = useState<'details' | 'comments' | 'activity'>('details');
  const [descEditing, setDescEditing] = useState(false);
  const [addingChild, setAddingChild] = useState(false);
  // Bumped on any mutation that should trigger the activity feed to refresh.
  const [activityKey, setActivityKey] = useState(0);
  const descTextareaRef = useRef<HTMLTextAreaElement>(null);
  const currentUserId = useAuthStore((s) => s.user?.id ?? null);
  const qc = useQueryClient();
  useEscapeKey(onClose);

  useEffect(() => {
    let cancelled = false;
    api.get(`/issues/${issueId}`).then((r) => {
      if (!cancelled) setIssue(r.data.issue);
    });
    return () => {
      cancelled = true;
    };
  }, [issueId]);

  // Once issue is loaded we know its projectId — fetch epics + sprints for the dropdowns.
  const { data: epics } = useQuery({
    queryKey: ['epics', issue?.projectId],
    queryFn: async () =>
      (await api.get<Paged<EpicWithProgress>>(`/projects/${issue!.projectId}/epics`)).data.items,
    enabled: !!issue?.projectId,
  });
  const { data: sprints } = useQuery({
    queryKey: ['sprints', issue?.projectId],
    queryFn: async () =>
      (await api.get<Paged<Sprint>>(`/projects/${issue!.projectId}/sprints`)).data.items,
    enabled: !!issue?.projectId,
  });

  // Sub-issues: direct children of this issue
  const childrenKey = ['issue-children', issueId];
  const { data: children } = useQuery({
    queryKey: childrenKey,
    queryFn: async () =>
      (await api.get<{ items: Issue[] }>(`/issues/${issueId}/children`)).data.items,
    enabled: !!issueId,
  });

  // Parent issue (minimal fetch — just title for the link)
  const { data: parentIssue } = useQuery({
    queryKey: ['issue', issue?.parentId],
    queryFn: async () => (await api.get<{ issue: Issue }>(`/issues/${issue!.parentId}`)).data.issue,
    enabled: !!issue?.parentId,
  });

  async function patch(patchBody: IssuePatch) {
    if (!issue) return;
    setSaving(true);
    try {
      const r = await api.patch(`/issues/${issue.id}`, patchBody);
      setIssue(r.data.issue);
      setActivityKey((k) => k + 1);
      onChange();
    } finally {
      setSaving(false);
    }
  }

  async function remove() {
    if (!issue) return;
    const ok = await confirmDialog({
      title: 'Delete this issue?',
      description: 'This permanently removes the issue and all of its comments and activity.',
      confirmLabel: 'Delete issue',
      destructive: true,
    });
    if (!ok) return;
    await api.delete(`/issues/${issue.id}`);
    onChange();
    onClose();
  }

  return (
    <AnimatePresence>
      <motion.div
        className="fixed inset-0 bg-black/60 z-50"
        initial={{ opacity: 0 }}
        animate={{ opacity: 1 }}
        exit={{ opacity: 0 }}
        onClick={onClose}
      >
        <motion.div
          className="absolute right-0 top-0 h-full w-full max-w-lg panel border-l overflow-y-auto"
          initial={{ x: '100%' }}
          animate={{ x: 0 }}
          exit={{ x: '100%' }}
          transition={{ type: 'tween', duration: 0.2 }}
          onClick={(e) => e.stopPropagation()}
        >
          {!issue ? (
            <div className="p-6 mono text-text-muted">loading…</div>
          ) : (
            <div className="p-6 space-y-5">
              <div className="flex items-center justify-between">
                <p className="mono text-xs uppercase tracking-widest text-text-dim">// issue</p>
                <button onClick={onClose} className="btn-ghost text-xl px-2">
                  ×
                </button>
              </div>

              <input
                className="input mono text-lg"
                value={issue.title}
                onChange={(e) => setIssue({ ...issue, title: e.target.value })}
                onBlur={(e) => patch({ title: e.target.value })}
              />

              <nav className="flex gap-1 border-b border-border -mx-6 px-6">
                {(['details', 'comments', 'activity'] as const).map((t) => (
                  <button
                    key={t}
                    onClick={() => setTab(t)}
                    className={`mono text-xs uppercase tracking-widest px-3 py-2 -mb-px border-b-2 ${
                      tab === t
                        ? 'border-accent text-accent'
                        : 'border-transparent text-text-dim hover:text-text'
                    }`}
                  >
                    {t}
                  </button>
                ))}
              </nav>

              {tab === 'details' && (
                <div className="space-y-5">
                  <div className="grid grid-cols-2 gap-3">
                    <Field label="priority">
                      <select
                        className="input mono"
                        value={issue.priority}
                        onChange={(e) => patch({ priority: e.target.value as Priority })}
                      >
                        {priorities.map((p) => (
                          <option key={p} value={p}>
                            {p}
                          </option>
                        ))}
                      </select>
                    </Field>
                    <Field label="points">
                      <input
                        className="input mono"
                        type="number"
                        min={0}
                        value={issue.storyPoints}
                        onChange={(e) =>
                          setIssue({ ...issue, storyPoints: parseInt(e.target.value || '0') })
                        }
                        onBlur={(e) => patch({ storyPoints: parseInt(e.target.value || '0') })}
                      />
                    </Field>
                  </div>

                  <div className="grid grid-cols-2 gap-3">
                    <Field label="epic">
                      <select
                        className="input mono"
                        value={issue.epicId ?? ''}
                        onChange={(e) => {
                          const v = e.target.value;
                          patch({ epicId: v === '' ? EMPTY_GUID : v });
                        }}
                      >
                        <option value="">— none —</option>
                        {(epics ?? []).map((ep) => (
                          <option key={ep.id} value={ep.id}>
                            {ep.title}
                          </option>
                        ))}
                      </select>
                    </Field>
                    <Field label="sprint">
                      <select
                        className="input mono"
                        value={issue.sprintId ?? ''}
                        onChange={(e) => {
                          const v = e.target.value;
                          patch({ sprintId: v === '' ? EMPTY_GUID : v });
                        }}
                      >
                        <option value="">— none —</option>
                        {(sprints ?? []).map((s) => (
                          <option key={s.id} value={s.id}>
                            {s.name}
                            {s.status !== 'planned' ? ` · ${s.status}` : ''}
                          </option>
                        ))}
                      </select>
                    </Field>
                  </div>

                  {/* Description – click to edit (Jira-style) */}
                  <div>
                    <label className="mono text-xs uppercase tracking-widest text-text-dim mb-1.5 block">
                      description
                    </label>
                    {descEditing ? (
                      <>
                        <MarkdownToolbar
                          textareaRef={descTextareaRef}
                          value={issue.description ?? ''}
                          onChange={(next) => setIssue({ ...issue, description: next })}
                        />
                        <textarea
                          ref={descTextareaRef}
                          autoFocus
                          className="input mono min-h-[120px] rounded-t-none border-t-0 resize-y"
                          placeholder="supports markdown…"
                          value={issue.description ?? ''}
                          onChange={(e) => setIssue({ ...issue, description: e.target.value })}
                          onBlur={(e) => {
                            patch({ description: e.target.value });
                            setDescEditing(false);
                          }}
                        />
                      </>
                    ) : (
                      <div
                        role="button"
                        tabIndex={0}
                        onClick={() => setDescEditing(true)}
                        onKeyDown={(e) => e.key === 'Enter' && setDescEditing(true)}
                        className="prose prose-sm max-w-none border border-border rounded p-3 min-h-[80px] text-sm text-text cursor-text hover:border-border-strong transition-colors"
                      >
                        {issue.description?.trim() ? (
                          <ReactMarkdown
                            remarkPlugins={[remarkGfm]}
                            rehypePlugins={[rehypeSanitize]}
                          >
                            {issue.description}
                          </ReactMarkdown>
                        ) : (
                          <span className="text-text-dim mono text-xs italic">
                            click to add a description…
                          </span>
                        )}
                      </div>
                    )}
                  </div>

                  {/* Parent issue link */}
                  {issue.parentId && (
                    <div>
                      <label className="mono text-xs uppercase tracking-widest text-text-dim mb-1.5 block">
                        parent issue
                      </label>
                      <div className="flex items-center justify-between border border-border px-3 py-2">
                        <span className="text-sm truncate">
                          {parentIssue ? parentIssue.title : '…'}
                        </span>
                        <button
                          className="mono text-[10px] text-text-dim hover:text-priority-critical ml-2 flex-shrink-0"
                          title="Detach from parent"
                          onClick={() => patch({ parentId: EMPTY_GUID })}
                        >
                          ×
                        </button>
                      </div>
                    </div>
                  )}

                  {/* Sub-issues */}
                  <div>
                    <div className="flex items-center justify-between mb-1.5">
                      <label className="mono text-xs uppercase tracking-widest text-text-dim">
                        sub-issues {children && children.length > 0 && `(${children.length})`}
                      </label>
                      <button
                        className="mono text-[10px] uppercase tracking-widest text-accent hover:underline"
                        onClick={() => setAddingChild(true)}
                      >
                        + add
                      </button>
                    </div>
                    {(children ?? []).length > 0 ? (
                      <div className="border border-border divide-y divide-border">
                        {(children ?? []).map((child) => (
                          <div key={child.id} className="flex items-center gap-2 px-3 py-1.5">
                            <span
                              className={`w-1.5 h-1.5 rounded-full flex-shrink-0 ${
                                child.closedAt ? 'bg-priority-low' : 'bg-accent'
                              }`}
                            />
                            <span
                              className={`text-sm flex-1 truncate ${child.closedAt ? 'line-through text-text-muted' : ''}`}
                            >
                              {child.title}
                            </span>
                            <span className="mono text-[10px] text-text-dim flex-shrink-0">
                              {child.storyPoints}pt
                            </span>
                          </div>
                        ))}
                      </div>
                    ) : (
                      <p className="mono text-xs text-text-dim">no sub-issues yet.</p>
                    )}
                  </div>

                  <Field label="status">
                    <button
                      className={`btn ${issue.closedAt ? 'border-priority-low' : 'border-accent text-accent'}`}
                      onClick={() => patch({ closed: !issue.closedAt })}
                    >
                      {issue.closedAt ? '◉ closed — reopen' : '○ open — close'}
                    </button>
                  </Field>

                  <IssueLabels
                    issueId={issue.id}
                    projectId={issue.projectId}
                    onChange={() => setActivityKey((k) => k + 1)}
                  />

                  <div className="pt-4 border-t border-border flex justify-between items-center">
                    <span className="mono text-xs text-text-dim">
                      {saving ? 'saving…' : 'auto-save on blur'}
                    </span>
                    <button onClick={remove} className="btn-ghost text-priority-critical">
                      delete issue
                    </button>
                  </div>
                </div>
              )}

              {/* Create sub-issue modal */}
              {addingChild && issue && (
                <CreateIssueModal
                  projectId={issue.projectId}
                  parentId={issue.id}
                  onClose={() => setAddingChild(false)}
                  onCreated={() => {
                    setAddingChild(false);
                    qc.invalidateQueries({ queryKey: childrenKey });
                    onChange();
                  }}
                />
              )}

              {tab === 'comments' && (
                <IssueComments
                  issueId={issue.id}
                  currentUserId={currentUserId}
                  onChange={() => setActivityKey((k) => k + 1)}
                />
              )}

              {tab === 'activity' && <IssueActivity issueId={issue.id} refreshKey={activityKey} />}
            </div>
          )}
        </motion.div>
      </motion.div>
    </AnimatePresence>
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
