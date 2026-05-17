interface Props {
  title: string;
  color: string;
}

export function EpicPill({ title, color }: Props) {
  return (
    <span
      className="inline-flex items-center gap-1.5 px-2 py-0.5 mono text-[9px] uppercase tracking-widest border rounded shadow-sm"
      style={{
        borderColor: `${color}40`,
        color,
        backgroundColor: `${color}10`,
      }}
    >
      <span className="w-1.5 h-1.5 rounded-full shadow-sm" style={{ background: color }} />
      {title}
    </span>
  );
}
