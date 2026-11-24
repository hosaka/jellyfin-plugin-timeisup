using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using System.Threading.Tasks;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.TimeIsUp.Api;

/// <summary>
/// Today's usage and extra time, for the plugin's settings page.
/// </summary>
[ApiController]
[Route("TimeIsUp")]
[Authorize(Policy = Policies.RequiresElevation)]
[Produces(MediaTypeNames.Application.Json)]
public class TimeIsUpController : ControllerBase
{
    private readonly TimeKeeper _timeKeeper;
    private readonly IAuthorizationContext _authorizationContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="TimeIsUpController"/> class.
    /// </summary>
    /// <param name="timeKeeper">The <see cref="TimeKeeper"/>.</param>
    /// <param name="authorizationContext">Instance of the <see cref="IAuthorizationContext"/> interface.</param>
    public TimeIsUpController(TimeKeeper timeKeeper, IAuthorizationContext authorizationContext)
    {
        _timeKeeper = timeKeeper;
        _authorizationContext = authorizationContext;
    }

    /// <summary>
    /// Gets today's usage of every limited user.
    /// </summary>
    /// <returns>One status per limited user.</returns>
    [HttpGet("Status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<UserStatus>> GetStatus() => Ok(_timeKeeper.GetStatus());

    /// <summary>
    /// Grants extra minutes for today.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="minutes">The minutes to add.</param>
    /// <returns>No content, or not found if the user has no limit.</returns>
    [HttpPost("Users/{userId}/Extra")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> AddExtra([FromRoute, Required] Guid userId, [FromQuery, Range(1, 1440)] int minutes)
    {
        return _timeKeeper.AddExtra(userId, minutes, await CallerName().ConfigureAwait(false)) ? NoContent() : NotFound();
    }

    /// <summary>
    /// Lifts the limit for the rest of the day.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <returns>No content, or not found if the user has no limit.</returns>
    [HttpPost("Users/{userId}/Party")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> StartParty([FromRoute, Required] Guid userId)
    {
        return _timeKeeper.StartParty(userId, await CallerName().ConfigureAwait(false)) ? NoContent() : NotFound();
    }

    /// <summary>
    /// Removes today's extra minutes and party mode.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <returns>No content, or not found if the user has no limit.</returns>
    [HttpDelete("Users/{userId}/Extra")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> ClearExtra([FromRoute, Required] Guid userId)
    {
        return _timeKeeper.ClearExtra(userId, await CallerName().ConfigureAwait(false)) ? NoContent() : NotFound();
    }

    private async Task<string?> CallerName()
    {
        var auth = await _authorizationContext.GetAuthorizationInfo(Request).ConfigureAwait(false);
        return auth.User?.Username;
    }
}
