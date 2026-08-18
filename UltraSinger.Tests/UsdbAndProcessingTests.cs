using UltraSinger.Blazor.Services;
using UltraSinger.Processor.Services;
using Xunit;

namespace UltraSinger.Tests;

public class UsdbAndProcessingTests
{
    [Fact]
    public void TestParseUsdbHtml()
    {
        var html = """
            <table class="list">
                <tr><th>ID</th><th>Artist</th><th>Title</th><th>Edition</th><th>Gold</th><th>Language</th><th>Year</th><th>Genre</th><th>Creator</th><th>Rating</th><th>Views</th></tr>
                <tr>
                    <td><a href="?link=detail&id=12345">12345</a></td>
                    <td>Queen</td>
                    <td>Bohemian Rhapsody</td>
                    <td>A Night at the Opera</td>
                    <td><img src="images/gold.png" /></td>
                    <td>English</td>
                    <td>1975</td>
                    <td>Rock</td>
                    <td>Author</td>
                    <td>5.0</td>
                    <td>1,234</td>
                </tr>
            </table>
            """;

        var results = UsdbService.ParseSearchResultsHtml(html);
        Assert.Single(results);
        var song = results[0];
        Assert.Equal(12345, song.Id);
        Assert.Equal("Queen", song.Artist);
        Assert.Equal("Bohemian Rhapsody", song.Title);
        Assert.Equal("A Night at the Opera", song.Edition);
        Assert.True(song.GoldenNotes);
        Assert.Equal("English", song.Language);
        Assert.Equal(1975, song.Year);
        Assert.Equal("Rock", song.Genre);
        Assert.Equal(5.0, song.Rating);
        Assert.Equal(1234, song.Views);
    }

    [Fact]
    public void TestParseSongDetailsHtml()
    {
        var html = """
            <html>
                <body>
                    <iframe src="https://www.youtube.com/embed/fJ9rUzIMcZQ"></iframe>
                    <table>
                        <tr><td>Artist:</td><td>Queen</td></tr>
                        <tr><td>Title:</td><td>Bohemian Rhapsody</td></tr>
                        <tr><td>BPM:</td><td>285.5</td></tr>
                        <tr><td>GAP:</td><td>1450</td></tr>
                    </table>
                </body>
            </html>
            """;

        var details = UsdbService.ParseSongDetailsHtml(html, 12345);
        Assert.Equal(12345, details.Id);
        Assert.Equal("Queen", details.Artist);
        Assert.Equal("Bohemian Rhapsody", details.Title);
        Assert.Equal("https://www.youtube.com/watch?v=fJ9rUzIMcZQ", details.YoutubeUrl);
        Assert.Equal(285.5, details.Bpm);
        Assert.Equal(1450, details.Gap);
    }

    [Fact]
    public void TestUpdateUltraStarTxtHeaders()
    {
        var txt = """
            #TITLE:Bohemian Rhapsody
            #ARTIST:Queen
            #BPM:285.5
            #GAP:1450
            : 0 4 60 Is
            : 5 4 62 this
            - 10
            E
            """;

        var updated = SongProcessingJob.UpdateUltraStarTxtHeaders(txt, "audio.mp3", "video.mp4");
        Assert.Contains("#MP3:audio.mp3", updated);
        Assert.Contains("#VIDEO:video.mp4", updated);
        Assert.Contains("#TITLE:Bohemian Rhapsody", updated);
        Assert.Contains(": 0 4 60 Is", updated);
    }

    [Fact]
    public void TestParseLocalTxtFile()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, """
                #TITLE:Don't Stop Me Now
                #ARTIST:Queen
                #GENRE:Rock
                #YEAR:1978
                #LANGUAGE:English
                #BPM:312.4
                : 0 4 60 To
                E
                """);

            var result = LocalLibraryService.ParseUltraStarTxtFile(tempFile);
            Assert.NotNull(result);
            Assert.Equal("Queen", result.Artist);
            Assert.Equal("Don't Stop Me Now", result.Title);
            Assert.Equal("Rock", result.Genre);
            Assert.Equal("1978", result.Year);
            Assert.Equal("English", result.Language);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }
}
