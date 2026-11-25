using UltraSinger.Processor.Services;

namespace UltraSinger.Processor.Tests;

public class OpenAIImproverServiceTests
{
    [Theory]
    [InlineData("#TITLE:T\n: 0 1 1 x", "#TITLE:T\n: 0 1 1 x")]
    [InlineData("```\n#TITLE:T\n```", "#TITLE:T")]
    [InlineData("```txt\n#TITLE:T\n: 0 1 1 x\n```\n", "#TITLE:T\n: 0 1 1 x")]
    [InlineData("  \n#TITLE:T  \n", "#TITLE:T")]
    public void StripCodeFences_RemovesMarkdownWrapping(string input, string expected)
    {
        Assert.Equal(expected, OpenAIImproverService.StripCodeFences(input));
    }
}
