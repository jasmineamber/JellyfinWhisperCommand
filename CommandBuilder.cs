namespace JellyfinWhisperCommand;

public static class CommandBuilder
{
    public static string FormatCommand(ProcessStartInfo startInfo)
    {
        var arguments = startInfo.ArgumentList.Count > 0
            ? string.Join(" ", startInfo.ArgumentList.Select(QuoteArgument))
            : startInfo.Arguments;

        return string.IsNullOrEmpty(arguments)
            ? QuoteArgument(startInfo.FileName)
            : $"{QuoteArgument(startInfo.FileName)} {arguments}";
    }

    public static ProcessStartInfo BuildSeconvStartInfo(string mediaPath, SeconvSettings settings)
    {
        var mediaName = Path.GetFileNameWithoutExtension(mediaPath);
        if (string.IsNullOrWhiteSpace(mediaName))
            throw new InvalidOperationException($"Invalid media path: {mediaPath}");

        var startInfo = new ProcessStartInfo
        {
            FileName = settings.ExecutablePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add($"{mediaName}.chi.whisperjav.srt");
        startInfo.ArgumentList.Add("subrip");
        AddArgument(startInfo, "--multiple-replace", settings.MultipleReplaceRulesFile);
        AddArgument(startInfo, "--input-folder", settings.InputFolder);
        startInfo.ArgumentList.Add("--overwrite");
        return startInfo;
    }

    public static ProcessStartInfo BuildTranslateStartInfo(string subtitlePath, WhisperJavTranslateSettings settings)
    {
        if (string.IsNullOrWhiteSpace(subtitlePath))
            throw new InvalidOperationException("Subtitle path is required for translation.");

        var startInfo = new ProcessStartInfo
        {
            FileName = settings.ExecutablePath,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(settings.ExecutablePath))!,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(subtitlePath);
        AddArgument(startInfo, "--provider", settings.Provider);
        AddArgument(startInfo, "--endpoint", settings.Endpoint);
        AddArgument(startInfo, "--api-key", settings.ApiKey);
        AddArgument(startInfo, "--model", settings.Model);
        AddArgument(startInfo, "--source", settings.SourceLanguage);
        AddArgument(startInfo, "--target", settings.TargetLanguage);
        AddArgument(startInfo, "--tone", settings.Tone);
        AddArgument(startInfo, "--instructions-file", settings.InstructionsFilePath);
        if (settings.Stream) startInfo.ArgumentList.Add("--stream");
        return startInfo;
    }

    public static ProcessStartInfo BuildStartInfo(IEnumerable<string> paths, WhisperJavSettings settings)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = settings.ExecutablePath,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(settings.ExecutablePath))!,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true
        };

        foreach (var path in paths) startInfo.ArgumentList.Add(path);
        foreach (var arg in ParseArgs(settings.Args)) startInfo.ArgumentList.Add(arg);
        AddArgument(startInfo, "--output-dir", settings.OutputDir);
        return startInfo;
    }

    private static void AddArgument(ProcessStartInfo startInfo, string option, string value)
    {
        if (string.IsNullOrEmpty(value)) return;
        startInfo.ArgumentList.Add(option);
        startInfo.ArgumentList.Add(value);
    }

    private static List<string> ParseArgs(string args)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(args)) return result;

        var current = new StringBuilder();
        bool inQuote = false;

        foreach (var c in args)
        {
            if (c == '"')
            {
                inQuote = !inQuote;
                current.Append(c);
            }
            else if (c == ' ' && !inQuote)
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0) result.Add(current.ToString());
        return result;
    }

    private static string QuoteArgument(string value)
    {
        var quoted = new StringBuilder("\"");
        var backslashCount = 0;
        foreach (var character in value)
        {
            if (character == '\\')
            {
                backslashCount++;
                continue;
            }

            if (character == '\"')
            {
                quoted.Append('\\', backslashCount * 2 + 1);
                quoted.Append(character);
                backslashCount = 0;
                continue;
            }

            quoted.Append('\\', backslashCount);
            quoted.Append(character);
            backslashCount = 0;
        }

        quoted.Append('\\', backslashCount * 2);
        quoted.Append('\"');
        return quoted.ToString();
    }
}
