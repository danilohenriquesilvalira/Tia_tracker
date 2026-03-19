using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using TiaTracker.Models;

namespace TiaTracker.Core
{
    public class SnapshotManager
    {
        private readonly string _baseDir;

        public SnapshotManager(string baseDir)
        {
            _baseDir = baseDir;
        }

        public string CreateSnapshotFolder(string projectName)
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            var folder = Path.Combine(
                _baseDir, "snapshots",
                BlockExporter.Sanitize(projectName),
                timestamp);
            Directory.CreateDirectory(folder);
            return folder;
        }

        public void SaveIndex(string folder, SnapshotInfo info)
        {
            var path = Path.Combine(folder, "_snapshot.json");
            File.WriteAllText(path, JsonConvert.SerializeObject(info, Formatting.Indented));
        }

        public List<SnapshotInfo> GetSnapshots(string projectName)
        {
            var root = Path.Combine(_baseDir, "snapshots", BlockExporter.Sanitize(projectName));
            if (!Directory.Exists(root)) return new List<SnapshotInfo>();

            var result = new List<SnapshotInfo>();
            foreach (var dir in Directory.GetDirectories(root).OrderByDescending(d => d))
            {
                var idx = Path.Combine(dir, "_snapshot.json");
                if (File.Exists(idx))
                {
                    var info = JsonConvert.DeserializeObject<SnapshotInfo>(File.ReadAllText(idx));
                    info.FolderPath = dir;
                    result.Add(info);
                }
            }
            return result;
        }

        public (SnapshotInfo older, SnapshotInfo newer) GetLastTwo(string projectName)
        {
            var snaps = GetSnapshots(projectName);
            if (snaps.Count < 2) return (null, null);
            return (snaps[1], snaps[0]);  // índice 0 = mais recente
        }

        public string GetReportDir() =>
            Path.Combine(_baseDir, "reports");
    }
}
