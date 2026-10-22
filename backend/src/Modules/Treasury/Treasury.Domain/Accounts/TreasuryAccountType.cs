namespace Treasury.Domain.Accounts;

/// <summary>Nature d'un compte de trésorerie (« méthode de paiement »).</summary>
public enum TreasuryAccountType
{
    /// <summary>Caisse en espèces. Une agence peut en avoir plusieurs.</summary>
    Cash,

    /// <summary>Compte bancaire.</summary>
    Bank,

    /// <summary>Portefeuille mobile : Bankily, Sedad, Wave, Orange Money…</summary>
    Wallet,
}
