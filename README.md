# AppListener

[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg)](https://www.gnu.org/licenses/gpl-3.0)
[![Build Status](https://img.shields.io/github/actions/workflow/status/Swiftly-Tracker/AppListener/build.yml?branch=main)](https://github.com/Swiftly-Tracker/AppListener/actions)
[![Release](https://img.shields.io/github/v/release/Swiftly-Tracker/AppListener?include_prereleases)](https://github.com/Swiftly-Tracker/AppListener/releases)

Watches Steam apps for build updates and dispatches a GitHub Actions workflow when one lands.

Uses [SteamKit2](https://github.com/SteamRE/SteamKit) to poll Steam's PICS product info for the branches you care about. Instead of keeping its own "last seen build" cache, it reads the target repo's latest commit - the dump tooling in this org writes commit messages as `BuildID - description...`, so that number is the source of truth for what's already been processed. When Steam's build ID for a branch gets ahead of it, AppListener fires a `workflow_dispatch` to bring the repo up to date.

## Install

Grab an archive from the [latest release](https://github.com/Swiftly-Tracker/AppListener/releases/latest):

| Archive                                                                                                                                       | Needs .NET installed? | Use when                  |
| ---------------------------------------------------------------------------------------------------------------------------------------------- | --------------------- | ------------------------- |
| [`AppListener-win-x64.zip`](https://github.com/Swiftly-Tracker/AppListener/releases/latest/download/AppListener-win-x64.zip)                   | No                    | Windows, just run it      |
| [`AppListener-linux-x64.zip`](https://github.com/Swiftly-Tracker/AppListener/releases/latest/download/AppListener-linux-x64.zip)               | No                    | Linux, just run it        |
| [`AppListener-win-x64-portable.zip`](https://github.com/Swiftly-Tracker/AppListener/releases/latest/download/AppListener-win-x64-portable.zip) | .NET 10 runtime       | Windows, smaller download |
| [`AppListener-linux-x64-portable.zip`](https://github.com/Swiftly-Tracker/AppListener/releases/latest/download/AppListener-linux-x64-portable.zip) | .NET 10 runtime   | Linux, smaller download   |

Those links always resolve to the newest stable release. On Linux, `chmod +x AppListener` after unzipping.

## Configuration

Copy [`config.example.toml`](config.example.toml) to `config.toml` next to the binary and fill it in:

```toml
[steam]
# Omit username/password entirely to log in anonymously.
username = "your_steam_username"
password = "your_steam_password"
remember_password = true

[github]
token = "your_default_github_token"

[apps.730]
branch = "public"                            # Steam branch to watch, default "public"
repo = "swiftly-tracker/cs2-dumps"           # repo whose latest commit encodes the last-processed BuildID
dispatch_repo = "swiftly-tracker/cs2-dumps"  # repo to send workflow_dispatch to, defaults to `repo`
workflow_id = "update.yml"
git_ref = "main"                             # branch of `repo` to read commits from / dispatch on
# token = "..."                              # optional per-app override of [github].token
```

One `[apps.<appid>]` table per Steam app to watch. `STEAM_USERNAME`, `STEAM_PASSWORD`, and `GITHUB_TOKEN` environment variables override the config file. Leaving out `[steam]` username/password logs in anonymously, which is enough for public app info.

`config.toml` holds real credentials - it's git-ignored, keep it that way.

## Usage

Run with no arguments to start the watcher daemon:

```bash
./AppListener
```

It logs into Steam, checks every configured app's current build ID against its tracking repo once at startup, then keeps watching Steam's PICS changelist and re-checks whenever a watched app changes. `Ctrl+C` shuts it down cleanly (Steam logs off before the process exits).

Print an app's PICS branches and build IDs without running the daemon:

```bash
./AppListener app_info 730
```

```
Counter-Strike 2 (app 730)
  csgo                 buildid 12426195
  csgo_legacy          buildid 12426195
  public               buildid 24701871
  ...
```

Show usage:

```bash
./AppListener -help
```

## How it works

1. Log into Steam (anonymous or username/password, with Steam Guard prompted on the console if needed).
2. For each configured app, read the branch's `buildid` via SteamKit2's PICS API and compare it against the leading number in the tracking repo's latest commit message.
3. If Steam is ahead, POST a `workflow_dispatch` to `dispatch_repo`'s `workflow_id` on `git_ref`.
4. Poll Steam's PICS changelist on an interval; whenever a watched app shows up in a changelist, repeat step 2 for it.

A dispatch is only fired once per build ID per run - it won't re-dispatch on every poll tick while the tracking repo hasn't caught up yet.

## Architecture

```
AppListener/
├── src/
│   ├── Configuration/    # config.toml model + loader (Tomlyn)
│   ├── Steam/            # SteamKit2 session: login, PICS product info, changelist polling
│   ├── GitHub/           # commit BuildID parsing + workflow_dispatch
│   ├── Watcher/           # BackgroundService tying Steam + GitHub together
│   ├── Commands/          # app_info one-shot command
│   └── Entrypoint.cs      # CLI dispatch / Generic Host bootstrap
└── config.example.toml
```

## Building from source

Requires the **.NET 10 SDK**.

```bash
git clone https://github.com/Swiftly-Tracker/AppListener.git
cd AppListener
dotnet build AppListener.slnx -c Release
```

Run it directly with `dotnet run`, or publish a standalone binary:

```bash
dotnet publish AppListener.csproj -c Release -r linux-x64 --self-contained true \
  -p:PublishSingleFile=true -o out/linux-x64
```

## Community

- **Issues**: [Report bugs and request features](https://github.com/Swiftly-Tracker/AppListener/issues)
- **Security**: [Report privately](https://github.com/Swiftly-Tracker/AppListener/security/advisories/new) - never in a public issue

## License

GPL-3.0. See [LICENSE](LICENSE).

---

<div align="center">
  <strong>Made with ❤️ by the Swiftly Development team</strong>
</div>
