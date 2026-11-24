using System;
using System.Diagnostics.CodeAnalysis;

namespace Jellyfin.Plugin.TimeIsUp.Configuration;

/// <summary>
/// The daily limit of one user.
/// </summary>
public class UserLimit
{
    /// <summary>
    /// Gets or sets the user id.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets the minutes allowed per day, indexed by <see cref="DayOfWeek"/> (0 is Sunday).
    /// </summary>
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Serialized by Jellyfin")]
    public int[] Minutes { get; set; } = [45, 45, 45, 45, 45, 45, 45];

    /// <summary>
    /// Gets the minutes allowed on a day.
    /// </summary>
    /// <param name="day">The day of the week.</param>
    /// <returns>The minutes allowed.</returns>
    public int MinutesOn(DayOfWeek day) => (int)day < Minutes.Length ? Math.Max(0, Minutes[(int)day]) : 0;
}
