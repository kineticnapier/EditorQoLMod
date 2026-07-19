using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kiner.ADOFAIEditorQoL.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Kiner.ADOFAIEditorQoL.Runtime
{
    public sealed class CustomTextFontRuntime : MonoBehaviour
    {
        private static readonly Dictionary<string, Font> FileFontCache =
            new Dictionary<string, Font>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Font> SingleFontCache =
            new Dictionary<string, Font>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Font> CompositeFontCache =
            new Dictionary<string, Font>(StringComparer.OrdinalIgnoreCase);

        private static readonly string[] CommonFallbacks =
        {
            "Meiryo",
            "Yu Gothic UI",
            "Yu Gothic",
            "Segoe UI",
            "Arial"
        };

        private scrTextDecoration decoration;
        private Text text;
        private string family;
        private Font appliedFont;
        private bool applying;
        private bool warned;
        private bool loggedResolution;

        internal void Configure(scrTextDecoration source, string fontFamily)
        {
            decoration = source;
            text = source == null ? null : source.text;
            family = (fontFamily ?? string.Empty).Trim();
            warned = false;
            loggedResolution = false;
            ApplyOnce();
        }

        private void ApplyOnce()
        {
            if (applying || text == null || string.IsNullOrEmpty(family)) return;

            applying = true;
            try
            {
                int size = Math.Max(16, text.fontSize);
                string characters = text.text ?? string.Empty;
                string[] candidates = CustomFontOperations.GetRuntimeFontCandidates(family);
                string[] paths = CustomFontOperations.GetRuntimeFontPaths(family);

                string workingCandidate;
                string reportedName;
                string sourceDescription;
                Font selectedFont = ResolveSelectedFont(paths, candidates, size,
                    out workingCandidate, out reportedName, out sourceDescription);
                if (selectedFont == null)
                {
                    WarnOnce("フォント「" + family + "」の実ファイルとWindows名をUnityで解決できませんでした。標準フォントを維持します。");
                    return;
                }

                // Assign the real file-backed font before probing it. Some Unity
                // dynamic fonts do not initialize their glyph atlas until a UI.Text is
                // actually using the Font. v0.6.7 probed an unattached Font, saw every
                // glyph as missing, and replaced it with the fallback chain.
                text.font = selectedFont;
                text.SetAllDirty();
                try
                {
                    text.cachedTextGenerator.Invalidate();
                    text.cachedTextGeneratorForLayout.Invalidate();
                    Canvas.ForceUpdateCanvases();
                }
                catch
                {
                    // The generator may not exist yet during decoration setup.
                }

                int missingGlyphs;
                bool direct = PrepareAndCheckCharacters(selectedFont, characters,
                    text.fontSize, text.fontStyle, out missingGlyphs);
                Font result = selectedFont;

                if (!direct)
                {
                    // Only use an OS fallback chain when the real file-backed font has
                    // genuinely failed to provide at least one requested glyph. Include
                    // every known alias, because file-internal names are not always valid
                    // OS family names even though new Font(path) can load them.
                    string[] chain = BuildFontChain(candidates, reportedName);
                    string key = string.Join("\n", chain) + "\n" + size;
                    Font fallbackFont;
                    if (!CompositeFontCache.TryGetValue(key, out fallbackFont) || fallbackFont == null)
                    {
                        try
                        {
                            fallbackFont = Font.CreateDynamicFontFromOSFont(chain, size);
                            if (fallbackFont != null) CompositeFontCache[key] = fallbackFont;
                        }
                        catch (Exception ex)
                        {
                            WarnOnce("フォント「" + family + "」のフォールバック作成に失敗しました: " + ex.Message);
                            fallbackFont = null;
                        }
                    }

                    // Do not silently throw away a successfully loaded file font. The
                    // fallback object is accepted only when it can actually provide all
                    // currently displayed glyphs; otherwise keep the selected font so
                    // supported characters still use the requested face.
                    int fallbackMissing;
                    if (fallbackFont != null && PrepareAndCheckCharacters(fallbackFont, characters,
                        text.fontSize, text.fontStyle, out fallbackMissing))
                    {
                        result = fallbackFont;
                    }
                }

                appliedFont = result;

                text.font = appliedFont;
                text.SetAllDirty();

                if (!loggedResolution)
                {
                    loggedResolution = true;
                    string reported = appliedFont.fontNames == null
                        ? string.Empty
                        : string.Join(", ", appliedFont.fontNames);
                    Main.Logger.Log("Custom font loaded from " + sourceDescription + ": " + family +
                                    " -> " + reportedName +
                                    " (candidate=" + workingCandidate +
                                    ", mode=" + (ReferenceEquals(appliedFont, selectedFont) ? "direct-file" : "fallback-chain") +
                                    ", selectedMissing=" + missingGlyphs +
                                    ", assigned=" + (text.font == appliedFont ? "yes" : "no") + ")" +
                                    (string.IsNullOrEmpty(reported) ? string.Empty : " [" + reported + "]"));
                }
            }
            finally
            {
                applying = false;
            }
        }

        private static Font ResolveSelectedFont(IEnumerable<string> paths, IEnumerable<string> candidates,
            int size, out string workingCandidate, out string reportedName, out string sourceDescription)
        {
            workingCandidate = null;
            reportedName = null;
            sourceDescription = null;

            string[] candidateArray = (candidates ?? Enumerable.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            // Prefer the actual TTF/OTF/TTC file. CreateDynamicFontFromOSFont can
            // return a valid-looking Font object while silently rendering another OS
            // font when the supplied Windows display name is not the name Unity uses.
            foreach (string path in (paths ?? Enumerable.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x) && File.Exists(x))
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string cacheKey = path;
                Font font;
                if (!FileFontCache.TryGetValue(cacheKey, out font) || font == null)
                {
                    try
                    {
                        font = new Font(path);
                        if (font != null)
                            FileFontCache[cacheKey] = font;
                    }
                    catch
                    {
                        font = null;
                    }
                }

                if (font == null) continue;
                if (!IsRequestedFace(font, candidateArray)) continue;

                workingCandidate = path;
                reportedName = PreferredResolvedName(font, Path.GetFileNameWithoutExtension(path));
                sourceDescription = "file " + path;
                return font;
            }

            foreach (string candidate in candidateArray)
            {
                string cacheKey = candidate + "\n" + size;
                Font font;
                if (!SingleFontCache.TryGetValue(cacheKey, out font) || font == null)
                {
                    try
                    {
                        font = Font.CreateDynamicFontFromOSFont(candidate, size);
                        if (font != null && IsRequestedFace(font, candidateArray))
                            SingleFontCache[cacheKey] = font;
                        else
                            font = null;
                    }
                    catch
                    {
                        font = null;
                    }
                }

                if (font == null) continue;
                workingCandidate = candidate;
                reportedName = PreferredResolvedName(font, candidate);
                sourceDescription = "OS name";
                return font;
            }

            return null;
        }

        private static bool IsRequestedFace(Font font, IEnumerable<string> acceptedNames)
        {
            if (font == null) return false;
            string[] reported = font.fontNames;
            if (reported == null || reported.Length == 0) return true;

            foreach (string actual in reported)
            {
                foreach (string expected in acceptedNames)
                {
                    if (CustomFontOperations.FontNamesEquivalent(actual, expected)) return true;
                }
            }
            return false;
        }

        private static string PreferredResolvedName(Font font, string requested)
        {
            if (font != null && font.fontNames != null)
            {
                string reported = font.fontNames.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
                if (!string.IsNullOrEmpty(reported)) return reported;
            }
            return requested;
        }

        private static bool PrepareAndCheckCharacters(Font font, string characters,
            int size, FontStyle style, out int missingGlyphs)
        {
            missingGlyphs = 0;
            if (font == null) return false;
            if (string.IsNullOrEmpty(characters)) return true;

            try
            {
                if (font.dynamic)
                    font.RequestCharactersInTexture(characters, size, style);
            }
            catch (Exception ex)
            {
                Main.Logger.Warning("Custom font glyph request failed: " + ex.Message);
            }

            HashSet<char> checkedCharacters = new HashSet<char>();
            foreach (char character in characters)
            {
                if (char.IsControl(character)) continue;
                if (!checkedCharacters.Add(character)) continue;
                if (char.IsSurrogate(character))
                {
                    missingGlyphs++;
                    continue;
                }

                CharacterInfo info;
                bool available;
                try
                {
                    available = font.GetCharacterInfo(character, out info, size, style);
                    if (!available)
                        available = font.HasCharacter(character);
                }
                catch
                {
                    available = font.HasCharacter(character);
                }

                if (!available) missingGlyphs++;
            }
            return missingGlyphs == 0;
        }

        private static string[] BuildFontChain(IEnumerable<string> candidates, string reportedName)
        {
            List<string> result = new List<string>();
            if (candidates != null)
            {
                foreach (string candidate in candidates) AddUnique(result, candidate);
            }
            AddUnique(result, reportedName);
            foreach (string fallback in CommonFallbacks) AddUnique(result, fallback);
            return result.ToArray();
        }

        private static void AddUnique(ICollection<string> values, string value)
        {
            string normalized = (value ?? string.Empty).Trim();
            if (normalized.Length == 0) return;
            foreach (string existing in values)
            {
                if (string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase)) return;
            }
            values.Add(normalized);
        }

        private void WarnOnce(string message)
        {
            if (warned) return;
            warned = true;
            Main.Logger.Warning(message);
        }
    }
}
