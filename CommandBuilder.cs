namespace JellyfinWhisperCommand;

public static class CommandBuilder
{
    public static string Build(IEnumerable<string> paths, WhisperJavSettings settings)
    {
        var quotedPaths = string.Join(" ", paths.Select(Quote));
        var pass1 = """{"model_name":"large-v2","device":"cuda","temperature":[0],"compression_ratio_threshold":2.4,"logprob_threshold":-1,"logprob_margin":0,"no_speech_threshold":0.71,"beam_size":2,"best_of":2,"patience":1.2,"suppress_blank":true,"without_timestamps":false,"condition_on_previous_text":false,"word_timestamps":true,"repetition_penalty":1.3,"no_repeat_ngram_size":3,"chunk_length":30,"max_initial_timestamp":0,"threshold":0.3,"min_speech_duration_ms":150,"min_silence_duration_ms":150,"max_speech_duration_s":5,"speech_pad_ms":400,"chunk_threshold_s":1,"max_group_duration_s":10,"force_cpu":"false","scene_detection_method":"auditok","min_duration":20,"max_duration":420,"snap_window":5,"clustering_threshold":18,"visualize":false}""";
        var pass2 = """{"model_name": "large-v2", "device": "cuda", "temperature": [0, 0.2], "compression_ratio_threshold": 2.6, "logprob_threshold": -1, "logprob_margin": 0, "no_speech_threshold": 0.72, "beam_size": 3, "best_of": 2, "patience": 1.3, "suppress_blank": true, "without_timestamps": false, "condition_on_previous_text": false, "word_timestamps": true, "max_initial_timestamp": 0, "threshold": 0.1, "hop_size": 256, "min_speech_duration_ms": 100, "min_silence_duration_ms": 100, "max_speech_duration_s": 4, "chunk_threshold_s": 2, "max_group_duration_s": 10, "start_pad_ms": 0, "end_pad_ms": 200, "scene_detection_method": "silero", "max_duration_s": 120, "min_duration_s": 0.2, "pass1_max_duration_s": 2700, "pass1_max_silence_s": 2.5, "pass1_energy_threshold": 32, "pass2_max_duration_s": 1800, "pass2_max_silence_s": 1.8, "pass2_energy_threshold": 38, "brute_force_fallback": true, "brute_force_chunk_s": 29}""";

        return $"whisperjav.exe {quotedPaths} --ensemble --pass1-pipeline fidelity --pass1-sensitivity balanced --pass1-params {JsonArgument(pass1)} --pass1-scene-detector auditok --pass1-speech-segmenter ten --pass1-model large-v2 --pass2-pipeline balanced --pass2-sensitivity aggressive --pass2-params {JsonArgument(pass2)} --pass2-scene-detector auditok --pass2-speech-segmenter whisperseg --pass2-model large-v2 --merge-strategy pass1_primary --output-dir {Quote(settings.OutputDir)} --subs-language native --language japanese --translate --translate-provider custom --translate-target chinese --translate-tone standard --translate-model {Quote(settings.TranslateModel)} --translate-api-key {Quote(settings.TranslateApiKey)} --translate-endpoint {Quote(settings.TranslateEndpoint)} --stream --skip-pass2-on-pass1-failure";
    }

    private static string JsonArgument(string json) => $"\"{json.Replace("\"", "\\\"")}\"";
    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";
}
