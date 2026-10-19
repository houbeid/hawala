namespace Hawala.SharedKernel.Money;

/// <summary>
/// Les deux seules devises de l'app.
/// MRO = ancienne ouguiya (1 MRU = 10 MRO) : n'est plus un code ISO, c'est un code interne,
/// choisi parce que l'app actuelle et ses utilisateurs comptent en MRO.
/// Montant + devise : <see cref="Money"/> ; taux « 5 000 FCFA = ? MRO » : <see cref="ExchangeRate"/>.
/// </summary>
public enum Currency
{
    MRO,
    XOF,
}