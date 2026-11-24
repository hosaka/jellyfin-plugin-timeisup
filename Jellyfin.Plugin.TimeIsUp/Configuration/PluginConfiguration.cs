using System;
using System.Diagnostics.CodeAnalysis;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.TimeIsUp.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets a value indicating whether limits are enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the users that have a daily limit.
    /// </summary>
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Serialized by Jellyfin")]
    public UserLimit[] Users { get; set; } = [];

    /// <summary>
    /// Gets or sets the hour (0-23, server local time) at which a new day starts.
    /// </summary>
    public int ResetHour { get; set; }

    /// <summary>
    /// Gets or sets how many minutes before the limit the warning is shown, 0 to turn it off.
    /// </summary>
    public int WarningMinutes { get; set; } = 5;

    /// <summary>
    /// Gets or sets what happens to the playing item when time runs out.
    /// </summary>
    public TimeUpBehavior TimeUpBehavior { get; set; } = TimeUpBehavior.FinishWithGrace;

    /// <summary>
    /// Gets or sets how many minutes of playing time the current item may run over the limit.
    /// </summary>
    public int GraceMinutes { get; set; } = 10;

    /// <summary>
    /// Gets or sets a value indicating whether the time left is shown when playback starts.
    /// </summary>
    public bool ShowTimeLeftOnStart { get; set; } = true;

    /// <summary>
    /// Gets or sets the header of every message.
    /// </summary>
    public string MessageHeader { get; set; } = "Screen Time";

    /// <summary>
    /// Gets or sets a value indicating whether {username} starts with a capital letter.
    /// </summary>
    public bool CapitalizeUserName { get; set; }

    /// <summary>
    /// Gets or sets the warning message. {minutes} is replaced with the time left, like "5 minutes".
    /// </summary>
    public string WarningMessage { get; set; } = "{minutes} left";

    /// <summary>
    /// Gets or sets the message shown when time runs out and the current item may finish.
    /// </summary>
    public string FinishingMessage { get; set; } = "Time is up after this one";

    /// <summary>
    /// Gets or sets the message shown when playback is stopped or refused.
    /// </summary>
    public string TimeUpMessage { get; set; } = "Time is up! See you tomorrow.";

    /// <summary>
    /// Gets or sets the message shown when playback starts. {minutes} is replaced with the time left, like "5 minutes".
    /// </summary>
    public string TimeLeftMessage { get; set; } = "{minutes} left today";

    /// <summary>
    /// Gets the limit for a user, if they have one.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <returns>The limit, or null if the user has none.</returns>
    public UserLimit? GetLimit(Guid userId) => Array.Find(Users, u => u.UserId.Equals(userId));
}
