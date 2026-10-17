using Hawala.SharedKernel.Money;

namespace Organization.Domain.Agencies;

/// <summary>Les deux pays du corridor. Le pays fixe la devise de l'agence.</summary>
public enum Country
{
    Mauritania,
    Senegal,
}

public static class CountryExtensions
{
    /// <summary>Devise de travail d'une agence de ce pays : MRO en Mauritanie, FCFA (XOF) au Sénégal.</summary>
    public static Currency Currency(this Country country) => country switch
    {
        Country.Mauritania => Hawala.SharedKernel.Money.Currency.MRO,
        Country.Senegal => Hawala.SharedKernel.Money.Currency.XOF,
        _ => throw new ArgumentOutOfRangeException(nameof(country), country, "Pays inconnu."),
    };
}