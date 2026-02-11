# Jellyfin LiveTV Scheduler Plugin

A Jellyfin plugin that creates virtual live TV channels from your existing media library. Simulates live TV programming with automatic scheduling — no external services, no transcoding overhead, no lag.

## How It Works

Unlike ErsatzTV or Tunarr which run as separate services and re-encode streams, this plugin runs **inside Jellyfin** and points the player directly at your local media files. When a user tunes into a virtual channel:

1. The **ScheduleManager** determines what should be playing right now using a deterministic algorithm
2. The **StreamManager** returns a `MediaSourceInfo` pointing at the local file
3. Jellyfin's native player handles playback with a seek offset to the correct position
4. The result: instant channel switching with zero additional latency

## Features

- **Virtual Channels** — Create unlimited channels with custom names, numbers, and groups
- **Schedule Modes** — Shuffle (randomized but deterministic) or Sequential (ordered playlist)
- **Library Integration** — Pull content from any Jellyfin library or add specific shows/movies
- **Native EPG** — Full TV guide data appears in Jellyfin's built-in Live TV guide
- **Series Support** — Add all episodes of a series to a channel in one click
- **Schedule Preview** — Preview what's playing on any channel for the next 24 hours
- **Zero Dependencies** — No external services, Docker containers, or additional software
- **Lightweight** — Schedule computed on-the-fly, no database or background processes

## Installation

### Manual Install

1. Build the plugin: `dotnet build -c Release`
2. Copy `Jellyfin.Plugin.LiveTV.dll` to your Jellyfin plugins directory:
   - Linux: `/var/lib/jellyfin/plugins/LiveTVScheduler/`
   - Docker: `/config/plugins/LiveTVScheduler/`
   - Windows: `%APPDATA%\jellyfin\plugins\LiveTVScheduler\`
3. Restart Jellyfin

### Building

```bash
dotnet restore
dotnet build -c Release
```

The compiled DLL will be in `Jellyfin.Plugin.LiveTV/bin/Release/net8.0/`.

## Usage

1. After installation, go to **Dashboard > Plugins > LiveTV Scheduler**
2. Click **+ New Channel** to create your first virtual channel
3. Give it a name and channel number
4. Either:
   - Select **library sources** (entire libraries/collections to pull from)
   - **Search and add** specific shows and movies
5. Choose a schedule mode:
   - **Shuffle**: Content plays in a randomized but consistent order
   - **Sequential**: Content plays in the order you added it, looping
6. Save the channel
7. Go to **Live TV > Guide** to see your channels in the program guide
8. Tune in and enjoy!

## Architecture

```
Plugin.cs                    — Entry point, config page registration
ServiceRegistrator.cs        — DI container registration
Configuration/
  PluginConfiguration.cs     — Channel/schedule data model
  configPage.html            — Web UI (embedded resource)
Models/
  VirtualChannel.cs          — Channel definition
  ChannelProgram.cs          — Media item reference
  ScheduleSlot.cs            — Resolved time slot
Services/
  ScheduleManager.cs         — Deterministic schedule generation
  StreamManager.cs           — Media source creation for playback
  LiveTvService.cs           — ILiveTvService implementation
Api/
  LiveTvSchedulerController.cs — REST API for the config UI
```

## API Endpoints

All endpoints require admin authorization.

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/LiveTvScheduler/Channels` | List all channels |
| POST | `/LiveTvScheduler/Channels` | Create a channel |
| PUT | `/LiveTvScheduler/Channels/{id}` | Update a channel |
| DELETE | `/LiveTvScheduler/Channels/{id}` | Delete a channel |
| GET | `/LiveTvScheduler/Channels/{id}/Programs` | Get channel programs |
| POST | `/LiveTvScheduler/Channels/{id}/Programs` | Add a program |
| PUT | `/LiveTvScheduler/Channels/{id}/Programs` | Replace all programs |
| POST | `/LiveTvScheduler/Channels/{id}/AddSeries/{seriesId}` | Add all episodes of a series |
| GET | `/LiveTvScheduler/Channels/{id}/Schedule?hours=24` | Preview schedule |
| GET | `/LiveTvScheduler/Channels/{id}/NowPlaying` | What's on now |
| GET | `/LiveTvScheduler/Libraries` | Available content libraries |
| GET | `/LiveTvScheduler/Search?query=...` | Search media items |

## Requirements

- Jellyfin Server 10.10+
- .NET 8.0 (for building)

## License

GPLv3 (required by Jellyfin plugin SDK licensing)
