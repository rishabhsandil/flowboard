// Shared frontend types — mirror the API DTOs
export type Priority = 'low' | 'medium' | 'high' | 'critical';

export interface User {
  id: string;
  email: string;
  name: string;
  avatarUrl?: string | null;
  createdAt: string;
}

export interface Project {
  id: string;
  name: string;
  slug: string;
  description?: string | null;
  ownerId?: string | null;
  createdAt: string;
  role?: 'owner' | 'member';
}

export interface BoardIssue {
  id: string;
  title: string;
  priority: Priority;
  story_points: number;
  position: number;
  assignee_id: string | null;
  epic_id: string | null;
  sprint_id: string | null;
  epic_color: string | null;
  epic_title: string | null;
  labels: { id: string; name: string; color: string }[];
}

// Row shape returned by GET /sprints/:id/issues. Mirrors SprintIssueRow
// in src/FlowBoard.Core/Models/Domain.cs.
export interface SprintIssue {
  id: string;
  title: string;
  priority: Priority;
  story_points: number;
  column_name: string | null;
  closed_at: string | null;
  epic_color: string | null;
  epic_title: string | null;
}

export interface BoardColumn {
  id: string;
  name: string;
  position: number;
  isDone: boolean;
  issues: BoardIssue[];
}

export interface Board {
  boardId: string;
  columns: BoardColumn[];
}

export interface Issue {
  id: string;
  projectId: string;
  columnId: string | null;
  epicId: string | null;
  sprintId: string | null;
  assigneeId: string | null;
  parentId: string | null;
  title: string;
  description: string | null;
  priority: Priority;
  storyPoints: number;
  position: number;
  createdAt: string;
  updatedAt: string;
  closedAt: string | null;
}

export interface BurndownPoint {
  day: string;
  remaining: number;
}

export interface EpicWithProgress {
  id: string;
  title: string;
  color: string;
  dueDate: string | null;
  startDate: string | null;
  description: string | null;
  totalIssues: number;
  closedIssues: number;
  percentComplete: number | null;
  totalPoints: number;
  completedPoints: number;
}

export interface Sprint {
  id: string;
  projectId: string;
  name: string;
  goal: string | null;
  startDate: string;
  endDate: string;
  status: 'planned' | 'active' | 'completed';
  createdAt: string;
}

export interface VelocityRow {
  id: string;
  name: string;
  startDate: string;
  endDate: string;
  pointsCompleted: number;
  cumulativePoints: number;
}

// ---------- Comments ----------
export interface Comment {
  id: string;
  issueId: string;
  authorId: string | null;
  authorName: string | null;
  authorAvatarUrl: string | null;
  body: string;
  createdAt: string;
  updatedAt: string;
  edited: boolean;
}

// ---------- Labels ----------
export interface Label {
  id: string;
  projectId: string;
  name: string;
  color: string;
  createdAt: string;
}

// ---------- Activity feed ----------
// `payload` is a JSON string from the API — parse before reading fields.
export interface ActivityRow {
  id: string;
  projectId: string;
  issueId: string | null;
  actorId: string | null;
  actorName: string | null;
  actorAvatarUrl: string | null;
  type: string;
  payload: string;
  createdAt: string;
}

// ---------- Issue list (project-wide) ----------
// Mirrors IssueListRow in src/FlowBoard.Core/Models/Domain.cs.
// `labelsJson` is a JSON string from the API; parse with JSON.parse() before
// rendering the chips (matches how BoardIssue.labels would look post-parse).
export interface IssueListRow {
  id: string;
  projectId: string;
  columnId: string | null;
  epicId: string | null;
  sprintId: string | null;
  assigneeId: string | null;
  title: string;
  description: string | null;
  priority: Priority;
  storyPoints: number;
  position: number;
  createdAt: string;
  updatedAt: string;
  closedAt: string | null;
  columnName: string | null;
  epicTitle: string | null;
  epicColor: string | null;
  sprintName: string | null;
  assigneeName: string | null;
  labelsJson: string;
}

export interface ProjectMember {
  id: string;
  email: string;
  name: string;
  avatarUrl: string | null;
  createdAt: string;
  role: 'owner' | 'member';
}
