using System.ClientModel;
using System.ClientModel.Primitives;
using System.Linq;
using System.Text;
using OpenAI;
using OpenAI.Chat;

namespace UltraSingerUI.Services;

public static class OpenAIImproverService
{
    public static async Task<string?> ImproveUltraStarAsync(string ultraStarTxt, string referenceLyrics, string title, string apiKey, string model)
    {
        // Compose a strict system prompt to ensure only corrected UltraStar text is returned
        var system = """
                     You are an expert on the UltraStar Deluxe .txt format. Given a current UltraStar file and reference lyrics, update only the lyric lines and syllable splits to match the reference lyrics while preserving timing, note types, BPM metadata, and file headers.
                     
                     You may update the type of note used for lines that are important to a song, such as the line meaning, referencing the song title, being part of a chorus etc. For example, in the song 'All Star' by Smashmouth, the chorus beginning with 'hey now you're an all star' might deserve to be annotated with golden notes.
                     
                      Return ONLY the complete, corrected UltraStar .txt content without any commentary or code fences.
                      
                      Ultrastar specifications:
                      The sing line is defined so that it has the NoteType, StartBeat, Length, Pitch and Text.
                     
                     For the styles see NoteTypes.
                     The StartBeat and Length must be calculated against the BPM, GAP and Relative. Is a beat number.
                     The pitch describes the note as a number. The number 0 corresponds to the note C4 and Midi Note 60.
                     Text is the part of the lyrics that is sung in this note.
                     
                     Note types:
                     
                     ```
                     Normal `:`
                     The normal note.
                     Example:
                     `: 0 1 8 Normal`
                     
                     Golden `*`
                     The golden note.
                     Gives twice the point of a normal point.
                     `* 0 1 8 Golden`
                                         
                     Freestyle `F`
                     Note that will NOT be scored.
                     `F 0 1 8 Freestyle`
                                         
                     Rap `R`
                     Rap note
                     `R 0 1 8 Rap`
                                         
                     Rap Golden G
                     Golden Rap note.
                     Gives twice the point of a rap point
                     `G 0 1 8 RapGolden`
                     ```
                                         
                     You MUST ONLY OUTPUT VALID ULTRASTAR FILES.
                     
                     If the "authoritative" reference lyrics are not valid or you encounter a problem, return the original UltraStar file but add `[OpenAI mismatch]` to the `#COMMENT` line.
                     """;

        var user =
            $"""
             Title: {title}
             REFERENCE LYRICS (authoritative):
             ```
             {referenceLyrics}
             ```
             CURRENT ULTRASTAR FILE:
             ```
             {ultraStarTxt}
             ```
             
             Only respond with a valid ultrastar 
             """;
        try
        {
            var client = new OpenAIClient(new ApiKeyCredential(apiKey), new OpenAIClientOptions()
            {
                RetryPolicy = new ClientRetryPolicy(0),
                NetworkTimeout = TimeSpan.FromSeconds(120)
            });
            var chat = client.GetChatClient(model);

            Console.WriteLine($"[OpenAI][Improve] Sending request to OpenAI API for improved UltraStar file...");
            var result = await chat.CompleteChatAsync(new ChatMessage[]
            {
                new SystemChatMessage(system),
                new UserChatMessage(user)
            });
            
            Console.WriteLine($"[OpenAI][Improve] Received response from OpenAI API.");
            
            var completion = result.Value;
            var content = string.Concat(completion.Content.Select(p => p.Text ?? string.Empty));
            if (string.IsNullOrWhiteSpace(content)) return null;

            content = StripCodeFences(content);
            return content;
        }
        catch
        {
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
