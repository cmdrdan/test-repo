using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Jellyfin.Plugin.LiveTV.Models;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LiveTV.Services;

/// <summary>
/// Manages live stream sessions for virtual channels.
/// The key optimization: instead of transcoding through an external service,
/// we point Jellyfin directly at the local media file and set the start position
/// so playback begins at the correct offset. This eliminates the lag that
/// tools like ErsatzTV or Tunarr introduce.
/// </summary>
public class StreamManager
{
    private readonly ILibraryManager _libraryManager;
    private readonly IMediaSourceManager _mediaSourceManager;
    private readonly ILogger<StreamManager> _logger;
    private readonly ConcurrentDictionary<string, ActiveStream> _activeStreams = new();

    public StreamManager(
        ILibraryManager libraryManager,
        IMediaSourceManager mediaSourceManager,
        ILogger<StreamManager> logger)
    {
        _libraryManager = libraryManager;
        _mediaSourceManager = mediaSourceManager;
        _logger = logger;
    }

    /// <summary>
    /// Creates a MediaSourceInfo that points directly at the local media file.
    /// Jellyfin handles all the playback, seeking, and (if needed) transcoding.
    /// </summary>
    public MediaSourceInfo CreateMediaSource(string channelId, ScheduleSlot slot)
    {
        if (!Guid.TryParse(slot.ItemId, out var itemGuid))
        {
            throw new ArgumentException($"Invalid item ID: {slot.ItemId}");
        }

        var item = _libraryManager.GetItemById(itemGuid);
        if (item is null)
        {
            throw new InvalidOperationException($"Library item {slot.ItemId} not found");
        }

        var streamId = $"livetv_{channelId}_{DateTime.UtcNow.Ticks}";
        var filePath = item.Path;

        _logger.LogInformation(
            "Opening stream for channel {ChannelId}: {Title} at offset {Offset}",
            channelId,
            slot.Title,
            slot.ElapsedTime);

        // Track active stream
        _activeStreams[streamId] = new ActiveStream
        {
            StreamId = streamId,
            ChannelId = channelId,
            ItemId = slot.ItemId,
            StartedAtUtc = DateTime.UtcNow
        };

        // Get media sources from the library item through the media source manager
        List<MediaSourceInfo>? existingSources = null;
        try
        {
            existingSources = _mediaSourceManager
                .GetStaticMediaSources(item, false)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not get media sources for item {ItemId}", slot.ItemId);
        }

        var primarySource = existingSources?.FirstOrDefault();

        var mediaSource = new MediaSourceInfo
        {
            Id = streamId,
            Path = filePath,
            Protocol = MediaProtocol.File,
            Container = primarySource?.Container ?? GetContainerFromPath(filePath),
            IsInfiniteStream = false,
            IsRemote = false,
            SupportsDirectPlay = true,
            SupportsDirectStream = true,
            SupportsTranscoding = true,
            SupportsProbing = true,
            RequiresOpening = false,
            RequiresClosing = true,
            LiveStreamId = streamId,
            ReadAtNativeFramerate = false,
            RunTimeTicks = slot.RuntimeTicks,
            MediaStreams = primarySource?.MediaStreams ?? new List<MediaStream>(),
            Bitrate = primarySource?.Bitrate
        };

        return mediaSource;
    }

    /// <summary>
    /// Clean up an active stream session.
    /// </summary>
    public void CloseStream(string streamId)
    {
        if (_activeStreams.TryRemove(streamId, out var stream))
        {
            _logger.LogInformation("Closed stream {StreamId} for channel {ChannelId}",
                streamId, stream.ChannelId);
        }
    }

    /// <summary>
    /// Get the number of currently active streams.
    /// </summary>
    public int ActiveStreamCount => _activeStreams.Count;

    private static string GetContainerFromPath(string path)
    {
        var ext = System.IO.Path.GetExtension(path)?.TrimStart('.').ToLowerInvariant();
        return ext switch
        {
            "mkv" => "matroska",
            "avi" => "avi",
            "mp4" => "mp4",
            "m4v" => "mp4",
            "ts" => "mpegts",
            "wmv" => "asf",
            _ => ext ?? "mp4"
        };
    }

    private class ActiveStream
    {
        public string StreamId { get; set; } = string.Empty;
        public string ChannelId { get; set; } = string.Empty;
        public string ItemId { get; set; } = string.Empty;
        public DateTime StartedAtUtc { get; set; }
    }
}
