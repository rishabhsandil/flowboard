import clsx from 'clsx';
import { useNavigate, useParams } from 'react-router-dom';
import type { BoardIssue } from '../types';
import { priorityBorder, priorityLabel } from '../lib/priority';
import { EpicPill } from './EpicPill';

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
        'bg-bg-panel border border-border border-l-[3px] p-3 cursor-pointer',
        'hover:border-border-strong transition-colors',
        priorityBorder[issue.priority],
        dragging && 'shadow-2xl shadow-accent/20 rotate-1',
      )}
    >
      <p className="text-sm leading-snug mb-2 line-clamp-3">{issue.title}</p>

      {issue.epic_title && (
        <div className="mb-2">
          <EpicPill title={issue.epic_title} color={issue.epic_color ?? '#22d3ee'} />
        </div>
      )}

      {issue.labels && issue.labels.length > 0 && (
        <div className="flex flex-wrap gap-1 mb-2">
          {issue.labels.map((l) => (
            <button
              key={l.id}
              type="button"
              onClick={(e) => jumpToLabel(e, l.id)}
              title={`filter issues by ${l.name}`}
              className="mono text-[9px] uppercase tracking-wider px-1.5 py-0.5 rounded-full border hover:opacity-80 transition-opacity"
              style={{
                borderColor: l.color,
                color: l.color,
                background: `${l.color}1a`,
              }}
            >
              {l.name}
            </button>
          ))}
        </div>
      )}

      <div className="flex items-center justify-between mono text-[10px] uppercase tracking-wider">
        <span className="text-text-dim">{priorityLabel[issue.priority]}</span>
        {issue.story_points > 0 && (
          <span className="px-1.5 py-0.5 border border-border text-text-muted">
            {issue.story_points}pt
          </span>
        )}
      </div>
    </div>
  );
}
