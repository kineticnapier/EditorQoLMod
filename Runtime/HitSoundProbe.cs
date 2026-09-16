using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace Kiner.ADOFAIEditorQoL.Runtime
{
    internal static class HitSoundProbe
    {
        internal static AssetProbeResult Probe()
        {
            string baseDirectory = Path.Combine(Main.ModPath, "AssetProbe");
            Directory.CreateDirectory(baseDirectory);
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string exportDirectory = Path.Combine(baseDirectory, stamp + "-hitsounds-assets");
            Directory.CreateDirectory(exportDirectory);

            Type audioClipType = ResolveAudioClipType();
            UnityEngine.Object[] loadedClips = Resources.FindObjectsOfTypeAll(audioClipType);
            AudioManager manager = AudioManager.Instance;
            if (manager == null)
                throw new InvalidOperationException("AudioManager.Instance がまだ生成されていません。");

            var report = new StringBuilder(16384);
            var manifest = new StringBuilder(4096);
            report.AppendLine("ADOFAI Editor QoL HitSound Probe");
            report.AppendLine("Mod version: " + ModVersion.Current);
            report.AppendLine("Unity: " + Application.unityVersion);
            report.AppendLine("AudioClip runtime type: " + audioClipType.AssemblyQualifiedName);
            report.AppendLine("Initially loaded AudioClips: " + loadedClips.Length);
            report.AppendLine("Export directory: " + exportDirectory);
            report.AppendLine();
            manifest.AppendLine("HitSound\tFile\tOffsetSeconds\tSamples\tChannels\tFrequency");

            var clipsByName = new Dictionary<string, UnityEngine.Object>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < loadedClips.Length; i++)
            {
                UnityEngine.Object clip = loadedClips[i];
                if (clip == null || string.IsNullOrEmpty(clip.name)) continue;
                if (!clipsByName.ContainsKey(clip.name)) clipsByName.Add(clip.name, clip);
            }

            int exported = 0;
            int missing = 0;
            int lazyLoaded = 0;

            foreach (HitSound hitSound in Enum.GetValues(typeof(HitSound)))
            {
                string name = hitSound.ToString();
                if (string.Equals(name, "None", StringComparison.OrdinalIgnoreCase))
                    continue;

                string key = "snd" + name;
                UnityEngine.Object clip;
                bool wasLoaded = clipsByName.TryGetValue(key, out clip) && clip != null;
                if (!wasLoaded)
                {
                    clip = FindOrLoadAudioClip(manager, key, audioClipType, report);
                    if (clip != null)
                    {
                        lazyLoaded++;
                        clipsByName[key] = clip;
                    }
                }

                if (clip == null)
                {
                    report.AppendLine(name + ": MISSING after AudioManager.FindOrLoadAudioClip (expected " + key + ")");
                    missing++;
                    continue;
                }

                try
                {
                    int samples = ReadIntProperty(clip, "samples");
                    int channels = ReadIntProperty(clip, "channels");
                    int frequency = ReadIntProperty(clip, "frequency");

                    string fileName = key + ".wav";
                    string path = Path.Combine(exportDirectory, fileName);
                    ExportPcm16Wav(clip, samples, channels, frequency, path);

                    double offset = 0.0;
                    try
                    {
                        if (ADOBase.gc != null && ADOBase.gc.hitSoundOffsets != null)
                            ADOBase.gc.hitSoundOffsets.TryGetValue(hitSound, out offset);
                    }
                    catch (Exception ex)
                    {
                        report.AppendLine(name + ": offset lookup failed: " + ex.Message);
                    }

                    manifest.Append(name).Append('\t')
                        .Append(fileName).Append('\t')
                        .Append(offset.ToString("R", CultureInfo.InvariantCulture)).Append('\t')
                        .Append(samples.ToString(CultureInfo.InvariantCulture)).Append('\t')
                        .Append(channels.ToString(CultureInfo.InvariantCulture)).Append('\t')
                        .Append(frequency.ToString(CultureInfo.InvariantCulture)).AppendLine();
                    report.AppendLine(name + ": " + clip.name + " -> " + fileName +
                                      " | " + samples + " samples | " + channels + " ch | " +
                                      frequency + " Hz | offset " + offset.ToString("R", CultureInfo.InvariantCulture) +
                                      (wasLoaded ? " | already loaded" : " | lazy-loaded"));
                    exported++;
                }
                catch (Exception ex)
                {
                    report.AppendLine(name + ": EXPORT FAILED: " + ex);
                }
            }

            string manifestPath = Path.Combine(exportDirectory, "hitsounds.tsv");
            File.WriteAllText(manifestPath, manifest.ToString(), new UTF8Encoding(false));
            report.AppendLine();
            report.AppendLine("Exported: " + exported);
            report.AppendLine("Lazy-loaded: " + lazyLoaded);
            report.AppendLine("Missing: " + missing);
            report.AppendLine("Manifest: " + manifestPath);

            string reportPath = Path.Combine(baseDirectory, stamp + "-hitsounds.txt");
            File.WriteAllText(reportPath, report.ToString(), new UTF8Encoding(false));
            if (Main.Logger != null)
                Main.Logger.Log("HitSound Probe: " + exported + " WAVs (" + lazyLoaded + " lazy-loaded) -> " + exportDirectory);

            return new AssetProbeResult
            {
                TargetName = "runtime-hitsounds",
                ReportPath = reportPath,
                ExportDirectory = exportDirectory,
                GameObjectCount = 0,
                ComponentCount = 0,
                ExportedTextureCount = exported,
                RootSummary = exported + " hitsounds / " + missing + " missing"
            };
        }

        private static Type ResolveAudioClipType()
        {
            Type type = Type.GetType("UnityEngine.AudioClip, UnityEngine.AudioModule", false);
            if (type != null) return type;

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Assembly assembly = assemblies[i];
                if (!string.Equals(assembly.GetName().Name, "UnityEngine.AudioModule", StringComparison.Ordinal))
                    continue;

                type = assembly.GetType("UnityEngine.AudioClip", false);
                if (type != null) return type;
            }

            throw new InvalidOperationException("UnityEngine.AudioClip のruntime型が見つかりません。");
        }

        private static UnityEngine.Object FindOrLoadAudioClip(
            AudioManager manager,
            string key,
            Type audioClipType,
            StringBuilder report)
        {
            try
            {
                MethodInfo method = manager.GetType().GetMethod(
                    "FindOrLoadAudioClip",
                    BindingFlags.Instance | BindingFlags.Public,
                    null,
                    new[] { typeof(string), typeof(string), typeof(bool) },
                    null);
                if (method == null)
                    throw new MissingMethodException(manager.GetType().FullName,
                        "FindOrLoadAudioClip(string, string, bool)");

                object value = method.Invoke(manager, new object[] { key, null, false });
                if (value == null)
                    return null;
                if (!audioClipType.IsInstanceOfType(value))
                    throw new InvalidCastException("FindOrLoadAudioClip returned " + value.GetType().FullName);
                return value as UnityEngine.Object;
            }
            catch (Exception ex)
            {
                report.AppendLine(key + ": lazy-load failed: " + Unwrap(ex).Message);
                return null;
            }
        }

        private static Exception Unwrap(Exception ex)
        {
            TargetInvocationException target = ex as TargetInvocationException;
            return target != null && target.InnerException != null ? target.InnerException : ex;
        }

        private static int ReadIntProperty(UnityEngine.Object clip, string propertyName)
        {
            PropertyInfo property = clip.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            if (property == null)
                throw new MissingMemberException(clip.GetType().FullName, propertyName);

            object value = property.GetValue(clip, null);
            if (value == null)
                throw new InvalidOperationException(propertyName + " returned null for " + clip.name);
            return Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }

        private static void ExportPcm16Wav(UnityEngine.Object clip, int samplesPerChannel, int channelsValue, int sampleRate, string path)
        {
            if (samplesPerChannel <= 0 || channelsValue <= 0 || sampleRate <= 0)
                throw new InvalidOperationException("Invalid AudioClip format: " + clip.name);

            int sampleValues = checked(samplesPerChannel * channelsValue);
            var samples = new float[sampleValues];

            MethodInfo getData = clip.GetType().GetMethod(
                "GetData",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new[] { typeof(float[]), typeof(int) },
                null);
            if (getData == null)
                throw new MissingMethodException(clip.GetType().FullName, "GetData(float[], int)");

            bool success = InvokeGetData(getData, clip, samples);
            if (!success)
            {
                // Some clips are known to exist but have not loaded their sample data yet.
                // Ask Unity to load it, then retry once. This still avoids a compile-time
                // UnityEngine.AudioModule reference.
                MethodInfo loadAudioData = clip.GetType().GetMethod(
                    "LoadAudioData",
                    BindingFlags.Instance | BindingFlags.Public,
                    null,
                    Type.EmptyTypes,
                    null);
                if (loadAudioData != null)
                {
                    loadAudioData.Invoke(clip, null);
                    success = InvokeGetData(getData, clip, samples);
                }
            }

            if (!success)
                throw new InvalidOperationException("AudioClip.GetData returned false after LoadAudioData retry: " + clip.name);

            const short bitsPerSample = 16;
            short channels = checked((short)channelsValue);
            short blockAlign = checked((short)(channels * (bitsPerSample / 8)));
            int byteRate = checked(sampleRate * blockAlign);
            int dataBytes = checked(sampleValues * 2);

            using (FileStream stream = File.Create(path))
            using (var writer = new BinaryWriter(stream, Encoding.ASCII))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + dataBytes);
                writer.Write(Encoding.ASCII.GetBytes("WAVE"));
                writer.Write(Encoding.ASCII.GetBytes("fmt "));
                writer.Write(16);
                writer.Write((short)1);
                writer.Write(channels);
                writer.Write(sampleRate);
                writer.Write(byteRate);
                writer.Write(blockAlign);
                writer.Write(bitsPerSample);
                writer.Write(Encoding.ASCII.GetBytes("data"));
                writer.Write(dataBytes);

                for (int i = 0; i < samples.Length; i++)
                {
                    float sample = Math.Max(-1f, Math.Min(1f, samples[i]));
                    short pcm = sample <= -1f
                        ? short.MinValue
                        : (short)Math.Round(sample * short.MaxValue);
                    writer.Write(pcm);
                }
            }
        }

        private static bool InvokeGetData(MethodInfo getData, UnityEngine.Object clip, float[] samples)
        {
            object result = getData.Invoke(clip, new object[] { samples, 0 });
            return !(result is bool) || (bool)result;
        }
    }
}
