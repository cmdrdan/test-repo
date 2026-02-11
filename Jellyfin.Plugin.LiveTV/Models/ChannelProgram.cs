namespace Jellyfin.Plugin.LiveTV.Models;

/// <summary>
/// Represents a media item assigned to a channel's schedule.
/// </summary>
public class ChannelProgram
{
    /// <summary>
    /// The Jellyfin library item ID (the media to play).
    /// </summary>
    public string ItemId { get; set; } = string.Empty;

    /// <summary>
    /// Display name of the program (cached from library metadata).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Runtime in ticks (cached from library metadata).
    /// </summary>
    public long RuntimeTicks { get; set; }
}
