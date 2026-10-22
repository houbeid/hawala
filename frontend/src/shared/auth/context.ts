import { createContext } from 'react';
import type { SessionManager } from './session';

export const SessionContext = createContext<SessionManager | null>(null);
