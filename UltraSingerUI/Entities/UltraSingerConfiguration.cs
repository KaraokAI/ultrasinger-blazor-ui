namespace UltraSingerUI.Entities;

public record UltraSingerConfiguration
{
    public string? UltraSingerPath { get; init; }
    public string? UltraStarDeluxeLocalLibraryPath { get; init; }
    public string? KaraokeLanguage { get; init; }
    
    /// Additional variables need to be in the form:
    /// MYVAR=value;OTHERVAR=othervalue
    ///
    /// Values including semicolons (;) are not supported at current time. 
    public string? UltraSingerAdditionalEnvVars { get; init; }
    public string? YTDLPPath { get; init; }
}