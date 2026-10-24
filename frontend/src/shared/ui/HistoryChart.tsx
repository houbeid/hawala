import { useEffect, useRef, useState, type RefObject } from 'react';
import { useTranslation } from 'react-i18next';
import { CartesianGrid, Line, LineChart, ReferenceLine, Tooltip, XAxis, YAxis, } from 'recharts';
import { formatDayShort } from '../format/dates';
import { canDrawHistory, historyDomain, showsZero, type HistoryPoint } from './historySeries';

// Courbe d'un historique enregistré (maquettes 10 et 12) : une seule série, une seule devise, donc un seul axe.
// Tracée à l'encre du thème (comme la maquette) : la couleur d'une agence est choisie par le Superviseur et son
// contraste n'est pas garanti ; l'agence est nommée dans le titre. Pas d'axe vertical chiffré : la dernière valeur
// est écrite au bout de la courbe et le survol donne chaque jour ; la liste sous la courbe reste la vue en texte
// (lecteurs d'écran, impression). Le graphique se lit toujours de gauche à droite, même en arabe.

const HEIGHT = 160;

interface Props {
  /** Titre court sous la courbe (« Budget enregistré · MRO »). */
  title: string;
  points: HistoryPoint[];
  /** Montant complet, avec son unité. */
  formatValue: (value: number) => string;
}

export function HistoryChart({ title, points, formatValue }: Props) {
  const { t } = useTranslation();
  const drawable = canDrawHistory(points);
  const [box, width] = useWidth<HTMLDivElement>(drawable);
  if (!drawable) return null;

  const first = points[0]!;
  const last = points[points.length - 1]!;
  const summary = t('chart.summary', {
    title,
    from: formatDayShort(first.day),
    to: formatDayShort(last.day),
    first: formatValue(first.value),
    last: formatValue(last.value),
  });

  return (
    <figure className="m-0 flex flex-col gap-1 rounded-2xl border border-border bg-surface px-2.5 pt-3 pb-2 text-fg">
      <div ref={box} role="img" aria-label={summary} dir="ltr" style={{ height: HEIGHT }}>
        {width > 0 && (
          <LineChart width={width} height={HEIGHT} data={points} margin={{ top: 22, right: 12, bottom: 0, left: 12 }} accessibilityLayer={false}>
            <CartesianGrid vertical={false} stroke="var(--border)" />
            <XAxis
              dataKey="day"
              tickFormatter={formatDayShort}
              ticks={[first.day, last.day]}
              interval={0}
              tickLine={false}
              axisLine={false}
              tick={{ fill: 'var(--muted)', fontSize: 11 }}
              padding={{ left: 4, right: 4 }}
            />
            <YAxis hide domain={historyDomain(points)} />
            {showsZero(points) && <ReferenceLine y={0} stroke="var(--muted)" strokeDasharray="4 4" />}
            <Tooltip
              content={({ active, payload }) => <ChartTooltip active={active} point={payload?.[0]?.payload as HistoryPoint | undefined} formatValue={formatValue} />}
              cursor={{ stroke: 'var(--muted)', strokeWidth: 1 }}
              isAnimationActive={false}
            />
            <Line
              type="linear"
              dataKey="value"
              stroke="currentColor"
              strokeWidth={2}
              strokeLinejoin="round"
              strokeLinecap="round"
              dot={false}
              activeDot={{ r: 5, fill: 'currentColor', stroke: 'var(--surface)', strokeWidth: 2 }}
              label={<LastValueLabel lastIndex={points.length - 1} text={formatValue(last.value)} />}
              isAnimationActive={false}
            />
          </LineChart>
        )}
      </div>
      <figcaption className="px-1.5 text-sm text-muted">{title}</figcaption>
    </figure>
  );
}

function ChartTooltip({ active, point, formatValue }: { active?: boolean; point?: HistoryPoint; formatValue: (value: number) => string }) {
  if (!active || !point) return null;
  return (
    <div className="rounded-lg border border-border bg-surface px-2.5 py-1.5 text-sm shadow-sm">
      <div className="text-muted tabular-nums">{formatDayShort(point.day)}</div>
      <div className="amount font-semibold">{formatValue(point.value)}</div>
    </div>
  );
}

/** Point plein et valeur écrite au bout de la courbe (dernier jour enregistré). */
function LastValueLabel({ x, y, index, lastIndex, text }: { x?: number; y?: number; index?: number; lastIndex: number; text: string }) {
  if (index !== lastIndex || x === undefined || y === undefined) return null;
  return (
    <g>
      <circle cx={x} cy={y} r={4.5} fill="currentColor" stroke="var(--surface)" strokeWidth={2} />
      <text x={x} y={y - 10} textAnchor="end" fontSize={11} fontWeight={700} fill="currentColor">
        {text}
      </text>
    </g>
  );
}

/** Largeur disponible, suivie au redimensionnement (0 tant qu'elle est inconnue : rien n'est tracé). */
function useWidth<T extends HTMLElement>(enabled: boolean): [RefObject<T | null>, number] {
  const ref = useRef<T>(null);
  const [width, setWidth] = useState(0);
  useEffect(() => {
    const element = ref.current;
    if (!element || typeof ResizeObserver === 'undefined') return;
    const observer = new ResizeObserver(([entry]) => setWidth(Math.floor(entry?.contentRect.width ?? 0)));
    observer.observe(element);
    return () => observer.disconnect();
  }, [enabled]);
  return [ref, width];
}
