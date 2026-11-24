using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.TimeIsUp.Api;
using Jellyfin.Plugin.TimeIsUp.Configuration;
using Jellyfin.Plugin.TimeIsUp.Usage;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Activity;
using MediaBrowser.Model.Session;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TimeIsUp;

/// <summary>
/// Counts how long limited users play video each day and enforces their limits.
/// </summary>
public sealed partial class TimeKeeper : BackgroundService
{
    private static readonly TimeSpan _tick = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan _maxTick = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan _staleCheckIn = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan _saveInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan _noticeInterval = TimeSpan.FromSeconds(15);

    private readonly ISessionManager _sessionManager;
    private readonly IUserManager _userManager;
    private readonly IActivityManager _activityManager;
    private readonly ILogger<TimeKeeper> _logger;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, DateTime> _lastNotice = [];
    private UsageStore? _store;
    private UsageDay _today = new();
    private bool _dirty;
    private DateTime _lastSave = DateTime.MinValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="TimeKeeper"/> class.
    /// </summary>
    /// <param name="sessionManager">Instance of the <see cref="ISessionManager"/> interface.</param>
    /// <param name="userManager">Instance of the <see cref="IUserManager"/> interface.</param>
    /// <param name="activityManager">Instance of the <see cref="IActivityManager"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{TimeKeeper}"/> interface.</param>
    public TimeKeeper(
        ISessionManager sessionManager,
        IUserManager userManager,
        IActivityManager activityManager,
        ILogger<TimeKeeper> logger)
    {
        _sessionManager = sessionManager;
        _userManager = userManager;
        _activityManager = activityManager;
        _logger = logger;
    }

    private static PluginConfiguration Config => Plugin.Instance?.Configuration ?? new PluginConfiguration();

    /// <inheritdoc />
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        var plugin = Plugin.Instance ?? throw new InvalidOperationException("The plugin is not loaded");
        _store = new UsageStore(plugin.DataFolderPath);
        lock (_lock)
        {
            try
            {
                _today = _store.Load() ?? new UsageDay();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                LogLoadFailed(ex);
                _today = new UsageDay();
            }

            RollOver(Config);
        }

