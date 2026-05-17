import { type RefObject, useState, useRef, useEffect } from 'react';
import {
  Bold,
  Italic,
  Strikethrough,
  Heading1,
  Heading2,
  Heading3,
  List,
  ListOrdered,
  ListChecks,
  Quote,
  Code,
  SquareCode,
  Link as LinkIcon,
  Image as ImageIcon,
  Table as TableIcon,
  Type,
  Eye,
  EyeOff,
} from 'lucide-react';
import clsx from 'clsx';

// ---------------------------------------------------------------------------
// Action definitions
// ---------------------------------------------------------------------------

interface WrapAction {
  kind: 'wrap';
  icon: React.ElementType;
  title: string;
  before: string;
  after: string;
  /** Inserted (and selected) when nothing is selected in the textarea. */
  placeholder: string;
}

interface LineAction {
  kind: 'line';
  icon: React.ElementType;
  title: string;
  prefix: string;
}

interface SpecialAction {
  kind: 'special';
  icon: React.ElementType;
  title: string;
  type: 'table' | 'preview';
}

interface Sep {
  kind: 'sep';
}

type ToolbarAction = WrapAction | LineAction | SpecialAction | Sep;

const ACTIONS: ToolbarAction[] = [
  { kind: 'wrap', icon: Bold, title: 'Bold', before: '**', after: '**', placeholder: 'bold text' },
  {
    kind: 'wrap',
    icon: Italic,
    title: 'Italic',
    before: '*',
    after: '*',
    placeholder: 'italic text',
  },
  {
    kind: 'wrap',
    icon: Strikethrough,
    title: 'Strikethrough',
    before: '~~',
    after: '~~',
    placeholder: 'strikethrough',
  },
  { kind: 'sep' },
  { kind: 'line', icon: Heading1, title: 'Heading 1', prefix: '# ' },
  { kind: 'line', icon: Heading2, title: 'Heading 2', prefix: '## ' },
  { kind: 'line', icon: Heading3, title: 'Heading 3', prefix: '### ' },
  { kind: 'sep' },
  { kind: 'line', icon: List, title: 'Bullet list', prefix: '- ' },
  { kind: 'line', icon: ListOrdered, title: 'Numbered list', prefix: '1. ' },
  { kind: 'line', icon: ListChecks, title: 'Task list', prefix: '- [ ] ' },
  { kind: 'sep' },
  { kind: 'line', icon: Quote, title: 'Blockquote', prefix: '> ' },
  { kind: 'wrap', icon: Code, title: 'Inline code', before: '`', after: '`', placeholder: 'code' },
  {
    kind: 'wrap',
    icon: SquareCode,
    title: 'Code block',
    before: '```\n',
    after: '\n```',
    placeholder: 'code',
  },
  { kind: 'sep' },
  {
    kind: 'wrap',
    icon: LinkIcon,
    title: 'Link',
    before: '[',
    after: '](url)',
    placeholder: 'link text',
  },
  {
    kind: 'wrap',
    icon: ImageIcon,
    title: 'Image',
    before: '![',
    after: '](url)',
    placeholder: 'alt text',
  },
  {
    kind: 'special',
    icon: TableIcon,
    title: 'Insert Table',
    type: 'table',
  },
];

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

interface Props {
  textareaRef: RefObject<HTMLTextAreaElement | null>;
  value: string;
  onChange: (next: string) => void;
  className?: string;
  /** When provided, shows a preview toggle. */
  onTogglePreview?: (show: boolean) => void;
  isPreviewing?: boolean;
}

