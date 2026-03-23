namespace TiaTracker.Core.TcpServer
{
    public class TagValue
    {
        public string Name     { get; set; }
        public string Value    { get; set; }
        public string DataType { get; set; } = "";
        public System.DateTime LastUpdate { get; set; }
    }
}
