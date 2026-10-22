import { z } from 'zod';
import { passwordIsValid } from './passwordRules';

// Contrôles de saisie seulement (champ vide, confirmation) ; les messages sont des clés de traduction.
// Les règles qui demandent le serveur (identifiants, compte bloqué…) reviennent par le code d'erreur.

export const loginSchema = z.object({
  login: z.string().trim().min(1, 'auth.validation.loginRequired'),
  password: z.string().min(1, 'auth.validation.passwordRequired'),
});

export type LoginForm = z.infer<typeof loginSchema>;

export const changePasswordSchema = z
  .object({
    currentPassword: z.string().min(1, 'auth.validation.currentPasswordRequired'),
    newPassword: z.string().refine(passwordIsValid, 'auth.validation.passwordRules'),
    confirmPassword: z.string(),
  })
  .refine((form) => form.confirmPassword === form.newPassword, {
    path: ['confirmPassword'],
    message: 'auth.validation.confirmMismatch',
  });

export type ChangePasswordForm = z.infer<typeof changePasswordSchema>;
