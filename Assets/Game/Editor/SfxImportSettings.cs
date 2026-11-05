using UnityEditor;
using UnityEngine;

namespace AiHackathon.Game.Editor
{
    /// <summary>Repeatable import contract for generated SFX, including future replacements.</summary>
    public sealed class SfxImportSettings : AssetPostprocessor
    {
        void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith("Assets/Game/Resources/Audio/SFX/", System.StringComparison.Ordinal)) return;
            var importer = (AudioImporter)assetImporter;
            bool ambience = assetPath.EndsWith(".mp3", System.StringComparison.OrdinalIgnoreCase);
            importer.forceToMono = !ambience;
            importer.loadInBackground = ambience;
            var settings = importer.defaultSampleSettings;
            settings.loadType = ambience ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = ambience ? AudioCompressionFormat.Vorbis : AudioCompressionFormat.PCM;
            settings.sampleRateSetting = AudioSampleRateSetting.OverrideSampleRate;
            settings.sampleRateOverride = 44100;
            settings.quality = 0.65f;
            settings.preloadAudioData = true;
            importer.defaultSampleSettings = settings;
        }
    }
}
