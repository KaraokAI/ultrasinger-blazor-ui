# UltraSinger Blazor UI

Integrates with UltraSinger to provide a basic UI to add YouTube videos which will get turned into UltraStar Deluxe songs.

- When you add links, they're added to a queue that are processed sequentially - no jumping the queue!
- Looks up videos on YouTube
- Shows the live output of the UltraSinger process for progress (and nerds)

## Architecture

The app is split into two deployables so the browser-facing UI doesn't have to live on the
machine with Python, WSL, yt-dlp and the GPU:

| Project | What it is | Where it runs |
| --- | --- | --- |
| `UltraSinger.Processor` | ASP.NET Core Web API + Hangfire. Runs UltraSinger, yt-dlp, lyric lookup and the OpenAI pass. Owns all job state. | The machine with Python/WSL/GPU and the UltraStar library |
| `UltraSingerUI` | Blazor Server. Talks to the processor over HTTP; holds no job state of its own. | Anywhere |
| `UltraSinger.Contracts` | DTOs shared by both. | — |

The rule for what goes where: anything touching local machine resources or job state lives
on the processor. The YouTube Data API search is a plain outbound HTTPS call, so it stays in
the UI.

### Processor API

| Endpoint | Purpose |
| --- | --- |
| `POST /api/songs` | Queue a song. Resolves the title via yt-dlp if you don't supply one. `409` if already queued/processing/done. |
| `GET /api/songs` | All songs, newest first, with state. |
| `GET /api/songs/{id}` | One song. |
| `GET /api/songs/{id}/log?sinceOffset=N` | Incremental log tail — pass back the `offset` you last received. |
| `GET /api/songs/{id}/bundle` | Downloads the finished song as a zip. `404` until it's ready. |
| `POST /api/songs/{id}/bundle/ack` | Marks a bundle as fetched. Idempotent. |
| `GET /api/activity?sinceOffset=N` | Current song + new log output in one call. What the output pane polls. |
| `GET /api/health` | Config sanity check; drives the UI's warning banner. |

Hangfire's dashboard is on the processor at `/hangfire`.

### Persistence

The processor keeps both the song queue and Hangfire's job store in SQLite (`songs.db` and
`hangfire.db`, under `DatabasePath`), so a restart doesn't wipe history or a running queue.

Song records are cached in memory and flushed to disk on a 1-second debounce rather than on
every log line, since a live UltraSinger run produces one `AppendLog` call per output line.
At most a second of the freshest log text is at risk on a hard kill. On startup, any song
still `IN_PROGRESS` is marked `FAILED` with an explanatory log line — that state can only
belong to a process that no longer exists.

### Lyric post-processing

When `EnableOpenAICorrections` is on, the processor shells out to the `syncedlyrics` Python
CLI (`SyncedLyricsPath`) after a song finishes, using the resolved video title as the search
term. Its output is conditioned (LRC ID tags, word-level timing tags, instrumental markers and
duplicate lines stripped) before being handed to OpenAI alongside the generated UltraStar
file.

When the lyrics come back time-synced (`[mm:ss.xx]` per line), the model is told to use those
timestamps only to line up and verify lyrics against the UltraStar file — never to recompute
`StartBeat`/`Length` from them. The existing beat numbers, derived from the audio
transcription, stay authoritative.

### Delivering finished songs

The processor runs each job's UltraSinger output into its own subdirectory (so it always
knows exactly what's new for that job), zips it with no compression once the job finishes,
and deletes the raw directory — the zip under `BundleStoragePath` becomes the artifact of
record. Bundling failures fail the job, since a completed song with no bundle isn't actually
a completed song.

If the UI has a `Library:LocalPath` configured, a background service polls the processor for
songs it hasn't fetched yet, downloads each bundle, extracts it straight into that path (the
zip already contains the same Artist/Title folder layout UltraSinger produces), and acks it.
Extraction always overwrites, so a dropped ack or an interrupted extract just gets retried on
the next poll rather than getting stuck. Leaving `Library:LocalPath` unset makes this a no-op
— a pure viewer instance doesn't need it.

