import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { useSessionManager } from '@/shared/auth/hooks';
import { listAudit } from './api';
import type { AuditParams } from './types';

export const auditKeys = {
  all: ['audit'] as const,
  list: (params: AuditParams) => ['audit', 'list', params] as const,
};

export function useAudit(params: AuditParams) {
  const session = useSessionManager();
  return useQuery({ queryKey: auditKeys.list(params), queryFn: () => listAudit(session.http, params), placeholderData: keepPreviousData });
}
