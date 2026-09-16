using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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

            AudioManager manager = AudioManager.Instance;
            if (manager == null) throw new InvalidOperationException("AudioManager.Instance がまだ生成されていません。");

            var report = new StringBuilder(16384);
            var manifest = new StringBuilder(4096);
            report.AppendLine("ADOFAI Editor QoL HitSound Probe");
            report.AppendLine("Mod version: " + ModVersion.Current);
            report.AppendLine("Unity: " + Application.unityVersion);
            report.AppendLine("Export directory: " + exportDirectory);
            report.AppendLine();
            manifest.AppendLine("HitSound\tFile\tOffsetSeconds\tSamples\tChannels\tFrequency");

            AudioClip[] loadedClips = Resources.FindObjectsOfTypeAll<AudioClip>();
            int exported = 0;
            int missing = 0;

            foreach (HitSound hitSound in Enum.GetValues(typeof(HitSound)))
            {
                string name = hitSound.ToString();
                if (string.Equals(name, "None", StringComparison.OrdinalIgnoreCase))
                    continue;

                string key = "snd" + name;
                AudioClip clip = null;
                if (manager.audioLib != null)
                    manager.audioLib.TryGetValue(key, out clip);

                if (clip == null)
                {
                    for (int i = 0; i < loadedClips.Length; i++)
                    {
                        AudioClip candidate = loadedClips[i];
                        if (candidate != null && string.Equals(candidate.name, key, StringComparison.OrdinalIgnoreCase))
                        {
                            clip = candidate;
                            break;
                        }
                    }
                }

                if (clip == null)
                {
                    report.AppendLine(name + ": MISSING (expected " + key + ")");
                    missing++;
                    continue;
                }

                try
                {
                    string fileName = key + ".wav";
                    string path = Path.Combine(exportDirectory, fileName);
                    ExportPcm16Wav(clip, path);

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
                        .Append(clip.samples.ToString(CultureInfo.InvariantCulture)).Append('\t')
                        .Append(clip.channels.ToString(CultureInfo.InvariantCulture)).Append('\t')
                        .Append(clip.frequency.ToString(CultureInfo.InvariantCulture)).AppendLine();
                    report.AppendLine(name + ": " + clip.name + " -> " + fileName +
                                      " | " + clip.samples + " samples | " + clip.channels + " ch | " +
                                      clip.frequency + " Hz | offset " + offset.ToString("R", CultureInfo.InvariantCulture));
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
            report.AppendLine("Missing: " + missing);
            report.AppendLine("Manifest: " + manifestPath);

            string reportPath = Path.Combine(baseDirectory, stamp + "-hitsounds.txt");
            File.WriteAllText(reportPath, report.ToString(), new UTF8Encoding(false));
            if (Main.Logger != null)
                Main.Logger.Log("HitSound Probe: " + exported + " WAVs -> " + exportDirectory);

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

        private static void ExportPcm16Wav(AudioClip clip, string path)
        {
            if (clip.samples <= 0 || clip.channels <= 0 || clip.frequency <= 0)
                throw new InvalidOperationException("Invalid AudioClip format: " + clip.name);

            int sampleValues = checked(clip.samples * clip.channels);
            var samples = new float[sampleValues];
            if (!clip.GetData(samples, 0))
                throw new InvalidOperationException("AudioClip.GetData returned false: " + clip.name);

            const short bitsPerSample = 16;
            short channels = checked((short)clip.channels);
            int sampleRate = clip.frequency;
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
                    float sample = Mathf.Clamp(samples[i], -1f, 1f);
                    short pcm = sample <= -1f
                        ? short.MinValue
                        : (short)Mathf.RoundToInt(sample * short.MaxValue);
                    writer.Write(pcm);
                }
            }
        }
    }
}
