namespace UltraSinger.Blazor.Tests.Infrastructure;

/// <summary>Trimmed-down copies of the USDB pages the client scrapes.</summary>
public static class UsdbPages
{
    public const string SearchResults = """
        <html><body>
        <table class="list">
          <tr class="list_head"><td>Interpret</td><td>Title</td><td>Genre</td><td>Year</td><td>Edition</td><td>Golden</td><td>Language</td><td>Creator</td><td>Rating</td><td>Views</td></tr>
          <tr>
            <td>Queen</td><td><a href="?link=detail&amp;id=123">Bohemian Rhapsody</a></td><td>Rock</td><td>1975</td><td>SingStar</td>
            <td><img src="images/gold.png"/></td><td>English</td><td>someone</td><td>4.5</td><td>1,234</td>
          </tr>
          <tr>
            <td>Queen</td><td><a href="?link=detail&amp;id=456">Don&#39;t Stop Me Now</a></td><td></td><td>n/a</td><td></td>
            <td>no</td><td></td><td>someone</td><td><img src="star.png"/><img src="star.png"/><img src="star.png"/></td><td>1.000</td>
          </tr>
        </table>
        </body></html>
        """;

    public const string EmptySearchResults = """<html><body><table class="list"><tr class="list_head"><td>Interpret</td><td>Title</td></tr></table></body></html>""";

    public const string BohemianDetails = """
        <html><body>
        <table>
          <tr class="list_head"><td>Queen</td><td>Bohemian Rhapsody</td></tr>
          <tr><td>BPM</td><td>310.5</td></tr>
          <tr><td>GAP</td><td>1200</td></tr>
          <tr><td>Year</td><td>1975</td></tr>
          <tr><td>Genre</td><td>Rock</td></tr>
          <tr><td>Language</td><td>English</td></tr>
        </table>
        <p>Video: <a href="https://www.youtube.com/watch?v=fJ9rUzIMcZQ">YouTube</a></p>
        </body></html>
        """;

    public const string BohemianTxt = "#ARTIST:Queen\n#TITLE:Bohemian Rhapsody\n#BPM:310.5\n: 0 4 5 Is\nE";

    public static string GetTxtPage(string txt) =>
        $"""<html><body><form><textarea name="txt">{System.Net.WebUtility.HtmlEncode(txt)}</textarea></form></body></html>""";

    /// <summary>Serves detail + gettxt for song 123, and the search page for any list query.</summary>
    public static HttpResponseMessage? Route(HttpRequestMessage request, string? body) =>
        request.RequestUri!.Query switch
        {
            "?link=detail&id=123" => RoutingHandler.Html(BohemianDetails),
            "?link=gettxt&id=123" => RoutingHandler.Html(GetTxtPage(BohemianTxt)),
            "?link=list" => RoutingHandler.Html(SearchResults),
            _ => null,
        };
}
