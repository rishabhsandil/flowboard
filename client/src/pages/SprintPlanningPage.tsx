import { useEffect, useMemo, useState } from 'react';
import { useOutletContext } from 'react-router-dom';
import {
  DndContext,
  DragOverlay,
  PointerSensor,
  useSensor,
  useSensors,
  useDroppable,
  type DragEndEvent,
  type DragStartEvent,
} from '@dnd-kit/core';
import { SortableContext, useSortable, verticalListSortingStrategy } from '@dnd-kit/sortable';
import { CSS } from '@dnd-kit/utilities';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { api } from '../lib/api';
import { toast } from '../lib/toast';
import type { IssueListRow, Project, Sprint } from '../types';
import type { Paged } from '../types/api';

const EMPTY_GUID = '00000000-0000-0000-0000-000000000000';

interface Ctx {
  project?: Project;
}

// ── Draggable issue card ────────────────────────────────────────────────────

function PlanIssueCard({ issue, isDragging }: { issue: IssueListRow; isDragging?: boolean }) {
  const { attributes, listeners, setNodeRef, transform, transition } = useSortable({
    id: issue.id,
  });

  const style = {
    transform: CSS.Transform.toString(transform),
    transition,
    opacity: isDragging ? 0.4 : 1,
  };

  return (
    <div
      ref={setNodeRef}
      style={style}
      {...attributes}
      {...listeners}
      className="bg-bg-panel border border-border rounded px-3 py-2 cursor-grab active:cursor-grabbing select-none"
    >
      <p className="mono text-sm truncate">{issue.title}</p>
      <div className="flex items-center gap-2 mt-1">
        <span className={`mono text-xs px-1.5 rounded ${priorityClass(issue.priority)}`}>
          {issue.priority}
        </span>
        {issue.storyPoints > 0 && (
          <span className="mono text-xs text-text-muted">{issue.storyPoints} pts</span>
        )}
        {issue.epicTitle && (
          <span
            className="mono text-xs px-1.5 rounded"
            style={{ background: issue.epicColor ?? '#64748b', color: '#fff' }}
          >
            {issue.epicTitle}
          </span>
        )}
      </div>
    </div>
  );
}

function IssueCardOverlay({ issue }: { issue: IssueListRow }) {
  return (
    <div className="bg-bg-panel border border-accent rounded px-3 py-2 shadow-lg rotate-1 w-64">
      <p className="mono text-sm truncate">{issue.title}</p>
    </div>
  );
}

// ── Droppable column ────────────────────────────────────────────────────────

function PlanColumn({
  id,
  label,
  badge,
  issues,
  activeId,
}: {
  id: string;
  label: string;
  badge?: string;
  issues: IssueListRow[];
  activeId: string | null;
}) {
  const { setNodeRef, isOver } = useDroppable({ id });

  return (
    <div className="flex flex-col flex-1 min-w-0">
      <div className="flex items-center gap-2 mb-3">
        <span className="mono text-xs uppercase tracking-widest text-text-muted">{label}</span>
        {badge && (
          <span className="mono text-xs bg-bg-panel border border-border rounded-full px-2">
            {badge}
          </span>
        )}
      </div>

      <div
        ref={setNodeRef}
        className={`flex-1 border rounded-lg p-2 space-y-2 min-h-[200px] transition-colors ${
          isOver ? 'border-accent bg-accent/5' : 'border-border bg-bg-subtle'
        }`}
      >
        <SortableContext items={issues.map((i) => i.id)} strategy={verticalListSortingStrategy}>
          {issues.map((i) => (
            <PlanIssueCard key={i.id} issue={i} isDragging={activeId === i.id} />
          ))}
        </SortableContext>

        {issues.length === 0 && (
          <p className="mono text-xs text-text-dim text-center py-8">
            {isOver ? 'drop here' : 'no issues'}
          </p>
        )}
      </div>
    </div>
  );
}

// ── Main page ───────────────────────────────────────────────────────────────

