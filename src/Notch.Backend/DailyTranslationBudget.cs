namespace Notch.Backend;

// Single-instance conservative quota. Failed requests also consume reservations.
public sealed class DailyTranslationBudget(TimeProvider clock, int maximumCharacters = 5000)
{
    private readonly object _gate = new();
    private DateOnly _day;
    private int _used;
    public bool TryReserve(int characters)
    {
        lock (_gate)
        {
            var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            if (_day != today) { _day = today; _used = 0; }
            if (characters <= 0 || characters > maximumCharacters - _used) return false;
            _used += characters;
            return true;
        }
    }
}
