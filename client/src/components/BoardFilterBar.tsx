import { useMemo } from 'react';
import { Search, X } from 'lucide-react';
import type { EpicWithProgress, Label, ProjectMember, Sprint } from '../types';
import {
  ACTIVE,
  ALL,
  countActiveBoardFilters,
  DEFAULT_BOARD_FILTERS,
  ME,
  NONE,
  type AssigneeFilter,
  type BoardFilters,
  type EpicFilter,
  type LabelFilter,
  type PriorityFilter,
  type SprintFilter,
} from '../lib/boardFilters';

interface Props {
  filters: BoardFilters;
  onChange: (next: BoardFilters) => void;
  epics: EpicWithProgress[] | undefined;
  sprints: Sprint[] | undefined;
  members: ProjectMember[] | undefined;
  labels: Label[] | undefined;
  activeSprint: Sprint | undefined;
  /** Total issue count after filters (for the count badge); optional. */
  visibleCount?: number;
}

export function BoardFilterBar({
  filters,
  onChange,
  epics,
  sprints,
  members,
  labels,
  activeSprint,
  visibleCount,
}: Props) {
  function set<K extends keyof BoardFilters>(key: K, value: BoardFilters[K]) {
    onChange({ ...filters, [key]: value });
  }

  const activeCount = useMemo(() => countActiveBoardFilters(filters), [filters]);

  return (
    <div className="border-b border-border bg-bg-subtle/40">
      <div className="px-6 py-3 flex items-center gap-2 flex-wrap">
        {/* Search */}
        <div className="relative">
          <Search
            size={13}
            className="absolute left-2.5 top-1/2 -translate-y-1/2 text-text-dim pointer-events-none"
          />
          <input
            value={filters.search}
            onChange={(e) => set('search', e.target.value)}
            placeholder="search…"
            className="input mono text-xs py-1.5 pl-7 pr-2 w-48"
            aria-label="search board by title"
          />
        </div>

        <FilterSelect
          ariaLabel="filter by epic"
          value={filters.epic}
          onChange={(v) => set('epic', v as EpicFilter)}
          options={[
            [ALL, 'epic: any'],
            [NONE, 'epic: none'],
            ...(epics ?? []).map((e) => [e.id, `epic: ${e.title}`] as [string, string]),
          ]}
          active={filters.epic !== ALL}
        />

        <FilterSelect
          ariaLabel="filter by sprint"
          value={filters.sprint}
          onChange={(v) => set('sprint', v as SprintFilter)}
          options={[
            [ALL, 'sprint: any'],
            [
              ACTIVE,
              activeSprint ? `sprint: active (${activeSprint.name})` : 'sprint: active — none',
            ],
            [NONE, 'sprint: backlog'],
            ...(sprints ?? []).map(
              (s) =>
                [s.id, `sprint: ${s.name}${s.status !== 'planned' ? ` · ${s.status}` : ''}`] as [
                  string,
                  string,
                ],
            ),
          ]}
          active={filters.sprint !== ALL}
          disabledValues={!activeSprint ? [ACTIVE] : []}
        />

        <FilterSelect
          ariaLabel="filter by assignee"
          value={filters.assignee}
          onChange={(v) => set('assignee', v as AssigneeFilter)}
          options={[
            [ALL, 'assignee: anyone'],
            [ME, 'assignee: me'],
            [NONE, 'assignee: unassigned'],
            ...(members ?? []).map((m) => [m.id, `assignee: ${m.name}`] as [string, string]),
          ]}
          active={filters.assignee !== ALL}
        />

        <FilterSelect
          ariaLabel="filter by priority"
          value={filters.priority}
          onChange={(v) => set('priority', v as PriorityFilter)}
          options={[
            [ALL, 'priority: any'],
            ['critical', 'priority: critical'],
            ['high', 'priority: high'],
            ['medium', 'priority: medium'],
            ['low', 'priority: low'],
          ]}
          active={filters.priority !== ALL}
        />

        <FilterSelect
          ariaLabel="filter by label"
          value={filters.label}
          onChange={(v) => set('label', v as LabelFilter)}
          options={[
            [ALL, 'label: any'],
            ...(labels ?? []).map((l) => [l.id, `label: ${l.name}`] as [string, string]),
          ]}
          active={filters.label !== ALL}
        />

        {/* Spacer pushes count/reset to the right on wide layouts */}
        <div className="flex-1" />

        {visibleCount !== undefined && (
          <span className="mono text-[11px] text-text-dim">{visibleCount} visible</span>
        )}

        {activeCount > 0 && (
          <button
            onClick={() => onChange(DEFAULT_BOARD_FILTERS)}
            className="btn-ghost text-xs mono inline-flex items-center gap-1.5"
            aria-label="clear all filters"
          >
            <X size={12} />
            reset
            <span className="px-1.5 py-0.5 rounded-full bg-accent/10 text-accent text-[10px]">
              {activeCount}
            </span>
          </button>
        )}
      </div>
    </div>
  );
}

function FilterSelect({
  value,
  onChange,
  options,
  ariaLabel,
  active,
  disabledValues = [],
}: {
  value: string;
  onChange: (v: string) => void;
  options: [string, string][];
  ariaLabel: string;
  active: boolean;
  disabledValues?: string[];
}) {
  return (
    <select
      aria-label={ariaLabel}
      title={ariaLabel}
      value={value}
      onChange={(e) => onChange(e.target.value)}
      className={`input mono text-xs py-1.5 pr-7 w-auto ${
        active ? 'border-accent text-accent' : ''
      }`}
    >
      {options.map(([v, label]) => (
        <option key={v} value={v} disabled={disabledValues.includes(v)}>
          {label}
        </option>
      ))}
    </select>
  );
}
