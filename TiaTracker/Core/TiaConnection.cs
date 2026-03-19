using System;
using System.Reflection;
using Siemens.Engineering;

namespace TiaTracker.Core
{
    public class TiaConnection : IDisposable
    {
        private static readonly string ApiPath =
            @"C:\Program Files\Siemens\Automation\Portal V18\PublicAPI\V18\";

        public TiaPortal Portal  { get; private set; }
        public Project   Project { get; private set; }

        public static void RegisterAssemblyResolver()
        {
            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                var name = new AssemblyName(args.Name).Name;
                var path = System.IO.Path.Combine(ApiPath, name + ".dll");
                return System.IO.File.Exists(path) ? Assembly.LoadFrom(path) : null;
            };
        }

        private readonly string _projectPath;

        public TiaConnection(string projectPath = null)
        {
            _projectPath = projectPath ?? @"C:\Users\Admin\Desktop\C#_PLC\C#_PLC.ap18";
        }

        public bool Connect()
        {
            try
            {
                // Se já está aberto, liga directamente
                var processes = TiaPortal.GetProcesses();
                if (processes.Count > 0)
                {
                    Console.Write($"  TIA Portal já aberto (PID {processes[0].Id}) — a ligar...");
                    var cts = TiaOpennessAutoAccept.StartWatcher(processes[0].Id);
                    try   { Portal = processes[0].Attach(); }
                    finally { cts.Cancel(); }
                    Console.WriteLine(" OK");
                }
                else
                {
                    // Abre o TIA Portal com interface visual
                    Console.WriteLine("  A abrir TIA Portal V18...");
                    Portal = new TiaPortal(TiaPortalMode.WithUserInterface);
                    Console.WriteLine("  TIA Portal aberto.");
                }

                // Abre o projecto se não estiver aberto
                if (Portal.Projects.Count == 0)
                {
                    Console.Write($"  A abrir projecto...");
                    Project = Portal.Projects.Open(new System.IO.FileInfo(_projectPath));
                    Console.WriteLine(" OK");
                }
                else
                {
                    Project = Portal.Projects[0];
                }

                Console.WriteLine($"  Projecto : {Project.Name}");
                Console.WriteLine($"  Caminho  : {Project.Path.FullName}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n  ERRO: {ex.Message}");
                return false;
            }
        }

        public void Dispose()
        {
            Portal?.Dispose();
        }
    }
}
