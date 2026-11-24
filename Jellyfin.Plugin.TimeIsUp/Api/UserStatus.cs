using System;

namespace Jellyfin.Plugin.TimeIsUp.Api;

/// <summary>
/// Today's usage of one limited user.
/// </summary>
public class UserStatus
{
    /// <summary>
    /// Gets or sets the user id.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets the user name.
    /// </summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets today's limit in minutes.
    /// </summary>
    public int LimitMinutes { get; set; }

    /// <summary>
    /// Gets or sets the extra minutes granted today.
    /// </summary>
    public int ExtraMinutes { get; set; }

    /// <summary>
    /// Gets or sets the seconds watched today.
    /// </summary>
    public int UsedSeconds { get; set; }

    /// <summary>
    /// Gets or sets the seconds left today, never below zero.
    /// </summary>
    public int RemainingSeconds { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether there is no limit for the rest of the day.
    /// </summary>
    public bool Party { get; set; }

    /// <summary>
    /// Gets or sets the state: Ok, Warning, Finishing, TimeUp or Party.
    /// </summary>
    public string State { get; set; } = "Ok";

    /// <summary>
    /// Gets or sets what the user is playing, if anything.
    /// </summary>
    public string? NowPlaying { get; set; }

    /// <summary>
    /// Gets or sets the device the user is playing on.
    /// </summary>
    public string? Device { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether playback is paused.
    /// </summary>
    public bool IsPaused { get; set; }
}
