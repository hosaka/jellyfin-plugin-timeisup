namespace Jellyfin.Plugin.TimeIsUp.Configuration;

/// <summary>
/// What happens to the playing item when time runs out.
/// </summary>
public enum TimeUpBehavior
{
    /// <summary>
    /// Stop playback right away.
    /// </summary>
    StopImmediately,

    /// <summary>
    /// Let the current item play to the end.
    /// </summary>
    FinishCurrent,

    /// <summary>
    /// Let the current item play to the end, up to the grace limit.
    /// </summary>
    FinishWithGrace
}
