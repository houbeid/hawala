namespace Hawala.SharedKernel.Money;

/// <summary>
/// Les deux seules devises de l'app.
/// MRO = ancienne ouguiya (1 MRU = 10 MRO) : n'est plus un code ISO, c'est un code interne,
/// choisi parce que l'app actuelle et ses utilisateurs comptent en MRO.
/// Le type Money (montant + devise, arrondi) viendra avec le Grand livre.
/// </summary>
public enum Currency
{
    MRO,
    XOF,
}