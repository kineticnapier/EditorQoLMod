using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using ADOFAI;
using UnityEngine;

namespace Kiner.ADOFAIEditorQoL.Runtime
{
    /// <summary>
    /// Dumps the editor's actual event/icon dictionaries and the special floor-icon
    /// sprites from the running game. Sprite pixels are cropped from their source
    /// textures locally; nothing is bundled with the mod.
    /// </summary>
    internal static class IconCatalogProbe
    {
        private const int MaxRuntimeFloorSamples = 512;

        private sealed class Context
        {
            internal readonly StringBuilder Report = new StringBuilder(65536);
            internal readonly HashSet<int> ExportedSprites = new HashSet<int>();
            internal string ExportDirectory;
        }

        internal static AssetProbeResult Probe()
        {
            string baseDirectory = Path.Combine(Main.ModPath, "AssetProbe");
            Directory.CreateDirectory(baseDirectory);
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string exportDirectory = Path.Combine(baseDirectory, stamp + "-icon-catalog-assets");
            Directory.CreateDirectory(exportDirectory);

            Context context = new Context { ExportDirectory = exportDirectory };
            StringBuilder report = context.Report;
            report.AppendLine("ADOFAI Editor QoL Icon Catalog Probe");
            report.AppendLine("Mod version: " + ModVersion.Current);
            report.AppendLine("Unity: " + Application.unityVersion);
            report.AppendLine("Asset export directory: " + exportDirectory);
            report.AppendLine();

            DumpLevelEventIcons(context);
            DumpEventCategoryIcons(context);
            DumpFloorIconAssets(context);
            DumpRuntimeFloorSamples(context);

            string reportPath = Path.Combine(baseDirectory, stamp + "-icon-catalog.txt");
            File.WriteAllText(reportPath, report.ToString(), new UTF8Encoding(false));
            if (Main.Logger != null)
                Main.Logger.Log("Icon Catalog Probe -> " + reportPath + " (" + context.ExportedSprites.Count + " sprite PNGs)");

            return new AssetProbeResult
            {
                TargetName = "Icon Catalog",
                ReportPath = reportPath,
                ExportDirectory = exportDirectory,
                GameObjectCount = 0,
                ComponentCount = 0,
                ExportedTextureCount = context.ExportedSprites.Count,
                RootSummary = "event/category/floor icon catalog"
            };
        }

        private static void DumpLevelEventIcons(Context context)
        {
            context.Report.AppendLine("[LevelEventType -> Sprite]");
            Array values = Enum.GetValues(typeof(LevelEventType));
            for (int i = 0; i < values.Length; i++)
            {
                LevelEventType type = (LevelEventType)values.GetValue(i);
                string resourcePath = "LevelEditor/LevelEvents/" + type;
                Sprite dictionarySprite = null;
                if (GCS.levelEventIcons != null)
                    GCS.levelEventIcons.TryGetValue(type, out dictionarySprite);
                Sprite resourceSprite = Resources.Load<Sprite>(resourcePath);
                Sprite sprite = dictionarySprite != null ? dictionarySprite : resourceSprite;

                context.Report.Append("  ").Append(type)
                    .Append(" | resource=").Append(resourcePath)
                    .Append(" | dictionary=").Append(dictionarySprite != null ? DescribeSprite(dictionarySprite) : "<missing>")
                    .Append(" | Resources.Load=").Append(resourceSprite != null ? DescribeSprite(resourceSprite) : "<missing>")
                    .AppendLine();
                if (sprite != null)
                    ExportSprite(sprite, "event-" + type, context, "    ");
            }
            context.Report.AppendLine();
        }

