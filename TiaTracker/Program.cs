using System.Windows.Forms;
using TiaTracker.Core;
using TiaTracker.UI;

namespace TiaTracker
{
    static class Program
    {
        [System.STAThread]
        static void Main()
        {
            TiaConnection.RegisterAssemblyResolver();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
