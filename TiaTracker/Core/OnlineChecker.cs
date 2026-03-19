using System;
using System.Collections.Generic;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.Online;

namespace TiaTracker.Core
{
    public class OnlineStatus
    {
        public string   DeviceName   { get; set; }
        public string   State        { get; set; }
        public bool     IsOnline     { get; set; }
        public bool     IsModified   { get; set; }
        public string   ModifiedBy   { get; set; }
        public DateTime LastModified { get; set; }
    }

    public class OnlineChecker
    {
        private readonly Project _project;

        public OnlineChecker(Project project)
        {
            _project = project;
        }

        /// <summary>
        /// Lê o estado actual sem tentar ligar ao PLC (sem GoOnline).
        /// Evita popups desnecessários no TIA Portal.
        /// </summary>
        public List<OnlineStatus> CheckAll()
        {
            var results = new List<OnlineStatus>();

            foreach (Device device in _project.Devices)
            {
                var status = CheckDevice(device);
                if (status != null) results.Add(status);
            }

            foreach (DeviceGroup group in _project.DeviceGroups)
                foreach (Device device in group.Devices)
                {
                    var status = CheckDevice(device);
                    if (status != null) results.Add(status);
                }

            return results;
        }

        /// <summary>
        /// Tenta ligar ao PLC fisicamente (só chamar quando o utilizador pede explicitamente).
        /// </summary>
        public List<OnlineStatus> GoOnlineAll()
        {
            var results = new List<OnlineStatus>();

            foreach (Device device in _project.Devices)
            {
                var status = GoOnlineDevice(device);
                if (status != null) results.Add(status);
            }

            return results;
        }

        private OnlineStatus CheckDevice(Device device)
        {
            foreach (DeviceItem item in device.DeviceItems)
            {
                var sc = item.GetService<SoftwareContainer>();
                if (sc == null) continue;

                return new OnlineStatus
                {
                    DeviceName   = device.Name,
                    State        = "Offline",
                    IsOnline     = false,
                    IsModified   = _project.IsModified,
                    ModifiedBy   = _project.LastModifiedBy,
                    LastModified = _project.LastModified
                };
            }
            return null;
        }

        private OnlineStatus GoOnlineDevice(Device device)
        {
            foreach (DeviceItem item in device.DeviceItems)
            {
                var sc = item.GetService<SoftwareContainer>();
                if (sc == null) continue;

                var status = new OnlineStatus
                {
                    DeviceName   = device.Name,
                    IsModified   = _project.IsModified,
                    ModifiedBy   = _project.LastModifiedBy,
                    LastModified = _project.LastModified
                };

                try
                {
                    var provider = item.GetService<OnlineProvider>();
                    if (provider != null)
                    {
                        var state    = provider.GoOnline();
                        status.State    = state.ToString();
                        status.IsOnline = (state == OnlineState.Online);
                    }
                    else
                    {
                        status.State    = "SemLigação";
                        status.IsOnline = false;
                    }
                }
                catch (Exception ex)
                {
                    status.State    = $"Erro: {ex.Message}";
                    status.IsOnline = false;
                }

                return status;
            }
            return null;
        }
    }
}
