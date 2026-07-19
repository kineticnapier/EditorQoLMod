using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using ADOFAI;
using Microsoft.Win32;
using UnityEngine;

namespace Kiner.ADOFAIEditorQoL.Core
{
    internal static class CustomFontOperations
    {
        private const byte DefaultCharset = 1;
        private static readonly object CatalogLock = new object();
        private static FontCatalog catalog;

        public static string[] GetInstalledFontNames()
        {
            lock (CatalogLock)
            {
                catalog = BuildCatalog();
                return catalog.DisplayNames.ToArray();
            }
        }

        internal static string[] GetRuntimeFontCandidates(string displayName)
        {
            lock (CatalogLock)
            {
                if (catalog == null) catalog = BuildCatalog();
                return catalog.Resolve(displayName).ToArray();
            }
        }

        internal static string[] GetRuntimeFontPaths(string displayName)
        {
            lock (CatalogLock)
            {
                if (catalog == null) catalog = BuildCatalog();
                return catalog.ResolveFiles(displayName).ToArray();
            }
        }

        internal static bool FontNamesEquivalent(string left, string right)
        {
            return string.Equals(NormalizeLookupKey(left), NormalizeLookupKey(right),
                StringComparison.OrdinalIgnoreCase);
        }

        public static string Apply(scnEditor editor, string family)
        {
            if (editor == null) throw new ArgumentNullException("editor");
            family = (family ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(family) || family == "__no_font_match__")
                throw new ArgumentException("フォント名を選択してください。");

            List<LevelEvent> texts = GetSelectedTexts(editor);
            if (texts.Count == 0)
                throw new InvalidOperationException("テキスト装飾を1個以上選択してください。");

            string canonical;
            lock (CatalogLock)
            {
                if (catalog == null) catalog = BuildCatalog();
                canonical = catalog.DisplayNames.FirstOrDefault(x =>
                    string.Equals(x, family, StringComparison.OrdinalIgnoreCase));
            }

            if (canonical == null)
                throw new InvalidOperationException("このPCで確認できないフォントです: " + family +
                                                    "。一覧を更新するか、Windowsでフォントを再インストールしてください。");

            string marker = DecorationMarkerTags.CustomFontPrefix + DecorationMarkerTags.EncodeFontFamily(canonical);
            using (new EditorUndoScope(editor))
            {
                foreach (LevelEvent text in texts)
                    DecorationMarkerTags.ReplacePrefix(text, DecorationMarkerTags.CustomFontPrefix, marker);
                Refresh(editor);
            }
            return texts.Count + "個のテキスト装飾へ「" + canonical + "」を適用しました。";
        }

        public static string Remove(scnEditor editor)
        {
            if (editor == null) throw new ArgumentNullException("editor");
            List<LevelEvent> texts = GetSelectedTexts(editor);
            if (texts.Count == 0)
                throw new InvalidOperationException("テキスト装飾を1個以上選択してください。");

            int count = texts.Count(x => DecorationMarkerTags.FindTag(x, DecorationMarkerTags.CustomFontPrefix) != null);
            if (count == 0)
                throw new InvalidOperationException("選択したテキストには追加フォントが設定されていません。");

            using (new EditorUndoScope(editor))
            {
                foreach (LevelEvent text in texts)
                    DecorationMarkerTags.RemovePrefix(text, DecorationMarkerTags.CustomFontPrefix);
                Refresh(editor);
            }
            return count + "個のテキスト装飾を標準フォントへ戻しました。";
        }

