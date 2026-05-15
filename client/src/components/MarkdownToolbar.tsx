import type { RefObject } from 'react';

// ---------------------------------------------------------------------------
// Action definitions
// ---------------------------------------------------------------------------

interface WrapAction {
  kind: 'wrap';
  label: string;
  title: string;
  before: string;
  after: string;
  /** Inserted (and selected) when nothing is selected in the textarea. */
  placeholder: string;
}

interface LineAction {
  kind: 'line';
  label: string;
  title: string;
  prefix: string;
}

interface Sep {
  kind: 'sep';
}

type ToolbarAction = WrapAction | LineAction | Sep;

const ACTIONS: ToolbarAction[] = [
  { kind: 'wrap', label: 'B', title: 'Bold', before: '**', after: '**', placeholder: 'bold text' },
  {
    kind: 'wrap',
    label: 'I',
    title: 'Italic',
    before: '*',
    after: '*',
    placeholder: 'italic text',
  },
  {
    kind: 'wrap',
    label: '~~',
    title: 'Strikethrough',
    before: '~~',
    after: '~~',
    placeholder: 'strikethrough',
  },
  { kind: 'sep' },
  { kind: 'line', label: '##', title: 'Heading 2', prefix: '## ' },
  { kind: 'line', label: '###', title: 'Heading 3', prefix: '### ' },
  { kind: 'sep' },
  {
    kind: 'wrap',
    label: '`·`',
    title: 'Inline code',
    before: '`',
    after: '`',
    placeholder: 'code',
  },
  {
    kind: 'wrap',
    label: '```',
    title: 'Code block',
    before: '```\n',
    after: '\n```',
    placeholder: 'code',
  },
  { kind: 'sep' },
  {
    kind: 'wrap',
    label: '[]',
    title: 'Link',
    before: '[',
    after: '](url)',
    placeholder: 'link text',
  },
  { kind: 'sep' },
  { kind: 'line', label: '—', title: 'Bullet list', prefix: '- ' },
  { kind: 'line', label: '1.', title: 'Numbered list', prefix: '1. ' },
  { kind: 'line', label: '>', title: 'Blockquote', prefix: '> ' },
];

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

interface Props {
  textareaRef: RefObject<HTMLTextAreaElement | null>;
  value: string;
  onChange: (next: string) => void;
}

export function MarkdownToolbar({ textareaRef, value, onChange }: Props) {
  function apply(action: ToolbarAction) {
    if (action.kind === 'sep') return;

    const ta = textareaRef.current;
    if (!ta) return;

    const start = ta.selectionStart;
    const end = ta.selectionEnd;
    const selected = value.slice(start, end);

    let newValue: string;
    let nextStart: number;
    let nextEnd: number;

    if (action.kind === 'wrap') {
      const inner = selected || action.placeholder;
      const replacement = action.before + inner + action.after;
      newValue = value.slice(0, start) + replacement + value.slice(end);
      // Select the inner text so user can immediately type over the placeholder
      nextStart = start + action.before.length;
      nextEnd = nextStart + inner.length;
    } else {
      // Line-prefix action: operate on the full lines covered by the selection.
      const beforeCursor = value.slice(0, start);
      const lineStart = beforeCursor.lastIndexOf('\n') + 1;
      const chunk = value.slice(lineStart, end);
      const lines = chunk.split('\n');

      // Toggle: if every line already starts with the prefix, remove it.
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

    // Restore focus + cursor after React flushes the new value.
    requestAnimationFrame(() => {
      ta.focus();
      ta.setSelectionRange(nextStart, nextEnd);
    });
  }

  return (
    <div className="flex items-center flex-wrap gap-0.5 border border-border border-b-0 bg-bg-subtle px-1.5 py-1">
      {ACTIONS.map((action, i) => {
        if (action.kind === 'sep') {
          return <span key={i} className="mx-0.5 h-3 w-px flex-shrink-0 bg-border" />;
        }

        const isB = action.label === 'B';
        const isI = action.label === 'I';

        return (
          <button
            key={i}
            type="button"
            title={action.title}
            // onMouseDown prevents the textarea from losing focus before we
            // read selectionStart / selectionEnd.
            onMouseDown={(e) => {
              e.preventDefault();
              apply(action);
            }}
            className={`mono rounded-sm px-1.5 py-0.5 text-[10px] text-text-dim transition-colors hover:bg-bg hover:text-text ${
              isB ? 'font-bold' : isI ? 'italic' : ''
            }`}
          >
            {action.label}
          </button>
        );
      })}
    </div>
  );
}
