import { useCallback, useEffect, useState } from 'react';
import { applyTheme, readThemePreference, watchSystemTheme, type ThemePreference } from './theme';

export function useTheme() {
  const [preference, setPreference] = useState<ThemePreference>(readThemePreference);

  useEffect(() => {
    applyTheme(preference);
    if (preference !== 'system') return;
    return watchSystemTheme(() => applyTheme('system'));
  }, [preference]);

  const choose = useCallback((next: ThemePreference) => setPreference(next), []);

  return { preference, choose };
}
