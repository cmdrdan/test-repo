using System;

namespace Jellyfin.Plugin.LiveTV.Models;

/// <summary>
/// A resolved time slot in the schedule — what's playing and when.
/// </summary>
public class ScheduleSlot
{
    /// <summary>
    /// The Jellyfin library item ID for this slot.
    /// </summary>
    public string ItemId { get; set; } = string.Empty;

    /// <summary>
    /// Display title for EPG.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// When this slot starts (UTC).
    /// </summary>
    public DateTime StartTimeUtc { get; set; }

    /// <summary>
    /// When this slot ends (UTC).
    /// </summary>
    public DateTime EndTimeUtc { get; set; }

    /// <summary>
    /// Full runtime of the media item in ticks.
    /// </summary>
    public long RuntimeTicks { get; set; }

    /// <summary>
    /// How far into the media item the current moment is (for live seek).
    /// Only meaningful for the "now playing" slot.
    /// </summary>
    public TimeSpan ElapsedTime { get; set; }

    /// <summary>
    /// Program overview/description from the library metadata.
    /// </summary>
    public string? Overview { get; set; }

    /// <summary>
    /// Episode title if applicable.
    /// </summary>
    public string? EpisodeTitle { get; set; }

    /// <summary>
    /// Season number if applicable.
    /// </summary>
    public int? SeasonNumber { get; set; }

    /// <summary>
    /// Episode number if applicable.
    /// </summary>
    public int? EpisodeNumber { get; set; }

    /// <summary>
    /// Production year.
    /// </summary>
    public int? ProductionYear { get; set; }

    /// <summary>
    /// Whether this item is a movie.
    /// </summary>
    public bool IsMovie { get; set; }

    /// <summary>
    /// Whether this item is a series episode.
    /// </summary>
    public bool IsSeries { get; set; }

    /// <summary>
    /// Image URL for EPG.
    /// </summary>
    public string? ImageUrl { get; set; }
}
