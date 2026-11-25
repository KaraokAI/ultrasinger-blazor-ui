using UltraSinger.Contracts;
using UltraSinger.Processor.Entities;

namespace UltraSinger.Processor.Tests;

public class SongRecordTests
{
    private static SongRecord NewRecord() => new() { Id = Guid.NewGuid(), Url = "https://youtu.be/abc" };

    [Fact]
    public void ReadLogFrom_ReturnsOnlyNewTextAndTheNewOffset()
    {
        var record = NewRecord();
        record.AppendLog("first");

        var first = record.ReadLogFrom(0);
        record.AppendLog("second");
        var second = record.ReadLogFrom(first.Offset);

        Assert.Equal("first" + Environment.NewLine, first.Text);
        Assert.Equal("second" + Environment.NewLine, second.Text);
        Assert.Equal(first.Offset + second.Text.Length, second.Offset);
    }

    [Fact]
    public void ReadLogFrom_CurrentOffsetYieldsNothing()
    {
        var record = NewRecord();
        record.AppendLog("line");
        var offset = record.ReadLogFrom(0).Offset;

        Assert.Equal(string.Empty, record.ReadLogFrom(offset).Text);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10_000)]
    public void ReadLogFrom_OutOfRangeOffsetResendsTheWholeLog(int offset)
    {
        var record = NewRecord();
        record.AppendLog("line");

        var chunk = record.ReadLogFrom(offset);

        Assert.Equal("line" + Environment.NewLine, chunk.Text);
    }

    [Fact]
    public void Errors_AreKeptOutOfTheVisibleLog()
    {
        var record = NewRecord();
        record.AppendError("boom");

        Assert.Equal(string.Empty, record.ReadLogFrom(0).Text);
        Assert.Contains("boom", record.ErrorText);
    }

    [Fact]
    public void Mutations_MarkDirty_AndSnapshotCleans()
    {
        var record = NewRecord();
        record.TakeSnapshotAndClean();
        Assert.False(record.IsDirty);

        record.State = SongState.IN_PROGRESS;
        Assert.True(record.IsDirty);

        record.TakeSnapshotAndClean();
        record.AppendLog("x");
        Assert.True(record.IsDirty);
    }

    [Fact]
    public void Snapshot_RoundTripsEverythingAndComesBackClean()
    {
        var record = NewRecord();
        record.Title = "Artist - Title";
        record.JobId = "17";
        record.State = SongState.COMPLETED;
        record.BeganProcessingAt = new DateTime(2026, 1, 1, 10, 0, 0);
        record.CompletedAt = new DateTime(2026, 1, 1, 10, 5, 0);
        record.UltraStarTxtPath = "Artist - Title/Artist - Title.txt";
        record.BundlePath = "/bundles/x.zip";
        record.FetchedAt = new DateTime(2026, 1, 1, 11, 0, 0);
        record.Source = SongSource.USDB;
        record.UsdbSongId = 123;
        record.UltraStarTxt = "#TITLE:Title";
        record.AppendLog("log line");
        record.AppendError("error line");

        var restored = SongRecord.FromSnapshot(record.TakeSnapshotAndClean());

        Assert.False(restored.IsDirty);
        Assert.Equivalent(record.ToDto(), restored.ToDto());
        Assert.Equal(record.JobId, restored.JobId);
        Assert.Equal(record.BundlePath, restored.BundlePath);
        Assert.Equal(record.UltraStarTxt, restored.UltraStarTxt);
        Assert.Equal(record.ReadLogFrom(0).Text, restored.ReadLogFrom(0).Text);
        Assert.Equal(record.ErrorText, restored.ErrorText);
    }

    [Fact]
    public void AppendLog_IsSafeFromConcurrentReaderThreads()
    {
        var record = NewRecord();

        Parallel.For(0, 1000, i => record.AppendLog($"line {i}"));

        var lines = record.ReadLogFrom(0).Text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(1000, lines.Length);
    }
}
