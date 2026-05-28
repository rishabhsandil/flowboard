import type { Priority } from '../types';

// Filter values use string sentinels rather than nullable ids to keep the
// dropdown <option> values trivially serialisable.
export const ALL = 'all';
export const NONE = 'none';
export const ACTIVE = 'active';
export const ME = 'me';

export type EpicFilter = typeof ALL | typeof NONE | string;
export type SprintFilter = typeof ALL | typeof ACTIVE | typeof NONE | string;
export type AssigneeFilter = typeof ALL | typeof ME | typeof NONE | string;
export type PriorityFilter = typeof ALL | Priority;
export type LabelFilter = typeof ALL | string;

export interface BoardFilters {
  search: string;
  epic: EpicFilter;
  sprint: SprintFilter;
  assignee: AssigneeFilter;
  priority: PriorityFilter;
  label: LabelFilter;
}

export const DEFAULT_BOARD_FILTERS: BoardFilters = {
  search: '',
  epic: ALL,
  sprint: ALL,
  assignee: ALL,
  priority: ALL,
  label: ALL,
};

export interface SavedFilter {
  id: string;
  projectId: string;
  userId: string;
  name: string;
  filters: BoardFilters;
  createdAt: string;
}

export function countActiveBoardFilters(f: BoardFilters): number {
  let n = 0;
  if (f.search.trim()) n++;
  if (f.epic !== ALL) n++;
  if (f.sprint !== ALL) n++;
  if (f.assignee !== ALL) n++;
  if (f.priority !== ALL) n++;
  if (f.label !== ALL) n++;
  return n;
}
