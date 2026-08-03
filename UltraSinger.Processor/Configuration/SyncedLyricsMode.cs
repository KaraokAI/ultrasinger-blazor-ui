namespace UltraSinger.Processor.Configuration;

public enum SyncedLyricsMode
{
    /// <summary>The CLI's own default: take synced lyrics if available, otherwise plain.</summary>
    PreferSynced,

    /// <summary>Pass <c>--synced-only</c>. Nothing is returned if only plain lyrics exist.</summary>
    SyncedOnly,

    /// <summary>Pass <c>--plain-only</c>. Untimed lyrics only.</summary>
    PlainOnly
}