        private static FontCatalog BuildCatalog()
        {
            FontCatalog result = new FontCatalog();

            try
            {
                string[] unityNames = Font.GetOSInstalledFontNames();
                string[] unityPaths = Font.GetPathsToOSFonts();

                if (unityNames != null)
                {
                    for (int i = 0; i < unityNames.Length; i++)
                    {
                        string name = unityNames[i];
                        result.Add(name, new[] { name }, true);

                        // Unity 2022 exposes the actual OS font file paths. When the
                        // arrays line up, remember the file so the runtime can construct
                        // Font from the file itself instead of asking Unity to resolve a
                        // Windows display name (which silently fell back for some fonts).
                        if (unityPaths != null && unityPaths.Length == unityNames.Length &&
                            i < unityPaths.Length && File.Exists(unityPaths[i]))
                        {
                            result.AddFontFile(name, unityPaths[i], new[] { name });
                        }
                    }
                }

                // Even if the name/path arrays do not line up, index every returned
                // path by its filename. Registry/GDI aliases can then connect the
                // selected display name to the actual file.
                if (unityPaths != null)
                {
                    foreach (string path in unityPaths)
                    {
                        if (string.IsNullOrEmpty(path) || !File.Exists(path)) continue;
                        string stem = Path.GetFileNameWithoutExtension(path);
                        result.AddFontFile(stem, path, new[]
                        {
                            stem,
                            stem == null ? null : stem.Replace('-', ' ').Replace('_', ' ')
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Main.Logger.Warning("UnityからOSフォント一覧またはパスを取得できませんでした: " + ex.Message);
            }

            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                try
                {
                    AddWindowsGdiFonts(result);
                }
                catch (Exception ex)
                {
                    Main.Logger.Warning("Windows GDIからフォント一覧を補完できませんでした: " + ex.Message);
                }

                AddRegistryFonts(result, Registry.LocalMachine,
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts", false);
                AddRegistryFonts(result, Registry.CurrentUser,
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts", true);
                AddRegistryFonts(result, Registry.LocalMachine,
                    @"SOFTWARE\WOW6432Node\Microsoft\Windows NT\CurrentVersion\Fonts", false);
            }

            if (result.DisplayNames.Count == 0)
            {
                foreach (string fallback in new[] { "Arial", "Segoe UI", "Meiryo", "Yu Gothic" })
                    result.Add(fallback, new[] { fallback }, true);
            }

            result.Sort();
            return result;
        }

        private static void AddRegistryFonts(FontCatalog destination, RegistryKey root, string path,
            bool userFont)
        {
            try
            {
                using (RegistryKey key = root.OpenSubKey(path, false))
                {
                    if (key == null) return;
                    foreach (string rawValueName in key.GetValueNames())
                    {
                        string displayName = NormalizeFontName(rawValueName);
                        if (string.IsNullOrEmpty(displayName)) continue;

                        List<string> candidates = new List<string>();
                        AddCandidate(candidates, displayName);

                        object rawValue = key.GetValue(rawValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                        string value = rawValue as string;
                        string fontPath = ResolveFontPath(value, userFont);
                        if (!string.IsNullOrEmpty(fontPath))
                        {
                            // Do not read every font file while the editor is opening.
                            // File-internal aliases are parsed lazily only for the font
                            // the user actually selects.
                            string stem = Path.GetFileNameWithoutExtension(fontPath);
                            AddCandidate(candidates, stem);
                            AddCandidate(candidates, stem == null ? null : stem.Replace('-', ' ').Replace('_', ' '));
                        }

                        destination.Add(displayName, candidates, false);
                        if (!string.IsNullOrEmpty(fontPath))
                            destination.AddFontFile(displayName, fontPath, candidates);
                    }
                }
            }
            catch (Exception ex)
            {
                Main.Logger.Warning("Windowsフォント登録情報を読めませんでした: " + ex.Message);
            }
        }

        private static string ResolveFontPath(string value, bool userFont)
        {
            string file = Environment.ExpandEnvironmentVariables((value ?? string.Empty).Trim().Trim('"'));
            if (string.IsNullOrEmpty(file)) return null;
            if (Path.IsPathRooted(file) && File.Exists(file)) return file;

            string windowsFonts = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts", file);
            if (File.Exists(windowsFonts)) return windowsFonts;

            string localFonts = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "Windows", "Fonts", file);
            if (File.Exists(localFonts)) return localFonts;

            if (userFont)
            {
                string profileFonts = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "AppData", "Local", "Microsoft", "Windows", "Fonts", file);
                if (File.Exists(profileFonts)) return profileFonts;
            }
            return null;
        }

        private static void AddWindowsGdiFonts(FontCatalog destination)
        {
            IntPtr hdc = GetDC(IntPtr.Zero);
            if (hdc == IntPtr.Zero) return;

            try
            {
                LOGFONT filter = new LOGFONT();
                filter.lfCharSet = DefaultCharset;
                EnumFontFamExProc callback = delegate(ref ENUMLOGFONTEX font, IntPtr metric,
                    uint fontType, IntPtr parameter)
                {
                    string family = NormalizeFontName(font.elfLogFont.lfFaceName);
                    string full = NormalizeFontName(font.elfFullName);
                    List<string> candidates = new List<string>();
                    AddCandidate(candidates, family);
                    AddCandidate(candidates, full);
                    destination.Add(family, candidates, false);
                    destination.Add(full, candidates, false);
                    return 1;
                };
                EnumFontFamiliesEx(hdc, ref filter, callback, IntPtr.Zero, 0);
                GC.KeepAlive(callback);
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, hdc);
            }
        }

        private static string NormalizeFontName(string value)
        {
            string name = (value ?? string.Empty).Trim();
            if (name.StartsWith("@", StringComparison.Ordinal)) return string.Empty;

            string[] registrySuffixes =
            {
                " (TrueType)", " (OpenType)", " (All res)", " (VGA res)",
                " (Plotter)", " (Raster)", " (Type 1)"
            };
            foreach (string suffix in registrySuffixes)
            {
                if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    name = name.Substring(0, name.Length - suffix.Length).Trim();
                    break;
                }
            }
            return name;
        }

        private static string NormalizeLookupKey(string value)
        {
            string source = NormalizeFontName(value).Replace("+", "plus");
            StringBuilder builder = new StringBuilder(source.Length);
            foreach (char c in source)
            {
                if (char.IsLetterOrDigit(c)) builder.Append(char.ToLowerInvariant(c));
            }
            return builder.ToString();
        }

        private static string StripStyleSuffix(string value)
        {
            string name = NormalizeFontName(value);
            string[] suffixes =
            {
                " Regular", " Normal", " Book", " Medium", " SemiBold", " Semibold",
                " DemiBold", " Demibold", " Bold", " ExtraBold", " Extra Bold",
                " Light", " ExtraLight", " Extra Light", " Thin", " Black", " Heavy",
                " Italic", " Oblique"
            };
            bool changed;
            do
            {
                changed = false;
                foreach (string suffix in suffixes)
                {
                    if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && name.Length > suffix.Length)
                    {
                        name = name.Substring(0, name.Length - suffix.Length).Trim();
                        changed = true;
                        break;
                    }
                }
            }
            while (changed);
            return name;
        }

        private static void AddCandidate(ICollection<string> values, string value)
        {
            string normalized = NormalizeFontName(value);
            if (string.IsNullOrEmpty(normalized)) return;
            foreach (string existing in values)
            {
                if (string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase)) return;
            }
            values.Add(normalized);
        }

