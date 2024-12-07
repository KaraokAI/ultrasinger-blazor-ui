using System.Collections;
using Microsoft.Extensions.Options;
using UltraSingerUI.Entities;

namespace UltraSingerUI.Services;

public class EnvironmentalValuesService
{
    public static IConfiguration Configuration { get; set; }

    private static UltraSingerConfiguration? ultraSingerConfiguration => Configuration.GetSection("ProcessorOptions").Get<UltraSingerConfiguration>();
    
    public string UltraSingerPath => ultraSingerConfiguration?.UltraSingerPath ?? throw new InvalidOperationException("Required environment variable ULTRASINGER_PATH is not set.");
    
    public string UltraStarDeluxeLocalLibraryPath => ultraSingerConfiguration?.UltraStarDeluxeLocalLibraryPath ?? throw new InvalidOperationException("Required environment variable ULTASTARDELUXE_LOCAL_LIBRARY_PATH is not set.");

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
}