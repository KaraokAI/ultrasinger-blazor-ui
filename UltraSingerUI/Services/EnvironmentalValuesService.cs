using System.Collections;

namespace UltraSingerUI.Services;

public class EnvironmentalValuesService()
{
    public string UltraSingerPath => Environment.GetEnvironmentVariable("ULTRASINGER_PATH") ?? throw new InvalidOperationException("Required environment variable ULTRASINGER_PATH is not set.");
    
    public string UltraStarDeluxeLocalLibraryPath => Environment.GetEnvironmentVariable("ULTASTARDELUXE_LOCAL_LIBRARY_PATH") ?? throw new InvalidOperationException("Required environment variable ULTASTARDELUXE_LOCAL_LIBRARY_PATH is not set.");

    public string KaraokeLanguage => Environment.GetEnvironmentVariable("LANGUAGE") ?? "en";
    
    /// <summary>
    /// ULTRASINGER_ADDITIONAL_VARIABLES needs to be in the form:
    /// MYVAR=value;OTHERVAR=othervalue
    ///
    /// Values including semicolons (;) are not supported at current time. 
    /// </summary>
    /// <returns></returns>
    public List<(string, string?)> GetUltraSingerAdditionalEnvironmentVariables()
    {
        var environmentVarList = Environment.GetEnvironmentVariable("ULTRASINGER_ADDITIONAL_VARIABLES");
        if (environmentVarList == null)
        {
            return new List<(string, string?)>();
        }
        
        return environmentVarList
            .Split(';')
            .Select(envKeypair => envKeypair.Split('='))
            .Select(envKeypair => (envKeypair.First().Trim(), envKeypair.ElementAtOrDefault(2)))
            .ToList();
    }
    
    public string YTDLPPath => Environment.GetEnvironmentVariable("YTDLP_PATH") ?? throw new InvalidOperationException("Required environment variable YTDL_PATH is not set.");
}