        private static List<LevelEvent> GetSelectedTexts(scnEditor editor)
        {
            return editor.selectedDecorations == null
                ? new List<LevelEvent>()
                : editor.selectedDecorations.Where(x => x != null && x.eventType == LevelEventType.AddText).ToList();
        }

        private static void Refresh(scnEditor editor)
        {
            editor.UpdateDecorationObjects();
            if (editor.propertyControlDecorationsList != null)
                editor.propertyControlDecorationsList.RefreshItemsList(true);
        }

        private sealed class FontCatalog
        {
            private readonly Dictionary<string, List<string>> exact =
                new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, List<string>> normalized =
                new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, List<string>> fontFiles =
                new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, List<string>> parsedFileNames =
                new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            private readonly HashSet<string> unityNames =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            internal readonly List<string> DisplayNames = new List<string>();

            internal void Add(string displayName, IEnumerable<string> candidates, bool fromUnity)
            {
                string display = NormalizeFontName(displayName);
                if (string.IsNullOrEmpty(display)) return;
                if (!DisplayNames.Any(x => string.Equals(x, display, StringComparison.OrdinalIgnoreCase)))
                    DisplayNames.Add(display);

                List<string> exactList;
                if (!exact.TryGetValue(display, out exactList))
                {
                    exactList = new List<string>();
                    exact[display] = exactList;
                }

                AddCandidate(exactList, display);
                if (candidates != null)
                {
                    foreach (string candidate in candidates) AddCandidate(exactList, candidate);
                }

                AddToNormalized(display, exactList);
                foreach (string candidate in exactList) AddToNormalized(candidate, exactList);

                if (fromUnity)
                {
                    unityNames.Add(display);
                    foreach (string candidate in exactList) unityNames.Add(candidate);
                }
            }

