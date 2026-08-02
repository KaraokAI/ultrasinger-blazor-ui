namespace UltraSingerUI.Entities;

public record UltraSingerConfiguration
{
    public string? PythonExecutable { get; init; }
    public string? PythonArguments { get; init; }
    public string? UltraSingerPath { get; init; }
    public string? UltraStarDeluxeWSLPath { get; init; }
    public string? UltraStarDeluxeLocalLibraryPath { get; init; }
    public string? KaraokeLanguage { get; init; }
    
    public string UltraSingerAdditionalArgs { get; init; } = "";
    
    /// Additional variables need to be in the form:
    /// MYVAR=value;OTHERVAR=othervalue
    ///
    /// Values including semicolons (;) are not supported at current time. 
    public string? UltraSingerAdditionalEnvVars { get; init; }
    public string? YTDLPPath { get; init; }

    // OpenAI configuration
    public string? OpenAIKey { get; init; }
    public string? OpenAIModel { get; init; }
    public bool EnableOpenAICorrections { get; init; } = false;
    public bool OverwriteUltraStarFile { get; init; } = false;

    // Some-Random-API configuration (for lyrics)
    public string? SomeRandomApiToken { get; init; }
}