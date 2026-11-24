using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.TimeIsUp.Usage;

/// <summary>
/// Usage of all limited users on one day.
/// </summary>
internal sealed class UsageDay
{
    public DateOnly Day { get; set; }

    public Dictionary<Guid, UserDay> Users { get; set; } = [];

    public UserDay Get(Guid userId)
    {
        if (!Users.TryGetValue(userId, out var day))
        {
            day = new UserDay();
            Users[userId] = day;
        }

        return day;
    }
}
