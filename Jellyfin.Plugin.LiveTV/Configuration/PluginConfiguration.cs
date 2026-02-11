using System.Collections.Generic;
using MediaBrowser.Model.Plugins;
using Jellyfin.Plugin.LiveTV.Models;

namespace Jellyfin.Plugin.LiveTV.Configuration;

/// <summary>
/// Plugin configuration holding all channel and schedule definitions.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    public PluginConfiguration()
    {
        Channels = new List<VirtualChannel>();
    }

    /// <summary>
    /// The list of user-defined virtual channels.
    /// </summary>
    public List<VirtualChannel> Channels { get; set; }

    /// <summary>
    /// How many days of EPG guide data to generate ahead.
    /// </summary>
    public int Guidedays { get; set; } = 3;

    /// <summary>
    /// Default padding (in seconds) between programs on a channel.
    /// </summary>
    public int PaddingSeconds { get; set; } = 0;

    /// <summary>
    /// When true, channels will pad short time slots with filler repeats.
    /// </summary>
    public bool PadShortSlots { get; set; } = true;
}
