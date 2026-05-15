import { useEffect, useState } from 'react';
import { api } from '../lib/api';
import { confirmDialog } from '../lib/confirmDialog';
import type { Comment } from '../types';

interface Props {
  issueId: string;
  /** Current user's id — used to gate the edit/delete affordances on each comment. */
  currentUserId: string | null;
  onChange?: () => void;
}

/**
 * Threaded comments under an issue. New comments are POSTed and the list is
 * re-fetched (cheap; threads are short and this avoids reasoning about
 * mention rows the client never sees). Mentions inside the body are
 * highlighted client-side; the server is the source of truth for whether a
 * handle resolved to a real user — that drives notifications, not display.
 */
export function IssueComments({ issueId, currentUserId, onChange }: Props) {
  const [comments, setComments] = useState<Comment[] | null>(null);
  const [draft, setDraft] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editBody, setEditBody] = useState('');

  async function load() {
    const r = await api.get<{ items: Comment[] }>(`/issues/${issueId}/comments`);
    setComments(r.data.items);
  }

  useEffect(() => {
    let cancelled = false;
    api.get<{ items: Comment[] }>(`/issues/${issueId}/comments`).then((r) => {
      if (!cancelled) setComments(r.data.items);
    });
    return () => {
      cancelled = true;
    };
  }, [issueId]);

  async function submit() {
    const body = draft.trim();
    if (!body) return;
    setSubmitting(true);
    try {
      await api.post(`/issues/${issueId}/comments`, { body });
      setDraft('');
      await load();
      onChange?.();
    } finally {
      setSubmitting(false);
    }
  }

  async function saveEdit(id: string) {
    const body = editBody.trim();
    if (!body) return;
    await api.patch(`/comments/${id}`, { body });
    setEditingId(null);
    setEditBody('');
    await load();
    onChange?.();
  }

  async function remove(id: string) {
    const ok = await confirmDialog({
      title: 'Delete this comment?',
      confirmLabel: 'Delete',
      destructive: true,
    });
    if (!ok) return;
    await api.delete(`/comments/${id}`);
    await load();
    onChange?.();
  }

  return (
    <div className="space-y-3">
      {comments === null ? (
        <p className="mono text-xs text-text-dim">loading…</p>
      ) : comments.length === 0 ? (
        <p className="mono text-xs text-text-dim">no comments yet — start the discussion.</p>
      ) : (
        comments.map((c) => {
          const isAuthor = c.authorId !== null && c.authorId === currentUserId;
          const isEditing = editingId === c.id;
          return (
            <div key={c.id} className="border border-border rounded p-3 bg-bg-soft/30">
              <div className="flex items-baseline justify-between mb-1.5">
                <span className="mono text-xs text-text">{c.authorName ?? '(deleted user)'}</span>
                <span className="mono text-[10px] text-text-dim">
                  {new Date(c.createdAt).toLocaleString()}
                  {c.edited ? ' · edited' : ''}
                </span>
              </div>

              {isEditing ? (
                <div className="space-y-2">
                  <textarea
                    className="input mono min-h-[80px]"
                    value={editBody}
                    onChange={(e) => setEditBody(e.target.value)}
                    autoFocus
                  />
                  <div className="flex gap-2">
                    <button className="btn text-xs" onClick={() => saveEdit(c.id)}>
                      save
                    </button>
                    <button
                      className="btn-ghost text-xs"
                      onClick={() => {
                        setEditingId(null);
                        setEditBody('');
                      }}
                    >
                      cancel
                    </button>
                  </div>
                </div>
              ) : (
                <>
                  <CommentBody body={c.body} />
                  {isAuthor && (
                    <div className="flex gap-3 mt-2">
                      <button
                        className="mono text-[11px] text-text-dim hover:text-accent"
                        onClick={() => {
                          setEditingId(c.id);
                          setEditBody(c.body);
                        }}
                      >
                        edit
                      </button>
                      <button
                        className="mono text-[11px] text-text-dim hover:text-priority-critical"
                        onClick={() => remove(c.id)}
                      >
                        delete
                      </button>
                    </div>
                  )}
                </>
              )}
            </div>
          );
        })
      )}

      <div className="pt-2">
        <textarea
          className="input mono min-h-[80px]"
          placeholder="Write a comment… use @name to mention a project member."
          value={draft}
          onChange={(e) => setDraft(e.target.value)}
        />
        <div className="flex justify-end mt-2">
          <button
            className="btn"
            onClick={submit}
            disabled={submitting || draft.trim().length === 0}
          >
            {submitting ? 'posting…' : 'post comment'}
          </button>
        </div>
      </div>
    </div>
  );
}

// Same handle pattern as the server-side regex (CommentsController.MentionRegex).
const MENTION_RE = /(^|\s|[([{,.;:!?])(@[A-Za-z0-9_-]{2,40})/g;

function CommentBody({ body }: { body: string }) {
  // Highlight @mentions; preserves whitespace via whitespace-pre-wrap. Uses
  // split-with-capture so we keep the prefix character (space, punctuation)
  // that the regex required as a left boundary.
  const parts = body.split(MENTION_RE);
  return (
    <p className="mono text-sm text-text whitespace-pre-wrap leading-relaxed">
      {parts.map((part, i) => {
        if (part?.startsWith('@')) {
          return (
            <span key={i} className="text-accent font-medium">
              {part}
            </span>
          );
        }
        return <span key={i}>{part}</span>;
      })}
    </p>
  );
}
