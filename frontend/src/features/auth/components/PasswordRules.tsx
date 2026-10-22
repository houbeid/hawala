import { useTranslation } from 'react-i18next';
import { Icon } from '@/shared/ui/Icon';
import { checkPassword } from '../passwordRules';

/** Règles du mot de passe cochées en direct : icône ET texte « respectée / non respectée ». */
export function PasswordRules({ password }: { password: string }) {
  const { t } = useTranslation();
  const check = checkPassword(password);
  const rules = [
    { key: 'minLength', ok: check.minLength },
    { key: 'hasDigit', ok: check.hasDigit },
  ] as const;

  return (
    <ul className="m-0 flex list-none flex-col gap-1 p-0" aria-label={t('auth.changePassword.rulesTitle')}>
      {rules.map((rule) => (
        <li key={rule.key} className="flex items-center gap-2 text-[15px]">
          <Icon name={rule.ok ? 'check' : 'x'} className={`size-5 ${rule.ok ? '' : 'text-muted'}`} />
          <span>
            {t(`auth.changePassword.rules.${rule.key}`)}
            <span className="sr-only"> — {t(rule.ok ? 'auth.changePassword.ruleMet' : 'auth.changePassword.ruleNotMet')}</span>
          </span>
        </li>
      ))}
    </ul>
  );
}
