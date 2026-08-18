namespace UltraSinger.Contracts;

public static class UltraStarTxtHelper
{
    public static string UpdateUltraStarTxtHeaders(
        string txtContent,
        string? audioFileName,
        string? videoFileName,
        string? vocalsFileName = null,
        string? instrumentalFileName = null)
    {
        var lines = txtContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None).ToList();
        var hasMp3 = false;
        var hasVideo = false;
        var hasVocals = false;
        var hasInstrumental = false;
        var headerEndIndex = 0;

        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i].Trim();
            if (line.StartsWith('#'))
            {
                headerEndIndex = i + 1;
                if (line.StartsWith("#MP3:", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrEmpty(audioFileName))
                    {
                        lines[i] = $"#MP3:{audioFileName}";
                    }
                    hasMp3 = true;
                }
                else if (line.StartsWith("#VIDEO:", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrEmpty(videoFileName))
                    {
                        lines[i] = $"#VIDEO:{videoFileName}";
                    }
                    hasVideo = true;
                }
                else if (line.StartsWith("#VOCALS:", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrEmpty(vocalsFileName))
                    {
                        lines[i] = $"#VOCALS:{vocalsFileName}";
                    }
                    hasVocals = true;
                }
                else if (line.StartsWith("#INSTRUMENTAL:", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrEmpty(instrumentalFileName))
                    {
                        lines[i] = $"#INSTRUMENTAL:{instrumentalFileName}";
                    }
                    hasInstrumental = true;
                }
            }
            else if (line.Length > 0 && ":*FRG-E".Contains(line[0]))
            {
                break;
            }
        }

        if (!hasMp3 && !string.IsNullOrEmpty(audioFileName))
        {
            lines.Insert(headerEndIndex, $"#MP3:{audioFileName}");
            headerEndIndex++;
        }

        if (!hasVideo && !string.IsNullOrEmpty(videoFileName))
        {
            lines.Insert(headerEndIndex, $"#VIDEO:{videoFileName}");
            headerEndIndex++;
        }

        if (!hasVocals && !string.IsNullOrEmpty(vocalsFileName))
        {
            lines.Insert(headerEndIndex, $"#VOCALS:{vocalsFileName}");
            headerEndIndex++;
        }

        if (!hasInstrumental && !string.IsNullOrEmpty(instrumentalFileName))
        {
            lines.Insert(headerEndIndex, $"#INSTRUMENTAL:{instrumentalFileName}");
        }

        return string.Join(Environment.NewLine, lines);
    }
}
