using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.LiveTV.Configuration;
using Jellyfin.Plugin.LiveTV.Models;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.LiveTv;
using MediaBrowser.Model.MediaInfo;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LiveTV.Services;

/// <summary>
/// Implements Jellyfin's ILiveTvService to provide virtual channels and EPG data
/// directly into the native Live TV guide. This is what makes channels appear
/// in the TV guide alongside any real tuner channels.
/// </summary>
public class LiveTvService : ILiveTvService
{
    private readonly ScheduleManager _scheduleManager;
    private readonly StreamManager _streamManager;
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<LiveTvService> _logger;

    public LiveTvService(
        ScheduleManager scheduleManager,
        StreamManager streamManager,
        ILibraryManager libraryManager,
        ILogger<LiveTvService> logger)
    {
        _scheduleManager = scheduleManager;
        _streamManager = streamManager;
        _libraryManager = libraryManager;
        _logger = logger;
    }

    public string Name => "LiveTV Scheduler";

    public string HomePageUrl => "https://github.com/cmdrdan/test-repo";

    private PluginConfiguration Config =>
        Plugin.Instance?.Configuration ?? new PluginConfiguration();

    /// <summary>
    /// Returns all enabled virtual channels to Jellyfin's Live TV system.
    /// </summary>
    public Task<IEnumerable<ChannelInfo>> GetChannelsAsync(CancellationToken cancellationToken)
    {
        var channels = Config.Channels
            .Where(c => c.Enabled)
            .Select(c => new ChannelInfo
            {
                Id = c.Id,
                Name = c.Name,
                Number = c.Number,
                ChannelType = ChannelType.TV,
                ChannelGroup = c.Group,
                ImageUrl = c.ImageUrl,
                HasImage = !string.IsNullOrEmpty(c.ImageUrl),
                IsHD = true
            });

        var list = channels.ToList();
        _logger.LogInformation("GetChannelsAsync: returning {Count} enabled channels", list.Count);
        return Task.FromResult<IEnumerable<ChannelInfo>>(list);
    }

    /// <summary>
    /// Generates EPG/guide program data for a channel over the requested time window.
    /// </summary>
    public Task<IEnumerable<ProgramInfo>> GetProgramsAsync(
        string channelId,
        DateTime startDateUtc,
        DateTime endDateUtc,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "GetProgramsAsync called: channelId={ChannelId}, start={Start}, end={End}",
            channelId, startDateUtc, endDateUtc);

        var channel = Config.Channels.FirstOrDefault(c => c.Id == channelId);
        if (channel is null)
        {
            _logger.LogWarning("GetProgramsAsync: channel {ChannelId} not found in config", channelId);
            return Task.FromResult(Enumerable.Empty<ProgramInfo>());
        }

        _logger.LogInformation(
            "GetProgramsAsync: found channel '{Name}' with {ProgCount} programs, {LibCount} libraryIds",
            channel.Name, channel.Programs.Count, channel.LibraryIds.Count);

        var slots = _scheduleManager.GenerateSchedule(channel, startDateUtc, endDateUtc);

        _logger.LogInformation(
            "GetProgramsAsync: generated {SlotCount} schedule slots for channel '{Name}'",
            slots.Count, channel.Name);

        var programs = slots.Select(slot => new ProgramInfo
        {
            Id = $"{channelId}_{slot.StartTimeUtc:yyyyMMddHHmmss}",
            ChannelId = channelId,
            Name = slot.Title,
            Overview = slot.Overview,
            StartDate = slot.StartTimeUtc,
            EndDate = slot.EndTimeUtc,
            EpisodeTitle = slot.EpisodeTitle,
            SeasonNumber = slot.SeasonNumber,
            EpisodeNumber = slot.EpisodeNumber,
            ProductionYear = slot.ProductionYear,
            IsMovie = slot.IsMovie,
            IsSeries = slot.IsSeries,
            IsRepeat = true, // All virtual content is technically a repeat
            ImageUrl = slot.ImageUrl,
            Genres = new List<string>()
        });

        return Task.FromResult(programs);
    }

    /// <summary>
    /// Opens a live stream for a channel. This is called when a user tunes in.
    /// Returns a MediaSourceInfo pointing directly at the local media file
    /// with the correct start position (seek offset), avoiding any transcoding overhead.
    /// </summary>
    public Task<MediaSourceInfo> GetChannelStream(
        string channelId,
        string streamId,
        CancellationToken cancellationToken)
    {
        var channel = Config.Channels.FirstOrDefault(c => c.Id == channelId);
        if (channel is null)
        {
            throw new ArgumentException($"Channel {channelId} not found");
        }

        var nowPlaying = _scheduleManager.GetNowPlaying(channel);
        if (nowPlaying is null)
        {
            throw new InvalidOperationException($"No content available for channel {channel.Name}");
        }

        var mediaSource = _streamManager.CreateMediaSource(channelId, nowPlaying);
        return Task.FromResult(mediaSource);
    }

    /// <summary>
    /// Returns available media sources for a channel.
    /// </summary>
    public Task<List<MediaSourceInfo>> GetChannelStreamMediaSources(
        string channelId,
        CancellationToken cancellationToken)
    {
        var channel = Config.Channels.FirstOrDefault(c => c.Id == channelId);
        if (channel is null)
        {
            return Task.FromResult(new List<MediaSourceInfo>());
        }

        var nowPlaying = _scheduleManager.GetNowPlaying(channel);
        if (nowPlaying is null)
        {
            return Task.FromResult(new List<MediaSourceInfo>());
        }

        var mediaSource = _streamManager.CreateMediaSource(channelId, nowPlaying);
        return Task.FromResult(new List<MediaSourceInfo> { mediaSource });
    }

    public Task CloseLiveStream(string id, CancellationToken cancellationToken)
    {
        _streamManager.CloseStream(id);
        return Task.CompletedTask;
    }

    public Task ResetTuner(string id, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    // Recording/timer features are not applicable for virtual channels.
    // Return empty results for all timer-related methods.

    public Task<IEnumerable<TimerInfo>> GetTimersAsync(CancellationToken cancellationToken)
        => Task.FromResult(Enumerable.Empty<TimerInfo>());

    public Task CreateTimerAsync(TimerInfo info, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task CancelTimerAsync(string timerId, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task UpdateTimerAsync(TimerInfo updatedTimer, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task<IEnumerable<SeriesTimerInfo>> GetSeriesTimersAsync(CancellationToken cancellationToken)
        => Task.FromResult(Enumerable.Empty<SeriesTimerInfo>());

    public Task<SeriesTimerInfo> GetNewTimerDefaultsAsync(
        CancellationToken cancellationToken,
        ProgramInfo? program = null)
        => Task.FromResult(new SeriesTimerInfo());

    public Task CreateSeriesTimerAsync(SeriesTimerInfo info, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task CancelSeriesTimerAsync(string timerId, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task UpdateSeriesTimerAsync(SeriesTimerInfo info, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
