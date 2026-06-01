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

### Option 1: Plugin Repository (recommended)

1. In Jellyfin, go to **Dashboard > Plugins > Repositories**
2. Click **+** and add this repository:
   - Name: `LiveTV Scheduler`
   - URL: `https://raw.githubusercontent.com/cmdrdan/test-repo/master/manifest.json`
3. Go to **Dashboard > Plugins > Catalog**, find **LiveTV Scheduler**, and install
4. Restart Jellyfin

### Option 2: Manual Install

1. Download the latest `livetv-scheduler-<version>.zip` from the [Releases page](https://github.com/cmdrdan/test-repo/releases)
2. Extract it into your Jellyfin plugins directory so the DLL and `meta.json` sit together:
   - Linux: `/var/lib/jellyfin/plugins/LiveTV Scheduler_1.0.0.0/`
   - Docker: `/config/plugins/LiveTV Scheduler_1.0.0.0/`
   - Windows: `%ProgramData%\Jellyfin\Server\plugins\LiveTV Scheduler_1.0.0.0\`
3. Restart Jellyfin

### Option 3: Build from source

```bash
dotnet restore
dotnet build -c Release
pwsh ./package.ps1
```

The packaged zip (DLL + `meta.json`) is written to `./dist/livetv-scheduler-<version>.zip`.
The raw DLL is in `Jellyfin.Plugin.LiveTV/bin/Release/net9.0/`.

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

- Jellyfin Server 10.11.6+
- .NET 9.0 SDK (for building)

## License

GPLv3 (required by Jellyfin plugin SDK licensing)
