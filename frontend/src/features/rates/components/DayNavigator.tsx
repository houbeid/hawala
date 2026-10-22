import { useTranslation } from 'react-i18next';
import { addDays, daysBetween, formatDayLong } from '@/shared/format/dates';
import { Icon } from '@/shared/ui/Icon';

// Navigation jour par jour (maquette 07) : flèches de 44 px, la date en clair avec « aujourd'hui »,
// « hier » ou « demain ». Les flèches suivent le sens de lecture (inversées en arabe).

export function DayNavigator({ day, today, onChange }: { day: string; today: string; onChange: (day: string) => void }) {
  const { t, i18n } = useTranslation();
  const offset = daysBetween(today, day);
  const relative = offset === 0 ? t('rates.today') : offset === -1 ? t('rates.yesterday') : offset === 1 ? t('rates.tomorrow') : null;

  return (
    <div className="flex items-center gap-2">
      <button
        type="button"
        onClick={() => onChange(addDays(day, -1))}
        aria-label={t('rates.previousDay')}
        className="inline-flex size-11 shrink-0 items-center justify-center rounded-xl border-[1.5px] border-border bg-surface"
      >
        <Icon name="chevronBack" />
      </button>
      <div
        className="flex min-h-11 flex-1 items-center justify-center gap-1.5 rounded-full border-[1.5px] border-border bg-surface px-3 text-[15px] font-semibold"
        aria-live="polite"
      >
        <span>{formatDayLong(day, i18n.language)}</span>
        {relative && <span className="text-muted">· {relative}</span>}
      </div>
      <button
        type="button"
        onClick={() => onChange(addDays(day, 1))}
        aria-label={t('rates.nextDay')}
        className="inline-flex size-11 shrink-0 items-center justify-center rounded-xl border-[1.5px] border-border bg-surface"
      >
        <Icon name="chevron" />
      </button>
      {offset !== 0 && (
        <button type="button" onClick={() => onChange(today)} className="min-h-11 shrink-0 rounded-full px-3 font-semibold underline">
          {t('rates.backToToday')}
        </button>
      )}
    </div>
  );
}
