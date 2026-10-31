using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Sinkii09.UIFramework.Tests.Editor
{
    // Real assets in a throwaway folder: the validator, codegen and rename tool all work through the
    // AssetDatabase, so in-memory objects would test nothing. Nothing here creates scripts, so no
    // compile or domain reload is triggered mid-run.
    internal sealed class SoundAssetFixture : IDisposable
    {
        public readonly string Root;

        public SoundAssetFixture(string name)
        {
            Root = "Assets/__" + name;
            if (AssetDatabase.IsValidFolder(Root)) AssetDatabase.DeleteAsset(Root);
            AssetDatabase.CreateFolder("Assets", "__" + name);
        }

        public void Dispose() => AssetDatabase.DeleteAsset(Root);

        public string Folder(string relative)
        {
            string path = Root;
            foreach (string part in relative.Split('/'))
            {
                if (!AssetDatabase.IsValidFolder(path + "/" + part)) AssetDatabase.CreateFolder(path, part);
                path += "/" + part;
            }
            return path;
        }

        public AudioClip Clip(string name, AudioClipLoadType loadType = AudioClipLoadType.DecompressOnLoad,
            AudioCompressionFormat format = AudioCompressionFormat.PCM)
        {
            string path = $"{Root}/{name}.wav";
            File.WriteAllBytes(path, SilentWav(441));
            AssetDatabase.ImportAsset(path);
            var importer = (AudioImporter)AssetImporter.GetAtPath(path);
            var settings = importer.defaultSampleSettings;
            settings.loadType = loadType;
            settings.compressionFormat = format;
            importer.defaultSampleSettings = settings;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }

        public SoundGroup Group(string name, AudioBus bus = AudioBus.Ui) =>
            Save(name, Create<SoundGroup>(g => g.Bus = bus));

        public SoundEvent Event(string name, string soundPath, SoundGroup group, params AudioClip[] clips) =>
            Save(name, Create<SoundEvent>(e => { e.Path = soundPath; e.Group = group; e.Clips = clips; }));

        public SoundBank Bank(string relativePath, params SoundEvent[] events) =>
            Save(relativePath, Create<SoundBank>(b => b.Events = events));

        public AudioConfig Config(string name, UISoundTheme theme, params string[] residentKeys) =>
            Save(name, Create<AudioConfig>(c => { c.Theme = theme; c.ResidentBankKeys = residentKeys; }));

        public UISoundTheme Theme(string name, string click) =>
            Save(name, Create<UISoundTheme>(t => t.ButtonClick = new SoundRef(click)));

        public T Save<T>(string relativePath, T asset) where T : UnityEngine.Object
        {
            int slash = relativePath.LastIndexOf('/');
            string folder = slash < 0 ? Root : Folder(relativePath.Substring(0, slash));
            AssetDatabase.CreateAsset(asset, $"{folder}/{relativePath.Substring(slash + 1)}.asset");
            return asset;
        }

        public string Prefab(string name, Action<GameObject> build)
        {
            var go = new GameObject(name, typeof(RectTransform));
            try
            {
                build(go);
                string path = $"{Root}/{name}.prefab";
                PrefabUtility.SaveAsPrefabAsset(go, path);
                return path;
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        private static T Create<T>(Action<T> setup) where T : ScriptableObject
        {
            var asset = ScriptableObject.CreateInstance<T>();
            setup(asset);
            return asset;
        }

        // 16-bit mono 44.1 kHz PCM of silence.
        private static byte[] SilentWav(int samples)
        {
            int dataBytes = samples * 2;
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write(new[] { 'R', 'I', 'F', 'F' });
            writer.Write(36 + dataBytes);
            writer.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(44100);
            writer.Write(44100 * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write(new[] { 'd', 'a', 't', 'a' });
            writer.Write(dataBytes);
            writer.Write(new byte[dataBytes]);
            writer.Flush();
            return stream.ToArray();
        }
    }
}
