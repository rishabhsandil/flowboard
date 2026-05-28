import { useEffect, useMemo, useState } from 'react';
import { useOutletContext } from 'react-router-dom';
import {
  DndContext,
  DragOverlay,
  PointerSensor,
  useSensor,
  useSensors,
  type DragEndEvent,
  type DragStartEvent,
} from '@dnd-kit/core';
import { SortableContext, verticalListSortingStrategy } from '@dnd-kit/sortable';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { api } from '../lib/api';
import { useAuthStore } from '../lib/auth';
import { toast } from '../lib/toast';
import type {
  Board,
  BoardIssue,
  EpicWithProgress,
  Label,
  Project,
  ProjectMember,
  Sprint,
} from '../types';
import type { Paged } from '../types/api';
import { KanbanColumn } from '../components/KanbanColumn';
import { IssueCard } from '../components/IssueCard';
import { CreateIssueModal } from '../components/CreateIssueModal';
import { IssueModal } from '../components/IssueModal';
import { BoardFilterBar } from '../components/BoardFilterBar';
import { DEFAULT_BOARD_FILTERS, type BoardFilters, type SavedFilter } from '../lib/boardFilters';

interface Ctx {
  project?: Project;
}

export default function BoardPage() {
  const { project } = useOutletContext<Ctx>();
  const qc = useQueryClient();
  const currentUserId = useAuthStore((s) => s.user?.id);
  const [activeId, setActiveId] = useState<string | null>(null);
  const [openCreate, setOpenCreate] = useState(false);
  const [openIssueId, setOpenIssueId] = useState<string | null>(null);
  const [filters, setFilters] = useState<BoardFilters>(DEFAULT_BOARD_FILTERS);

  const queryKey = ['board', project?.id];
  const { data, isLoading } = useQuery({
    queryKey,
    queryFn: async () => (await api.get<Board>(`/projects/${project!.id}/board`)).data,
    enabled: !!project,
  });

  const { data: epics } = useQuery({
    queryKey: ['epics', project?.id],
    queryFn: async () =>
      (await api.get<Paged<EpicWithProgress>>(`/projects/${project!.id}/epics`)).data.items,
    enabled: !!project,
  });
  const { data: sprints } = useQuery({
    queryKey: ['sprints', project?.id],
    queryFn: async () =>
      (await api.get<Paged<Sprint>>(`/projects/${project!.id}/sprints`)).data.items,
    enabled: !!project,
  });
  const { data: members } = useQuery({
    queryKey: ['members', project?.id],
    queryFn: async () =>
      (await api.get<{ items: ProjectMember[] }>(`/projects/${project!.id}/members`)).data.items,
    enabled: !!project,
  });
  const { data: labels } = useQuery({
    queryKey: ['labels', project?.id],
    queryFn: async () =>
      (await api.get<{ items: Label[] }>(`/projects/${project!.id}/labels`)).data.items,
    enabled: !!project,
  });
  const { data: savedFilters } = useQuery({
    queryKey: ['saved-filters', project?.id],
    queryFn: async () =>
      (await api.get<{ items: SavedFilter[] }>(`/projects/${project!.id}/saved-filters`)).data
        .items,
    enabled: !!project,
  });
  const activeSprint = useMemo(() => sprints?.find((s) => s.status === 'active'), [sprints]);

  const savedFiltersKey = ['saved-filters', project?.id];

  const saveFilter = useMutation({
    mutationFn: async (name: string) =>
      api.post(`/projects/${project!.id}/saved-filters`, { name, filters }),
    onSuccess: () => qc.invalidateQueries({ queryKey: savedFiltersKey }),
    onError: (err: { response?: { data?: { error?: string } } }) => {
      if (err.response?.data?.error === 'saved_filter_name_exists')
        toast.error('a preset with that name already exists');
    },
  });

  const deleteSavedFilter = useMutation({
    mutationFn: async (id: string) => api.delete(`/saved-filters/${id}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: savedFiltersKey }),
  });

  // local mirror so drag-and-drop is instant
  const [columns, setColumns] = useState(data?.columns ?? []);
  useEffect(() => {
    setColumns(data?.columns ?? []);
  }, [data]);

  // Resolve dynamic targets ("active sprint", "me") into ids once per render
  // so the filter loop below stays a pure predicate match.
  const wantSprintId =
    filters.sprint === 'all'
      ? undefined
      : filters.sprint === 'active'
        ? (activeSprint?.id ?? null)
        : filters.sprint === 'none'
          ? null
          : filters.sprint;
  const wantAssigneeId =
    filters.assignee === 'all'
      ? undefined
      : filters.assignee === 'me'
        ? (currentUserId ?? null)
        : filters.assignee === 'none'
          ? null
          : filters.assignee;
  const wantEpicId =
    filters.epic === 'all' ? undefined : filters.epic === 'none' ? null : filters.epic;
  const searchLower = filters.search.trim().toLowerCase();

  const filteredColumns = useMemo(() => {
    return columns.map((c) => ({
      ...c,
      issues: c.issues.filter((i) => {
        // Hide sub-issues from the main board — they live under their parent.
        if (i.parent_id) return false;

        if (wantEpicId !== undefined && (i.epic_id ?? null) !== wantEpicId) return false;
        if (wantSprintId !== undefined && (i.sprint_id ?? null) !== wantSprintId) return false;
        if (wantAssigneeId !== undefined && (i.assignee_id ?? null) !== wantAssigneeId)
          return false;
        if (filters.priority !== 'all' && i.priority !== filters.priority) return false;
        if (filters.label !== 'all' && !i.labels.some((l) => l.id === filters.label)) return false;
        if (searchLower && !i.title.toLowerCase().includes(searchLower)) return false;
        return true;
      }),
    }));
  }, [
    columns,
    wantEpicId,
    wantSprintId,
    wantAssigneeId,
    filters.priority,
    filters.label,
    searchLower,
  ]);

  const visibleCount = useMemo(
    () => filteredColumns.reduce((n, c) => n + c.issues.length, 0),
    [filteredColumns],
  );

  const reorder = useMutation({
    mutationFn: async (items: { id: string; columnId: string; position: number }[]) =>
      api.patch(`/projects/${project!.id}/issues/reorder`, { items }),
    onError: () => {
      // Revert the optimistic state by refetching the canonical board.
      // We also surface a toast — silent rollback would feel like the drag
      // just "didn't take" with no explanation.
      qc.invalidateQueries({ queryKey });
      toast.error('could not save the move — board reloaded');
    },
  });

  const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 4 } }));

  const activeIssue = useMemo<BoardIssue | null>(() => {
    if (!activeId) return null;
    for (const c of columns) {
      const i = c.issues.find((x) => x.id === activeId);
      if (i) return i;
    }
    return null;
  }, [activeId, columns]);

  function findContainer(id: string): { columnId: string; index: number } | null {
    for (const c of columns) {
      const idx = c.issues.findIndex((i) => i.id === id);
      if (idx >= 0) return { columnId: c.id, index: idx };
    }
    // dropping on empty column = id is the column id
    if (columns.find((c) => c.id === id)) return { columnId: id, index: -1 };
    return null;
  }

  function onDragStart(e: DragStartEvent) {
    setActiveId(String(e.active.id));
  }

  function onDragEnd(e: DragEndEvent) {
    setActiveId(null);
    const { active, over } = e;
    if (!over) return;
    const activeIdStr = String(active.id);
    const overIdStr = String(over.id);
    if (activeIdStr === overIdStr) return;

    const from = findContainer(activeIdStr);
    const to = findContainer(overIdStr);
    if (!from || !to) return;

    const next = columns.map((c) => ({ ...c, issues: [...c.issues] }));
    const fromCol = next.find((c) => c.id === from.columnId)!;
    const toCol = next.find((c) => c.id === to.columnId)!;
    const [moved] = fromCol.issues.splice(from.index, 1);
    const insertIndex = to.index < 0 ? toCol.issues.length : to.index;
    toCol.issues.splice(insertIndex, 0, moved);

    // recompute positions per column
    const reorderPayload: { id: string; columnId: string; position: number }[] = [];
    for (const c of next) {
      c.issues.forEach((i, idx) => {
        if (i.position !== idx || i.id === moved.id) {
          reorderPayload.push({ id: i.id, columnId: c.id, position: idx });
        }
        i.position = idx;
      });
    }
    setColumns(next);
    if (reorderPayload.length) reorder.mutate(reorderPayload);
  }

  if (isLoading || !project) {
    return <div className="p-8 mono text-text-muted">loading board…</div>;
  }

  const filtersActive =
    visibleCount !== columns.reduce((n, c) => n + c.issues.filter((i) => !i.parent_id).length, 0);

  return (
    <div className="h-screen flex flex-col">
      {/* Header: title + new-issue CTA */}
      <div className="px-6 pt-5 pb-3 flex items-center justify-between gap-4 flex-wrap">
        <div>
          <p className="mono text-xs uppercase tracking-widest text-text-dim">// board</p>
          <h1 className="mono text-xl">{project.name}</h1>
        </div>
        <button onClick={() => setOpenCreate(true)} className="btn-primary">
          + new issue
        </button>
      </div>

      {/* Filter bar (separate row so it stays compact across breakpoints) */}
      <BoardFilterBar
        filters={filters}
        onChange={setFilters}
        epics={epics}
        sprints={sprints}
        members={members}
        labels={labels}
        activeSprint={activeSprint}
        visibleCount={filtersActive ? visibleCount : undefined}
        savedFilters={savedFilters}
        onSaveFilter={(name) => saveFilter.mutate(name)}
        onDeleteSavedFilter={(id) => deleteSavedFilter.mutate(id)}
      />

      <DndContext sensors={sensors} onDragStart={onDragStart} onDragEnd={onDragEnd}>
        <div className="flex-1 overflow-x-auto overflow-y-hidden">
          <div className="flex gap-px bg-border h-full p-px min-w-max">
            {filteredColumns.map((col) => (
              <SortableContext
                key={col.id}
                items={col.issues.map((i) => i.id)}
                strategy={verticalListSortingStrategy}
              >
                <KanbanColumn
                  column={col}
                  filtersActive={filtersActive}
                  onIssueClick={(id) => setOpenIssueId(id)}
                />
              </SortableContext>
            ))}
          </div>
        </div>
        <DragOverlay>{activeIssue && <IssueCard issue={activeIssue} dragging />}</DragOverlay>
      </DndContext>

      {openCreate && (
        <CreateIssueModal
          projectId={project.id}
          firstColumnId={columns[0]?.id}
          defaultEpicId={filters.epic !== 'all' && filters.epic !== 'none' ? filters.epic : null}
          defaultSprintId={
            filters.sprint === 'active'
              ? (activeSprint?.id ?? null)
              : filters.sprint !== 'all' && filters.sprint !== 'none'
                ? filters.sprint
                : null
          }
          onClose={() => setOpenCreate(false)}
          onCreated={() => qc.invalidateQueries({ queryKey })}
        />
      )}
      {openIssueId && (
        <IssueModal
          issueId={openIssueId}
          onClose={() => setOpenIssueId(null)}
          onChange={() => qc.invalidateQueries({ queryKey })}
        />
      )}
    </div>
  );
}
