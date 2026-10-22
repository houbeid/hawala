import type { UseFormReturn } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { TextField } from '@/shared/ui/TextField';
import type { CreateForm, FieldErrors } from './createForm';

// Étape 3 (maquette 04) : expéditeur et bénéficiaire en texte libre (obligatoires, 200 caractères), note
// facultative (500), puis le récapitulatif fourni par la page avant « Créer le transfert ».

export function PeopleStep({ form, errors }: { form: UseFormReturn<CreateForm>; errors: FieldErrors }) {
  const { t } = useTranslation();
  const { register } = form;
  return (
    <div className="flex flex-col gap-3.5">
      <TextField
        label={t('transfers.create.sender')}
        autoComplete="off"
        maxLength={200}
        error={errors.senderInfo && t(errors.senderInfo)}
        {...register('senderInfo')}
      />
      <TextField
        label={t('transfers.create.beneficiary')}
        autoComplete="off"
        maxLength={200}
        error={errors.beneficiaryInfo && t(errors.beneficiaryInfo)}
        {...register('beneficiaryInfo')}
      />
      <TextField
        label={t('transfers.create.note')}
        autoComplete="off"
        maxLength={500}
        error={errors.note && t(errors.note)}
        {...register('note')}
      />
    </div>
  );
}
