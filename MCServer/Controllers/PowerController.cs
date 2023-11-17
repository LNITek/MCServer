using System.Management;

namespace MCServer
{
    public static class PowerController
    {
        /// <summary>
        /// MK 1.0
        /// </summary>
        /// <param name="Mode">Mode For System Power Control.</param>
        public static void Shutdown(PowerMode Mode)
        {
            if (Mode == PowerMode.None) return;
            if ((int)Mode < 0) Mode = (PowerMode)((int)Mode * -1);
            ManagementBaseObject mboShutdown;
            ManagementClass mcWin32 = new("Win32_OperatingSystem");
            mcWin32.Get();

            mcWin32.Scope.Options.EnablePrivileges = true;
            ManagementBaseObject mboShutdownParams =
                     mcWin32.GetMethodParameters("Win32Shutdown");

            mboShutdownParams["Flags"] = (int)Mode;
            mboShutdownParams["Reserved"] = "0";
            foreach (ManagementObject manObj in mcWin32.GetInstances().Cast<ManagementObject>())
                mboShutdown = manObj.InvokeMethod("Win32Shutdown", mboShutdownParams, null);

            Environment.Exit(0);
        }
    }

    public enum PowerMode
    {
        None = -1,

        LogOff = 0,
        Shutdown = 1,
        Restart = 2,
        PowerOff = 8,

        ForceLogOff = 4,
        ForceShutdown = 5,
        ForceRestart = 6,
        ForcePowerOff = 12,
    }
}