using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kiner.ADOFAIEditorQoL.Runtime
{
    internal sealed class AssetProbeResult
    {
        internal string TargetName;
        internal string ReportPath;
        internal string ExportDirectory;
        internal int GameObjectCount;
        internal int ComponentCount;
        internal int ExportedTextureCount;
        internal string RootSummary;
    }

    internal static class AssetProbe
    {
        private const int MaxNodes = 500;
        private const int MaxDepth = 10;
        private const int MaxMeshVertices = 20000;
        private const int MaxMeshIndices = 60000;

        private sealed class ProbeContext
        {
            internal readonly StringBuilder Report = new StringBuilder(65536);
            internal readonly HashSet<int> ExportedTextures = new HashSet<int>();
            internal string ExportDirectory;
            internal int GameObjects;
            internal int Components;
        }

        internal static AssetProbeResult ProbeMeshFloorPrefab()
        {
            scrLevelMaker maker = RequireLevelMaker();
            if (maker.meshFloor == null) throw new InvalidOperationException("scrLevelMaker.meshFloor がnullです。");
            return Probe(maker.meshFloor, "meshFloor-prefab");
        }

        internal static AssetProbeResult ProbeSpriteFloorPrefab()
        {
            scrLevelMaker maker = RequireLevelMaker();
            if (maker.spriteFloor == null) throw new InvalidOperationException("scrLevelMaker.spriteFloor がnullです。");
            return Probe(maker.spriteFloor, "spriteFloor-prefab");
        }

        internal static AssetProbeResult ProbeLoadedObject(string targetName)
        {
            if (string.IsNullOrWhiteSpace(targetName)) throw new ArgumentException("対象名を入力してください。");
            GameObject target = FindLoadedObject(targetName.Trim());
            if (target == null) throw new InvalidOperationException("ロード済みGameObjectが見つかりません: " + targetName);
            return Probe(target, "name-" + targetName.Trim());
        }

        internal static AssetProbeResult ProbeFirstEditorFloor()
        {
            scrLevelMaker maker = RequireLevelMaker();
            if (maker.listFloors == null || maker.listFloors.Count == 0 || maker.listFloors[0] == null)
                throw new InvalidOperationException("エディタ床がまだ生成されていません。");
            return Probe(maker.listFloors[0].gameObject, "editor-floor-0");
        }

        private static scrLevelMaker RequireLevelMaker()
        {
            scrLevelMaker maker = scrLevelMaker.instance;
            if (maker == null) throw new InvalidOperationException("scrLevelMakerがまだ生成されていません。");
            return maker;
        }

        private static GameObject FindLoadedObject(string targetName)
        {
            GameObject active = GameObject.Find(targetName);
            if (active != null) return active;
            GameObject[] all = Resources.FindObjectsOfTypeAll<GameObject>();
            GameObject partial = null;
            for (int i = 0; i < all.Length; i++)
            {
                GameObject go = all[i];
                if (go == null) continue;
                if (string.Equals(go.name, targetName, StringComparison.OrdinalIgnoreCase)) return go;
                if (partial == null && go.name.IndexOf(targetName, StringComparison.OrdinalIgnoreCase) >= 0) partial = go;
            }
            return partial;
        }

        private static AssetProbeResult Probe(GameObject root, string label)
        {
            string baseDirectory = Path.Combine(Main.ModPath, "AssetProbe");
            Directory.CreateDirectory(baseDirectory);
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string safeLabel = Sanitize(label);
            string exportDirectory = Path.Combine(baseDirectory, stamp + "-" + safeLabel + "-assets");
            Directory.CreateDirectory(exportDirectory);

            ProbeContext context = new ProbeContext { ExportDirectory = exportDirectory };
            StringBuilder report = context.Report;
            report.AppendLine("ADOFAI Editor QoL Asset Probe");
            report.AppendLine("Mod version: " + ModVersion.Current);
            report.AppendLine("Unity: " + Application.unityVersion);
            report.AppendLine("Root: " + root.name);
            report.AppendLine("HideFlags: " + root.hideFlags);
            report.AppendLine("Asset export directory: " + exportDirectory);
            report.AppendLine();
            DumpGameObject(root, context, 0);

            string reportPath = Path.Combine(baseDirectory, stamp + "-" + safeLabel + ".txt");
            File.WriteAllText(reportPath, report.ToString(), new UTF8Encoding(false));
            if (Main.Logger != null) Main.Logger.Log("Asset Probe: " + root.name + " -> " + reportPath + " (" + context.ExportedTextures.Count + " textures)");

            return new AssetProbeResult
            {
                TargetName = root.name,
                ReportPath = reportPath,
                ExportDirectory = exportDirectory,
                GameObjectCount = context.GameObjects,
                ComponentCount = context.Components,
                ExportedTextureCount = context.ExportedTextures.Count,
                RootSummary = BuildRootSummary(root)
            };
        }

        private static void DumpGameObject(GameObject go, ProbeContext context, int depth)
        {
            if (go == null || context.GameObjects >= MaxNodes || depth > MaxDepth) return;
            context.GameObjects++;
            string indent = new string(' ', depth * 2);
            context.Report.Append(indent).Append("GameObject: ").Append(go.name)
                .Append(" | activeSelf=").Append(go.activeSelf).Append(" | activeInHierarchy=").Append(go.activeInHierarchy)
                .Append(" | layer=").Append(go.layer).Append(" | tag=").Append(SafeTag(go)).AppendLine();

            Component[] local = go.GetComponents<Component>();
            for (int i = 0; i < local.Length; i++)
            {
                Component component = local[i];
                context.Components++;
                if (component == null) { context.Report.Append(indent).AppendLine("  Component: <missing script>"); continue; }
                DumpComponent(component, context, indent + "  ");
            }
            Transform transform = go.transform;
            for (int i = 0; i < transform.childCount && context.GameObjects < MaxNodes; i++)
                DumpGameObject(transform.GetChild(i).gameObject, context, depth + 1);
        }

        private static void DumpComponent(Component component, ProbeContext context, string indent)
        {
            Type type = component.GetType();
            context.Report.Append(indent).Append("Component: ").Append(type.FullName).AppendLine();

            Renderer renderer = component as Renderer;
            if (renderer != null)
            {
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++) if (materials[i] != null) DumpMaterial(materials[i], context, indent + "  ", i);
            }

            MeshFilter meshFilter = component as MeshFilter;
            if (meshFilter != null && meshFilter.sharedMesh != null) DumpMesh(meshFilter.sharedMesh, context.Report, indent + "  ");

            SpriteRenderer spriteRenderer = component as SpriteRenderer;
            if (spriteRenderer != null && spriteRenderer.sprite != null)
            {
                Sprite sprite = spriteRenderer.sprite;
                context.Report.Append(indent).Append("  Sprite: ").Append(sprite.name)
                    .Append(" | texture=").Append(sprite.texture != null ? DescribeObject(sprite.texture) : "<null>")
                    .Append(" | rect=").Append(sprite.rect).Append(" | pixelsPerUnit=").Append(Format(sprite.pixelsPerUnit)).AppendLine();
                ExportTexture(sprite.texture, "sprite-" + sprite.name, context, indent + "    ");
            }
            DumpUnityObjectFields(component, type, context, indent + "  ");
        }

        private static void DumpMaterial(Material material, ProbeContext context, string indent, int index)
        {
            Shader shader = material.shader;
            context.Report.Append(indent).Append("Material[").Append(index).Append("]: ").Append(material.name)
                .Append(" | shader=").Append(shader != null ? shader.name : "<null>")
                .Append(" | renderQueue=").Append(material.renderQueue)
                .Append(" | mainTexture=").Append(material.mainTexture != null ? DescribeObject(material.mainTexture) : "<null>").AppendLine();
            if (shader == null) return;

            int count;
            try { count = shader.GetPropertyCount(); }
            catch (Exception ex) { context.Report.Append(indent).Append("  Properties: <error: ").Append(ex.GetType().Name).AppendLine(">"); return; }

            for (int p = 0; p < count; p++)
            {
                string name;
                ShaderPropertyType propertyType;
                try { name = shader.GetPropertyName(p); propertyType = shader.GetPropertyType(p); }
                catch (Exception ex) { context.Report.Append(indent).Append("  Property[").Append(p).Append("]: <error: ").Append(ex.GetType().Name).AppendLine(">"); continue; }
                context.Report.Append(indent).Append("  ").Append(name).Append(" (").Append(propertyType).Append(") = ");
                try
                {
                    switch (propertyType)
                    {
                        case ShaderPropertyType.Color: context.Report.Append(Format(material.GetColor(name))); break;
                        case ShaderPropertyType.Vector: context.Report.Append(Format(material.GetVector(name))); break;
                        case ShaderPropertyType.Texture:
                            Texture texture = material.GetTexture(name);
                            context.Report.Append(texture != null ? DescribeObject(texture) : "<null>");
                            if (texture != null)
                            {
                                context.Report.Append(" | scale=").Append(Format(material.GetTextureScale(name))).Append(" | offset=").Append(Format(material.GetTextureOffset(name)));
                                ExportTexture(texture, "material-" + material.name + "-" + name, context, indent + "    ");
                            }
                            break;
                        default: context.Report.Append(Format(material.GetFloat(name))); break;
                    }
                }
                catch (Exception ex) { context.Report.Append("<error: ").Append(ex.GetType().Name).Append(">"); }
                context.Report.AppendLine();
            }
        }

        private static void DumpMesh(Mesh mesh, StringBuilder report, string indent)
        {
            report.Append(indent).Append("Mesh: ").Append(mesh.name).Append(" | vertices=").Append(mesh.vertexCount)
                .Append(" | subMeshes=").Append(mesh.subMeshCount).Append(" | bounds=").Append(mesh.bounds).AppendLine();
            if (mesh.vertexCount > MaxMeshVertices)
            {
                report.Append(indent).Append("  Geometry omitted: vertex count exceeds ").Append(MaxMeshVertices).AppendLine();
                return;
            }
            try
            {
                Vector3[] vertices = mesh.vertices;
                Vector2[] uv = mesh.uv;
                Vector3[] normals = mesh.normals;
                report.Append(indent).AppendLine("  Vertices:");
                for (int i = 0; i < vertices.Length; i++)
                {
                    report.Append(indent).Append("    [").Append(i).Append("] ").Append(Format(vertices[i]));
                    if (i < uv.Length) report.Append(" uv=").Append(Format(uv[i]));
                    if (i < normals.Length) report.Append(" normal=").Append(Format(normals[i]));
                    report.AppendLine();
                }
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    int[] triangles = mesh.GetTriangles(sub);
                    report.Append(indent).Append("  Triangles[").Append(sub).Append("] count=").Append(triangles.Length).AppendLine();
                    int limit = Math.Min(triangles.Length, MaxMeshIndices);
                    for (int i = 0; i < limit; i += 3)
                    {
                        report.Append(indent).Append("    ");
                        for (int j = 0; j < 3 && i + j < limit; j++) { if (j != 0) report.Append(", "); report.Append(triangles[i + j]); }
                        report.AppendLine();
                    }
                    if (triangles.Length > limit) report.Append(indent).Append("    ... omitted ").Append(triangles.Length - limit).AppendLine(" indices");
                }
            }
            catch (Exception ex) { report.Append(indent).Append("  Geometry: <error: ").Append(ex.GetType().Name).Append(": ").Append(ex.Message).AppendLine(">"); }
        }

        private static void DumpUnityObjectFields(Component component, Type type, ProbeContext context, string indent)
        {
            FieldInfo[] fields;
            try { fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic); }
            catch { return; }
            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo field = fields[i];
                if (!typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType)) continue;
                try
                {
                    UnityEngine.Object value = field.GetValue(component) as UnityEngine.Object;
                    if (value != null)
                    {
                        context.Report.Append(indent).Append("ref ").Append(field.Name).Append(" = ").Append(DescribeObject(value)).AppendLine();
                        Texture texture = value as Texture;
                        if (texture != null) ExportTexture(texture, "field-" + type.Name + "-" + field.Name + "-" + texture.name, context, indent + "  ");
                    }
                }
                catch (Exception ex) { context.Report.Append(indent).Append("ref ").Append(field.Name).Append(" = <error: ").Append(ex.GetType().Name).AppendLine(">"); }
            }
        }

        private static void ExportTexture(Texture texture, string label, ProbeContext context, string indent)
        {
            if (texture == null) return;
            int id = texture.GetInstanceID();
            if (!context.ExportedTextures.Add(id)) return;
            Texture2D source = texture as Texture2D;
            if (source == null)
            {
                context.Report.Append(indent).Append("Texture export skipped: ").Append(DescribeObject(texture)).Append(" is not Texture2D").AppendLine();
                return;
            }

            string path = Path.Combine(context.ExportDirectory, Sanitize(label) + "-" + id + ".png");
            Texture2D readable = null;
            RenderTexture previous = RenderTexture.active;
            RenderTexture temporary = null;
            try
            {
                temporary = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
                Graphics.Blit(source, temporary);
                RenderTexture.active = temporary;
                readable = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
                readable.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0, false);
                readable.Apply(false, false);
                byte[] png = EncodeToPng(readable);
                File.WriteAllBytes(path, png);
                context.Report.Append(indent).Append("Texture PNG: ").Append(path).Append(" | ").Append(source.width).Append("x").Append(source.height).Append(" | format=").Append(source.format).AppendLine();
            }
            catch (Exception ex)
            {
                context.Report.Append(indent).Append("Texture export failed: ").Append(DescribeObject(source)).Append(" | ").Append(ex.GetType().Name).Append(": ").Append(ex.Message).AppendLine();
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

        private static string BuildRootSummary(GameObject root)
        {
            Component[] components = root.GetComponents<Component>();
            List<string> names = new List<string>(components.Length);
            for (int i = 0; i < components.Length; i++) names.Add(components[i] != null ? components[i].GetType().Name : "<missing>");
            return root.name + " [" + string.Join(", ", names.ToArray()) + "]";
        }

        private static string DescribeObject(UnityEngine.Object value) { return value.GetType().Name + " '" + value.name + "' (id=" + value.GetInstanceID() + ")"; }
        private static string Format(float value) { return value.ToString("0.######", CultureInfo.InvariantCulture); }
        private static string Format(Vector2 value) { return "(" + Format(value.x) + ", " + Format(value.y) + ")"; }
        private static string Format(Vector3 value) { return "(" + Format(value.x) + ", " + Format(value.y) + ", " + Format(value.z) + ")"; }
        private static string Format(Vector4 value) { return "(" + Format(value.x) + ", " + Format(value.y) + ", " + Format(value.z) + ", " + Format(value.w) + ")"; }
        private static string Format(Color value) { return "(" + Format(value.r) + ", " + Format(value.g) + ", " + Format(value.b) + ", " + Format(value.a) + ")"; }
        private static string SafeTag(GameObject go) { try { return go.tag; } catch { return "<unavailable>"; } }

        private static string Sanitize(string value)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            StringBuilder result = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++) result.Append(Array.IndexOf(invalid, value[i]) >= 0 ? '_' : value[i]);
            return result.ToString();
        }
    }
}
