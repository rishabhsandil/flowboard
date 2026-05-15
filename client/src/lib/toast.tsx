import { create } from 'zustand';
import { useEffect } from 'react';

/**
 * Lightweight toast/error notification system. Anywhere in the app can call
 * <code>toast.error('label_name_exists')</code> or <code>toast.success(...)</code>
 * and a small stack of cards will appear in the bottom-right. The axios
 * response interceptor (in <code>lib/api.ts</code>) routes unhandled API
 * errors through here so a 500 / 4xx never goes silent.
 *
 * Implementation: a tiny Zustand store holding the queue + a host component
 * that renders + auto-dismisses entries. No external deps.
 */

export type ToastKind = 'error' | 'success' | 'info';

export interface Toast {
  id: number;
  kind: ToastKind;
  message: string;
}

interface ToastState {
  items: Toast[];
  push: (kind: ToastKind, message: string) => void;
  dismiss: (id: number) => void;
}

let nextId = 1;

const useToastStore = create<ToastState>((set) => ({
  items: [],
  push: (kind, message) =>
    set((s) => {
      // Coalesce duplicate consecutive messages so a flapping endpoint
      // doesn't pile up 50 identical cards.
      const last = s.items[s.items.length - 1];
      if (last && last.kind === kind && last.message === message) return s;
      return { items: [...s.items, { id: nextId++, kind, message }] };
    }),
  dismiss: (id) => set((s) => ({ items: s.items.filter((t) => t.id !== id) })),
}));

/** Programmatic API. Safe to call from anywhere — components, axios interceptors, etc. */
// eslint-disable-next-line react-refresh/only-export-components
export const toast = {
  error: (message: string) => useToastStore.getState().push('error', message),
  success: (message: string) => useToastStore.getState().push('success', message),
  info: (message: string) => useToastStore.getState().push('info', message),
};

/**
 * Mounts the toast stack. Place once near the root of the app
 * (see <code>App.tsx</code>). Each entry auto-dismisses after 5s but can
 * also be closed by clicking it.
 */
export function ToastHost() {
  const items = useToastStore((s) => s.items);
  const dismiss = useToastStore((s) => s.dismiss);

  return (
    <div
      className="fixed bottom-4 right-4 z-50 flex flex-col gap-2 pointer-events-none"
      role="region"
      aria-label="notifications"
    >
      {items.map((t) => (
        <ToastCard key={t.id} toast={t} onDismiss={() => dismiss(t.id)} />
      ))}
    </div>
  );
}

function ToastCard({ toast, onDismiss }: { toast: Toast; onDismiss: () => void }) {
  // Auto-dismiss. Errors stay visible a bit longer because they often have
  // copy worth reading.
  useEffect(() => {
    const ms = toast.kind === 'error' ? 6000 : 4000;
    const t = setTimeout(onDismiss, ms);
    return () => clearTimeout(t);
  }, [onDismiss, toast.kind]);

  const palette =
    toast.kind === 'error'
      ? 'border-priority-critical text-priority-critical bg-bg-panel'
      : toast.kind === 'success'
        ? 'border-accent text-accent bg-bg-panel'
        : 'border-border text-text bg-bg-panel';

  return (
    <button
      onClick={onDismiss}
      className={`pointer-events-auto mono text-xs px-3 py-2 rounded border ${palette} max-w-sm text-left shadow-lg`}
    >
      <span className="uppercase tracking-widest opacity-70 mr-2">{toast.kind}</span>
      {toast.message}
    </button>
  );
}
