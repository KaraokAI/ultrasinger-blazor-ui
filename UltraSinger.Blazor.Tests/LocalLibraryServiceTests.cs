using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using UltraSinger.Blazor.Configuration;
using UltraSinger.Blazor.Services;
using UltraSinger.Blazor.Tests.Infrastructure;

namespace UltraSinger.Blazor.Tests;

public sealed class LocalLibraryServiceTests : IDisposable
{
    private readonly LibraryFixture _library = new();

    public void Dispose() => _library.Dispose();

    private LocalLibraryService CreateService(string? path = null) =>
        new(Options.Create(new LibraryConfiguration { LocalPath = path ?? _library.Path }),
            NullLogger<LocalLibraryService>.Instance);

    [Fact]
    public void ParseUltraStarTxtFile_ReadsHeadersAndChecksMediaExists()
    {
        var txt = _library.AddSong("Queen", "Bohemian Rhapsody",
            "#MP3:song.mp3\n#VIDEO:missing.mp4\n#COVER:cover.jpg\n#YEAR:1975\n#GENRE:Rock\n#LANGUAGE:English\n#EDITION:SingStar\n",
            "song.mp3", "cover.jpg");

        var song = LocalLibraryService.ParseUltraStarTxtFile(txt)!;

        Assert.Equal("Queen", song.Artist);
        Assert.Equal("Bohemian Rhapsody", song.Title);
        Assert.Equal("1975", song.Year);
        Assert.Equal("Rock", song.Genre);
        Assert.Equal("English", song.Language);
        Assert.Equal("SingStar", song.Edition);
        Assert.True(song.HasMp3);
        Assert.False(song.HasVideo);
        Assert.True(song.HasCover);
        Assert.Equal(txt, song.TxtFilePath);
    }

    [Fact]
    public void ParseUltraStarTxtFile_IgnoresNonUltraStarText()
    {
        var path = Path.Combine(_library.Path, "readme.txt");
        File.WriteAllText(path, "Just some notes\n#not a header");

        Assert.Null(LocalLibraryService.ParseUltraStarTxtFile(path));
    }

    [Fact]
    public void Search_MatchesEveryTermAcrossFieldsAndSortsByArtist()
    {
        _library.AddSong("Queen", "Bohemian Rhapsody", "#GENRE:Rock\n");
        _library.AddSong("Queen", "Don't Stop Me Now");
        _library.AddSong("ABBA", "Dancing Queen", "#EDITION:Gold\n");

        var service = CreateService();

        Assert.Equal(["ABBA", "Queen", "Queen"], service.Search("").Select(s => s.Artist));
        Assert.Equal(["Dancing Queen", "Bohemian Rhapsody", "Don't Stop Me Now"], service.Search("queen").Select(s => s.Title));
        Assert.Equal(["Bohemian Rhapsody"], service.Search("queen rock").Select(s => s.Title));
        Assert.Equal(["Dancing Queen"], service.Search("gold").Select(s => s.Title));
        Assert.Empty(service.Search("metallica"));
    }

    [Fact]
    public void Results_AreCachedUntilRefreshed()
    {
        var service = CreateService();
        _library.AddSong("Queen", "One");
        Assert.Single(service.GetAll());

        _library.AddSong("Queen", "Two");
        Assert.Single(service.GetAll());

        service.Refresh();
        Assert.Equal(2, service.GetAll().Count);
    }

    [Fact]
    public void AMissingLibrary_IsJustEmpty()
    {
        Assert.Empty(CreateService(Path.Combine(_library.Path, "nope")).GetAll());
    }
}
