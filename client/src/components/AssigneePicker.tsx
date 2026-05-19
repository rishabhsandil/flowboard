import { useEffect, useMemo, useRef, useState } from 'react';
import { ChevronDown, X } from 'lucide-react';
import clsx from 'clsx';
import type { ProjectMember } from '../types';
import { Avatar } from './Avatar';

interface Props {
  /** Current assignee id, or null when unassigned. */
  value: string | null;
  /** Called with the new assignee id, or null to clear. */
  onChange: (next: string | null) => void;
  members: ProjectMember[] | undefined;
  /** Current logged-in user id; enables the "assign to me" shortcut. */
  currentUserId: string | null;
  disabled?: boolean;
  /** Pass true to render only the avatar/name display without a chevron. */
  compact?: boolean;
}

/**
 * Jira/Zenhub-style assignee picker: clicking the field opens a small popover
 * with search, "assign to me", and an unassigned option. Single-assignee
 * model — mirrors `issues.assignee_id` on the backend.
 */
export function AssigneePicker({
  value,
  onChange,
  members,
  currentUserId,
  disabled,
  compact,
}: Props) {
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState('');
  const rootRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);

  const current = useMemo(() => members?.find((m) => m.id === value) ?? null, [members, value]);

  // Sort: me first, then alpha. Filter by query.
  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase();
    const list = (members ?? []).slice().sort((a, b) => {
      if (a.id === currentUserId) return -1;
      if (b.id === currentUserId) return 1;
      return a.name.localeCompare(b.name);
    });
    if (!q) return list;
    return list.filter(
      (m) => m.name.toLowerCase().includes(q) || m.email.toLowerCase().includes(q),
    );
  }, [members, query, currentUserId]);

  useEffect(() => {
    if (!open) return;
    const onDocClick = (e: MouseEvent) => {
      if (!rootRef.current?.contains(e.target as Node)) setOpen(false);
    };
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setOpen(false);
    };
    document.addEventListener('mousedown', onDocClick);
    document.addEventListener('keydown', onKey);
    // Focus search on open.
    inputRef.current?.focus();
    return () => {
      document.removeEventListener('mousedown', onDocClick);
      document.removeEventListener('keydown', onKey);
    };
  }, [open]);

  function pick(next: string | null) {
    onChange(next);
    setOpen(false);
    setQuery('');
  }

  return (
    <div ref={rootRef} className="relative">
      <button
        type="button"
        disabled={disabled}
        onClick={() => setOpen((o) => !o)}
        className={clsx(
          'w-full flex items-center gap-2 text-left text-sm',
          'border rounded px-2.5 py-1.5 transition-colors',
          'bg-bg-soft/20 border-border hover:border-border-strong',
          open && 'border-accent ring-1 ring-accent',
          disabled && 'opacity-60 cursor-not-allowed',
        )}
        aria-haspopup="listbox"
        aria-expanded={open}
      >
        <Avatar
          id={current?.id ?? null}
          name={current?.name ?? null}
          avatarUrl={current?.avatarUrl ?? null}
          size="sm"
        />
        <span className={clsx('flex-1 truncate', !current && 'text-text-dim italic')}>
          {current?.name ?? 'Unassigned'}
        </span>
        {!compact && <ChevronDown size={14} className="text-text-dim shrink-0" />}
      </button>

      {open && (
        <div
          role="listbox"
          aria-label="select assignee"
          className="absolute z-30 mt-1 left-0 right-0 panel border bg-bg shadow-xl rounded overflow-hidden"
        >
          <div className="p-2 border-b border-border">
            <input
              ref={inputRef}
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              placeholder="search members…"
              className="input mono text-xs py-1.5 w-full"
              aria-label="search project members"
            />
          </div>

          <ul className="max-h-64 overflow-y-auto py-1 text-sm">
            {/* Quick action: assign to current user (only when not already
                self and current user is a member). */}
            {currentUserId &&
              currentUserId !== value &&
              members?.some((m) => m.id === currentUserId) && (
                <Row
                  selected={false}
                  onClick={() => pick(currentUserId)}
                  label="Assign to me"
                  hint={members.find((m) => m.id === currentUserId)?.name ?? ''}
                  member={members.find((m) => m.id === currentUserId)}
                />
              )}

            {/* Unassigned option */}
            <Row
              selected={value === null}
              onClick={() => pick(null)}
              label="Unassigned"
              icon={<X size={12} className="text-text-dim" />}
            />

            {filtered.length === 0 && query && (
              <li className="px-3 py-2 mono text-xs text-text-dim">no matches</li>
            )}

            {filtered.map((m) => (
              <Row
                key={m.id}
                selected={m.id === value}
                onClick={() => pick(m.id)}
                label={m.name}
                hint={m.email}
                member={m}
              />
            ))}
          </ul>
        </div>
      )}
    </div>
  );
}

function Row({
  selected,
  onClick,
  label,
  hint,
  member,
  icon,
}: {
  selected: boolean;
  onClick: () => void;
  label: string;
  hint?: string;
  member?: ProjectMember;
  icon?: React.ReactNode;
}) {
  return (
    <li>
      <button
        type="button"
        role="option"
        aria-selected={selected}
        onClick={onClick}
        className={clsx(
          'w-full flex items-center gap-2 px-3 py-1.5 text-left text-sm',
          'hover:bg-bg-soft/60 transition-colors',
          selected && 'bg-accent/10 text-accent',
        )}
      >
        {member ? (
          <Avatar id={member.id} name={member.name} avatarUrl={member.avatarUrl} size="sm" />
        ) : (
          <span className="w-5 h-5 rounded-full border border-dashed border-border-strong inline-flex items-center justify-center shrink-0">
            {icon}
          </span>
        )}
        <span className="flex-1 truncate">{label}</span>
        {hint && hint !== label && (
          <span className="mono text-[10px] text-text-dim truncate max-w-[40%]">{hint}</span>
        )}
      </button>
    </li>
  );
}