export function MarkdownToolbar({
  textareaRef,
  value,
  onChange,
  className,
  onTogglePreview,
  isPreviewing,
}: Props) {
  const [showTablePicker, setShowTablePicker] = useState(false);
  const [hoverGrid, setHoverGrid] = useState({ r: 2, c: 2 });
  const pickerRef = useRef<HTMLDivElement>(null);

  // Close picker on outside click
  useEffect(() => {
    function handler(e: MouseEvent) {
      if (pickerRef.current && !pickerRef.current.contains(e.target as Node)) {
        setShowTablePicker(false);
      }
    }
    document.addEventListener('mousedown', handler);
    return () => document.removeEventListener('mousedown', handler);
  }, []);

  function applyTable(rows: number, cols: number) {
    const ta = textareaRef.current;
    if (!ta) return;

    const start = ta.selectionStart;
    const end = ta.selectionEnd;
    const beforeCursor = value.slice(0, start);
    const needsNewLine = start !== 0 && !beforeCursor.endsWith('\n');
    const prefix = needsNewLine ? '\n' : '';

    // Header row
    let table = `${prefix}|`;
    for (let c = 0; c < cols; c++) table += ` Header ${c + 1} |`;

    // Separator row
    table += '\n|';
    for (let c = 0; c < cols; c++) table += ` ------ |`;

    // Data rows (rows - 1 because first row is header)
    const dataRows = Math.max(1, rows - 1);
    for (let r = 0; r < dataRows; r++) {
      table += '\n|';
      for (let c = 0; c < cols; c++) table += ` --- |`;
    }
    table += '\n';

    const newValue = value.slice(0, start) + table + value.slice(end);
    onChange(newValue);
    setShowTablePicker(false);

    // Focus the first cell of the first data row
    requestAnimationFrame(() => {
      ta.focus();
      const firstRowEnd = table.indexOf('\n');
      const secondRowEnd = table.indexOf('\n', firstRowEnd + 1);
      const firstCellStart = start + secondRowEnd + 3; // \n|
      // Select the "---" in the first cell so user can type over it
      ta.setSelectionRange(firstCellStart, firstCellStart + 3);
    });
  }

  function apply(action: ToolbarAction) {
    if (action.kind === 'sep') return;
    if (action.kind === 'special') {
      if (action.type === 'table') {
        setShowTablePicker(!showTablePicker);
        return;
      }
      if (action.type === 'preview') {
        onTogglePreview?.(!isPreviewing);
        return;
      }
      return;
    }

    const ta = textareaRef.current;
    if (!ta) return;

    const start = ta.selectionStart;
    const end = ta.selectionEnd;
    const selected = value.slice(start, end);

    let newValue: string;
    let nextStart: number;
    let nextEnd: number;

    if (action.kind === 'wrap') {
      const isWrapped = selected.startsWith(action.before) && selected.endsWith(action.after);
      if (isWrapped && selected.length >= action.before.length + action.after.length) {
        const inner = selected.slice(action.before.length, -action.after.length);
        newValue = value.slice(0, start) + inner + value.slice(end);
        nextStart = start;
        nextEnd = start + inner.length;
      } else {
        const inner = selected || action.placeholder;
        const replacement = action.before + inner + action.after;
        newValue = value.slice(0, start) + replacement + value.slice(end);
        nextStart = start + action.before.length;
        nextEnd = nextStart + inner.length;
      }
    } else {
      const beforeCursor = value.slice(0, start);
      const lineStart = beforeCursor.lastIndexOf('\n') + 1;
      const chunk = value.slice(lineStart, end);
      const lines = chunk.split('\n');
      const allHave = lines.every((l) => l.startsWith(action.prefix));
      const newLines = allHave
        ? lines.map((l) => l.slice(action.prefix.length))
        : lines.map((l) => action.prefix + l);
      const newChunk = newLines.join('\n');
      newValue = value.slice(0, lineStart) + newChunk + value.slice(end);
      const delta = newChunk.length - chunk.length;
      nextStart = Math.max(
        lineStart,
        start + (allHave ? -action.prefix.length : action.prefix.length),
      );
      nextEnd = end + delta;
    }

    onChange(newValue);
    requestAnimationFrame(() => {
      ta.focus();
      ta.setSelectionRange(nextStart, nextEnd);
    });
  }

  return (
    <div
      className={clsx(
        'flex items-center flex-wrap gap-0.5 bg-bg-subtle/50 px-1 py-1 backdrop-blur-sm border-b border-border min-w-0 rounded-t',
        className,
      )}
    >
      <div className="flex items-center flex-shrink-0 mr-1 ml-1">
        <Type size={12} className="text-text-dim/40" />
      </div>

      <div className="flex items-center flex-wrap gap-0.5 min-w-0">
        {ACTIONS.map((action, i) => {
          if (action.kind === 'sep') {
            return <div key={i} className="mx-0.5 h-4 w-px bg-border/60 flex-shrink-0" />;
          }

          const Icon = action.icon;
          const isTable = action.kind === 'special' && action.type === 'table';

          return (
            <div key={i} className="relative">
              <button
                type="button"
                title={action.title}
                onMouseDown={(e) => {
                  e.preventDefault();
                  if (isTable) e.stopPropagation();
                  apply(action);
                }}
                className={clsx(
                  'p-1.5 rounded hover:bg-bg-panel hover:text-accent transition-all group relative flex-shrink-0',
                  'text-text-dim',
                  isTable && showTablePicker && 'bg-bg-panel text-accent',
                )}
              >
                <Icon size={14} strokeWidth={2.2} />
              </button>

              {isTable && showTablePicker && (
                <div
                  ref={pickerRef}
                  onMouseDown={(e) => e.stopPropagation()}
                  className="absolute top-full left-0 mt-1 z-[100] bg-bg-panel border border-border shadow-2xl p-3 rounded-lg min-w-[160px]"
                >
                  <p className="mono text-[10px] uppercase tracking-widest text-text-dim mb-3 px-1 flex justify-between items-center">
                    <span>Table Grid</span>
                    <span className="text-accent font-bold text-xs">
                      {hoverGrid.r} × {hoverGrid.c}
                    </span>
                  </p>
                  <div className="grid grid-cols-6 gap-1.5">
                    {Array.from({ length: 36 }).map((_, idx) => {
                      const r = Math.floor(idx / 6) + 1;
                      const c = (idx % 6) + 1;
                      const active = r <= hoverGrid.r && c <= hoverGrid.c;
                      return (
                        <div
                          key={idx}
                          onMouseEnter={() => setHoverGrid({ r, c })}
                          onMouseDown={(e) => {
                            e.preventDefault();
                            e.stopPropagation();
                            applyTable(r, c);
                          }}
                          className={clsx(
                            'w-5 h-5 rounded-sm border cursor-pointer transition-all duration-75',
                            active
                              ? 'bg-accent border-accent scale-110 z-10'
                              : 'bg-bg/50 border-border hover:border-text-dim hover:scale-105',
                          )}
                        />
                      );
                    })}
                  </div>
                  <p className="mt-3 mono text-[9px] text-text-dim/60 text-center uppercase tracking-tighter">
                    Select rows × columns
                  </p>
                </div>
              )}
            </div>
          );
        })}

        {onTogglePreview && (
          <>
            <div className="mx-0.5 h-4 w-px bg-border/60 flex-shrink-0" />
            <button
              type="button"
              title={isPreviewing ? 'Show Editor' : 'Show Preview'}
              onClick={() => onTogglePreview(!isPreviewing)}
              className={clsx(
                'p-1.5 rounded hover:bg-bg-panel hover:text-accent transition-all flex-shrink-0',
                isPreviewing ? 'text-accent bg-bg-panel/50' : 'text-text-dim',
              )}
            >
              {isPreviewing ? (
                <EyeOff size={14} strokeWidth={2.2} />
              ) : (
                <Eye size={14} strokeWidth={2.2} />
              )}
            </button>
          </>
        )}
      </div>
    </div>
  );
}