export default function SprintPlanningPage() {
  const { project } = useOutletContext<Ctx>();
  const qc = useQueryClient();
  const [selectedSprintId, setSelectedSprintId] = useState<string | null>(null);
  const [activeId, setActiveId] = useState<string | null>(null);

  const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 4 } }));

  // Sprints list
  const { data: sprints } = useQuery({
    queryKey: ['sprints', project?.id],
    queryFn: async () =>
      (await api.get<Paged<Sprint>>(`/projects/${project!.id}/sprints?take=100`)).data.items,
    enabled: !!project,
  });

  // Auto-select active sprint, then first sprint
  useEffect(() => {
    if (!sprints || selectedSprintId) return;
    const active = sprints.find((s) => s.status === 'active') ?? sprints[0];
    if (active) setSelectedSprintId(active.id);
  }, [sprints, selectedSprintId]);

  // Backlog: issues with no sprint (use empty-guid sentinel)
  const backlogKey = ['plan-backlog', project?.id];
  const { data: backlogData } = useQuery({
    queryKey: backlogKey,
    queryFn: async () =>
      (
        await api.get<{ items: IssueListRow[] }>(
          `/projects/${project!.id}/issues?sprintId=${EMPTY_GUID}&take=200`,
        )
      ).data.items,
    enabled: !!project,
  });

  // Sprint issues
  const sprintKey = ['plan-sprint', project?.id, selectedSprintId];
  const { data: sprintData } = useQuery({
    queryKey: sprintKey,
    queryFn: async () =>
      (
        await api.get<{ items: IssueListRow[] }>(
          `/projects/${project!.id}/issues?sprintId=${selectedSprintId}&take=200`,
        )
      ).data.items,
    enabled: !!project && !!selectedSprintId,
  });

  const [backlog, setBacklog] = useState<IssueListRow[]>([]);
  const [sprint, setSprint] = useState<IssueListRow[]>([]);

  useEffect(() => {
    setBacklog(backlogData ?? []);
  }, [backlogData]);
  useEffect(() => {
    setSprint(sprintData ?? []);
  }, [sprintData]);

  const activeIssue = useMemo(() => {
    return [...backlog, ...sprint].find((i) => i.id === activeId) ?? null;
  }, [backlog, sprint, activeId]);

  const assignSprint = useMutation({
    mutationFn: ({ issueId, sprintId }: { issueId: string; sprintId: string | null }) =>
      api.patch(`/issues/${issueId}`, {
        sprintId: sprintId ?? EMPTY_GUID,
      }),
    onError: () => {
      // Revert optimistic update on failure
      qc.invalidateQueries({ queryKey: backlogKey });
      qc.invalidateQueries({ queryKey: sprintKey });
      toast.error('could not update sprint assignment — reloaded');
    },
  });

  function onDragStart(e: DragStartEvent) {
    setActiveId(String(e.active.id));
  }

  function onDragEnd(e: DragEndEvent) {
    setActiveId(null);
    const { active, over } = e;
    if (!over || !selectedSprintId) return;

    const issueId = String(active.id);
    const overId = String(over.id);

    const isInBacklog = backlog.some((i) => i.id === issueId);
    const isInSprint = sprint.some((i) => i.id === issueId);

    const droppedOnSprint = overId === 'sprint-col' || sprint.some((i) => i.id === overId);
    const droppedOnBacklog = overId === 'backlog-col' || backlog.some((i) => i.id === overId);

    if (isInBacklog && droppedOnSprint) {
      // Move to sprint
      const issue = backlog.find((i) => i.id === issueId)!;
      setBacklog((b) => b.filter((i) => i.id !== issueId));
      setSprint((s) => [...s, { ...issue, sprintId: selectedSprintId }]);
      assignSprint.mutate({ issueId, sprintId: selectedSprintId });
    } else if (isInSprint && droppedOnBacklog) {
      // Move back to backlog
      const issue = sprint.find((i) => i.id === issueId)!;
      setSprint((s) => s.filter((i) => i.id !== issueId));
      setBacklog((b) => [...b, { ...issue, sprintId: null }]);
      assignSprint.mutate({ issueId, sprintId: null });
    }
  }

  const selectedSprint = sprints?.find((s) => s.id === selectedSprintId);

  if (!project) return <div className="p-8 mono text-text-muted">loading…</div>;

  return (
    <div className="h-full flex flex-col">
      {/* Header */}
      <div className="px-6 py-4 border-b border-border flex items-center justify-between gap-4">
        <div>
          <p className="mono text-xs uppercase tracking-widest text-text-dim">// sprint planning</p>
          <h1 className="mono text-xl">{project.name}</h1>
        </div>

        <div className="flex items-center gap-2">
          <span className="mono text-xs text-text-muted">target sprint</span>
          <select
            className="input mono text-xs py-1.5"
            value={selectedSprintId ?? ''}
            onChange={(e) => setSelectedSprintId(e.target.value || null)}
          >
            <option value="">— pick sprint —</option>
            {sprints?.map((s) => (
              <option key={s.id} value={s.id}>
                {s.name} ({s.status})
              </option>
            ))}
          </select>
        </div>
      </div>

      {/* Columns */}
      <div className="flex-1 overflow-hidden p-6">
        {!selectedSprintId ? (
          <p className="mono text-text-muted text-sm">select a sprint to start planning</p>
        ) : (
          <DndContext sensors={sensors} onDragStart={onDragStart} onDragEnd={onDragEnd}>
            <div className="h-full flex gap-6">
              <PlanColumn
                id="backlog-col"
                label="// backlog"
                badge={String(backlog.length)}
                issues={backlog}
                activeId={activeId}
              />

              <div className="w-px bg-border flex-none" />

              <PlanColumn
                id="sprint-col"
                label={`// ${selectedSprint?.name ?? 'sprint'}`}
                badge={`${sprint.length} issues · ${sprint.reduce((a, i) => a + i.storyPoints, 0)} pts`}
                issues={sprint}
                activeId={activeId}
              />
            </div>

            <DragOverlay>
              {activeIssue ? <IssueCardOverlay issue={activeIssue} /> : null}
            </DragOverlay>
          </DndContext>
        )}
      </div>
    </div>
  );
}

function priorityClass(p: string) {
  switch (p) {
    case 'critical':
      return 'text-red-400 bg-red-900/30';
    case 'high':
      return 'text-orange-400 bg-orange-900/30';
    case 'medium':
      return 'text-yellow-400 bg-yellow-900/30';
    default:
      return 'text-text-muted bg-bg-panel';
  }
}