            internal void AddFontFile(string displayName, string path, IEnumerable<string> aliases)
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

                AddFileKey(displayName, path);
                AddFileKey(StripStyleSuffix(displayName), path);

                if (aliases != null)
                {
                    foreach (string alias in aliases)
                    {
                        AddFileKey(alias, path);
                        AddFileKey(StripStyleSuffix(alias), path);
                    }
                }

                string stem = Path.GetFileNameWithoutExtension(path);
                AddFileKey(stem, path);
                AddFileKey(stem == null ? null : stem.Replace('-', ' ').Replace('_', ' '), path);
            }

            internal IEnumerable<string> Resolve(string displayName)
            {
                List<string> result = new List<string>();
                string display = NormalizeFontName(displayName);
                AddCandidate(result, display);
                AddStoredCandidates(result, display);

                string noStyle = StripStyleSuffix(display);
                AddCandidate(result, noStyle);
                AddStoredCandidates(result, noStyle);

                // Parse only the selected font file(s). This avoids reading every
                // TTF/OTF/TTC synchronously when the editor panel is created.
                List<string> paths = new List<string>();
                AddMatchingFiles(paths, display);
                AddMatchingFiles(paths, noStyle);
                foreach (string candidate in result.ToArray())
                    AddMatchingFiles(paths, candidate);

                foreach (string path in paths)
                {
                    List<string> aliases;
                    if (!parsedFileNames.TryGetValue(path, out aliases))
                    {
                        aliases = OpenTypeNameReader.ReadNames(path).ToList();
                        parsedFileNames[path] = aliases;
                    }

                    foreach (string alias in aliases)
                    {
                        AddCandidate(result, alias);
                        AddCandidate(result, StripStyleSuffix(alias));
                    }
                }

                // Unity's own spelling is the most reliable candidate. Move exact
                // Unity names first, then try file-internal aliases and Windows names.
                List<string> ordered = new List<string>();
                foreach (string value in result.Where(x => unityNames.Contains(x)))
                    AddCandidate(ordered, value);
                foreach (string value in result)
                    AddCandidate(ordered, value);
                return ordered;
            }

            internal IEnumerable<string> ResolveFiles(string displayName)
            {
                List<string> names = Resolve(displayName).ToList();
                List<string> paths = new List<string>();

                string display = NormalizeFontName(displayName);
                AddMatchingFiles(paths, display);
                AddMatchingFiles(paths, StripStyleSuffix(display));

                foreach (string name in names)
                {
                    AddMatchingFiles(paths, name);
                    AddMatchingFiles(paths, StripStyleSuffix(name));
                }

                // The registry association is authoritative enough for loading. Name
                // table parsing is only used to add aliases, not to discard paths;
                // some valid fonts have unusual or incomplete name records.
                foreach (string path in paths.ToArray())
                {
                    List<string> aliases;
                    if (!parsedFileNames.TryGetValue(path, out aliases))
                    {
                        aliases = OpenTypeNameReader.ReadNames(path).ToList();
                        parsedFileNames[path] = aliases;
                    }

                    foreach (string alias in aliases)
                    {
                        AddCandidate(names, alias);
                        AddCandidate(names, StripStyleSuffix(alias));
                    }
                }

                return paths;
            }

            internal void Sort()
            {
                DisplayNames.Sort(StringComparer.CurrentCultureIgnoreCase);
            }

            private void AddToNormalized(string name, IEnumerable<string> values)
            {
                string key = NormalizeLookupKey(name);
                if (string.IsNullOrEmpty(key)) return;

                List<string> list;
                if (!normalized.TryGetValue(key, out list))
                {
                    list = new List<string>();
                    normalized[key] = list;
                }

                foreach (string value in values) AddCandidate(list, value);
            }

