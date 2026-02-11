using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.LiveTV.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LiveTV.Services;

/// <summary>
/// Generates deterministic schedules for virtual channels.
/// The schedule is computed on-the-fly from the channel config and media library
/// rather than stored — making it lightweight and always consistent.
/// </summary>
public class ScheduleManager
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<ScheduleManager> _logger;

    public ScheduleManager(ILibraryManager libraryManager, ILogger<ScheduleManager> logger)
    {
        _libraryManager = libraryManager;
        _logger = logger;
    }

    /// <summary>
    /// Get the list of media items available for a channel.
    /// </summary>
    public List<ChannelProgram> GetProgramsForChannel(VirtualChannel channel)
    {
        if (channel.Programs.Count > 0)
        {
            return channel.Programs.Where(p => p.RuntimeTicks > 0).ToList();
        }

        // Pull items from the specified library IDs
        var programs = new List<ChannelProgram>();

        foreach (var libraryId in channel.LibraryIds)
        {
            if (!Guid.TryParse(libraryId, out var parentGuid))
            {
                continue;
            }

            var query = new InternalItemsQuery
            {
                ParentId = parentGuid,
                Recursive = true,
                IsVirtualItem = false,
                IncludeItemTypes = new[]
                {
                    BaseItemKind.Movie,
                    BaseItemKind.Episode
                },
                OrderBy = new[]
                {
                    (ItemSortBy.SortName, SortOrder.Ascending)
                }
            };

            var items = _libraryManager.GetItemsResult(query);

            foreach (var item in items.Items)
            {
                if (item.RunTimeTicks is null or <= 0)
                {
                    continue;
                }

                programs.Add(new ChannelProgram
                {
                    ItemId = item.Id.ToString("N"),
                    Name = item.Name,
                    RuntimeTicks = item.RunTimeTicks.Value
                });
            }
        }

        return programs;
    }

    /// <summary>
    /// Generate schedule slots for a channel covering the given time range.
    /// The algorithm is deterministic: given the same programs and time range,
    /// it always produces the same schedule.
    /// </summary>
    public List<ScheduleSlot> GenerateSchedule(
        VirtualChannel channel,
        DateTime startUtc,
        DateTime endUtc)
    {
        var programs = GetProgramsForChannel(channel);
        if (programs.Count == 0)
        {
            _logger.LogWarning("Channel {ChannelName} has no programs available", channel.Name);
            return new List<ScheduleSlot>();
        }

        var slots = new List<ScheduleSlot>();
        var anchorUtc = new DateTime(channel.StartDateTicks, DateTimeKind.Utc);

        // Calculate total cycle length (all programs played once)
        long totalCycleTicks = programs.Sum(p => p.RuntimeTicks);
        if (totalCycleTicks <= 0)
        {
            return slots;
        }

        // Find where 'startUtc' falls within the cycle
        long ticksFromAnchor = (startUtc - anchorUtc).Ticks;

        // Handle times before the anchor by shifting forward
        if (ticksFromAnchor < 0)
        {
            long cyclesBack = (-ticksFromAnchor / totalCycleTicks) + 1;
            ticksFromAnchor += cyclesBack * totalCycleTicks;
        }

        // Determine ordering based on schedule mode
        var orderedPrograms = channel.Mode switch
        {
            ScheduleMode.Shuffle => ShuffleDeterministic(programs, channel.Id),
            ScheduleMode.Sequential => programs,
            _ => programs
        };

        long cycleCount = orderedPrograms.Count;

        // Find the current position within the cycle
        long positionInCycle = ticksFromAnchor % totalCycleTicks;

        // Walk through programs to find where we are
        long accumulated = 0;
        int startIndex = 0;
        long startOffset = 0;

        for (int i = 0; i < orderedPrograms.Count; i++)
        {
            long progTicks = orderedPrograms[i].RuntimeTicks;
            if (accumulated + progTicks > positionInCycle)
            {
                startIndex = i;
                startOffset = positionInCycle - accumulated;
                break;
            }

            accumulated += progTicks;
        }

        // Now generate slots from startIndex forward until we pass endUtc
        var currentTime = startUtc - TimeSpan.FromTicks(startOffset);
        int idx = startIndex;

        while (currentTime < endUtc)
        {
            // Wrap index for the current cycle
            int wrappedIdx = idx % orderedPrograms.Count;

            // For shuffle mode, re-shuffle when starting a new cycle
            if (channel.Mode == ScheduleMode.Shuffle && wrappedIdx == 0 && idx > startIndex)
            {
                long cycleNumber = (ticksFromAnchor + (currentTime - startUtc + TimeSpan.FromTicks(startOffset)).Ticks) / totalCycleTicks;
                orderedPrograms = ShuffleDeterministic(programs, channel.Id + cycleNumber);
            }

            var program = orderedPrograms[wrappedIdx];
            var slotStart = currentTime;
            var slotEnd = currentTime + TimeSpan.FromTicks(program.RuntimeTicks);

            // Only include slots that overlap the requested window
            if (slotEnd > startUtc)
            {
                var slot = new ScheduleSlot
                {
                    ItemId = program.ItemId,
                    Title = program.Name,
                    StartTimeUtc = slotStart,
                    EndTimeUtc = slotEnd,
                    RuntimeTicks = program.RuntimeTicks
                };

                // Enrich with library metadata
                EnrichSlotMetadata(slot);

                slots.Add(slot);
            }

            currentTime = slotEnd;
            idx++;
        }

        return slots;
    }

    /// <summary>
    /// Get what's currently playing on a channel right now, including seek offset.
    /// </summary>
    public ScheduleSlot? GetNowPlaying(VirtualChannel channel)
    {
        var now = DateTime.UtcNow;
        var slots = GenerateSchedule(channel, now, now.AddMinutes(1));

        if (slots.Count == 0)
        {
            return null;
        }

        var current = slots[0];
        current.ElapsedTime = now - current.StartTimeUtc;
        return current;
    }

    /// <summary>
    /// Deterministic shuffle using a seed derived from the channel ID.
    /// Same seed always produces the same ordering.
    /// </summary>
    private static List<ChannelProgram> ShuffleDeterministic(
        List<ChannelProgram> programs,
        string seed)
    {
        var shuffled = new List<ChannelProgram>(programs);
        var rng = new Random(seed.GetHashCode());

        for (int i = shuffled.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
        }

        return shuffled;
    }

    /// <summary>
    /// Look up the actual Jellyfin library item to fill in EPG metadata.
    /// </summary>
    private void EnrichSlotMetadata(ScheduleSlot slot)
    {
        if (!Guid.TryParse(slot.ItemId, out var itemGuid))
        {
            return;
        }

        var item = _libraryManager.GetItemById(itemGuid);
        if (item is null)
        {
            return;
        }

        slot.Overview = item.Overview;
        slot.ProductionYear = item.ProductionYear;

        if (item is Movie)
        {
            slot.IsMovie = true;
        }
        else if (item is Episode episode)
        {
            slot.IsSeries = true;
            slot.EpisodeTitle = episode.Name;
            slot.SeasonNumber = episode.ParentIndexNumber;
            slot.EpisodeNumber = episode.IndexNumber;

            // Use series name as the title for EPG consistency
            if (episode.Series is not null)
            {
                slot.Title = episode.Series.Name;
            }
        }

        // Get primary image for EPG
        if (item.HasImage(ImageType.Primary))
        {
            slot.ImageUrl = $"/Items/{item.Id}/Images/Primary";
        }
    }
}
