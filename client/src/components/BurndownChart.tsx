import {
  ResponsiveContainer,
  ComposedChart,
  Line,
  XAxis,
  YAxis,
  Tooltip,
  CartesianGrid,
  ReferenceLine,
} from 'recharts';
import type { BurndownPoint } from '../types';
import { useThemeStore } from '../lib/theme';

const TOKENS = {
  dark: {
    grid: '#1e1e22',
    axis: '#5a5a58',
    tooltipBg: '#0c0c0e',
    actual: '#22d3ee',
    ideal: '#5a5a58',
    cursor: 'rgba(34,211,238,0.05)',
    zero: '#ef4444',
  },
  light: {
    grid: '#e4e4e7',
    axis: '#71717a',
    tooltipBg: '#ffffff',
    actual: '#0891b2',
    ideal: '#a1a1aa',
    cursor: 'rgba(8,145,178,0.08)',
    zero: '#dc2626',
  },
} as const;

function fmtDate(iso: string) {
  return new Date(iso).toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
}

interface Props {
  data: BurndownPoint[];
  /** Total committed points at sprint start (first data point's remaining value). */
  totalPoints?: number;
}

export function BurndownChart({ data }: Props) {
  const theme = useThemeStore((s) => s.theme);
  const t = TOKENS[theme];

  if (!data.length) {
    return <p className="mono text-text-muted">no data yet — add issues to this sprint.</p>;
  }

  const total = data[0]?.remaining ?? 0;
  const n = data.length;

  // Build chart rows: actual remaining + computed ideal straight-line burndown
  const chartData = data.map((pt, i) => ({
    day: fmtDate(pt.day),
    actual: pt.remaining,
    // ideal: linear decline from total → 0 over sprint length
    ideal: n <= 1 ? 0 : Math.round(total * (1 - i / (n - 1))),
  }));

  return (
    <div className="h-72">
      <ResponsiveContainer width="100%" height="100%">
        <ComposedChart data={chartData} margin={{ top: 10, right: 10, left: -10, bottom: 0 }}>
          <CartesianGrid stroke={t.grid} vertical={false} />
          <XAxis
            dataKey="day"
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
            allowDecimals={false}
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
          <ReferenceLine y={0} stroke={t.zero} strokeDasharray="3 3" strokeWidth={1} />
          {/* Ideal burndown — dashed grey line */}
          <Line
            type="monotone"
            dataKey="ideal"
            stroke={t.ideal}
            strokeWidth={1}
            strokeDasharray="4 4"
            dot={false}
            name="Ideal"
          />
          {/* Actual remaining — solid accent line */}
          <Line
            type="monotone"
            dataKey="actual"
            stroke={t.actual}
            strokeWidth={2}
            dot={{ fill: t.actual, r: 3 }}
            name="Remaining"
          />
        </ComposedChart>
      </ResponsiveContainer>
    </div>
  );
}