            private void AddStoredCandidates(ICollection<string> destination, string name)
            {
                string normalizedName = NormalizeFontName(name);
                List<string> values;
                if (!string.IsNullOrEmpty(normalizedName) && exact.TryGetValue(normalizedName, out values))
                {
                    foreach (string value in values) AddCandidate(destination, value);
                }

                string key = NormalizeLookupKey(normalizedName);
                if (!string.IsNullOrEmpty(key) && normalized.TryGetValue(key, out values))
                {
                    foreach (string value in values) AddCandidate(destination, value);
                }
            }

            private void AddFileKey(string name, string path)
            {
                string key = NormalizeLookupKey(name);
                if (string.IsNullOrEmpty(key)) return;

                List<string> paths;
                if (!fontFiles.TryGetValue(key, out paths))
                {
                    paths = new List<string>();
                    fontFiles[key] = paths;
                }

                if (!paths.Any(x => string.Equals(x, path, StringComparison.OrdinalIgnoreCase)))
                    paths.Add(path);
            }

            private void AddMatchingFiles(ICollection<string> destination, string name)
            {
                string key = NormalizeLookupKey(name);
                if (string.IsNullOrEmpty(key)) return;

                List<string> paths;
                if (!fontFiles.TryGetValue(key, out paths)) return;
                foreach (string path in paths)
                {
                    if (!destination.Any(x => string.Equals(x, path, StringComparison.OrdinalIgnoreCase)))
                        destination.Add(path);
                }
            }
        }

        private static class OpenTypeNameReader
        {
            private static readonly HashSet<ushort> WantedNameIds = new HashSet<ushort>
            {
                1, 2, 4, 6, 16, 17
            };

            internal static IEnumerable<string> ReadNames(string path)
            {
                List<string> result = new List<string>();
                try
                {
                    byte[] data = File.ReadAllBytes(path);
                    foreach (int faceOffset in GetFaceOffsets(data))
                        ReadFaceNames(data, faceOffset, result);
                }
                catch
                {
                    // A broken or unsupported font file must not break the panel.
                }
                return result;
            }

            private static IEnumerable<int> GetFaceOffsets(byte[] data)
            {
                if (data == null || data.Length < 12) yield break;
                if (ReadTag(data, 0) != "ttcf")
                {
                    yield return 0;
                    yield break;
                }

                uint count = ReadUInt32(data, 8);
                int maximum = Math.Min((int)Math.Min(count, 256u), (data.Length - 12) / 4);
                for (int i = 0; i < maximum; i++)
                {
                    uint offset = ReadUInt32(data, 12 + i * 4);
                    if (offset < data.Length - 12) yield return (int)offset;
                }
            }

            private static void ReadFaceNames(byte[] data, int faceOffset, ICollection<string> destination)
            {
                if (!CanRead(data, faceOffset, 12)) return;
                int tableCount = ReadUInt16(data, faceOffset + 4);
                int directory = faceOffset + 12;
                int nameTable = -1;
                int nameLength = 0;

                for (int i = 0; i < tableCount; i++)
                {
                    int record = directory + i * 16;
                    if (!CanRead(data, record, 16)) break;
                    if (ReadTag(data, record) != "name") continue;
                    uint offset = ReadUInt32(data, record + 8);
                    uint length = ReadUInt32(data, record + 12);
                    if (offset > int.MaxValue || length > int.MaxValue) return;
                    nameTable = (int)offset;
                    nameLength = (int)length;
                    break;
                }

                if (nameTable < 0 || !CanRead(data, nameTable, Math.Min(nameLength, 6))) return;
                int count = ReadUInt16(data, nameTable + 2);
                int storage = nameTable + ReadUInt16(data, nameTable + 4);
                int records = nameTable + 6;
                Dictionary<ushort, List<string>> byId = new Dictionary<ushort, List<string>>();

                for (int i = 0; i < count; i++)
                {
                    int record = records + i * 12;
                    if (!CanRead(data, record, 12)) break;
                    ushort platform = ReadUInt16(data, record);
                    ushort nameId = ReadUInt16(data, record + 6);
                    if (!WantedNameIds.Contains(nameId)) continue;
                    int length = ReadUInt16(data, record + 8);
                    int offset = ReadUInt16(data, record + 10);
                    int stringPosition = storage + offset;
                    if (!CanRead(data, stringPosition, length)) continue;
                    string value = DecodeName(data, stringPosition, length, platform);
                    value = NormalizeFontName(value == null ? null : value.Replace("\0", string.Empty));
                    if (string.IsNullOrEmpty(value)) continue;

                    List<string> list;
                    if (!byId.TryGetValue(nameId, out list))
                    {
                        list = new List<string>();
                        byId[nameId] = list;
                    }
                    AddCandidate(list, value);
                    AddCandidate(destination, value);
                }

                AddCombinedNames(byId, 16, 17, destination);
                AddCombinedNames(byId, 1, 2, destination);
            }

