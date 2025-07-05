# UltraSinger Blazor UI

Integrates with UltraSinger to provide a basic UI to add YouTube videos which will get turned into UltraStar Deluxe songs.

- When you add links, they're added to a queue that are processed sequentially - no jumping the queue!
- Looks up videos on YouTube
- Shows the live output of the UltraSinger process for progress (and nerds)

TODO:
- [x] Integrate with the YouTube API for querying, instead of having to require people to copy YouTube links into the app.
- [ ] Upload PRs of generated songs to the GitHub song repo as part of this org, and perform lookups first.
- [ ] Documentation for setup, including remote setups (remote setups would require a means of downloading the new files onto your local machine).
- [ ] Unit tests
