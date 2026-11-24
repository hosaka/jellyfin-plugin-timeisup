using System;

namespace Jellyfin.Plugin.TimeIsUp.Usage;

/// <summary>
/// Usage of one user on one day.
/// </summary>
internal sealed class UserDay
{
    /// <summary>
    /// Gets or sets the seconds of video played.
    /// </summary>
    public double UsedSeconds { get; set; }

    /// <summary>
    /// Gets or sets the extra minutes granted for the day.
    /// </summary>
    public int ExtraMinutes { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether there is no limit for the rest of the day.
    /// </summary>
    public bool Party { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the warning was shown.
    /// </summary>
    public bool Warned { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether time ran out (logged once until more time is granted).
    /// </summary>
    public bool TimeUp { get; set; }

    /// <summary>
    /// Gets or sets the item that may finish after time ran out.
    /// </summary>
    public Guid? FinishingItemId { get; set; }

    /// <summary>
    /// Gets or sets the seconds played since time ran out while finishing.
    /// </summary>
    public double GraceUsedSeconds { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the item allowed to finish has ended.
    /// </summary>
    public bool Finished { get; set; }

    /// <summary>
    /// Clears the time-up state after more time was granted.
    /// </summary>
    public void ClearTimeUp()
    {
        TimeUp = false;
        FinishingItemId = null;
        GraceUsedSeconds = 0;
        Finished = false;
    }
}
