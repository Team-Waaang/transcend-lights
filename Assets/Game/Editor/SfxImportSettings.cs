using UnityEditor;
using UnityEngine;

namespace AiHackathon.Game.Editor
{
    /// <summary>Repeatable import contract for generated SFX, including future replacements.</summary>
    public sealed class SfxImportSettings : AssetPostprocessor
    {
        void OnPreprocessAudio()
        {
            bool music = assetPath.StartsWith("Assets/Game/Resources/Audio/Music/", System.StringComparison.Ordinal);
            if (!music && !assetPath.StartsWith("Assets/Game/Resources/Audio/SFX/", System.StringComparison.Ordinal)) return;
            var importer = (AudioImporter)assetImporter;
            bool ambience = assetPath.EndsWith(".mp3", System.StringComparison.OrdinalIgnoreCase);
            bool stereo = music || ambience;
            importer.forceToMono = !stereo;
            importer.loadInBackground = stereo;
            var settings = importer.defaultSampleSettings;
            settings.loadType = music ? AudioClipLoadType.Streaming : ambience ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = stereo ? AudioCompressionFormat.Vorbis : AudioCompressionFormat.PCM;
            settings.sampleRateSetting = AudioSampleRateSetting.OverrideSampleRate;
            settings.sampleRateOverride = 44100;
            settings.quality = 0.65f;
            settings.preloadAudioData = !music;
            importer.defaultSampleSettings = settings;
        }
    }
}
