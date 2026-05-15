import type { Priority } from '../types';

export const priorityBorder: Record<Priority, string> = {
  critical: 'border-l-priority-critical',
  high: 'border-l-priority-high',
  medium: 'border-l-priority-medium',
  low: 'border-l-priority-low',
};

export const priorityLabel: Record<Priority, string> = {
  critical: 'CRIT',
  high: 'HIGH',
  medium: 'MED',
  low: 'LOW',
};
