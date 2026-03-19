using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using TiaTracker.Models;

namespace TiaTracker.Diff
{
    public static class SnapshotDiff
    {
        public static DiffResult Compare(SnapshotInfo older, SnapshotInfo newer)
        {
            var result = new DiffResult
            {
                SnapshotOlder = older.Timestamp.ToString("dd/MM/yyyy HH:mm:ss"),
                SnapshotNewer = newer.Timestamp.ToString("dd/MM/yyyy HH:mm:ss")
            };

            var oldFiles = GetXmlMap(older.FolderPath);
            var newFiles = GetXmlMap(newer.FolderPath);

            // Blocos adicionados
            foreach (var key in newFiles.Keys.Except(oldFiles.Keys))
            {
                result.Changes.Add(new BlockChange
                {
                    Type      = ChangeType.Added,
                    Device    = ExtractDevice(key),
                    BlockName = ExtractBlock(key),
                    BlockType = DetectBlockType(newFiles[key])
                });
            }

            // Blocos removidos
            foreach (var key in oldFiles.Keys.Except(newFiles.Keys))
            {
                result.Changes.Add(new BlockChange
                {
                    Type      = ChangeType.Removed,
                    Device    = ExtractDevice(key),
                    BlockName = ExtractBlock(key),
                    BlockType = DetectBlockType(oldFiles[key])
                });
            }

            // Blocos modificados — entra dentro para ver o que mudou
            foreach (var key in newFiles.Keys.Intersect(oldFiles.Keys))
            {
                var oldXml = oldFiles[key];
                var newXml = newFiles[key];

                if (oldXml == newXml) continue;

                var networkChanges = BlockXmlDiff.Compare(oldXml, newXml);

                result.Changes.Add(new BlockChange
                {
                    Type      = ChangeType.Modified,
                    Device    = ExtractDevice(key),
                    BlockName = ExtractBlock(key),
                    BlockType = DetectBlockType(newXml),
                    Networks  = networkChanges
                });
            }

            return result;
        }

        // ── Utilitários ───────────────────────────────────────────────────────

        private static Dictionary<string, string> GetXmlMap(string root)
        {
            var map = new Dictionary<string, string>();
            if (!Directory.Exists(root)) return map;

            foreach (var file in Directory.GetFiles(root, "*.xml", SearchOption.AllDirectories))
            {
                // Ignora o índice
                if (Path.GetFileName(file).StartsWith("_")) continue;

                var rel = file.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar);
                map[rel] = File.ReadAllText(file);
            }
            return map;
        }

        private static string ExtractDevice(string relativePath)
        {
            var parts = relativePath.Split(Path.DirectorySeparatorChar);
            return parts.Length > 0 ? parts[0] : "";
        }

        private static string ExtractBlock(string relativePath)
        {
            var parts = relativePath.Split(Path.DirectorySeparatorChar);
            var file  = parts.Last();
            return Path.GetFileNameWithoutExtension(file);
        }

        private static string DetectBlockType(string xml)
        {
            if (xml.Contains("SW.Blocks.OB"))        return "OB";
            if (xml.Contains("SW.Blocks.FB"))        return "FB";
            if (xml.Contains("SW.Blocks.FC"))        return "FC";
            if (xml.Contains("SW.Blocks.GlobalDB"))  return "DB";
            if (xml.Contains("SW.Blocks.InstanceDB"))return "iDB";
            if (xml.Contains("SW.Tags"))             return "Tags";
            return "BLK";
        }
    }
}
