namespace UltraSingerUI.Entities;

public class OpenAIConfiguration
{
    public required string AIKey { get; set; }

    public string AdditionalInstructions { get; set; } = "";
}