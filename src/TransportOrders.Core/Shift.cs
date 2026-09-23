namespace TransportOrders.Core;

public enum ShiftKind
{
    Early,
    Late,
    Night
}

/// <summary>
/// A production shift in plant local time: Early 06:00–14:00, Late 14:00–22:00, Night 22:00–06:00.
/// </summary>
public sealed record Shift(ShiftKind Kind, DateTime StartLocal, DateTime EndLocal)
{
    public static Shift At(DateTime localTime)
    {
        var day = DateTime.SpecifyKind(localTime.Date, DateTimeKind.Unspecified);
        return localTime.Hour switch
        {
            >= 6 and < 14 => new Shift(ShiftKind.Early, day.AddHours(6), day.AddHours(14)),
            >= 14 and < 22 => new Shift(ShiftKind.Late, day.AddHours(14), day.AddHours(22)),
            >= 22 => new Shift(ShiftKind.Night, day.AddHours(22), day.AddDays(1).AddHours(6)),
            _ => new Shift(ShiftKind.Night, day.AddDays(-1).AddHours(22), day.AddHours(6)),
        };
    }
}
