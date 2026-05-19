import clsx from 'clsx';

interface Props {
  /** Stable id used to pick a deterministic colour; falls back to name. */
  id?: string | null;
  name?: string | null;
  avatarUrl?: string | null;
  size?: 'xs' | 'sm' | 'md' | 'lg';
  /** Renders a dashed empty-state circle when both id and name are missing. */
  className?: string;
  title?: string;
}

const SIZE: Record<NonNullable<Props['size']>, string> = {
  xs: 'w-4 h-4 text-[8px]',
  sm: 'w-5 h-5 text-[9px]',
  md: 'w-7 h-7 text-[10px]',
  lg: 'w-9 h-9 text-xs',
};

// Eight high-contrast hues that read well on both dark + light themes.
// Avatar colours are decorative only — never load-bearing for identity.
const PALETTE = [
  ['#0ea5e9', '#082f49'], // sky
  ['#22d3ee', '#083344'], // cyan
  ['#10b981', '#022c22'], // emerald
  ['#84cc16', '#1a2e05'], // lime
  ['#eab308', '#422006'], // amber
  ['#f97316', '#431407'], // orange
  ['#ec4899', '#500724'], // pink
  ['#a855f7', '#2e1065'], // violet
];

export function Avatar({ id, name, avatarUrl, size = 'sm', className, title }: Props) {
  const dimensions = SIZE[size];
  const fallbackTitle = title ?? name ?? 'unassigned';

  if (avatarUrl) {
    return (
      <img
        src={avatarUrl}
        alt={name ?? 'avatar'}
        title={fallbackTitle}
        className={clsx(
          dimensions,
          'rounded-full object-cover border border-border/60 shrink-0',
          className,
        )}
      />
    );
  }

  if (!id && !name) {
    // Unassigned placeholder — dashed outline so it reads as "empty slot"
    // rather than a real (but unnamed) person.
    return (
      <span
        title={fallbackTitle}
        aria-label={fallbackTitle}
        className={clsx(
          dimensions,
          'rounded-full border border-dashed border-border-strong text-text-dim shrink-0',
          'inline-flex items-center justify-center mono',
          className,
        )}
      >
        ?
      </span>
    );
  }

  const initials = getInitials(name ?? '?');
  const [bg, fg] = pickColor(id ?? name ?? '');

  return (
    <span
      title={fallbackTitle}
      aria-label={fallbackTitle}
      className={clsx(
        dimensions,
        'rounded-full shrink-0 inline-flex items-center justify-center mono font-semibold uppercase select-none',
        className,
      )}
      style={{
        backgroundColor: bg + '33',
        color: bg,
        boxShadow: `inset 0 0 0 1px ${bg}66`,
        ...({ '--fg': fg } as React.CSSProperties),
      }}
    >
      {initials}
    </span>
  );
}

function getInitials(name: string): string {
  const cleaned = name.trim();
  if (!cleaned) return '?';
  const parts = cleaned.split(/\s+/).filter(Boolean);
  if (parts.length === 1) return parts[0].slice(0, 2);
  return (parts[0][0] + parts[parts.length - 1][0]).toUpperCase();
}

function pickColor(seed: string): [string, string] {
  let hash = 0;
  for (let i = 0; i < seed.length; i++) {
    hash = (hash * 31 + seed.charCodeAt(i)) >>> 0;
  }
  return PALETTE[hash % PALETTE.length] as [string, string];
}
