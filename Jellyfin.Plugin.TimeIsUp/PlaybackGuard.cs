using System;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.MediaInfo;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Jellyfin.Plugin.TimeIsUp;

/// <summary>
/// Answers playback info requests of users whose time is up with "not allowed".
/// Clients ask for playback info before they start playing, so nothing starts at all.
/// </summary>
public class PlaybackGuard : IAsyncResultFilter
{
    private readonly TimeKeeper _timeKeeper;
    private readonly IAuthorizationContext _authorizationContext;
    private readonly ILibraryManager _libraryManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlaybackGuard"/> class.
    /// </summary>
    /// <param name="timeKeeper">The <see cref="TimeKeeper"/>.</param>
    /// <param name="authorizationContext">Instance of the <see cref="IAuthorizationContext"/> interface.</param>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    public PlaybackGuard(TimeKeeper timeKeeper, IAuthorizationContext authorizationContext, ILibraryManager libraryManager)
    {
        _timeKeeper = timeKeeper;
        _authorizationContext = authorizationContext;
        _libraryManager = libraryManager;
    }

    /// <inheritdoc />
    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        if (context.Result is ObjectResult { Value: PlaybackInfoResponse response }
            && context.RouteData.Values.TryGetValue("itemId", out var rawItemId)
            && Guid.TryParse(rawItemId?.ToString(), out var itemId)
            && _libraryManager.GetItemById(itemId) is { MediaType: MediaType.Video })
        {
            var auth = await _authorizationContext.GetAuthorizationInfo(context.HttpContext).ConfigureAwait(false);
            if (!auth.IsApiKey && !auth.UserId.Equals(Guid.Empty) && !_timeKeeper.IsPlaybackAllowed(auth.UserId, itemId))
            {
                response.MediaSources = Array.Empty<MediaSourceInfo>();
                response.ErrorCode = PlaybackErrorCode.NotAllowed;
                _timeKeeper.NotifyRefused(auth.UserId, auth.DeviceId);
            }
        }

        await next().ConfigureAwait(false);
    }
}
