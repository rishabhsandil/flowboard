import clsx from 'clsx';
import { useNavigate, useParams } from 'react-router-dom';
import { Layers } from 'lucide-react';
import type { BoardIssue } from '../types';
import { priorityBorder, priorityLabel } from '../lib/priority';
import { EpicPill } from './EpicPill';
import { Avatar } from './Avatar';

interface Props {
  issue: BoardIssue;
  onClick?: () => void;
  dragging?: boolean;
}

export function IssueCard({ issue, onClick, dragging }: Props) {
  const nav = useNavigate();
  const { slug } = useParams<{ slug: string }>();

  // A label chip on a board card jumps to the Issues page filtered by that
  // label — handy for "show me everything tagged 'blocked' across the
  // board". stopPropagation prevents the card's onClick from also firing.
  function jumpToLabel(e: React.MouseEvent, labelId: string) {
    e.stopPropagation();
    if (slug) nav(`/p/${slug}/issues?labelId=${labelId}`);
  }

  return (
    <div
      onClick={onClick}
      className={clsx(
        'group bg-bg-panel border border-border border-l-[3px] p-4 cursor-pointer rounded-sm shadow-sm relative overflow-hidden',
        'hover:border-border-strong hover:shadow-md transition-all duration-200',
        priorityBorder[issue.priority],
        dragging && 'shadow-2xl shadow-accent/30 rotate-[1deg] scale-[1.02] z-50 bg-bg-subtle',
      )}
    >
      <div className="flex justify-between items-start gap-3 mb-2.5">
        <p className="text-sm leading-snug font-medium line-clamp-3 group-hover:text-accent transition-colors">
          {issue.title}
        </p>
        <span className="mono text-[10px] text-text-dim/70 shrink-0 pt-0.5">#{issue.number}</span>
      </div>

      {issue.epic_title && (
        <div className="mb-3">
          <EpicPill title={issue.epic_title} color={issue.epic_color ?? '#22d3ee'} />
        </div>
      )}

      {issue.labels && issue.labels.length > 0 && (
        <div className="flex flex-wrap gap-1.5 mb-3">
          {issue.labels.map((l) => (
            <button
              key={l.id}
              type="button"
              onClick={(e) => jumpToLabel(e, l.id)}
              title={`Filter issues by ${l.name}`}
              className="mono text-[9px] uppercase tracking-wider px-2 py-0.5 rounded border border-transparent hover:border-current transition-all bg-bg-soft/50"
              style={{
                color: l.color,
                backgroundColor: `${l.color}15`,
              }}
            >
              {l.name}
            </button>
          ))}
        </div>
      )}

      <div className="flex items-center justify-between pt-2 border-t border-border/40 mono text-[9px] uppercase tracking-widest">
        <span className="text-text-dim/80 font-semibold">{priorityLabel[issue.priority]}</span>
        <div className="flex items-center gap-2">
          {issue.child_count > 0 && (
            <span
              className="flex items-center gap-1 px-1.5 py-0.5 bg-bg-soft text-accent rounded-sm border border-accent/20"
              title={`${issue.child_count} sub-issues`}
            >
              <Layers size={10} />
              {issue.child_count}
            </span>
          )}
          {issue.story_points > 0 && (
            <span className="px-1.5 py-0.5 bg-bg-soft text-text-muted rounded-sm border border-border/50">
              {issue.story_points} pt
            </span>
          )}
          <Avatar
            id={issue.assignee_id}
            name={issue.assignee_name}
            avatarUrl={issue.assignee_avatar_url}
            size="xs"
            title={issue.assignee_name ?? 'Unassigned'}
          />
        </div>
      </div>
    </div>
  );
}
