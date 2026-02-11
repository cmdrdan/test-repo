using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.LiveTV.Models;

/// <summary>
/// Represents a user-defined virtual TV channel.
/// </summary>
public class VirtualChannel
{
    public VirtualChannel()
    {
        Id = Guid.NewGuid().ToString("N");
        Programs = new List<ChannelProgram>();
        LibraryIds = new List<string>();
    }

    /// <summary>
    /// Unique identifier for this channel.
    /// </summary>
    public string Id { get; set; }

    /// <summary>
    /// Display name (e.g. "Comedy Central", "Movie Night").
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Channel number shown in the guide (e.g. "1", "5.1").
    /// </summary>
    public string Number { get; set; } = "1";

    /// <summary>
    /// Category/group for organizing channels in the guide.
    /// </summary>
    public string Group { get; set; } = "Virtual";

    /// <summary>
    /// Scheduling mode for this channel.
    /// </summary>
    public ScheduleMode Mode { get; set; } = ScheduleMode.Shuffle;

    /// <summary>
    /// Explicit program list for Sequential/Custom modes.
    /// Each entry references a Jellyfin library item by its ID.
    /// </summary>
    public List<ChannelProgram> Programs { get; set; }

    /// <summary>
    /// Jellyfin library/collection IDs to pull content from for Shuffle mode.
    /// </summary>
    public List<string> LibraryIds { get; set; }

    /// <summary>
    /// Whether this channel is currently active.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Optional channel logo image URL.
    /// </summary>
    public string? ImageUrl { get; set; }

    /// <summary>
    /// Epoch timestamp (UTC) used as the anchor point for schedule generation.
    /// Defaults to Unix epoch. The schedule repeats from this point forward.
    /// </summary>
    public long StartDateTicks { get; set; } = 0;
}

/// <summary>
/// Determines how a channel fills its schedule.
/// </summary>
public enum ScheduleMode
{
    /// <summary>
    /// Randomly shuffle all available content. Deterministic based on time slot.
    /// </summary>
    Shuffle = 0,

    /// <summary>
    /// Play programs in the order they appear in the Programs list, looping.
    /// </summary>
    Sequential = 1
}
