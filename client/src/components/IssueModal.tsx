import { useEffect, useRef, useState } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import ReactMarkdown from 'react-markdown';
import rehypeSanitize from 'rehype-sanitize';
import remarkGfm from 'remark-gfm';
import clsx from 'clsx';
import { api } from '../lib/api';
import { useAuthStore } from '../lib/auth';
import { useEscapeKey } from '../lib/useEscapeKey';
import { confirmDialog } from '../lib/confirmDialog';
import type { BoardColumn, EpicWithProgress, Issue, Priority, Sprint } from '../types';
import type { Paged } from '../types/api';
import { IssueComments } from './IssueComments';
import { IssueActivity } from './IssueActivity';
import { IssueLabels } from './IssueLabels';
import { IssueDependencies } from './IssueDependencies';
import { CreateIssueModal } from './CreateIssueModal';
import { MarkdownToolbar } from './MarkdownToolbar';
import { AssigneePicker } from './AssigneePicker';
import type { ProjectMember } from '../types';

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
  const [navStack, setNavStack] = useState<string[]>([issueId]);
  const activeIssueId = navStack[navStack.length - 1];

  const [issue, setIssue] = useState<Issue | null>(null);
  const [saving, setSaving] = useState(false);
  const [tab, setTab] = useState<'comments' | 'activity'>('comments');
  const [descEditing, setDescEditing] = useState(false);
  const [previewing, setPreviewing] = useState(false);
  const [addingChild, setAddingChild] = useState(false);
  // Bumped on any mutation that should trigger the activity feed to refresh.
  const [activityKey, setActivityKey] = useState(0);
  const descTextareaRef = useRef<HTMLTextAreaElement>(null);
  const currentUserId = useAuthStore((s) => s.user?.id ?? null);
  const qc = useQueryClient();
  useEscapeKey(() => {
    if (navStack.length > 1) navigateBack();
    else onClose();
  });

  function navigateTo(id: string) {
    setNavStack((s) => [...s, id]);
    setIssue(null);
    setDescEditing(false);
    setPreviewing(false);
    setAddingChild(false);
    setTab('comments');
  }

  function navigateBack() {
    setNavStack((s) => s.slice(0, -1));
    setIssue(null);
    setDescEditing(false);
    setPreviewing(false);
    setAddingChild(false);
    setTab('comments');
  }

  useEffect(() => {
    let cancelled = false;
    api.get(`/issues/${activeIssueId}`).then((r) => {
      if (!cancelled) setIssue(r.data.issue);
    });
    return () => {
      cancelled = true;
    };
  }, [activeIssueId]);

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

  const { data: board } = useQuery({
    queryKey: ['board', issue?.projectId],
    queryFn: async () =>
      (await api.get<{ columns: BoardColumn[] }>(`/projects/${issue!.projectId}/board`)).data,
    enabled: !!issue?.projectId,
  });

  const { data: members } = useQuery({
    queryKey: ['members', issue?.projectId],
    queryFn: async () =>
      (await api.get<{ items: ProjectMember[] }>(`/projects/${issue!.projectId}/members`)).data
        .items,
    enabled: !!issue?.projectId,
  });

  // Sub-issues: direct children of this issue
  const childrenKey = ['issue-children', activeIssueId];
  const { data: children } = useQuery({
    queryKey: childrenKey,
    queryFn: async () =>
      (await api.get<{ items: Issue[] }>(`/issues/${activeIssueId}/children`)).data.items,
    enabled: !!activeIssueId,
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
        className="fixed inset-0 bg-black/60 z-50 flex justify-end"
        initial={{ opacity: 0 }}
        animate={{ opacity: 1 }}
        exit={{ opacity: 0 }}
        onClick={onClose}
      >
        <motion.div
          className="h-full w-full max-w-4xl panel border-l shadow-2xl flex flex-col bg-bg overflow-hidden relative"
          initial={{ x: '100%' }}
          animate={{ x: 0 }}
          exit={{ x: '100%' }}
          transition={{ type: 'tween', duration: 0.2 }}
          onClick={(e) => e.stopPropagation()}
        >
          {!issue ? (
            <div className="p-6 mono text-text-muted">loading…</div>
          ) : (
            <>
              {/* Header */}
              <div className="flex items-center justify-between px-6 py-4 border-b border-border shrink-0 bg-bg-soft/30">
                <div className="flex items-center gap-3">
                  {navStack.length > 1 && (
                    <button
                      onClick={navigateBack}
                      className="btn-ghost mono text-xs px-2 py-1 hover:bg-bg-soft rounded flex items-center gap-1"
                    >
                      ← Back
                    </button>
                  )}
                  <div className="flex items-center gap-2 mono text-xs text-text-dim">
                    <span className="text-text-muted font-semibold">#{issue.number}</span>
                    {issue.epicId && (
                      <span className="flex items-center gap-2">
                        <span className="uppercase tracking-widest text-accent">
                          {epics?.find((e) => e.id === issue.epicId)?.title || 'Epic'}
                        </span>
                        {(issue.parentId || issue.title) && (
                          <span className="text-text-muted">/</span>
                        )}
                      </span>
                    )}
                    {issue.parentId && (
                      <span className="flex items-center gap-2">
                        <button
                          onClick={() => navigateTo(issue.parentId!)}
                          className="hover:underline uppercase tracking-widest truncate max-w-[150px]"
                          title={parentIssue?.title}
                        >
                          {parentIssue?.title || 'Parent'}
                        </button>
                        {issue.title && <span className="text-text-muted">/</span>}
                      </span>
                    )}
                  </div>
                </div>
                <div className="flex items-center gap-4">
                  <span className="mono text-xs text-text-dim">
                    {saving ? 'saving…' : 'auto-saved'}
                  </span>
                  <button
                    onClick={onClose}
                    className="btn-ghost text-xl px-2 hover:bg-bg-soft rounded"
                  >
                    ×
                  </button>
                </div>
              </div>

              {/* Body */}
              <div className="flex-1 overflow-y-auto p-6 md:p-8">
                <div className="flex flex-col md:flex-row gap-8">
                  {/* Left Column - Main Content */}
                  <div className="flex-1 space-y-8 min-w-0">
                    {/* Title */}
                    <div>
                      <input
                        className="input mono text-2xl font-bold border-transparent px-0 hover:border-border focus:border-accent transition-colors w-full bg-transparent"
                        value={issue.title}
                        onChange={(e) => setIssue({ ...issue, title: e.target.value })}
                        onBlur={(e) => patch({ title: e.target.value })}
                        placeholder="Issue title"
                      />
                    </div>

                    {/* Description */}
                    <div>
                      <label className="mono text-xs uppercase tracking-widest text-text-dim mb-3 block">
                        Description
                      </label>
                      {descEditing ? (
                        <div className="border border-border focus-within:border-accent rounded">
                          <MarkdownToolbar
                            textareaRef={descTextareaRef}
                            value={issue.description ?? ''}
                            onChange={(next) => setIssue({ ...issue, description: next })}
                            onTogglePreview={setPreviewing}
                            isPreviewing={previewing}
                          />
                          <div
                            className={clsx(
                              'flex divide-x divide-border',
                              previewing ? 'flex-col md:flex-row' : 'flex-col',
                            )}
                          >
                            <textarea
                              ref={descTextareaRef}
                              autoFocus
                              className={clsx(
                                'input mono min-h-[200px] border-0 rounded-none rounded-b w-full resize-y focus:ring-0 bg-bg',
                                previewing && 'md:w-1/2',
                              )}
                              placeholder="Add a description... (supports markdown)"
                              value={issue.description ?? ''}
                              onChange={(e) => setIssue({ ...issue, description: e.target.value })}
                              onBlur={(e) => {
                                // Only save and close if we didn't just click the toolbar
                                // (mousedown on toolbar prevents default so blur shouldn't fire early,
                                // but we use a small timeout to be safe with React state flushes)
                                setTimeout(() => {
                                  if (document.activeElement !== descTextareaRef.current) {
                                    patch({ description: e.target.value });
                                    setDescEditing(false);
                                  }
                                }, 100);
                              }}
                            />
                            {previewing && (
                              <div className="md:w-1/2 p-4 overflow-y-auto min-h-[200px] prose prose-invert max-w-none bg-bg-subtle/20">
                                <ReactMarkdown
                                  remarkPlugins={[remarkGfm]}
                                  rehypePlugins={[rehypeSanitize]}
                                >
                                  {issue.description ?? ''}
                                </ReactMarkdown>
                              </div>
                            )}
                          </div>
                        </div>
                      ) : (
                        <div
                          role="button"
                          tabIndex={0}
                          onClick={() => setDescEditing(true)}
                          onKeyDown={(e) => e.key === 'Enter' && setDescEditing(true)}
                          className="prose prose-sm max-w-none rounded p-4 min-h-[120px] text-sm text-text cursor-text hover:bg-bg-soft/40 transition-colors border border-transparent hover:border-border"
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
                              Add a description…
                            </span>
                          )}
                        </div>
                      )}
                    </div>

                    {/* Sub-issues */}
                    <div>
                      <div className="flex items-center justify-between mb-3">
                        <label className="mono text-xs uppercase tracking-widest text-text-dim">
                          Sub-issues {children && children.length > 0 && `(${children.length})`}
                        </label>
                        <button
                          className="mono text-[10px] uppercase tracking-widest text-accent hover:underline flex items-center gap-1"
                          onClick={() => setAddingChild(true)}
                        >
                          <span className="text-sm leading-none">+</span> Add Sub-issue
                        </button>
                      </div>
                      {(children ?? []).length > 0 ? (
                        <div className="border border-border rounded divide-y divide-border">
                          {(children ?? []).map((child) => (
                            <div
                              key={child.id}
                              className="flex items-center gap-3 px-4 py-3 hover:bg-bg-soft/40 transition-colors cursor-pointer"
                              onClick={() => navigateTo(child.id)}
                            >
                              <span
                                className={`w-2 h-2 rounded-full flex-shrink-0 ${
                                  child.closedAt ? 'bg-priority-low' : 'bg-accent'
                                }`}
                              />
                              <span
                                className={`text-sm flex-1 truncate ${child.closedAt ? 'line-through text-text-muted' : ''}`}
                              >
                                {child.title}
                              </span>
                              <span className="mono text-[10px] text-text-dim flex-shrink-0 bg-bg-soft px-1.5 py-0.5 rounded">
                                {child.storyPoints} pt
                              </span>
                            </div>
                          ))}
                        </div>
                      ) : (
                        <div className="border border-dashed border-border rounded p-6 text-center">
                          <p className="mono text-xs text-text-dim">No sub-issues yet.</p>
                        </div>
                      )}
                    </div>

                    {/* Linked issues (blocks / blocked by / relates) */}
                    <div>
                      <IssueDependencies
                        issueId={issue.id}
                        onChange={() => setActivityKey((k) => k + 1)}
                      />
                    </div>

                    {/* Activity / Comments Tabs */}
                    <div className="pt-6">
                      <nav className="flex gap-6 border-b border-border mb-6">
                        {(['comments', 'activity'] as const).map((t) => (
                          <button
                            key={t}
                            onClick={() => setTab(t)}
                            className={`mono text-xs uppercase tracking-widest pb-3 border-b-2 transition-colors ${
                              tab === t
                                ? 'border-accent text-accent'
                                : 'border-transparent text-text-dim hover:text-text'
                            }`}
                          >
                            {t}
                          </button>
                        ))}
                      </nav>

                      {tab === 'comments' && (
                        <IssueComments
                          issueId={issue.id}
                          currentUserId={currentUserId}
                          onChange={() => setActivityKey((k) => k + 1)}
                        />
                      )}

                      {tab === 'activity' && (
                        <IssueActivity issueId={issue.id} refreshKey={activityKey} />
                      )}
                    </div>
                  </div>

                  {/* Right Column - Metadata */}
                  <div className="w-full md:w-72 flex-shrink-0 space-y-6">
                    {/* Status Box */}
                    <div className="p-4 border border-border rounded bg-bg-soft/20 space-y-5">
                      <Field label="Status">
                        <select
                          className={clsx(
                            'input mono text-sm w-full bg-bg py-2 appearance-none cursor-pointer border-accent/30 focus:border-accent',
                            issue.closedAt && 'text-text-dim border-border',
                          )}
                          value={issue.columnId ?? ''}
                          onChange={(e) => patch({ columnId: e.target.value })}
                        >
                          {(board?.columns ?? []).map((col) => (
                            <option key={col.id} value={col.id}>
                              {col.name} {col.isDone ? ' (Closed)' : ''}
                            </option>
                          ))}
                        </select>
                        {issue.closedAt && (
                          <div className="mt-2 flex items-center gap-2 px-2 py-1 bg-priority-low/10 rounded border border-priority-low/20">
                            <span className="w-1.5 h-1.5 rounded-full bg-priority-low" />
                            <span className="mono text-[10px] text-priority-low uppercase tracking-tighter font-bold">
                              Issue Closed
                            </span>
                          </div>
                        )}
                      </Field>

                      <div className="grid grid-cols-2 gap-4">
                        <Field label="Priority">
                          <select
                            className="input mono text-sm w-full bg-bg py-1.5"
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
                        <Field label="Points">
                          <input
                            className="input mono text-sm w-full bg-bg py-1.5"
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
                    </div>

                    {/* Details Box */}
                    <div className="space-y-5">
                      {/* Parent Issue */}
                      {issue.parentId && (
                        <Field label="Parent">
                          <div className="flex items-center justify-between border border-border rounded px-3 py-2 bg-bg-soft/20 hover:bg-bg-soft/40 transition-colors">
                            <span className="text-sm truncate">
                              {parentIssue ? parentIssue.title : 'Loading...'}
                            </span>
                            <button
                              className="mono text-[10px] text-text-dim hover:text-priority-critical ml-2 flex-shrink-0 px-2 py-1 rounded"
                              title="Detach from parent"
                              onClick={() => patch({ parentId: EMPTY_GUID })}
                            >
                              ✕
                            </button>
                          </div>
                        </Field>
                      )}

                      <Field label="Assignee">
                        <AssigneePicker
                          value={issue.assigneeId}
                          members={members}
                          currentUserId={currentUserId}
                          onChange={(next) =>
                            // null → clear (empty-Guid sentinel); id → assign.
                            patch({ assigneeId: next === null ? EMPTY_GUID : next })
                          }
                        />
                      </Field>

                      <Field label="Epic">
                        <select
                          className="input mono text-sm w-full bg-bg-soft/20 py-2"
                          value={issue.epicId ?? ''}
                          onChange={(e) => {
                            const v = e.target.value;
                            patch({ epicId: v === '' ? EMPTY_GUID : v });
                          }}
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
                          className="input mono text-sm w-full bg-bg-soft/20 py-2"
                          value={issue.sprintId ?? ''}
                          onChange={(e) => {
                            const v = e.target.value;
                            patch({ sprintId: v === '' ? EMPTY_GUID : v });
                          }}
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

                    <div className="space-y-3 pt-2">
                      <IssueLabels
                        issueId={issue.id}
                        projectId={issue.projectId}
                        onChange={() => setActivityKey((k) => k + 1)}
                      />
                    </div>

                    <div className="pt-6 mt-6 border-t border-border">
                      <button
                        onClick={remove}
                        className="btn-ghost text-priority-critical w-full flex justify-center py-2 hover:bg-priority-critical/10 transition-colors"
                      >
                        Delete Issue
                      </button>
                    </div>
                  </div>
                </div>
              </div>
            </>
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
        </motion.div>
      </motion.div>
    </AnimatePresence>
  );
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div>
      <label className="mono text-xs uppercase tracking-widest text-text-dim mb-2 block">
        {label}
      </label>
      {children}
    </div>
  );
}
