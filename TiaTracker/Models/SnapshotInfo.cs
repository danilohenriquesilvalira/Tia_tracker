using System;
using System.Collections.Generic;

namespace TiaTracker.Models
{
    public class SnapshotInfo
    {
        public string ProjectName { get; set; }
        public DateTime Timestamp { get; set; }
        public string FolderPath  { get; set; }
        public int TotalBlocks    { get; set; }
        public int TotalTagTables { get; set; }
        public List<string> Devices { get; set; } = new List<string>();
    }
}
