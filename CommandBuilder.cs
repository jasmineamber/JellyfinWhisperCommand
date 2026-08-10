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
        startInfo.ArgumentList.Add("--stream");
        return startInfo;
    }

    public static ProcessStartInfo BuildStartInfo(IEnumerable<string> paths, WhisperJavSettings settings)
    {
        var pass1 = """{"model_name":"large-v2","device":"cuda","temperature":[0],"compression_ratio_threshold":2.4,"logprob_threshold":-1,"logprob_margin":0,"no_speech_threshold":0.71,"beam_size":2,"best_of":2,"patience":1.2,"suppress_blank":true,"without_timestamps":false,"condition_on_previous_text":false,"word_timestamps":true,"repetition_penalty":1.3,"no_repeat_ngram_size":3,"chunk_length":30,"max_initial_timestamp":0,"threshold":0.3,"min_speech_duration_ms":150,"min_silence_duration_ms":150,"max_speech_duration_s":5,"speech_pad_ms":400,"chunk_threshold_s":1,"max_group_duration_s":10,"force_cpu":"false","scene_detection_method":"auditok","min_duration":20,"max_duration":420,"snap_window":5,"clustering_threshold":18,"visualize":false}""";
        var pass2 = """{"model_name": "large-v2", "device": "cuda", "temperature": [0, 0.2], "compression_ratio_threshold": 2.6, "logprob_threshold": -1, "logprob_margin": 0, "no_speech_threshold": 0.72, "beam_size": 3, "best_of": 2, "patience": 1.3, "suppress_blank": true, "without_timestamps": false, "condition_on_previous_text": false, "word_timestamps": true, "repetition_penalty": 1.3, "no_repeat_ngram_size": 3, "chunk_length": 30, "max_initial_timestamp": 0, "threshold": 0.1, "min_speech_duration_ms": 100, "min_silence_duration_ms": 100, "max_speech_duration_s": 4, "speech_pad_ms": 150, "chunk_threshold_s": 2, "max_group_duration_s": 5, "force_cpu": "false", "scene_detection_method": "silero", "max_duration_s": 120, "min_duration_s": 0.2, "pass1_max_duration_s": 2700, "pass1_max_silence_s": 2.5, "pass1_energy_threshold": 32, "pass2_max_duration_s": 1800, "pass2_max_silence_s": 1.8, "pass2_energy_threshold": 38, "brute_force_fallback": true, "brute_force_chunk_s": 29}""";

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
        startInfo.ArgumentList.Add("--ensemble");
        AddArgument(startInfo, "--pass1-pipeline", "fidelity");
        AddArgument(startInfo, "--pass1-sensitivity", "balanced");
        AddArgument(startInfo, "--pass1-params", pass1);
        AddArgument(startInfo, "--pass1-scene-detector", "auditok");
        AddArgument(startInfo, "--pass1-speech-segmenter", "ten");
        AddArgument(startInfo, "--pass1-model", "large-v2");
        AddArgument(startInfo, "--pass2-pipeline", "balanced");
        AddArgument(startInfo, "--pass2-sensitivity", "aggressive");
        AddArgument(startInfo, "--pass2-params", pass2);
        AddArgument(startInfo, "--pass2-scene-detector", "silero");
        AddArgument(startInfo, "--pass2-speech-segmenter", "silero-v6.2");
        AddArgument(startInfo, "--pass2-model", "large-v2");
        AddArgument(startInfo, "--merge-strategy", "pass1_primary");
        AddArgument(startInfo, "--output-dir", settings.OutputDir);
        startInfo.ArgumentList.Add("--subs-language"); startInfo.ArgumentList.Add("native");
        startInfo.ArgumentList.Add("--language"); startInfo.ArgumentList.Add("japanese");
        startInfo.ArgumentList.Add("--skip-pass2-on-pass1-failure");
        return startInfo;
    }

    private static void AddArgument(ProcessStartInfo startInfo, string option, string value)
    {
        startInfo.ArgumentList.Add(option);
        startInfo.ArgumentList.Add(value);
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
