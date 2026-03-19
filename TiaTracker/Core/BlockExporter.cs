using System;
using System.IO;
using System.Collections.Generic;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Tags;
using TiaTracker.Models;

namespace TiaTracker.Core
{
    public class ExportStats
    {
        public int Blocks    { get; set; }
        public int TagTables { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
    }

    public class BlockExporter
    {
        private readonly Project _project;

        public BlockExporter(Project project)
        {
            _project = project;
        }

        public ExportStats ExportAll(string targetFolder)
        {
            var stats = new ExportStats();

            foreach (Device device in GetAllDevices())
            {
                var plcSw = GetPlcSoftware(device);
                if (plcSw == null) continue;

                var deviceFolder = Path.Combine(targetFolder, Sanitize(device.Name));
                Directory.CreateDirectory(deviceFolder);

                Console.WriteLine($"\n  PLC: {device.Name}");

                // Blocos: OB, FB, FC, DB, UDT
                int blocks = ExportBlockGroup(plcSw.BlockGroup, deviceFolder, stats);
                Console.WriteLine($"    Blocos      : {blocks}");

                // Tabelas de tags
                int tags = ExportTagTables(plcSw.TagTableGroup, deviceFolder, stats);
                Console.WriteLine($"    Tag Tables  : {tags}");

                stats.Blocks    += blocks;
                stats.TagTables += tags;
            }

            return stats;
        }

        private int ExportBlockGroup(PlcBlockGroup group, string folder, ExportStats stats)
        {
            int count = 0;

            foreach (PlcBlock block in group.Blocks)
            {
                try
                {
                    var file = new FileInfo(Path.Combine(folder, Sanitize(block.Name) + ".xml"));
                    block.Export(file, ExportOptions.WithDefaults);
                    Console.WriteLine($"      [{GetBlockType(block),-4}] {block.Name}");
                    count++;
                }
                catch (Exception ex)
                {
                    var msg = $"SKIP {block.Name}: {ex.Message}";
                    Console.WriteLine($"      {msg}");
                    stats.Errors.Add(msg);
                }
            }

            foreach (PlcBlockGroup sub in group.Groups)
            {
                var subFolder = Path.Combine(folder, Sanitize(sub.Name));
                Directory.CreateDirectory(subFolder);
                count += ExportBlockGroup(sub, subFolder, stats);
            }

            return count;
        }

        private int ExportTagTables(PlcTagTableGroup group, string folder, ExportStats stats)
        {
            int count = 0;
            var tagsFolder = Path.Combine(folder, "_TagTables");
            Directory.CreateDirectory(tagsFolder);

            foreach (PlcTagTable table in group.TagTables)
            {
                try
                {
                    var file = new FileInfo(Path.Combine(tagsFolder, Sanitize(table.Name) + ".xml"));
                    table.Export(file, ExportOptions.WithDefaults);
                    count++;
                }
                catch { /* tabelas de sistema ignoradas */ }
            }

            foreach (PlcTagTableGroup sub in group.Groups)
                count += ExportTagTables(sub, tagsFolder, stats);

            return count;
        }

        private IEnumerable<Device> GetAllDevices()
        {
            foreach (Device d in _project.Devices) yield return d;
            foreach (DeviceGroup g in _project.DeviceGroups)
                foreach (Device d in g.Devices) yield return d;
        }

        private static PlcSoftware GetPlcSoftware(Device device)
        {
            foreach (DeviceItem item in device.DeviceItems)
            {
                var sc = item.GetService<SoftwareContainer>();
                if (sc?.Software is PlcSoftware plc) return plc;
            }
            return null;
        }

        private static string GetBlockType(PlcBlock block)
        {
            if (block is OB)  return "OB";
            if (block is FB)  return "FB";
            if (block is FC)  return "FC";
            if (block is GlobalDB) return "DB";
            if (block is InstanceDB) return "iDB";
            return "BLK";
        }

        public static string Sanitize(string name) =>
            string.Concat(name.Split(Path.GetInvalidFileNameChars()));
    }
}
