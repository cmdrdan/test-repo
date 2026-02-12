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
/// Clones the actual library item's media source so Jellyfin's playback pipeline
/// (HLS, direct play, transcoding) has complete codec/stream metadata to work with.
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
    /// Creates a MediaSourceInfo for the "media sources available" query.
    /// Sets RequiresOpening = true so Jellyfin opens the stream through the
    /// proper Live TV pipeline before playback begins.
    /// </summary>
    public MediaSourceInfo CreateMediaSourcePreview(string channelId, ScheduleSlot slot)
    {
        return BuildMediaSource(channelId, slot, isPreview: true);
    }

    /// <summary>
    /// Creates a MediaSourceInfo for actual stream playback.
    /// Uses the library item's real media source so all codec/stream metadata
    /// is present for Jellyfin's HLS and transcoding pipeline.
    /// </summary>
    public MediaSourceInfo CreateMediaSource(string channelId, ScheduleSlot slot)
    {
        return BuildMediaSource(channelId, slot, isPreview: false);
    }

    private MediaSourceInfo BuildMediaSource(string channelId, ScheduleSlot slot, bool isPreview)
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

        // Use a stable ID for previews so the client can reference it later in PlaybackInfo.
        // The preview ID must be deterministic — if it changes between
        // GetChannelStreamMediaSources and GetPlaybackInfo, Jellyfin can't match them.
        // For actual opened streams, use a unique ID per session.
        var stableId = $"livetv_{channelId}";
        var streamId = isPreview ? stableId : $"livetv_{channelId}_{DateTime.UtcNow.Ticks}";

        _logger.LogInformation(
            "Building media source for channel {ChannelId}: {Title} at offset {Offset} (preview={Preview}, id={StreamId})",
            channelId, slot.Title, slot.ElapsedTime, isPreview, streamId);

        // Get the actual media source from the library item — this has all the
        // codec info, MediaStreams, container format, etc. that Jellyfin's
        // playback pipeline needs.
        MediaSourceInfo? primarySource = null;
        try
        {
            var existingSources = _mediaSourceManager
                .GetStaticMediaSources(item, false)
                .ToList();

            primarySource = existingSources.FirstOrDefault();

            _logger.LogInformation(
                "Got {Count} static media sources for item {ItemId}, primary container={Container}, streams={StreamCount}",
                existingSources.Count,
                slot.ItemId,
                primarySource?.Container ?? "null",
                primarySource?.MediaStreams?.Count ?? 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get media sources for item {ItemId}", slot.ItemId);
        }

        MediaSourceInfo mediaSource;

        if (primarySource is not null)
        {
            // Use the real media source — this preserves all MediaStreams,
            // codec info, container format, bitrate, etc.
            mediaSource = primarySource;
            mediaSource.Id = streamId;
            mediaSource.IsInfiniteStream = false;
            mediaSource.RunTimeTicks = slot.RuntimeTicks;
            mediaSource.SupportsDirectPlay = true;
            mediaSource.SupportsDirectStream = true;
            mediaSource.SupportsTranscoding = true;
            mediaSource.SupportsProbing = true;
            mediaSource.ReadAtNativeFramerate = false;

            if (isPreview)
            {
                // Preview sources must NOT have LiveStreamId set.
                // If LiveStreamId is set, GetStreamingState tries to look up
                // the live stream in IMediaSourceManager — but since we never
                // called OpenLiveStream, it's not registered, and we get null.
                // Without LiveStreamId, Jellyfin treats it as a regular file source.
                mediaSource.LiveStreamId = null;
                mediaSource.RequiresOpening = false;
                mediaSource.RequiresClosing = false;
            }
            else
            {
                mediaSource.LiveStreamId = streamId;
                mediaSource.RequiresOpening = false;
                mediaSource.RequiresClosing = true;
            }
        }
        else
        {
            // Fallback: build a minimal source from what we know
            _logger.LogWarning("No existing media source found for {ItemId}, building minimal source", slot.ItemId);
            var filePath = item.Path;

            mediaSource = new MediaSourceInfo
            {
                Id = streamId,
                Path = filePath,
                Protocol = MediaProtocol.File,
                Container = GetContainerFromPath(filePath),
                IsInfiniteStream = false,
                IsRemote = false,
                SupportsDirectPlay = true,
                SupportsDirectStream = true,
                SupportsTranscoding = true,
                SupportsProbing = true,
                RequiresOpening = false,
                RequiresClosing = !isPreview,
                LiveStreamId = isPreview ? null : streamId,
                ReadAtNativeFramerate = false,
                RunTimeTicks = slot.RuntimeTicks,
                MediaStreams = new List<MediaStream>(),
                Bitrate = null
            };
        }

        if (!isPreview)
        {
            // Track active stream for cleanup
            _activeStreams[streamId] = new ActiveStream
            {
                StreamId = streamId,
                ChannelId = channelId,
                ItemId = slot.ItemId,
                StartedAtUtc = DateTime.UtcNow
            };
        }

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
