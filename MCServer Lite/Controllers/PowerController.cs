using System.Management;

namespace MCServer
{
    public static class PowerController
    {
        #region MK 0
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
            foreach (ManagementObject manObj in mcWin32.GetInstances())
                mboShutdown = manObj.InvokeMethod("Win32Shutdown", mboShutdownParams, null);
        }
        #endregion
    }

    public enum PowerMode
    {
        None = -10,

        LogOff = 0,
        Shutdown = 1,
        Restart = 2,
        PowerOff = 8,

        ForceLogOff = 4,
        ForceShutdown = 5,
        ForceRestart = 6,
        ForcePowerOff = 12,

        SoftForceShutdown = -1,
        SoftForceRestart = -2,
        SoftForcePowerOff = -8,
    }
}