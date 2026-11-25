namespace UltraSinger.Processor.Tests.Infrastructure;

/// <summary>
/// A scratch directory deleted on dispose. The path deliberately contains a space — the
/// real library lives under "Storage 2", and spaces in paths are exactly what the old
/// shell-escaping got wrong.
/// </summary>
public sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ultrasinger tests {Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    public string CreateDirectory(params string[] parts)
    {
        var path = Combine(parts);
        Directory.CreateDirectory(path);
        return path;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: SQLite may still hold a handle briefly.
        }
    }
}
