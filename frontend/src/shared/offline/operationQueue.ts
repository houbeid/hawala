import { isApiError } from '../api/errors';
import type { HttpClient } from '../api/http';
import type { KeyValueStore } from './offlineStore';

// File des créations saisies sans réseau (décisions du 24 octobre 2026) : transfert, encaissement, décaissement.
// - chaque opération garde sa clé d'idempotence (son identifiant) et l'heure de sa saisie ; un renvoi ne crée jamais
//   deux fois la même opération ;
// - la file appartient au compte qui a saisi et n'est JAMAIS effacée par une déconnexion : elle part à la prochaine
//   connexion de ce compte ;
// - envoi une par une, dans l'ordre de saisie ; réseau coupé ou erreur du serveur (5xx) : on s'arrête et on réessaie
//   plus tard ; refus métier (4xx) : l'opération devient « rejetée » avec le code, et la suivante part ;
// - une opération envoyée disparaît, sauf si son résultat doit encore être montré (code de retrait d'un transfert) :
//   elle reste « envoyée » jusqu'à ce que l'agent la marque comme vue, puis le résultat est effacé du téléphone.
// shared/ ne connaît aucune fonctionnalité : chaque type d'opération fournit son envoi (`senders`).

export type QueuedStatus = 'pending' | 'rejected' | 'sent';

export interface QueuedError {
  status: number;
  code: string;
  details: Readonly<Record<string, unknown>>;
}

export interface QueuedOperation<TRequest = unknown, TResult = unknown> {
  /** Clé d'idempotence, générée sur le téléphone à la saisie. */
  id: string;
  accountId: string;
  /** « transfer », « cashMovement »… */
  kind: string;
  request: TRequest;
  /** Texte d'affichage (« Expéditeur → Bénéficiaire », nom du tiers) : la requête ne porte que des identifiants. */
  label: string;
  /** Heure de la saisie (ISO). */
  enteredAt: string;
  status: QueuedStatus;
  error: QueuedError | null;
  /** Réponse du serveur, gardée seulement tant qu'elle doit être montrée (code de retrait). */
  result: TResult | null;
}

export interface QueueSender {
  send(http: HttpClient, request: unknown, idempotencyKey: string): Promise<unknown>;
  /** Vrai : la réponse est gardée jusqu'à ce que l'agent la voie (transfert : code de retrait). */
  keepResult?: (result: unknown) => boolean;
}

const KEY = 'operations';

export class OperationQueue {
  private operations: QueuedOperation[] = [];
  private readonly listeners = new Set<() => void>();
  private loaded: Promise<void> | null = null;
  private flushing: Promise<void> | null = null;
  private readonly store: KeyValueStore;
  private readonly senders: Readonly<Record<string, QueueSender>>;
  private readonly onSent: (kind: string) => void;

  /** `onSent` : appelé après chaque opération envoyée (pour relire les listes concernées). */
  constructor(store: KeyValueStore, senders: Readonly<Record<string, QueueSender>>, onSent: (kind: string) => void = () => {}) {
    this.store = store;
    this.senders = senders;
    this.onSent = onSent;
  }

  /** Lit la file gardée sur le téléphone (une fois). */
  load(): Promise<void> {
    this.loaded ??= this.store.get<QueuedOperation[]>(KEY).then((saved) => {
      // Une opération saisie pendant la lecture n'est pas perdue.
      const known = new Set(this.operations.map((op) => op.id));
      this.operations = [...(saved ?? []).filter((op) => !known.has(op.id)), ...this.operations];
      this.notify();
    });
    return this.loaded;
  }

  getSnapshot = (): readonly QueuedOperation[] => this.operations;

  subscribe = (listener: () => void): (() => void) => {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  };

  /** Opérations d'un compte, dans l'ordre de saisie. */
  forAccount(accountId: string, kind?: string): QueuedOperation[] {
    return this.operations.filter((op) => op.accountId === accountId && (!kind || op.kind === kind));
  }

  async enqueue<TRequest>(operation: {
    id: string;
    accountId: string;
    kind: string;
    request: TRequest;
    label: string;
    enteredAt?: string;
  }): Promise<void> {
    await this.load();
    if (this.operations.some((op) => op.id === operation.id)) return;
    this.operations = [
      ...this.operations,
      { ...operation, enteredAt: operation.enteredAt ?? new Date().toISOString(), status: 'pending', error: null, result: null },
    ];
    await this.save();
  }

  /** Retire une opération (rejetée abandonnée, ou corrigée et envoyée autrement). */
  async remove(id: string): Promise<void> {
    await this.load();
    this.operations = this.operations.filter((op) => op.id !== id);
    await this.save();
  }

  /**
   * Envoie les opérations en attente de ce compte, dans l'ordre. Un seul envoi à la fois ; s'arrête au premier
   * problème de réseau ou de serveur (les suivantes attendent leur tour).
   */
  flush(http: HttpClient, accountId: string): Promise<void> {
    this.flushing ??= (async () => {
      try {
        await this.load();
        for (const operation of this.forAccount(accountId).filter((op) => op.status === 'pending')) {
          const sender = this.senders[operation.kind];
          if (!sender) continue;
          try {
            const result = await sender.send(http, operation.request, operation.id);
            const keep = sender.keepResult?.(result) ?? false;
            this.operations = keep
              ? this.operations.map((op) => (op.id === operation.id ? { ...op, status: 'sent', error: null, result } : op))
              : this.operations.filter((op) => op.id !== operation.id);
            await this.save();
            this.onSent(operation.kind);
          } catch (error) {
            if (isApiError(error) && !error.isNetworkError && error.status >= 400 && error.status < 500 && error.status !== 401) {
              const rejected: QueuedError = { status: error.status, code: error.code, details: error.details };
              this.operations = this.operations.map((op) => (op.id === operation.id ? { ...op, status: 'rejected', error: rejected } : op));
              await this.save();
              continue;
            }
            // Réseau, session à reprendre (401) ou serveur en panne : on réessaiera plus tard, dans le même ordre.
            return;
          }
        }
      } finally {
        this.flushing = null;
      }
    })();
    return this.flushing;
  }

  private async save(): Promise<void> {
    this.notify();
    await this.store.set(KEY, this.operations);
  }

  private notify(): void {
    for (const listener of this.listeners) listener();
  }
}
