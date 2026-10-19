namespace Ledger.Contracts;

/// <summary>Nature d'un compte du grand livre : à qui il appartient.</summary>
public enum LedgerAccountKind
{
    /// <summary>Caisse espèces, banque ou wallet (module Trésorerie).</summary>
    Treasury,

    /// <summary>Tiers : client, fournisseur, personnel (module Tiers). Un compte par tiers et par agence.</summary>
    Party,

    /// <summary>Compte technique du grand livre (voir <see cref="SystemAccountRole"/>).</summary>
    System,
}
