import { useEffect } from 'react';

/**
 * Calls `onEscape` whenever the user presses the Escape key while the
 * component is mounted. Used by all modals so users can close them with the
 * keyboard, not just by clicking the overlay.
 */
export function useEscapeKey(onEscape: () => void) {
  useEffect(() => {
    function handler(e: KeyboardEvent) {
      if (e.key === 'Escape') onEscape();
    }
    window.addEventListener('keydown', handler);
    return () => window.removeEventListener('keydown', handler);
  }, [onEscape]);
}
