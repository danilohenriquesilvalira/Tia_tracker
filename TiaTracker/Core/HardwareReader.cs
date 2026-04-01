using System;
using System.Collections.Generic;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;

namespace TiaTracker.Core
{
    // ── Models ─────────────────────────────────────────────────────────────────

    public class HwModuleInfo
    {
        public int    Slot        { get; set; }
        public string Name        { get; set; } = "";
        public string OrderNumber { get; set; } = "";   // ex: "6ES7 521-1BL00-0AB0"
        public string Comment     { get; set; } = "";
        public string InputRange  { get; set; } = "";   // ex: "IB0..IB3"
        public string OutputRange { get; set; } = "";   // ex: "QB0..QB3"
        public List<HwModuleInfo> SubModules { get; set; } = new List<HwModuleInfo>();
    }

    public class HwNetworkInfo
    {
        public string IpAddress     { get; set; } = "";
        public string SubnetMask    { get; set; } = "";
        public string RouterAddress { get; set; } = "";
        public string ProfinetName  { get; set; } = "";
    }

    public class HwDeviceInfo
    {
        public string Name        { get; set; } = "";
        public string OrderNumber { get; set; } = "";   // referência catálogo da CPU/HMI
        public string Comment     { get; set; } = "";
        public bool   IsPlc       { get; set; } = false;
        public HwNetworkInfo         Network   { get; set; } = new HwNetworkInfo();
        public List<HwModuleInfo>    Modules   { get; set; } = new List<HwModuleInfo>();
        public List<HwDeviceInfo>    IoDevices { get; set; } = new List<HwDeviceInfo>(); // escravos PROFINET
    }

    // ── Reader ─────────────────────────────────────────────────────────────────

    public class HardwareReader
    {
        private readonly Project _project;

        public HardwareReader(Project project) => _project = project;

        public List<HwDeviceInfo> ReadAll()
        {
            var result = new List<HwDeviceInfo>();
            foreach (Device device in GetAllDevices())
            {
                try
                {
                    var info = ReadDevice(device);
                    result.Add(info);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  [HW] Skip {device.Name}: {ex.Message}");
                }
            }
            return result;
        }

        // ── Device ─────────────────────────────────────────────────────────────

        private HwDeviceInfo ReadDevice(Device device)
        {
            // Usa o nome do software PLC (nome definido pelo utilizador na árvore do TIA Portal)
            // em vez de device.Name que devolve o nome genérico da estação (ex: "S71500")
            var plcSw = FindPlcSoftware(device.DeviceItems);
            var info = new HwDeviceInfo
            {
                Name        = plcSw?.Name ?? device.Name,
                OrderNumber = SafeAttr(device, "TypeIdentifier") ?? "",
                Comment     = SafeAttr(device, "Comment") ?? ""
            };
            info.OrderNumber = StripPrefix(info.OrderNumber);

            // Verifica se tem software PLC
            info.IsPlc = FindPlcSoftware(device.DeviceItems) != null;

            foreach (DeviceItem item in device.DeviceItems)
                ReadItem(item, info);

            return info;
        }

        // ── DeviceItem (módulo) ─────────────────────────────────────────────────

