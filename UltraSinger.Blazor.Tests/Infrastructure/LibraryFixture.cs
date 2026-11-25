namespace UltraSinger.Blazor.Tests.Infrastructure;

/// <summary>A throwaway UltraStar library on disk.</summary>
public sealed class LibraryFixture : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ultrasinger ui tests {Guid.NewGuid():N}");

    public LibraryFixture() => Directory.CreateDirectory(Path);

    /// <summary>Writes "&lt;Artist&gt; - &lt;Title&gt;/&lt;Artist&gt; - &lt;Title&gt;.txt", plus any media files named.</summary>
    public string AddSong(string artist, string title, string? extraHeaders = null, params string[] mediaFiles)
    {
        var name = $"{artist} - {title}";
        var directory = Directory.CreateDirectory(System.IO.Path.Combine(Path, name)).FullName;
        var txt = System.IO.Path.Combine(directory, name + ".txt");

        File.WriteAllText(txt, $"#ARTIST:{artist}\n#TITLE:{title}\n{extraHeaders}: 0 4 5 Hi\nE\n");

        foreach (var media in mediaFiles)
        {
            File.WriteAllText(System.IO.Path.Combine(directory, media), "x");
        }

        return txt;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