        private static void DumpEventCategoryIcons(Context context)
        {
            context.Report.AppendLine("[LevelEventCategory -> Sprite]");
            Array values = Enum.GetValues(typeof(LevelEventCategory));
            for (int i = 0; i < values.Length; i++)
            {
                LevelEventCategory category = (LevelEventCategory)values.GetValue(i);
                string resourcePath = "LevelEditor/EventCategories/" + category;
                Sprite dictionarySprite = null;
                if (GCS.eventCategoryIcons != null)
                    GCS.eventCategoryIcons.TryGetValue(category, out dictionarySprite);
                Sprite resourceSprite = Resources.Load<Sprite>(resourcePath);
                Sprite sprite = dictionarySprite != null ? dictionarySprite : resourceSprite;

                context.Report.Append("  ").Append(category)
                    .Append(" | resource=").Append(resourcePath)
                    .Append(" | dictionary=").Append(dictionarySprite != null ? DescribeSprite(dictionarySprite) : "<missing>")
                    .Append(" | Resources.Load=").Append(resourceSprite != null ? DescribeSprite(resourceSprite) : "<missing>")
                    .AppendLine();
                if (sprite != null)
                    ExportSprite(sprite, "category-" + category, context, "    ");
            }
            context.Report.AppendLine();
        }

        private static void DumpFloorIconAssets(Context context)
        {
            context.Report.AppendLine("[RDConstants special floor-icon sprites]");
            RDConstants constants = RDConstants.data;
            if (constants == null)
            {
                context.Report.AppendLine("  RDConstants.data = <null>");
                context.Report.AppendLine();
                return;
            }

            FieldInfo[] fields = typeof(RDConstants).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Array.Sort(fields, delegate(FieldInfo a, FieldInfo b) { return string.CompareOrdinal(a.Name, b.Name); });
            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo field = fields[i];
                if (!typeof(Sprite).IsAssignableFrom(field.FieldType)) continue;
                if (!IsFloorIconField(field.Name)) continue;

                Sprite sprite = null;
                try { sprite = field.GetValue(constants) as Sprite; }
                catch (Exception ex)
                {
                    context.Report.Append("  ").Append(field.Name).Append(" = <error: ").Append(ex.GetType().Name).AppendLine(">");
                    continue;
                }

                context.Report.Append("  ").Append(field.Name).Append(" = ")
                    .Append(sprite != null ? DescribeSprite(sprite) : "<null>").AppendLine();
                if (sprite != null)
                    ExportSprite(sprite, "floor-" + field.Name, context, "    ");
            }
            context.Report.AppendLine();
        }

