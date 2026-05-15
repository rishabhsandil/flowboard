import { create } from 'zustand';
import { useEscapeKey } from './useEscapeKey';

/**
 * Promise-based confirm dialog. Replaces window.confirm() with a styled,
 * keyboard-accessible modal that matches the rest of the UI. The store
 * holds at most one pending request — opening a second confirm while one
 * is open replaces the first (consistent with browser confirm).
 *
 * Usage anywhere — components, async flows, etc.:
 *
 *   if (!(await confirmDialog({
 *     title: 'Delete this issue?',
 *     description: 'This cannot be undone.',
 *     confirmLabel: 'Delete',
 *     destructive: true,
 *   }))) return;
 *
 * The host component (`<ConfirmDialogHost />`) is mounted once near the
 * app root, beside `<ToastHost />`.
 */

export interface ConfirmOptions {
  title: string;
  description?: string;
  /** Label for the confirm button. Defaults to "Confirm". */
  confirmLabel?: string;
  /** Label for the cancel button. Defaults to "Cancel". */
  cancelLabel?: string;
  /** Renders the confirm button in the critical/red palette. */
  destructive?: boolean;
}

interface PendingRequest extends ConfirmOptions {
  id: number;
  resolve: (ok: boolean) => void;
}

interface ConfirmState {
  current: PendingRequest | null;
  open: (req: PendingRequest) => void;
  resolve: (id: number, ok: boolean) => void;
}

let nextId = 1;

const useConfirmStore = create<ConfirmState>((set, get) => ({
  current: null,
  open: (req) =>
    set((s) => {
      // Cancel any in-flight request before showing the new one.
      if (s.current) s.current.resolve(false);
      return { current: req };
    }),
  resolve: (id, ok) => {
    const cur = get().current;
    if (!cur || cur.id !== id) return;
    cur.resolve(ok);
    set({ current: null });
  },
}));

/** Programmatic API. Returns a promise that resolves true on confirm, false on cancel/escape/dismiss. */
// eslint-disable-next-line react-refresh/only-export-components
export function confirmDialog(opts: ConfirmOptions): Promise<boolean> {
  return new Promise((resolve) => {
    useConfirmStore.getState().open({ id: nextId++, ...opts, resolve });
  });
}

/** Mounts the dialog. Place once near the root of the app (see App.tsx). */
export function ConfirmDialogHost() {
  const current = useConfirmStore((s) => s.current);
  const resolve = useConfirmStore((s) => s.resolve);

  // Always call hooks in the same order — wire Escape to "cancel" whether
  // or not a dialog is currently open. The hook is a no-op when there's
  // nothing to cancel.
  useEscapeKey(() => {
    if (current) resolve(current.id, false);
  });

  if (!current) return null;

  const cancelLabel = current.cancelLabel ?? 'Cancel';
  const confirmLabel = current.confirmLabel ?? 'Confirm';
  // No dedicated `btn-danger` class — borrow the priority-critical palette
  // used by other destructive controls (see EpicModal's delete button).
  const confirmClass = current.destructive
    ? 'btn border-priority-critical text-priority-critical hover:bg-priority-critical/10'
    : 'btn-primary';

  return (
    <div
      className="fixed inset-0 bg-black/60 flex items-center justify-center z-[60] p-4"
      role="dialog"
      aria-modal="true"
      aria-labelledby="confirm-dialog-title"
      onClick={() => resolve(current.id, false)}
    >
      <div className="panel w-full max-w-sm p-6" onClick={(e) => e.stopPropagation()}>
        <p className="mono text-xs uppercase tracking-widest text-text-dim">// confirm</p>
        <h3 id="confirm-dialog-title" className="heading text-lg mt-1">
          {current.title}
        </h3>
        {current.description ? (
          <p className="text-sm text-text-dim mt-3">{current.description}</p>
        ) : null}

        <div className="flex justify-end gap-2 mt-6">
          <button type="button" className="btn-ghost" onClick={() => resolve(current.id, false)}>
            {cancelLabel}
          </button>
          <button
            type="button"
            autoFocus
            className={confirmClass}
            onClick={() => resolve(current.id, true)}
          >
            {confirmLabel}
          </button>
        </div>
      </div>
    </div>
  );
}
