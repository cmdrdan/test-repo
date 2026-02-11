using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using Jellyfin.Plugin.LiveTV.Configuration;
using Jellyfin.Plugin.LiveTV.Models;
using Jellyfin.Plugin.LiveTV.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.LiveTV.Api;

/// <summary>
/// API controller for managing virtual channels and schedules.
/// Provides endpoints consumed by the plugin's configuration UI.
/// </summary>
[ApiController]
[Authorize(Policy = "RequiresElevation")]
[Route("LiveTvScheduler")]
[Produces(MediaTypeNames.Application.Json)]
public class LiveTvSchedulerController : ControllerBase
{
    private readonly ILibraryManager _libraryManager;
    private readonly ScheduleManager _scheduleManager;

    public LiveTvSchedulerController(
        ILibraryManager libraryManager,
        ScheduleManager scheduleManager)
    {
        _libraryManager = libraryManager;
        _scheduleManager = scheduleManager;
    }

    private PluginConfiguration Config =>
        Plugin.Instance?.Configuration ?? new PluginConfiguration();

    private void SaveConfig()
    {
        Plugin.Instance?.SaveConfiguration();
    }

    // ── Channel CRUD ──────────────────────────────────────────────

    /// <summary>
    /// Get all channels.
    /// </summary>
    [HttpGet("Channels")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<List<VirtualChannel>> GetChannels()
    {
        return Ok(Config.Channels);
    }

    /// <summary>
    /// Get a single channel by ID.
    /// </summary>
    [HttpGet("Channels/{channelId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<VirtualChannel> GetChannel(string channelId)
    {
        var channel = Config.Channels.FirstOrDefault(c => c.Id == channelId);
        if (channel is null)
        {
            return NotFound();
        }

        return Ok(channel);
    }

    /// <summary>
    /// Create a new channel.
    /// </summary>
    [HttpPost("Channels")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public ActionResult<VirtualChannel> CreateChannel([FromBody] CreateChannelRequest request)
    {
        var channel = new VirtualChannel
        {
            Name = request.Name,
            Number = request.Number ?? (Config.Channels.Count + 1).ToString(),
            Group = request.Group ?? "Virtual",
            Mode = request.Mode,
            Enabled = true,
            ImageUrl = request.ImageUrl,
            LibraryIds = request.LibraryIds ?? new List<string>()
        };

        Config.Channels.Add(channel);
        SaveConfig();

        return Created($"/LiveTvScheduler/Channels/{channel.Id}", channel);
    }

    /// <summary>
    /// Update an existing channel.
    /// </summary>
    [HttpPut("Channels/{channelId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<VirtualChannel> UpdateChannel(
        string channelId,
        [FromBody] UpdateChannelRequest request)
    {
        var channel = Config.Channels.FirstOrDefault(c => c.Id == channelId);
        if (channel is null)
        {
            return NotFound();
        }

        if (request.Name is not null) channel.Name = request.Name;
        if (request.Number is not null) channel.Number = request.Number;
        if (request.Group is not null) channel.Group = request.Group;
        if (request.Mode.HasValue) channel.Mode = request.Mode.Value;
        if (request.Enabled.HasValue) channel.Enabled = request.Enabled.Value;
        if (request.ImageUrl is not null) channel.ImageUrl = request.ImageUrl;
        if (request.LibraryIds is not null) channel.LibraryIds = request.LibraryIds;

        SaveConfig();

        return Ok(channel);
    }

    /// <summary>
    /// Delete a channel.
    /// </summary>
    [HttpDelete("Channels/{channelId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult DeleteChannel(string channelId)
    {
        var channel = Config.Channels.FirstOrDefault(c => c.Id == channelId);
        if (channel is null)
        {
            return NotFound();
        }

        Config.Channels.Remove(channel);
        SaveConfig();

        return NoContent();
    }

    // ── Program Management ────────────────────────────────────────

    /// <summary>
    /// Get programs assigned to a channel.
    /// </summary>
    [HttpGet("Channels/{channelId}/Programs")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<List<ChannelProgram>> GetChannelPrograms(string channelId)
    {
        var channel = Config.Channels.FirstOrDefault(c => c.Id == channelId);
        if (channel is null)
        {
            return NotFound();
        }

        return Ok(channel.Programs);
    }

    /// <summary>
    /// Add a program (media item) to a channel.
    /// </summary>
    [HttpPost("Channels/{channelId}/Programs")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<ChannelProgram> AddProgram(
        string channelId,
        [FromBody] AddProgramRequest request)
    {
        var channel = Config.Channels.FirstOrDefault(c => c.Id == channelId);
        if (channel is null)
        {
            return NotFound();
        }

        if (!Guid.TryParse(request.ItemId, out var itemGuid))
        {
            return BadRequest("Invalid item ID");
        }

        var item = _libraryManager.GetItemById(itemGuid);
        if (item is null)
        {
            return NotFound("Library item not found");
        }

        var program = new ChannelProgram
        {
            ItemId = request.ItemId,
            Name = item.Name,
            RuntimeTicks = item.RunTimeTicks ?? 0
        };

        channel.Programs.Add(program);
        SaveConfig();

        return Ok(program);
    }

    /// <summary>
    /// Replace all programs on a channel (for reordering / bulk update).
    /// </summary>
    [HttpPut("Channels/{channelId}/Programs")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<List<ChannelProgram>> SetPrograms(
        string channelId,
        [FromBody] List<ChannelProgram> programs)
    {
        var channel = Config.Channels.FirstOrDefault(c => c.Id == channelId);
        if (channel is null)
        {
            return NotFound();
        }

        channel.Programs = programs;
        SaveConfig();

        return Ok(channel.Programs);
    }

    /// <summary>
    /// Remove a program from a channel by item ID.
    /// </summary>
    [HttpDelete("Channels/{channelId}/Programs/{itemId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult RemoveProgram(string channelId, string itemId)
    {
        var channel = Config.Channels.FirstOrDefault(c => c.Id == channelId);
        if (channel is null)
        {
            return NotFound();
        }

        var removed = channel.Programs.RemoveAll(p => p.ItemId == itemId);
        if (removed == 0)
        {
            return NotFound("Program not found on channel");
        }

        SaveConfig();
        return NoContent();
    }

    // ── Schedule Preview ──────────────────────────────────────────

    /// <summary>
    /// Preview the generated schedule for a channel.
    /// </summary>
    [HttpGet("Channels/{channelId}/Schedule")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<List<ScheduleSlot>> GetSchedulePreview(
        string channelId,
        [FromQuery] int hours = 24)
    {
        var channel = Config.Channels.FirstOrDefault(c => c.Id == channelId);
        if (channel is null)
        {
            return NotFound();
        }

        var now = DateTime.UtcNow;
        var slots = _scheduleManager.GenerateSchedule(channel, now, now.AddHours(hours));

        return Ok(slots);
    }

    /// <summary>
    /// Get what's currently playing on a channel.
    /// </summary>
    [HttpGet("Channels/{channelId}/NowPlaying")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<ScheduleSlot> GetNowPlaying(string channelId)
    {
        var channel = Config.Channels.FirstOrDefault(c => c.Id == channelId);
        if (channel is null)
        {
            return NotFound();
        }

        var slot = _scheduleManager.GetNowPlaying(channel);
        if (slot is null)
        {
            return NotFound("No content currently playing");
        }

        return Ok(slot);
    }

    // ── Library Browser ───────────────────────────────────────────

    /// <summary>
    /// Get available libraries/collections that can be used as content sources.
    /// </summary>
    [HttpGet("Libraries")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<List<LibraryInfo>> GetLibraries()
    {
        var query = new InternalItemsQuery
        {
            IncludeItemTypes = new[]
            {
                BaseItemKind.CollectionFolder
            },
            Recursive = false
        };

        var folders = _libraryManager.GetItemsResult(query);

        var libraries = folders.Items.Select(f => new LibraryInfo
        {
            Id = f.Id.ToString("N"),
            Name = f.Name,
            Type = f.GetType().Name
        }).ToList();

        return Ok(libraries);
    }

    /// <summary>
    /// Search for media items to add to a channel.
    /// </summary>
    [HttpGet("Search")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<List<MediaItemInfo>> SearchItems(
        [FromQuery] string? query = null,
        [FromQuery] string? parentId = null,
        [FromQuery] int limit = 50)
    {
        var itemQuery = new InternalItemsQuery
        {
            Recursive = true,
            IsVirtualItem = false,
            IncludeItemTypes = new[]
            {
                BaseItemKind.Movie,
                BaseItemKind.Episode,
                BaseItemKind.Series
            },
            Limit = limit,
            OrderBy = new[]
            {
                (ItemSortBy.SortName, SortOrder.Ascending)
            }
        };

        if (!string.IsNullOrWhiteSpace(query))
        {
            itemQuery.SearchTerm = query;
        }

        if (!string.IsNullOrWhiteSpace(parentId) && Guid.TryParse(parentId, out var parentGuid))
        {
            itemQuery.ParentIds = new[] { parentGuid };
        }

        var results = _libraryManager.GetItemsResult(itemQuery);

        var items = results.Items.Select(item => new MediaItemInfo
        {
            Id = item.Id.ToString("N"),
            Name = item.Name,
            Type = item is Movie ? "Movie" : item is Episode ? "Episode" : item is Series ? "Series" : "Unknown",
            RuntimeTicks = item.RunTimeTicks ?? 0,
            RuntimeDisplay = item.RunTimeTicks.HasValue
                ? TimeSpan.FromTicks(item.RunTimeTicks.Value).ToString(@"h\:mm\:ss")
                : "Unknown",
            SeriesName = (item as Episode)?.SeriesName,
            SeasonNumber = (item as Episode)?.ParentIndexNumber,
            EpisodeNumber = (item as Episode)?.IndexNumber,
            ProductionYear = item.ProductionYear,
            ImageUrl = item.HasImage(ImageType.Primary) ? $"/Items/{item.Id}/Images/Primary" : null
        }).ToList();

        return Ok(items);
    }

    /// <summary>
    /// Add all episodes of a series to a channel.
    /// </summary>
    [HttpPost("Channels/{channelId}/AddSeries/{seriesId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<List<ChannelProgram>> AddSeries(string channelId, string seriesId)
    {
        var channel = Config.Channels.FirstOrDefault(c => c.Id == channelId);
        if (channel is null)
        {
            return NotFound();
        }

        if (!Guid.TryParse(seriesId, out var seriesGuid))
        {
            return BadRequest("Invalid series ID");
        }

        var query = new InternalItemsQuery
        {
            AncestorIds = new[] { seriesGuid },
            IncludeItemTypes = new[] { BaseItemKind.Episode },
            Recursive = true,
            IsVirtualItem = false,
            OrderBy = new[]
            {
                (ItemSortBy.ParentIndexNumber, SortOrder.Ascending),
                (ItemSortBy.IndexNumber, SortOrder.Ascending)
            }
        };

        var episodes = _libraryManager.GetItemsResult(query);
        var added = new List<ChannelProgram>();

        foreach (var ep in episodes.Items)
        {
            if (ep.RunTimeTicks is null or <= 0) continue;

            var program = new ChannelProgram
            {
                ItemId = ep.Id.ToString("N"),
                Name = ep.Name,
                RuntimeTicks = ep.RunTimeTicks.Value
            };

            channel.Programs.Add(program);
            added.Add(program);
        }

        SaveConfig();

        return Ok(added);
    }
}

// ── Request/Response DTOs ──────────────────────────────────────

public class CreateChannelRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Number { get; set; }
    public string? Group { get; set; }
    public ScheduleMode Mode { get; set; } = ScheduleMode.Shuffle;
    public string? ImageUrl { get; set; }
    public List<string>? LibraryIds { get; set; }
}

public class UpdateChannelRequest
{
    public string? Name { get; set; }
    public string? Number { get; set; }
    public string? Group { get; set; }
    public ScheduleMode? Mode { get; set; }
    public bool? Enabled { get; set; }
    public string? ImageUrl { get; set; }
    public List<string>? LibraryIds { get; set; }
}

public class AddProgramRequest
{
    public string ItemId { get; set; } = string.Empty;
}

public class LibraryInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
}

public class MediaItemInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public long RuntimeTicks { get; set; }
    public string RuntimeDisplay { get; set; } = string.Empty;
    public string? SeriesName { get; set; }
    public int? SeasonNumber { get; set; }
    public int? EpisodeNumber { get; set; }
    public int? ProductionYear { get; set; }
    public string? ImageUrl { get; set; }
}