            private static void AddCombinedNames(Dictionary<ushort, List<string>> names, ushort familyId,
                ushort styleId, ICollection<string> destination)
            {
                List<string> families;
                List<string> styles;
                if (!names.TryGetValue(familyId, out families) || !names.TryGetValue(styleId, out styles)) return;
                foreach (string family in families)
                {
                    foreach (string style in styles)
                    {
                        if (string.Equals(style, "Regular", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(style, "Normal", StringComparison.OrdinalIgnoreCase))
                            AddCandidate(destination, family);
                        AddCandidate(destination, family + " " + style);
                    }
                }
            }

            private static string DecodeName(byte[] data, int offset, int length, ushort platform)
            {
                if (platform == 0 || platform == 2 || platform == 3)
                {
                    if ((length & 1) != 0) length--;
                    char[] chars = new char[length / 2];
                    for (int i = 0; i < chars.Length; i++)
                        chars[i] = (char)((data[offset + i * 2] << 8) | data[offset + i * 2 + 1]);
                    return new string(chars);
                }

                StringBuilder builder = new StringBuilder(length);
                for (int i = 0; i < length; i++)
                {
                    byte value = data[offset + i];
                    builder.Append(value < 128 ? (char)value : '?');
                }
                return builder.ToString();
            }

            private static bool CanRead(byte[] data, int offset, int length)
            {
                return data != null && offset >= 0 && length >= 0 && offset <= data.Length - length;
            }

            private static ushort ReadUInt16(byte[] data, int offset)
            {
                if (!CanRead(data, offset, 2)) return 0;
                return (ushort)((data[offset] << 8) | data[offset + 1]);
            }

            private static uint ReadUInt32(byte[] data, int offset)
            {
                if (!CanRead(data, offset, 4)) return 0;
                return ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) |
                       ((uint)data[offset + 2] << 8) | data[offset + 3];
            }

            private static string ReadTag(byte[] data, int offset)
            {
                if (!CanRead(data, offset, 4)) return string.Empty;
                return new string(new[]
                {
                    (char)data[offset], (char)data[offset + 1],
                    (char)data[offset + 2], (char)data[offset + 3]
                });
            }
        }

        private delegate int EnumFontFamExProc(ref ENUMLOGFONTEX lpelfe, IntPtr lpntme,
            uint fontType, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
        private static extern int EnumFontFamiliesEx(IntPtr hdc, ref LOGFONT lpLogfont,
            EnumFontFamExProc lpEnumFontFamExProc, IntPtr lParam, uint dwFlags);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct LOGFONT
        {
            public int lfHeight;
            public int lfWidth;
            public int lfEscapement;
            public int lfOrientation;
            public int lfWeight;
            public byte lfItalic;
            public byte lfUnderline;
            public byte lfStrikeOut;
            public byte lfCharSet;
            public byte lfOutPrecision;
            public byte lfClipPrecision;
            public byte lfQuality;
            public byte lfPitchAndFamily;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string lfFaceName;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ENUMLOGFONTEX
        {
            public LOGFONT elfLogFont;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string elfFullName;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string elfStyle;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string elfScript;
        }
    }
}
