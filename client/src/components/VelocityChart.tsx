import {
  ResponsiveContainer,
  ComposedChart,
  Bar,
  Line,
  XAxis,
  YAxis,
  Tooltip,
  CartesianGrid,
} from 'recharts';
import type { VelocityRow } from '../types';
import { useThemeStore } from '../lib/theme';

// Recharts can't read tailwind classes, so we resolve theme tokens to hex
// strings here. Subscribing to the theme store ensures the chart re-renders
// when the user toggles light/dark.
const TOKENS = {
  dark: {
    grid: '#1e1e22',
    axis: '#5a5a58',
    tooltipBg: '#0c0c0e',
    bar: '#22d3ee',
    line: '#ededea',
    cursor: 'rgba(34,211,238,0.05)',
  },
  light: {
    grid: '#e4e4e7',
    axis: '#71717a',
    tooltipBg: '#ffffff',
    bar: '#0891b2',
    line: '#18181b',
    cursor: 'rgba(8,145,178,0.08)',
  },
} as const;

export function VelocityChart({ data }: { data: VelocityRow[] }) {
  const theme = useThemeStore((s) => s.theme);
  const t = TOKENS[theme];

  if (!data.length) {
    return <p className="mono text-text-muted">no completed sprints yet.</p>;
  }

  const chartData = data.map((d) => ({
    name: d.name,
    points: d.pointsCompleted,
    cumulative: d.cumulativePoints,
  }));

  return (
    <div className="h-72">
      <ResponsiveContainer width="100%" height="100%">
        <ComposedChart data={chartData} margin={{ top: 10, right: 10, left: -10, bottom: 0 }}>
          <CartesianGrid stroke={t.grid} vertical={false} />
          <XAxis
            dataKey="name"
            stroke={t.axis}
            tick={{ fontFamily: 'IBM Plex Mono', fontSize: 11 }}
            axisLine={{ stroke: t.grid }}
            tickLine={false}
          />
          <YAxis
            stroke={t.axis}
            tick={{ fontFamily: 'IBM Plex Mono', fontSize: 11 }}
            axisLine={{ stroke: t.grid }}
            tickLine={false}
          />
          <Tooltip
            contentStyle={{
              background: t.tooltipBg,
              border: `1px solid ${t.grid}`,
              fontFamily: 'IBM Plex Mono',
              fontSize: 12,
            }}
            cursor={{ fill: t.cursor }}
          />
          <Bar dataKey="points" fill={t.bar} name="Per-sprint" />
          <Line
            type="monotone"
            dataKey="cumulative"
            stroke={t.line}
            strokeWidth={1.5}
            dot={{ fill: t.line, r: 3 }}
            name="Cumulative"
          />
        </ComposedChart>
      </ResponsiveContainer>
    </div>
  );
}