## Configuration

Both projects share a `UserSecretsId`, so a single `secrets.json` serves both.

**Processor** — `ProcessorOptions` section:

| Key | Notes |
| --- | --- |
| `PythonExecutable`, `PythonArguments` | How to invoke Python (e.g. `wsl` plus args) |
| `UltraSingerPath` | Repo root; `src/UltraSinger.py` is appended |
| `UltraStarDeluxeWSLPath` | Output path as UltraSinger sees it |
| `UltraStarDeluxeLocalLibraryPath` | The same directory as the processor's OS sees it |
| `KaraokeLanguage` | Defaults to `en` |
| `UltraSingerAdditionalArgs` | Appended verbatim, e.g. `--demucs htdemucs_ft` |
| `UltraSingerAdditionalEnvVars` | `MYVAR=value;OTHER=value`. Values containing `;` aren't supported. |
| `YTDLPPath` | Path to the yt-dlp binary |
| `DatabasePath` | Directory for `songs.db` / `hangfire.db`. Defaults to the content root. |
| `BundleStoragePath` | Directory for finished song zips. Defaults to a `bundles` subdirectory of `DatabasePath`. |
| `OpenAIKey`, `OpenAIModel`, `EnableOpenAICorrections`, `OverwriteUltraStarFile` | Post-processing pass |
| `OpenAIAdditionalInstructions` | Appended verbatim to the OpenAI system prompt |
| `SendTimestampsToOpenAI` | Keep `[mm:ss.xx]` tags in the reference lyrics sent to the model (default `true`) |
| `SyncedLyricsPath` | Path to the `syncedlyrics` CLI executable |
| `SyncedLyricsMode` | `PreferSynced` (default), `SyncedOnly`, or `PlainOnly` |
| `SyncedLyricsProviders` | Space-separated provider names, e.g. `lrclib musixmatch`. Empty means all. |
| `SyncedLyricsLanguage` | Maps to the CLI's `-l` |
| `SyncedLyricsEnhanced` | Word-level timing (`--enhanced`) |
| `SyncedLyricsTimeoutSeconds` | Kill a hung lookup after this many seconds (default `60`) |

Plus `Processor:ApiKey` — a shared secret required in an `X-Api-Key` header. **If it isn't
set the API is unauthenticated**, which is fine locally but don't expose the processor to an
untrusted network that way.

**UI** — `Processor:BaseUrl` (e.g. `http://karaoke-box:5209`), `Processor:ApiKey` (must match
the processor's), and `YT_API_KEY` / `YT_API_KEY_BACKUP` for search.

`Library` section: `LocalPath` — where finished songs get extracted on this machine. Unset
means auto-fetch is disabled. `PollIntervalSeconds` — how often to check for unfetched
completed songs (default `15`).

## Running

Start the processor on the machine that does the work:

```bash
dotnet run --project UltraSinger.Processor        # http://*:5209, https://*:7337
```

Check `GET /api/health` reports no problems, then start the UI anywhere:

```bash
dotnet run --project UltraSingerUI                # http://*:5109, https://*:7237
```

If the UI can't reach the processor, or the processor is misconfigured, a banner says so
rather than the page just sitting there looking idle.

TODO:
- [x] Integrate with the YouTube API for querying, instead of having to require people to copy YouTube links into the app.
- [ ] Upload PRs of generated songs to the GitHub song repo as part of this org, and perform lookups first.
- [x] Documentation for setup, including remote setups (remote setups would require a means of downloading the new files onto your local machine).
  - The processor bundles each finished song into a zip and the UI pulls it down and
    extracts it automatically when `Library:LocalPath` is configured — see "Delivering
    finished songs" above.
- [ ] Unit tests
