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
using Jellyfin.Data.Enums;
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
    public MediaSourceInfo CreateMediaSourcePreview(VirtualChannel channel, ScheduleSlot slot)
    {
        return BuildMediaSource(channel, slot, isPreview: true);
    }

    /// <summary>
    /// Creates a MediaSourceInfo for actual stream playback.
    /// Uses the library item's real media source so all codec/stream metadata
    /// is present for Jellyfin's HLS and transcoding pipeline.
    /// </summary>
    public MediaSourceInfo CreateMediaSource(VirtualChannel channel, ScheduleSlot slot)
    {
        return BuildMediaSource(channel, slot, isPreview: false);
    }

    private MediaSourceInfo BuildMediaSource(VirtualChannel channel, ScheduleSlot slot, bool isPreview)
    {
        var channelId = channel.Id;
        if (!Guid.TryParse(slot.ItemId, out var itemGuid))
        {
            throw new ArgumentException($"Invalid item ID: {slot.ItemId}");
        }

        var item = _libraryManager.GetItemById(itemGuid);
        if (item is null)
        {
            throw new InvalidOperationException($"Library item {slot.ItemId} not found");
        }

        // MediaSourceInfo.Id MUST be a valid GUID — Jellyfin's DynamicHlsHelper
        // calls Guid.Parse() on it. Use the channelId (already a GUID in "N" format)
        // for stable preview IDs, and a fresh GUID for opened streams.
        var streamId = isPreview ? channelId : Guid.NewGuid().ToString("N");

        _logger.LogInformation(
            "Building media source for channel {ChannelId} ({Mode}): {Title} at offset {Offset} (preview={Preview}, id={StreamId})",
            channelId, channel.StreamMode, slot.Title, slot.ElapsedTime, isPreview, streamId);

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

        // In Live mode, the viewer starts at the elapsed offset within the file
        // (mirroring real broadcast TV) and the stream's effective runtime is
        // only the remaining ticks. In OnDemand mode the file plays from its
        // beginning so the reported runtime is the full duration.
        var isLive = channel.StreamMode == StreamMode.Live;
        var startTimeTicks = isLive ? slot.ElapsedTime.Ticks : 0L;
        var reportedRuntimeTicks = isLive
            ? Math.Max(slot.RuntimeTicks - startTimeTicks, TimeSpan.FromSeconds(1).Ticks)
            : slot.RuntimeTicks;

        if (primarySource is not null)
        {
            // Use the real media source — this preserves all MediaStreams,
            // codec info, container format, bitrate, etc.
            mediaSource = primarySource;
            mediaSource.Id = streamId;
            mediaSource.IsInfiniteStream = false;
            mediaSource.RunTimeTicks = reportedRuntimeTicks;
            mediaSource.SupportsProbing = true;
            mediaSource.ReadAtNativeFramerate = false;

            if (isLive)
            {
                // Force HLS transcoding so Jellyfin's pipeline bakes the
                // StartTimeTicks offset into the served segments. Without
                // transcoding there is no MediaSourceInfo field that
                // reliably tells a client "open this file at offset X".
                ApplyLiveTranscodingHints(mediaSource, item.Id, streamId, startTimeTicks);
            }
            else
            {
                mediaSource.SupportsDirectPlay = true;
                mediaSource.SupportsDirectStream = true;
                mediaSource.SupportsTranscoding = true;
            }

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
                SupportsDirectPlay = !isLive,
                SupportsDirectStream = !isLive,
                SupportsTranscoding = true,
                SupportsProbing = true,
                RequiresOpening = false,
                RequiresClosing = !isPreview,
                LiveStreamId = isPreview ? null : streamId,
                ReadAtNativeFramerate = false,
                RunTimeTicks = reportedRuntimeTicks,
                MediaStreams = new List<MediaStream>(),
                Bitrate = null
            };

            if (isLive)
            {
                ApplyLiveTranscodingHints(mediaSource, item.Id, streamId, startTimeTicks);
            }
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

    /// <summary>
    /// Configures the MediaSourceInfo to route through Jellyfin's HLS
    /// transcoding endpoint with StartTimeTicks baked in. This is the only
    /// reliable way to bake a server-side seek into the stream for a Live
    /// virtual channel — direct-play has no MediaSourceInfo field that
    /// communicates a fixed start offset for the player to honor on open.
    /// </summary>
    private static void ApplyLiveTranscodingHints(
        MediaSourceInfo source,
        Guid itemId,
        string streamId,
        long startTimeTicks)
    {
        source.SupportsDirectPlay = false;
        source.SupportsDirectStream = false;
        source.SupportsTranscoding = true;

        var query =
            $"MediaSourceId={source.Id}" +
            $"&StartTimeTicks={startTimeTicks}" +
            $"&PlaySessionId={streamId}" +
            $"&LiveStreamId={streamId}" +
            "&VideoCodec=h264" +
            "&AudioCodec=aac" +
            "&TranscodingProtocol=hls" +
            "&TranscodingContainer=ts" +
            "&SegmentContainer=ts" +
            "&BreakOnNonKeyFrames=True";

        source.TranscodingUrl = $"/Videos/{itemId:N}/master.m3u8?{query}";
        source.TranscodingSubProtocol = MediaStreamProtocol.hls;
        source.TranscodingContainer = "ts";
    }

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
