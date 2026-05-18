import { useState } from 'react';
import { useOutletContext } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import {
  AreaChart,
  Area,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
  Legend,
  ResponsiveContainer,
} from 'recharts';
import { api } from '../lib/api';
import type { CfdPoint, Project } from '../types';

interface Ctx {
  project?: Project;
}

// Consistent palette for up to 8 columns — falls back to slate for extras.
const COLUMN_COLORS = [
  '#64748b', // Backlog — slate
  '#3b82f6', // In Progress — blue
  '#a855f7', // In Review — purple
  '#22c55e', // Done — green
  '#f59e0b', // amber
  '#ef4444', // red
  '#06b6d4', // cyan
  '#ec4899', // pink
];

const DAY_OPTIONS = [7, 14, 30, 60, 90];

function formatDate(iso: string) {
  return new Date(iso).toLocaleDateString('en-US', { month: 'short', day: 'numeric' });
}

export default function CfdPage() {
  const { project } = useOutletContext<Ctx>();
  const [days, setDays] = useState(30);

  const { data, isLoading } = useQuery({
    queryKey: ['cfd', project?.id, days],
    queryFn: async () =>
      (await api.get<{ cfd: CfdPoint[] }>(`/projects/${project!.id}/reports/cfd?days=${days}`)).data
        .cfd,
    enabled: !!project,
  });

  // Pivot the flat rows into { date, [columnName]: count } objects for Recharts.
  const { chartData, columns } = pivotCfd(data ?? []);

  return (
    <div className="p-8 h-full overflow-y-auto">
      <div className="flex items-center justify-between mb-6">
        <div>
          <h1 className="mono text-lg font-semibold">Cumulative Flow</h1>
          <p className="mono text-xs text-text-muted mt-0.5">open issues per column over time</p>
        </div>

        <div className="flex items-center gap-2">
          <span className="mono text-xs text-text-muted">last</span>
          <select
            value={days}
            onChange={(e) => setDays(Number(e.target.value))}
            className="mono text-xs bg-bg-panel border border-border rounded px-2 py-1 text-text"
          >
            {DAY_OPTIONS.map((d) => (
              <option key={d} value={d}>
                {d} days
              </option>
            ))}
          </select>
        </div>
      </div>

      {isLoading ? (
        <p className="mono text-text-muted text-sm">loading…</p>
      ) : chartData.length === 0 ? (
        <div className="border border-border rounded p-8 text-center">
          <p className="mono text-text-muted text-sm">no data yet</p>
          <p className="mono text-text-dim text-xs mt-1">
            snapshots are taken automatically each day you visit this page
          </p>
        </div>
      ) : (
        <div className="border border-border rounded p-4 bg-bg-panel">
          <ResponsiveContainer width="100%" height={380}>
            <AreaChart data={chartData} margin={{ top: 8, right: 16, bottom: 8, left: 0 }}>
              <CartesianGrid strokeDasharray="3 3" stroke="var(--color-border)" />
              <XAxis
                dataKey="date"
                tickFormatter={formatDate}
                tick={{ fontSize: 11, fontFamily: 'monospace', fill: 'var(--color-text-muted)' }}
                tickLine={false}
              />
              <YAxis
                allowDecimals={false}
                tick={{ fontSize: 11, fontFamily: 'monospace', fill: 'var(--color-text-muted)' }}
                tickLine={false}
                axisLine={false}
              />
              <Tooltip
                contentStyle={{
                  background: 'var(--color-bg-panel)',
                  border: '1px solid var(--color-border)',
                  fontFamily: 'monospace',
                  fontSize: 12,
                }}
                labelFormatter={formatDate}
              />
              <Legend wrapperStyle={{ fontFamily: 'monospace', fontSize: 12 }} />
              {columns.map((col, i) => (
                <Area
                  key={col}
                  type="monotone"
                  dataKey={col}
                  stackId="1"
                  stroke={COLUMN_COLORS[i % COLUMN_COLORS.length]}
                  fill={COLUMN_COLORS[i % COLUMN_COLORS.length]}
                  fillOpacity={0.6}
                />
              ))}
            </AreaChart>
          </ResponsiveContainer>
        </div>
      )}
    </div>
  );
}

interface ChartRow {
  date: string;
  [columnName: string]: string | number;
}

function pivotCfd(points: CfdPoint[]): { chartData: ChartRow[]; columns: string[] } {
  if (points.length === 0) return { chartData: [], columns: [] };

  const columnSet = new Set<string>();
  const byDate = new Map<string, Record<string, number>>();

  for (const p of points) {
    const day = p.day.slice(0, 10); // ISO date string
    columnSet.add(p.columnName);
    const row = byDate.get(day) ?? {};
    row[p.columnName] = p.issueCount;
    byDate.set(day, row);
  }

  const columns = Array.from(columnSet);
  const chartData: ChartRow[] = Array.from(byDate.entries())
    .sort(([a], [b]) => a.localeCompare(b))
    .map(([date, counts]) => ({
      date,
      ...Object.fromEntries(columns.map((c) => [c, counts[c] ?? 0])),
    }));

  return { chartData, columns };
}
