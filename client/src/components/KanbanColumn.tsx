import { useDroppable } from '@dnd-kit/core';
import type { BoardColumn } from '../types';
import { IssueCard } from './IssueCard';
import { useSortable } from '@dnd-kit/sortable';
import { CSS } from '@dnd-kit/utilities';

interface Props {
  column: BoardColumn;
  onIssueClick: (id: string) => void;
}

export function KanbanColumn({ column, onIssueClick }: Props) {
  const { setNodeRef, isOver } = useDroppable({ id: column.id });
  const totalPoints = column.issues.reduce((s, i) => s + (i.story_points || 0), 0);

  return (
    <div className="bg-bg w-72 flex-shrink-0 flex flex-col">
      <div className="px-4 py-3 border-b border-border flex items-center justify-between">
        <h3 className="heading text-xs">{column.name}</h3>
        <div className="flex items-center gap-2 mono text-xs text-text-dim">
          <span>{column.issues.length}</span>
          {totalPoints > 0 && <span className="text-accent">{totalPoints}pt</span>}
        </div>
      </div>

      <div
        ref={setNodeRef}
        className={`flex-1 overflow-y-auto p-2 space-y-2 transition-colors ${
          isOver ? 'bg-accent/5' : ''
        }`}
      >
        {column.issues.map((issue) => (
          <SortableIssue key={issue.id} issueId={issue.id}>
            <IssueCard issue={issue} onClick={() => onIssueClick(issue.id)} />
          </SortableIssue>
        ))}
        {column.issues.length === 0 && (
          <div className="mono text-xs text-text-dim text-center py-8">// empty</div>
        )}
      </div>
    </div>
  );
}

function SortableIssue({ issueId, children }: { issueId: string; children: React.ReactNode }) {
  const { attributes, listeners, setNodeRef, transform, transition, isDragging } = useSortable({
    id: issueId,
  });
  const style = {
    transform: CSS.Translate.toString(transform),
    transition,
    opacity: isDragging ? 0.4 : 1,
  };
  return (
    <div ref={setNodeRef} style={style} {...attributes} {...listeners}>
      {children}
    </div>
  );
}