        _sessionManager.PlaybackStart += OnPlaybackStart;
        _sessionManager.PlaybackStopped += OnPlaybackStopped;
        LogStarted(plugin.Version.ToString(), Config.ResetHour, TimeZoneInfo.Local.Id);
        return base.StartAsync(cancellationToken);
    }

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _sessionManager.PlaybackStart -= OnPlaybackStart;
        _sessionManager.PlaybackStopped -= OnPlaybackStopped;
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        lock (_lock)
        {
            Save();
        }
    }

    /// <summary>
    /// Checks whether a user may start playing an item right now.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="itemId">The video's item id.</param>
    /// <returns>Whether playback is allowed.</returns>
    public bool IsPlaybackAllowed(Guid userId, Guid itemId)
    {
        var config = Config;
        var limit = config.GetLimit(userId);
        if (!config.Enabled || limit is null)
        {
            return true;
        }

        lock (_lock)
        {
            RollOver(config);
            var day = _today.Get(userId);
            return day.Party || Remaining(limit, day) > 0 || day.FinishingItemId == itemId;
        }
    }

    /// <summary>
    /// Tells the user's device why playback was refused.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="deviceId">The device that asked to play.</param>
    public void NotifyRefused(Guid userId, string? deviceId)
    {
        var session = _sessionManager.Sessions.FirstOrDefault(s => s.UserId.Equals(userId) && s.DeviceId == deviceId);
        if (session is null)
        {
            return;
        }

        lock (_lock)
        {
            if (!ShouldNotify(session))
            {
                return;
            }
        }

        _ = SendAsync(new Notice(session, Config.TimeUpMessage, false));
    }

    /// <summary>
    /// Gets today's usage of every limited user.
    /// </summary>
    /// <returns>One status per limited user.</returns>
    public IReadOnlyList<UserStatus> GetStatus()
    {
        var config = Config;
        var sessions = _sessionManager.Sessions.Where(s => s.NowPlayingItem is not null && IsFresh(s)).ToList();
        var result = new List<UserStatus>();
        lock (_lock)
        {
            RollOver(config);
            foreach (var limit in config.Users)
            {
                var user = _userManager.GetUserById(limit.UserId);
                if (user is null)
                {
                    continue;
                }

                var day = _today.Get(limit.UserId);
                var remaining = Remaining(limit, day);
                var session = sessions.FirstOrDefault(s => s.UserId.Equals(limit.UserId));
                result.Add(new UserStatus
                {
                    UserId = limit.UserId,
                    UserName = user.Username,
                    LimitMinutes = limit.MinutesOn(_today.Day.DayOfWeek),
                    ExtraMinutes = day.ExtraMinutes,
                    UsedSeconds = (int)day.UsedSeconds,
                    RemainingSeconds = (int)Math.Max(0, Math.Ceiling(remaining)),
                    Party = day.Party,
                    State = StateOf(config, day, remaining),
                    NowPlaying = session is null ? null : Title(session),
                    Device = session?.DeviceName,
                    IsPaused = session?.PlayState?.IsPaused ?? false
                });
            }
        }

        return result;
    }

    /// <summary>
    /// Grants extra minutes for today.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="minutes">The minutes to add.</param>
    /// <param name="grantedBy">The name of the administrator.</param>
    /// <returns>Whether the user has a limit.</returns>
    public bool AddExtra(Guid userId, int minutes, string? grantedBy)
    {
        var name = string.Create(CultureInfo.InvariantCulture, $"{grantedBy ?? "An administrator"} added {minutes} minutes for {UserName(userId)}");
        return ChangeDay(userId, name, (config, limit, day) =>
        {
            day.ExtraMinutes += minutes;
            AfterMoreTime(config, limit, day);
        });
    }

    /// <summary>
    /// Lifts the limit for the rest of the day.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="grantedBy">The name of the administrator.</param>
    /// <returns>Whether the user has a limit.</returns>
    public bool StartParty(Guid userId, string? grantedBy)
    {
        var name = $"{grantedBy ?? "An administrator"} started party mode for {UserName(userId)}";
        return ChangeDay(userId, name, (config, limit, day) =>
        {
            day.Party = true;
            AfterMoreTime(config, limit, day);
        });
    }

    /// <summary>
    /// Removes today's extra minutes and party mode.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="removedBy">The name of the administrator.</param>
    /// <returns>Whether the user has a limit.</returns>
    public bool ClearExtra(Guid userId, string? removedBy)
    {
        var name = $"{removedBy ?? "An administrator"} removed today's extra time for {UserName(userId)}";
        return ChangeDay(userId, name, (_, _, day) =>
        {
            day.ExtraMinutes = 0;
            day.Party = false;
        });
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_tick);
        var clock = Stopwatch.StartNew();
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            var elapsed = clock.Elapsed;
            clock.Restart();
            try
            {
                await UpdateAsync(elapsed > _maxTick ? _maxTick : elapsed).ConfigureAwait(false);
            }
