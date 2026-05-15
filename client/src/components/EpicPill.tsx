interface Props {
  title: string;
  color: string;
}

export function EpicPill({ title, color }: Props) {
  return (
    <span
      className="inline-flex items-center gap-1.5 px-1.5 py-0.5 mono text-[10px] uppercase tracking-wider border"
      style={{ borderColor: color, color }}
    >
      <span className="w-1.5 h-1.5 rounded-full" style={{ background: color }} />
      {title}
    </span>
  );
}
