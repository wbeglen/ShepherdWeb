using System.Globalization;

namespace ShepherdWeb.Infrastructure.Time;

/// <summary>
/// The only place ShepherdWeb converts or formats timestamps (docs/ARCHITECTURE.md AD-10).
/// Every persisted timestamp is UTC. ShepherdAPI returns them without a "Z" (finding F4), so
/// values from the API are declared UTC on arrival; values going to the API must already be UTC,
/// because a local-kind value would be stored as the API server's local time.
/// </summary>
public static class Utc
{
    public static DateTime Now => DateTime.UtcNow;

    /// <summary>Declares a timestamp received from ShepherdAPI to be UTC.</summary>
    public static DateTime FromApi(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    /// <summary>
    /// Makes a timestamp safe to send to ShepherdAPI. Unspecified kind is rejected rather than
    /// guessed: it usually means a form value that was never converted, and guessing wrong would
    /// silently record the wrong time.
    /// </summary>
    public static DateTime ToApi(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => throw new InvalidOperationException(
            $"Timestamp {value:O} has unspecified kind. Convert it with Utc.FromUtcInput or Utc.FromLocalInput before sending it to ShepherdAPI."),
    };

    /// <summary>An operator-entered value that the UI labeled as UTC.</summary>
    public static DateTime FromUtcInput(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    /// <summary>An operator-entered value in this machine's local time (operator and server share a machine, A1).</summary>
    public static DateTime FromLocalInput(DateTime value) =>
        DateTime.SpecifyKind(value, DateTimeKind.Local).ToUniversalTime();

    public static string Format(DateTime value) =>
        FromApi(value).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC";

    public static string FormatLocal(DateTime value) =>
        FromApi(value).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " local";
}