#pragma warning disable CA1031 // A failed tick must not stop the loop
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogTickFailed(ex);
            }
        }
    }

    private static bool IsFresh(SessionInfo session) => DateTime.UtcNow - session.LastPlaybackCheckIn < _staleCheckIn;

    private static bool IsPlayingVideo(SessionInfo session) =>
        session.NowPlayingItem is { MediaType: MediaType.Video }
        && session.PlayState is { IsPaused: false }
        && IsFresh(session);

    private static string Title(SessionInfo session)
    {
        var item = session.NowPlayingItem;
        return string.IsNullOrEmpty(item.SeriesName) ? item.Name : $"{item.SeriesName}: {item.Name}";
    }

    private static string Format(string template, string userName, double remainingSeconds)
    {
        var minutes = (int)Math.Ceiling(Math.Max(0, remainingSeconds) / 60);
        var text = minutes == 1 ? "1 minute" : string.Create(CultureInfo.InvariantCulture, $"{minutes} minutes");
        return MinutesPlaceholder().Replace(template, text).Replace("{username}", userName, StringComparison.Ordinal);
    }

    // {minutes} becomes "1 minute" or "5 minutes"; a unit written after it is absorbed, so "{minutes} minutes" works too.
    [GeneratedRegex(@"\{minutes\}( minutes?\b)?")]
    private static partial Regex MinutesPlaceholder();

    private static string StateOf(PluginConfiguration config, UserDay day, double remaining)
    {
        if (!config.Enabled)
        {
            return "Off";
        }

        if (day.Party)
        {
            return "Party";
        }

        if (day.FinishingItemId is not null)
        {
            return "Finishing";
        }

        if (remaining <= 0)
        {
            return "TimeUp";
        }

        return config.WarningMinutes > 0 && remaining <= config.WarningMinutes * 60 ? "Warning" : "Ok";
    }

    private void AfterMoreTime(PluginConfiguration config, UserLimit limit, UserDay day)
    {
        var remaining = Remaining(limit, day);
        if (day.Party || remaining > 0)
        {
            day.ClearTimeUp();
        }

        if (day.Party || remaining > config.WarningMinutes * 60)
        {
            day.Warned = false;
        }
    }

    private double Remaining(UserLimit limit, UserDay day) =>
        ((limit.MinutesOn(_today.Day.DayOfWeek) + day.ExtraMinutes) * 60) - day.UsedSeconds;

    private async Task UpdateAsync(TimeSpan elapsed)
    {
        var config = Config;
        var notices = new List<Notice>();
        var logs = new List<LogEntry>();
        var sessions = _sessionManager.Sessions.ToList();
        lock (_lock)
        {
            RollOver(config);
            foreach (var limit in config.Users)
            {
                var playing = sessions.Where(s => s.UserId.Equals(limit.UserId) && IsPlayingVideo(s)).ToList();
                var day = _today.Get(limit.UserId);
                if (playing.Count > 0)
                {
                    day.UsedSeconds += elapsed.TotalSeconds;
                    if (day.FinishingItemId is not null)
                    {
                        day.GraceUsedSeconds += elapsed.TotalSeconds;
                    }

                    _dirty = true;
                }

                if (config.Enabled && !day.Party)
                {
                    Enforce(config, limit, day, playing, notices, logs);
                }
            }

            if (_dirty && DateTime.UtcNow - _lastSave > _saveInterval)
            {
                Save();
            }
        }

        foreach (var notice in notices)
        {
            _ = SendAsync(notice);
        }

        foreach (var log in logs)
        {
            await LogActivityAsync(log).ConfigureAwait(false);
        }
    }

    private void Enforce(PluginConfiguration config, UserLimit limit, UserDay day, List<SessionInfo> playing, List<Notice> notices, List<LogEntry> logs)
    {
        var remaining = Remaining(limit, day);
        if (remaining > 0)
        {
            if (config.WarningMinutes > 0 && !day.Warned && playing.Count > 0 && remaining <= config.WarningMinutes * 60)
            {
                day.Warned = true;
                _dirty = true;
                notices.AddRange(playing.Select(s => new Notice(s, config.WarningMessage, false, remaining)));
            }

            return;
        }

        if (!day.TimeUp)
        {
            day.TimeUp = true;
            _dirty = true;
            logs.Add(new LogEntry($"Time is up for {UserName(limit.UserId)}", limit.UserId));
        }

        foreach (var session in playing)
        {
            var itemId = session.NowPlayingItem.Id;
            if (config.TimeUpBehavior == TimeUpBehavior.StopImmediately)
            {
                StopWithNotice(session, config, notices);
            }
            else if (day.FinishingItemId is null && !day.Finished)
            {
                day.FinishingItemId = itemId;
                day.GraceUsedSeconds = 0;
                _dirty = true;
                notices.Add(new Notice(session, config.FinishingMessage, false));
            }
            else if (day.FinishingItemId != itemId)
            {
                StopWithNotice(session, config, notices);
            }
            else if (config.TimeUpBehavior == TimeUpBehavior.FinishWithGrace && day.GraceUsedSeconds >= config.GraceMinutes * 60)
            {
                day.FinishingItemId = null;
                day.Finished = true;
                _dirty = true;
                StopWithNotice(session, config, notices);
            }
        }
    }

    private void StopWithNotice(SessionInfo session, PluginConfiguration config, List<Notice> notices)
    {
        if (ShouldNotify(session))
        {
            notices.Add(new Notice(session, config.TimeUpMessage, true));
        }
    }

    private bool ShouldNotify(SessionInfo session)
    {
        var now = DateTime.UtcNow;
        if (_lastNotice.TryGetValue(session.Id, out var last) && now - last < _noticeInterval)
        {
            return false;
        }

        _lastNotice[session.Id] = now;
        return true;
    }

    private void OnPlaybackStart(object? sender, PlaybackProgressEventArgs e)
    {
        if (e.Session is null || e.Item is not { MediaType: MediaType.Video })
        {
            return;
        }

        var config = Config;
        var limit = config.GetLimit(e.Session.UserId);
        if (!config.Enabled || limit is null)
        {
            return;
        }

        Notice? notice = null;
        lock (_lock)
        {
            RollOver(config);
            var day = _today.Get(e.Session.UserId);
            var remaining = Remaining(limit, day);
            if (day.Party)
            {
                return;
            }

            if (remaining <= 0 && day.FinishingItemId != e.Item.Id)
            {
                if (ShouldNotify(e.Session))
                {
                    notice = new Notice(e.Session, config.TimeUpMessage, true);
                }
            }
            else if (remaining > 0 && config.ShowTimeLeftOnStart)
            {
                notice = new Notice(e.Session, config.TimeLeftMessage, false, remaining);
            }
        }

        if (notice is not null)
        {
            _ = SendAsync(notice);
        }
    }

    private void OnPlaybackStopped(object? sender, PlaybackStopEventArgs e)
    {
        if (e.Session is null || e.Item is null)
        {
            return;
        }

        var config = Config;
        lock (_lock)
        {
            var day = _today.Get(e.Session.UserId);
            if (day.FinishingItemId != e.Item.Id)
            {
                return;
            }

            day.FinishingItemId = null;
            day.Finished = true;
            _dirty = true;
        }

        _ = SendAsync(new Notice(e.Session, config.TimeUpMessage, false));
    }

    private bool ChangeDay(Guid userId, string activity, Action<PluginConfiguration, UserLimit, UserDay> change)
    {
        var config = Config;
        var limit = config.GetLimit(userId);
        if (limit is null)
        {
            return false;
        }

        lock (_lock)
        {
            RollOver(config);
            change(config, limit, _today.Get(userId));
            _dirty = true;
            Save();
        }

        _ = LogActivityAsync(new LogEntry(activity, userId));
        return true;
    }

    private void RollOver(PluginConfiguration config)
    {
        var today = DateOnly.FromDateTime(DateTime.Now.AddHours(-Math.Clamp(config.ResetHour, 0, 23)));
        if (_today.Day == today)
        {
            return;
        }

        _today = new UsageDay { Day = today };
        _lastNotice.Clear();
        _dirty = true;
        Save();
        LogNewDay(today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    private void Save()
    {
        if (_store is null || !_dirty)
        {
            return;
        }

        try
        {
            _store.Save(_today);
            _dirty = false;
            _lastSave = DateTime.UtcNow;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogSaveFailed(ex);
        }
    }

    private string UserName(Guid userId) => _userManager.GetUserById(userId)?.Username ?? userId.ToString("N", CultureInfo.InvariantCulture);

    private async Task SendAsync(Notice notice)
    {
        try
        {
            if (notice.Stop)
            {
                LogStopping(notice.Session.UserName, notice.Session.DeviceName);
                await _sessionManager.SendPlaystateCommand(null, notice.Session.Id, new PlaystateRequest { Command = PlaystateCommand.Stop }, CancellationToken.None).ConfigureAwait(false);
            }

            var userName = notice.Session.UserName;
            if (Config.CapitalizeUserName && userName.Length > 0)
            {
                userName = char.ToUpperInvariant(userName[0]) + userName[1..];
            }

            var message = new MessageCommand
            {
                Header = Format(Config.MessageHeader, userName, notice.RemainingSeconds),
                Text = Format(notice.Template, userName, notice.RemainingSeconds),
                TimeoutMs = 10000
            };
            await _sessionManager.SendMessageCommand(null, notice.Session.Id, message, CancellationToken.None).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // A client that can't be reached must not break anything else
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogSendFailed(ex, notice.Session.DeviceName);
        }
    }

    private async Task LogActivityAsync(LogEntry entry)
    {
        try
        {
            await _activityManager.CreateAsync(new ActivityLog(entry.Name, "TimeIsUp", entry.UserId) { LogSeverity = LogLevel.Information }).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // The activity log is informational
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogActivityFailed(ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Time Is Up {Version} started, days start at {ResetHour}:00 in {TimeZone}")]
    private partial void LogStarted(string version, int resetHour, string timeZone);

    [LoggerMessage(Level = LogLevel.Information, Message = "New day {Day}, usage reset")]
    private partial void LogNewDay(string day);

    [LoggerMessage(Level = LogLevel.Information, Message = "Stopping playback of {User} on {Device}")]
    private partial void LogStopping(string user, string device);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read the saved usage, starting from zero")]
    private partial void LogLoadFailed(Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not save usage")]
    private partial void LogSaveFailed(Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not send a message to {Device}")]
    private partial void LogSendFailed(Exception ex, string device);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not write to the activity log")]
    private partial void LogActivityFailed(Exception ex);

    [LoggerMessage(Level = LogLevel.Error, Message = "Updating usage failed")]
    private partial void LogTickFailed(Exception ex);

    private sealed record Notice(SessionInfo Session, string Template, bool Stop, double RemainingSeconds = 0);

    private sealed record LogEntry(string Name, Guid UserId);
}
