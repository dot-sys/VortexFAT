using System;
using System.Diagnostics;
using System.Windows;

// Main application UI layer namespace
namespace Vortex.UI
{
    // Application entry point and initialization handler
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                string resourceName = new System.Reflection.AssemblyName(args.Name).Name + ".dll";
                string resourcePath = "Vortex.UI.Resources.Embedded." + resourceName;

                using (var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(resourcePath))
                {
                    if (stream == null) return null;
                    byte[] assemblyData = new byte[stream.Length];
                    stream.Read(assemblyData, 0, assemblyData.Length);
                    return System.Reflection.Assembly.Load(assemblyData);
                }
            };

            AppDomain.CurrentDomain.FirstChanceException += (sender, args) =>
            {
                if (args.Exception is System.Security.Cryptography.CryptographicException)
                {
                }
            };

            base.OnStartup(e);
        }
    }
}