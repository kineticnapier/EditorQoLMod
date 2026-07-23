using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ADOFAI;

namespace Kiner.ADOFAIEditorQoL.Core
{
    internal static class DecorationMarkerTags
    {
        internal const string TextMaskPrefix = "qolTextMask_";
        internal const string CustomFontPrefix = "qolFont_";
        internal const string ManagedMultiTilePrefix = "qolMultiTile_";

        internal static string[] GetTags(LevelEvent decoration)
        {
            if (decoration == null || decoration.data == null) return new string[0];
            object raw;
            if (!decoration.data.TryGetValue("tag", out raw) || raw == null) return new string[0];
            return Convert.ToString(raw).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        }

        internal static string FindTag(LevelEvent decoration, string prefix)
        {
            return GetTags(decoration).FirstOrDefault(x => x.StartsWith(prefix, StringComparison.Ordinal));
        }

        internal static void AddTag(LevelEvent decoration, string tag)
        {
            List<string> tags = GetTags(decoration).ToList();
            if (!tags.Contains(tag)) tags.Add(tag);
            SetTags(decoration, tags);
        }

        internal static void RemoveTag(LevelEvent decoration, string tag)
        {
            SetTags(decoration, GetTags(decoration).Where(x => x != tag));
        }

        internal static void RemovePrefix(LevelEvent decoration, string prefix)
        {
            SetTags(decoration, GetTags(decoration).Where(x => !x.StartsWith(prefix, StringComparison.Ordinal)));
        }

        internal static void ReplacePrefix(LevelEvent decoration, string prefix, string replacement)
        {
            List<string> tags = GetTags(decoration)
                .Where(x => !x.StartsWith(prefix, StringComparison.Ordinal)).ToList();
            if (!string.IsNullOrEmpty(replacement)) tags.Add(replacement);
            SetTags(decoration, tags);
        }

        internal static string EncodeFontFamily(string family)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(family ?? string.Empty);
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        internal static string DecodeFontFamily(string marker)
        {
            if (string.IsNullOrEmpty(marker) || !marker.StartsWith(CustomFontPrefix, StringComparison.Ordinal))
                return string.Empty;
            string encoded = marker.Substring(CustomFontPrefix.Length).Replace('-', '+').Replace('_', '/');
            switch (encoded.Length % 4)
            {
                case 2: encoded += "=="; break;
                case 3: encoded += "="; break;
            }
            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            }
            catch
            {
                return string.Empty;
            }
        }

        private static void SetTags(LevelEvent decoration, IEnumerable<string> tags)
        {
            if (decoration == null) return;
            string value = string.Join(" ", (tags ?? Enumerable.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToArray());
            decoration.data["tag"] = value;
            if (decoration.disabled.ContainsKey("tag")) decoration.disabled["tag"] = false;
        }
    }
}
