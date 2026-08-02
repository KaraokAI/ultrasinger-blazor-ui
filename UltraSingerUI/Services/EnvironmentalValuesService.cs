using System.Collections;
using Microsoft.Extensions.Options;
using UltraSingerUI.Entities;

namespace UltraSingerUI.Services;

public class EnvironmentalValuesService
{
    public static IConfiguration Configuration { get; set; }

    private static UltraSingerConfiguration? ultraSingerConfiguration => Configuration.GetSection("ProcessorOptions").Get<UltraSingerConfiguration>();
    
    public string UltraSingerPath => ultraSingerConfiguration?.UltraSingerPath ?? throw new InvalidOperationException("Required environment variable ULTRASINGER_PATH is not set.");
    
    public string PythonExecutable => ultraSingerConfiguration?.PythonExecutable ?? throw new InvalidOperationException("Required environment variable PYTHON_EXECUTABLE is not set.");
    
    public string UltraSingerAdditionalArgs => ultraSingerConfiguration?.UltraSingerAdditionalArgs ?? "";
    
    public string PythonArguments => ultraSingerConfiguration?.PythonArguments ?? "";
    
    public string UltraStarDeluxeLocalLibraryPath => ultraSingerConfiguration?.UltraStarDeluxeLocalLibraryPath ?? throw new InvalidOperationException("Required environment variable ULTASTARDELUXE_LOCAL_LIBRARY_PATH is not set.");
    
    public string UltraStarDeluxeWSLPath => ultraSingerConfiguration?.UltraStarDeluxeWSLPath ?? throw new InvalidOperationException("Required environment variable ULTASTARDELUXE_WSL_PATH is not set.");

    public string KaraokeLanguage => ultraSingerConfiguration?.KaraokeLanguage ?? "en";
    
    /// <summary>
    /// ULTRASINGER_ADDITIONAL_VARIABLES needs to be in the form:
    /// MYVAR=value;OTHERVAR=othervalue
    ///
    /// Values including semicolons (;) are not supported at current time. 
    /// </summary>
    /// <returns></returns>
    public List<(string, string?)> GetUltraSingerAdditionalEnvironmentVariables()
    {
        var environmentVarList = ultraSingerConfiguration?.UltraSingerAdditionalEnvVars;
        if (environmentVarList == null)
        {
            return new List<(string, string?)>();
        }
        
        return environmentVarList
            .Split(';')
            .Select(envKeypair => envKeypair.Split('='))
            .Select(envKeypair => (envKeypair.First().Trim(), envKeypair.ElementAtOrDefault(1)))
            .ToList();
    }
    
    public string YTDLPPath => ultraSingerConfiguration?.YTDLPPath ?? throw new InvalidOperationException("Required environment variable YTDL_PATH is not set.");

    // OpenAI settings
    public string? OpenAIKey => ultraSingerConfiguration?.OpenAIKey;
    public string OpenAIModel => ultraSingerConfiguration?.OpenAIModel ?? "gpt-5-nano";
    public bool EnableOpenAICorrections => ultraSingerConfiguration?.EnableOpenAICorrections ?? false;
    public bool OverwriteUltraStarFile => ultraSingerConfiguration?.OverwriteUltraStarFile ?? false;

    // Some-Random-API settings (lyrics)
    public string? SomeRandomApiToken => ultraSingerConfiguration?.SomeRandomApiToken;
}