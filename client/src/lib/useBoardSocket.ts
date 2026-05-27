import { useEffect } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import {
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
  type HubConnection,
} from '@microsoft/signalr';
import { useAuthStore } from './auth';

/**
 * Resolves the hub URL by stripping the `/api` suffix off `VITE_API_URL`. The
 * REST base is `http://host/api`; the SignalR hub lives at `http://host/hubs/board`.
 */
function hubBaseUrl(): string {
  const apiBase = import.meta.env.VITE_API_URL ?? 'http://localhost:8080/api';
  return apiBase.replace(/\/api\/?$/, '');
}

interface BoardEvent {
  type: string;
  projectId: string;
  payload: {
    issueId?: string | null;
    actorId?: string | null;
    payload?: Record<string, unknown>;
  };
}

/**
 * Mounts a SignalR connection to `/hubs/board?projectId=...` and maps every
 * incoming event onto the right TanStack Query invalidations. Lifecycle is
 * keyed by `projectId` — the connection is rebuilt when the user switches
 * projects so the server-side group is correct.
 *
 * Granularity: events carry a typed payload (see backend `IBoardEventPublisher`)
 * and the dispatcher in `cacheEffectsFor` decides which keys to invalidate.
 * Reconnect blanket-invalidates every project-scoped query as a safety net
 * for any events missed during the disconnect window.
 *
 * Auth: the token is supplied via `accessTokenFactory` so SignalR auto-reads
 * from the live Zustand store on each (re)connect — handles token rotation
 * without forcing the hook consumer to track expiry.
 */
export function useBoardSocket(projectId: string | undefined) {
  const qc = useQueryClient();

  useEffect(() => {
    if (!projectId) return;

    const url = `${hubBaseUrl()}/hubs/board?projectId=${projectId}`;
    const connection: HubConnection = new HubConnectionBuilder()
      .withUrl(url, {
        accessTokenFactory: () => useAuthStore.getState().accessToken ?? '',
      })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    // After a reconnect we may have missed events while the socket was down;
    // blanket-invalidate the project's caches so every mounted view re-syncs.
    connection.onreconnected(() => {
      invalidateProjectCaches(qc, projectId);
    });

    connection.on('event', (evt: BoardEvent) => {
      if (!evt || typeof evt !== 'object') return;
      const effects = cacheEffectsFor(evt.type, evt.payload);
      for (const key of effects) {
        qc.invalidateQueries({ queryKey: key });
      }
    });

    connection.start().catch((err) => {
      // Silent: a failed connection just means no live updates. The user
      // can still interact with the app; manual refresh works.
      console.warn('[useBoardSocket] start failed', err);
    });

    return () => {
      if (
        connection.state === HubConnectionState.Connected ||
        connection.state === HubConnectionState.Connecting ||
        connection.state === HubConnectionState.Reconnecting
      ) {
        connection.stop().catch(() => {
          // Swallow stop errors on teardown — the page is unmounting anyway.
        });
      }
    };
  }, [projectId, qc]);
}

/**
 * Maps a SignalR event type to the TanStack Query keys that should be
 * invalidated. Keep this list aligned with the activity types emitted by
 * `ActivityLogger.LogAsync` on the backend — they're the canonical source.
 *
 * For events with rich payloads (e.g. `issue_updated` carries `issueId`) we
 * also invalidate the per-issue keys so an open IssueModal refreshes in
 * place; absent or malformed payloads degrade gracefully into the project-
 * wide invalidations.
 */
function cacheEffectsFor(type: string, payload: BoardEvent['payload']): (readonly unknown[])[] {
  const projectId = payload && 'projectId' in payload ? undefined : undefined; // not needed; we know pid from closure in caller
  void projectId;
  const issueId = payload?.issueId ?? undefined;
  const projectKeys: (readonly unknown[])[] = [];

  // Helper to add per-issue keys when an issueId is present.
  const issueKeys: (readonly unknown[])[] = issueId
    ? [
        ['issue', issueId],
        ['issue-children', issueId],
        ['comments', issueId],
        ['issue-activity', issueId],
        ['issue-labels', issueId],
        ['issue-deps', issueId],
      ]
    : [];

  switch (type) {
    case 'issue_created':
    case 'issue_updated':
    case 'issue_deleted':
    case 'issue_closed':
    case 'issue_reopened':
    case 'issue_moved':
      return [
        ['board'],
        ['issues-list'],
        ['sprint-issues'],
        ['epics'],
        ['project-activity'],
        ...issueKeys,
      ];

    case 'column_created':
    case 'column_updated':
    case 'column_deleted':
    case 'columns_reordered':
      return [['board'], ['project-activity']];

    case 'comment_added':
    case 'comment_edited':
    case 'comment_deleted':
    case 'mention':
      return [['comments', issueId], ['issue-activity', issueId], ['project-activity']];

    case 'label_added':
    case 'label_removed':
      return [['board'], ['issues-list'], ['issue-labels', issueId], ['project-activity']];

    case 'dependency_added':
    case 'dependency_removed':
      return [['issue-deps', issueId], ['project-activity']];

    case 'member_added':
    case 'member_role_changed':
    case 'member_removed':
    case 'member_left':
      return [['members'], ['project-activity']];

    default:
      // Unknown event types fall back to a project-wide refresh so a new
      // activity type added on the backend never goes unnoticed by the client.
      return [['board'], ['project-activity'], ...projectKeys];
  }
}

/**
 * Blanket invalidation used on reconnect. Covers every query whose first key
 * segment carries the project id directly OR whose data is project-scoped
 * even when the key doesn't include the id (e.g. `['palette-projects']` is
 * intentionally excluded — that's a cross-project list).
 */
function invalidateProjectCaches(qc: ReturnType<typeof useQueryClient>, projectId: string) {
  const keys: (readonly unknown[])[] = [
    ['board', projectId],
    ['issues-list', projectId],
    ['sprint-issues'],
    ['epics', projectId],
    ['sprints', projectId],
    ['members', projectId],
    ['labels', projectId],
    ['project-activity', projectId],
  ];
  for (const k of keys) qc.invalidateQueries({ queryKey: k });
}
