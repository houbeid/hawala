import type { AppUpdates } from '@/shared/pwa/appUpdates';

/** Faux service worker : `announce()` simule une nouvelle version prête ; `applied()` dit si elle a été activée. */
export function createFakeUpdates() {
  let notify: (() => void) | null = null;
  let applied = 0;
  const updates: AppUpdates = {
    start: (onNeedRefresh) => {
      notify = onNeedRefresh;
    },
    apply: async () => {
      applied += 1;
    },
  };
  return { updates, announce: () => notify?.(), applied: () => applied };
}