        private void ReadItem(DeviceItem item, HwDeviceInfo deviceInfo)
        {
            var typeId = item.TypeIdentifier ?? "";

            // Rack / cabos / itens de sistema — só recursão
            if (typeId.StartsWith("System:", StringComparison.OrdinalIgnoreCase)
             || string.IsNullOrWhiteSpace(typeId))
            {
                foreach (DeviceItem sub in item.DeviceItems)
                    ReadItem(sub, deviceInfo);
                return;
            }

            var mod = new HwModuleInfo
            {
                Slot        = item.PositionNumber,
                Name        = item.Name ?? "",
                OrderNumber = StripPrefix(typeId),
                Comment     = SafeAttr(item, "Comment") ?? ""
            };

            // Endereços I/O
            mod.InputRange  = BuildIoRange(
                SafeAttr(item, "StartAddress"),
                SafeAttr(item, "SizeInput"));
            mod.OutputRange = BuildIoRange(
                SafeAttr(item, "StartOutputAddress"),
                SafeAttr(item, "SizeOutput"));

            // Rede (IP, PROFINET)
            TryReadNetwork(item, deviceInfo);

            deviceInfo.Modules.Add(mod);

            // Sub-módulos (ex: ET200SP com vários módulos)
            foreach (DeviceItem sub in item.DeviceItems)
            {
                var subTypeId = sub.TypeIdentifier ?? "";
                if (string.IsNullOrWhiteSpace(subTypeId)
                 || subTypeId.StartsWith("System:", StringComparison.OrdinalIgnoreCase))
                    continue;

                mod.SubModules.Add(new HwModuleInfo
                {
                    Slot        = sub.PositionNumber,
                    Name        = sub.Name ?? "",
                    OrderNumber = StripPrefix(subTypeId),
                    Comment     = SafeAttr(sub, "Comment") ?? "",
                    InputRange  = BuildIoRange(SafeAttr(sub, "StartAddress"),  SafeAttr(sub, "SizeInput")),
                    OutputRange = BuildIoRange(SafeAttr(sub, "StartOutputAddress"), SafeAttr(sub, "SizeOutput"))
                });
            }
        }

        // ── Rede ───────────────────────────────────────────────────────────────

        private static void TryReadNetwork(DeviceItem item, HwDeviceInfo deviceInfo)
        {
            try
            {
                var ni = item.GetService<NetworkInterface>();
                if (ni == null) return;

                // IP via Nodes
                try
                {
                    foreach (var node in ni.Nodes)
                    {
                        try
                        {
                            var ip   = node.GetAttribute("Address")     as string;
                            var mask = node.GetAttribute("SubnetMask")  as string;
                            var gw   = node.GetAttribute("RouterAddress") as string;
                            if (!string.IsNullOrWhiteSpace(ip))
                            {
                                deviceInfo.Network.IpAddress     = ip;
                                deviceInfo.Network.SubnetMask    = mask ?? "";
                                deviceInfo.Network.RouterAddress = gw   ?? "";
                            }
                        }
                        catch { }
                    }
                }
                catch { }

                // Nome PROFINET (atributo no DeviceItem)
                var pnAttempts = new[] { "PnDeviceName", "DeviceName", "ProfinetDeviceName" };
                foreach (var attr in pnAttempts)
                {
                    try
                    {
                        var pn = item.GetAttribute(attr) as string;
                        if (!string.IsNullOrWhiteSpace(pn))
                        {
                            deviceInfo.Network.ProfinetName = pn;
                            break;
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        /// <summary>
        /// Constrói "IB0..IB3" a partir do endereço inicial e tamanho em bytes.
        /// </summary>
        private static string BuildIoRange(string startStr, string sizeStr)
        {
            if (string.IsNullOrEmpty(startStr)) return "";
            if (!int.TryParse(startStr, out int start)) return "";
            if (!int.TryParse(sizeStr,  out int size) || size <= 0) return $"B{start}";
            int end = start + size - 1;
            return start == end ? $"B{start}" : $"B{start}..B{end}";
        }

        private static string StripPrefix(string typeId)
        {
            if (string.IsNullOrEmpty(typeId)) return "";
            const string p = "OrderNumber:";
            return typeId.StartsWith(p, StringComparison.OrdinalIgnoreCase)
                ? typeId.Substring(p.Length).Trim()
                : typeId;
        }

        private static string SafeAttr(DeviceItem item, string name)
        {
            try { return item.GetAttribute(name)?.ToString(); } catch { return null; }
        }

        private static string SafeAttr(Device device, string name)
        {
            try { return device.GetAttribute(name)?.ToString(); } catch { return null; }
        }

        private static PlcSoftware FindPlcSoftware(DeviceItemComposition items)
        {
            foreach (DeviceItem item in items)
            {
                var sc = item.GetService<SoftwareContainer>();
                if (sc?.Software is PlcSoftware plc) return plc;
                var found = FindPlcSoftware(item.DeviceItems);
                if (found != null) return found;
            }
            return null;
        }

        private IEnumerable<Device> GetAllDevices()
        {
            foreach (Device d in _project.Devices) yield return d;
            foreach (DeviceGroup g in _project.DeviceGroups)
                foreach (Device d in g.Devices) yield return d;
        }
    }
}
