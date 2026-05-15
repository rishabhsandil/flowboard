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
import { toast } from '../lib/toast';
import type { Board, BoardIssue, EpicWithProgress, Project, Sprint } from '../types';
import type { Paged } from '../types/api';
import { KanbanColumn } from '../components/KanbanColumn';
import { IssueCard } from '../components/IssueCard';
import { CreateIssueModal } from '../components/CreateIssueModal';
import { IssueModal } from '../components/IssueModal';

interface Ctx {
  project?: Project;
}

type EpicFilter = 'all' | 'none' | string; // string = epic id
type SprintFilter = 'all' | 'active' | 'none' | string;

export default function BoardPage() {
  const { project } = useOutletContext<Ctx>();
  const qc = useQueryClient();
  const [activeId, setActiveId] = useState<string | null>(null);
  const [openCreate, setOpenCreate] = useState(false);
  const [openIssueId, setOpenIssueId] = useState<string | null>(null);
  const [epicFilter, setEpicFilter] = useState<EpicFilter>('all');
  const [sprintFilter, setSprintFilter] = useState<SprintFilter>('all');

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
  const activeSprint = useMemo(() => sprints?.find((s) => s.status === 'active'), [sprints]);

  // local mirror so drag-and-drop is instant
  const [columns, setColumns] = useState(data?.columns ?? []);
  useEffect(() => {
    setColumns(data?.columns ?? []);
  }, [data]);

  // Filtered view (filters affect display only — we still reorder against the full set).
  const filteredColumns = useMemo(() => {
    return columns.map((c) => ({
      ...c,
      issues: c.issues.filter((i) => {
        // Epic filter
        if (epicFilter === 'none' && i.epic_id) return false;
        if (epicFilter !== 'all' && epicFilter !== 'none' && i.epic_id !== epicFilter) return false;
        // Sprint filter — board issues don't currently include sprint_id; we look it up.
        if (sprintFilter === 'all') return true;
        const wantSprintId =
          sprintFilter === 'active'
            ? (activeSprint?.id ?? null)
            : sprintFilter === 'none'
              ? null
              : sprintFilter;
        const issueSprintId = i.sprint_id ?? null;
        return wantSprintId === null ? issueSprintId === null : issueSprintId === wantSprintId;
      }),
    }));
  }, [columns, epicFilter, sprintFilter, activeSprint]);

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

  const filtersActive = epicFilter !== 'all' || sprintFilter !== 'all';

  return (
    <div className="h-screen flex flex-col">
      <div className="px-6 py-4 border-b border-border flex items-center justify-between gap-4 flex-wrap">
        <div>
          <p className="mono text-xs uppercase tracking-widest text-text-dim">// board</p>
          <h1 className="mono text-xl">{project.name}</h1>
        </div>

        <div className="flex items-center gap-2 flex-wrap">
          <select
            className="input mono text-xs py-1.5"
            value={epicFilter}
            onChange={(e) => setEpicFilter(e.target.value as EpicFilter)}
            title="filter by epic"
          >
            <option value="all">epic: all</option>
            <option value="none">epic: none</option>
            {(epics ?? []).map((ep) => (
              <option key={ep.id} value={ep.id}>
                epic: {ep.title}
              </option>
            ))}
          </select>
          <select
            className="input mono text-xs py-1.5"
            value={sprintFilter}
            onChange={(e) => setSprintFilter(e.target.value as SprintFilter)}
            title="filter by sprint"
          >
            <option value="all">sprint: all</option>
            <option value="active" disabled={!activeSprint}>
              sprint: active{activeSprint ? ` (${activeSprint.name})` : ' — none'}
            </option>
            <option value="none">sprint: none</option>
            {(sprints ?? []).map((s) => (
              <option key={s.id} value={s.id}>
                sprint: {s.name}
                {s.status !== 'planned' ? ` · ${s.status}` : ''}
              </option>
            ))}
          </select>
          {filtersActive && (
            <button
              onClick={() => {
                setEpicFilter('all');
                setSprintFilter('all');
              }}
              className="btn-ghost text-xs"
            >
              clear
            </button>
          )}
          <button onClick={() => setOpenCreate(true)} className="btn-primary">
            + new issue
          </button>
        </div>
      </div>

      <DndContext sensors={sensors} onDragStart={onDragStart} onDragEnd={onDragEnd}>
        <div className="flex-1 overflow-x-auto overflow-y-hidden">
          <div className="flex gap-px bg-border h-full p-px min-w-max">
            {filteredColumns.map((col) => (
              <SortableContext
                key={col.id}
                items={col.issues.map((i) => i.id)}
                strategy={verticalListSortingStrategy}
              >
                <KanbanColumn column={col} onIssueClick={(id) => setOpenIssueId(id)} />
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
          defaultEpicId={epicFilter !== 'all' && epicFilter !== 'none' ? epicFilter : null}
          defaultSprintId={
            sprintFilter === 'active'
              ? (activeSprint?.id ?? null)
              : sprintFilter !== 'all' && sprintFilter !== 'none'
                ? sprintFilter
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
