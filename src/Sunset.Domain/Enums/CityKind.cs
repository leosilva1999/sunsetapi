namespace Sunset.Domain.Enums;

public enum CityKind
{
    /// <summary>A municipality (in Brazil, an IBGE municipality): the unit that gets a public city page.</summary>
    Municipality,

    /// <summary>A smaller place inside a municipality (village, district, beach...).</summary>
    Locality
}
