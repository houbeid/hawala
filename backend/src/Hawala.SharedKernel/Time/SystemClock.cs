namespace Hawala.SharedKernel.Time;

/// <summary>Horloge réelle, utilisée en production (enregistrée en singleton dans l'API).</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}