using System.Collections.Generic;

namespace TiaTracker.Models
{
    public enum ChangeType { Modified, Added, Removed }

    public class BlockChange
    {
        public ChangeType       Type        { get; set; }
        public string           Device      { get; set; }
        public string           BlockName   { get; set; }
        public string           BlockType   { get; set; }  // OB, FB, FC, DB, UDT
        public List<NetworkChange> Networks { get; set; } = new List<NetworkChange>();
    }

    public class NetworkChange
    {
        public ChangeType Type        { get; set; }
        public int        Index       { get; set; }   // Network 1, 2, 3...
        public string     Title       { get; set; }
        public string     Language    { get; set; }  // LAD, FBD, SCL, STL
        public List<string> Changes   { get; set; } = new List<string>();
    }

    public class DiffResult
    {
        public string SnapshotOlder      { get; set; }
        public string SnapshotNewer      { get; set; }
        public List<BlockChange> Changes { get; set; } = new List<BlockChange>();

        public int TotalModified => Changes.FindAll(c => c.Type == ChangeType.Modified).Count;
        public int TotalAdded    => Changes.FindAll(c => c.Type == ChangeType.Added).Count;
        public int TotalRemoved  => Changes.FindAll(c => c.Type == ChangeType.Removed).Count;
    }
}