        private static void DumpRuntimeFloorSamples(Context context)
        {
            context.Report.AppendLine("[Loaded editor floor icon states]");
            scrLevelMaker maker = scrLevelMaker.instance;
            if (maker == null || maker.listFloors == null || maker.listFloors.Count == 0)
            {
                context.Report.AppendLine("  No loaded editor floors.");
                context.Report.AppendLine();
                return;
            }

            FieldInfo lastIconField = typeof(scrFloor).GetField("lastIconSprite", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo lastOutlineField = typeof(scrFloor).GetField("lastIconOutlineSprite", BindingFlags.Instance | BindingFlags.NonPublic);
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            int written = 0;

            for (int i = 0; i < maker.listFloors.Count && written < MaxRuntimeFloorSamples; i++)
            {
                scrFloor floor = maker.listFloors[i];
                if (floor == null) continue;
                Sprite icon = GetPrivateSprite(lastIconField, floor);
                Sprite outline = GetPrivateSprite(lastOutlineField, floor);
                string key = floor.floorIcon + "|" + floor.eventIcon + "|" + SpriteName(icon) + "|" + SpriteName(outline);
                if (!seen.Add(key)) continue;

                context.Report.Append("  floor=").Append(floor.seqID)
                    .Append(" | floorIcon=").Append(floor.floorIcon)
                    .Append(" | eventIcon=").Append(floor.eventIcon)
                    .Append(" | icon=").Append(icon != null ? DescribeSprite(icon) : "<null>")
                    .Append(" | outline=").Append(outline != null ? DescribeSprite(outline) : "<null>")
                    .AppendLine();
                if (icon != null) ExportSprite(icon, "runtime-icon-" + floor.floorIcon + "-" + floor.eventIcon, context, "    ");
                if (outline != null) ExportSprite(outline, "runtime-outline-" + floor.floorIcon + "-" + floor.eventIcon, context, "    ");
                written++;
            }

            context.Report.Append("  unique states written: ").Append(written)
                .Append(" / floors scanned: ").Append(maker.listFloors.Count).AppendLine();
            context.Report.AppendLine();
        }

        private static Sprite GetPrivateSprite(FieldInfo field, scrFloor floor)
        {
            if (field == null) return null;
            try { return field.GetValue(floor) as Sprite; }
            catch { return null; }
        }

        private static bool IsFloorIconField(string name)
        {
            return name.StartsWith("sprIcon", StringComparison.Ordinal) ||
                   name.StartsWith("sprOutline", StringComparison.Ordinal) ||
                   string.Equals(name, "sprPortal", StringComparison.Ordinal);
        }

        private static void ExportSprite(Sprite sprite, string label, Context context, string indent)
        {
            if (sprite == null || sprite.texture == null) return;
            int spriteId = sprite.GetInstanceID();
            if (!context.ExportedSprites.Add(spriteId)) return;

            Texture2D source = sprite.texture;
            Rect textureRect;
            try { textureRect = sprite.textureRect; }
            catch { textureRect = sprite.rect; }

            int x = Mathf.Clamp(Mathf.RoundToInt(textureRect.x), 0, Math.Max(0, source.width - 1));
            int y = Mathf.Clamp(Mathf.RoundToInt(textureRect.y), 0, Math.Max(0, source.height - 1));
            int width = Mathf.Clamp(Mathf.RoundToInt(textureRect.width), 1, source.width - x);
            int height = Mathf.Clamp(Mathf.RoundToInt(textureRect.height), 1, source.height - y);

            string path = Path.Combine(context.ExportDirectory,
                Sanitize(label + "-" + sprite.name + "-" + spriteId) + ".png");
            RenderTexture previous = RenderTexture.active;
            RenderTexture temporary = null;
            Texture2D readable = null;
            try
            {
                temporary = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
                Graphics.Blit(source, temporary);
                RenderTexture.active = temporary;
                readable = new Texture2D(width, height, TextureFormat.RGBA32, false);
                readable.ReadPixels(new Rect(x, y, width, height), 0, 0, false);
                readable.Apply(false, false);
                File.WriteAllBytes(path, EncodeToPng(readable));
                context.Report.Append(indent).Append("Sprite PNG: ").Append(path)
                    .Append(" | source=").Append(source.name)
                    .Append(" | crop=").Append(x).Append(',').Append(y).Append(' ')
                    .Append(width).Append('x').Append(height).AppendLine();
            }
            catch (Exception ex)
            {
                context.Report.Append(indent).Append("Sprite export failed: ").Append(sprite.name)
                    .Append(" | ").Append(ex.GetType().Name).Append(": ").Append(ex.Message).AppendLine();
            }
            finally
            {
                RenderTexture.active = previous;
                if (temporary != null) RenderTexture.ReleaseTemporary(temporary);
                if (readable != null) UnityEngine.Object.Destroy(readable);
            }
        }

        private static byte[] EncodeToPng(Texture2D texture)
        {
            Type imageConversion = Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule");
            if (imageConversion == null) throw new InvalidOperationException("UnityEngine.ImageConversionModule が見つかりません。");
            MethodInfo method = imageConversion.GetMethod("EncodeToPNG", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Texture2D) }, null);
            if (method == null) throw new MissingMethodException("UnityEngine.ImageConversion.EncodeToPNG");
            return (byte[])method.Invoke(null, new object[] { texture });
        }

        private static string DescribeSprite(Sprite sprite)
        {
            return "Sprite '" + sprite.name + "' (id=" + sprite.GetInstanceID() + ")" +
                   " | texture=" + (sprite.texture != null ? sprite.texture.name : "<null>") +
                   " | rect=" + Format(sprite.rect) +
                   " | pivot=" + Format(sprite.pivot) +
                   " | ppu=" + sprite.pixelsPerUnit.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string SpriteName(Sprite sprite) { return sprite != null ? sprite.name : "<null>"; }
        private static string Format(Rect rect) { return "(" + F(rect.x) + "," + F(rect.y) + "," + F(rect.width) + "," + F(rect.height) + ")"; }
        private static string Format(Vector2 value) { return "(" + F(value.x) + "," + F(value.y) + ")"; }
        private static string F(float value) { return value.ToString("0.###", CultureInfo.InvariantCulture); }

        private static string Sanitize(string value)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            StringBuilder result = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
                result.Append(Array.IndexOf(invalid, value[i]) >= 0 ? '_' : value[i]);
            return result.ToString();
        }
    }
}
