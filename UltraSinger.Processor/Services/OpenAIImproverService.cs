using System.ClientModel;
using System.ClientModel.Primitives;
using OpenAI;
using OpenAI.Chat;

namespace UltraSinger.Processor.Services;

public class OpenAIImproverService(
    EnvironmentalValuesService environmentalValues,
    ILogger<OpenAIImproverService> logger)
{
    private const string BaseSystemPrompt =
        """
        Given a current UltraStar file and reference lyrics, update only the lyric lines.

        Ultrastar specifications:
        The sing line is defined so that it has the NoteType, StartBeat, Length, Pitch and Text.
         
        You are ONLY to output changes to the Text portion.
        
        For example, if given the following:
        : 796 4 9 I'm 
        : 801 7 9 gonna 
        : 809 3 9 pop
        : 813 5 7 ~ 
        : 819 7 7 my 
        : 829 9 7 bike 
        - 838
        : 842 4 7 far
        : 846 7 8 ~
        : 853 3 6 ~ 
        : 857 3 5 to 
        : 861 8 7 hell 
        - 868
        : 879 4 9 watch
        : 883 4 7 ~
        : 887 3 5 ~ 
        : 892 4 5 it 
        : 898 4 6 from
        : 902 10 5 ~ 
        : 914 4 5 afar
        : 918 4 10 ~
        : 922 4 8 ~
        : 926 5 7 ~ 
        
        But the lyrics for this are actually:
        I'm going to pack my bag, find a hill, watch it from afar
        
        Then you should change those bits like this:
        : 796 4 9 I'm 
        : 801 7 9 gonna 
        : 809 3 9 pack
        : 813 5 7 ~ 
        : 819 7 7 my 
        : 829 9 7 bag 
        - 838
        : 842 4 7 find
        : 846 7 8 ~
        : 853 3 6 ~ 
        : 857 3 5 a
        : 861 8 7 hill 
        - 868
        : 879 4 9 watch
        : 883 4 7 ~
        : 887 3 5 ~ 
        : 892 4 5 it 
        : 898 4 6 from
        : 902 10 5 ~ 
        : 914 4 5 afar
        : 918 4 10 ~
        : 922 4 8 ~
        : 926 5 7 ~ 
        
        The UltraStar text has been automatically generated from the audio of the file. 
        We want you to fix all transcription errors using the reference lyric as the transcriber did not have access to the lyrics.
        
        There may be cases sections that exist in the UltraStar text but don't appear to match anything in the reference lyrics;
        That's okay - since many of the source audio tracks are from music videos with additional content.
        Just ignore these sections and leave them as they are.
        
        If however the two files don't seem at all compatible, output ERROR.
        
        If you successfully process the file, return ONLY the complete, corrected UltraStar .txt content without any commentary or code fences, but also add '[Enhanced]' to the title, for example:
        #TITLE:The Flame [Enhanced]
        """;

    /// <summary>
    /// Added only when the reference lyrics carry LRC timestamps. The model is deliberately
    /// told to use them for alignment and never to derive beat values from them: UltraStar
    /// beats come from #BPM/#GAP, and asking a model to do that conversion produces
    /// confidently wrong timings that are worse than the transcription errors we're fixing.
    /// </summary>
    private const string SyncedLyricsPrompt =
        """

        The reference lyrics are time-synced. Each line begins with a `[mm:ss.xx]` timestamp giving its position measured from the start of the audio track.
        
        Don't include the timestamps in the output.

        """;

    public async Task<string?> ImproveUltraStarAsync(
        string ultraStarTxt,
        LyricsResult lyrics,
        string title,
        CancellationToken cancellationToken = default)
    {
        var apiKey = environmentalValues.OpenAIKey;

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        var system = BaseSystemPrompt;

        if (lyrics.IsSynced && environmentalValues.SendTimestampsToOpenAI)
        {
            system += SyncedLyricsPrompt;
        }

        var additional = environmentalValues.OpenAIAdditionalInstructions;

        if (!string.IsNullOrWhiteSpace(additional))
        {
            system += $"\n\nAdditional instructions for this library:\n{additional}";
        }

        var user =
            $"""
             Title: {title}
             REFERENCE LYRICS (authoritative{(lyrics.IsSynced ? ", time-synced" : "")}):
             ```
             {lyrics.Text}
             ```
             CURRENT ULTRASTAR FILE:
             ```
             {ultraStarTxt}
             ```

             Only respond with a valid ultrastar file.
             """;

        try
        {
            var client = new OpenAIClient(new ApiKeyCredential(apiKey), new OpenAIClientOptions
            {
                RetryPolicy = new ClientRetryPolicy(0),
                NetworkTimeout = TimeSpan.FromSeconds(300)
            });
            var chat = client.GetChatClient(environmentalValues.OpenAIModel);

            logger.LogInformation(
                "[OpenAI][Improve] Requesting improved UltraStar file for '{Title}' using {Model} ({Kind} lyrics).",
                title, environmentalValues.OpenAIModel, lyrics.IsSynced ? "synced" : "plain");

            var result = await chat.CompleteChatAsync(
                [new SystemChatMessage(system), new UserChatMessage(user)],
                cancellationToken: cancellationToken);

            logger.LogInformation("[OpenAI][Improve] Received response from OpenAI API.");

            var content = string.Concat(result.Value.Content.Select(part => part.Text ?? string.Empty));

            return string.IsNullOrWhiteSpace(content) ? null : StripCodeFences(content);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[OpenAI][Improve] Request failed for '{Title}'.", title);
            return null;
        }
    }

    private static string StripCodeFences(string input)
    {
        var s = input.Trim();
        if (s.StartsWith("```"))
        {
            // Remove first line fence
            var idx = s.IndexOf('\n');
            if (idx >= 0)
            {
                s = s[(idx + 1)..];
            }
            // Remove trailing fence
            var lastFence = s.LastIndexOf("```", StringComparison.Ordinal);
            if (lastFence >= 0)
            {
                s = s[..lastFence];
            }
            s = s.Trim();
        }
        return s;
    }
}
