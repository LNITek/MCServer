using System.Windows.Forms;
using ExtraFunctions.Extras;
using System;
using MCServer.Controllers;
using System.IO.Compression;

namespace MCServer
{
    public static class BackupController
    {
        #region MK 1
        /// <summary>
        /// Backup World : MK 1
        /// </summary>
        public static void Backup()
        {
            //Var
            bool Saved = false;
            List<string> Files = new();
            string sDate = " " + DateTime.Now.ToString("yyyy-MM-dd"), WorldName = "", Extension = "";
            int I = 0;

            BaseController.WriteLine("say Starting World Backup...");
            BaseController.WriteLine("say World Backup...");
            Console.WriteLine("Starting Backup...");
            BaseController.WriteLine("say §9 Starting World Backup...");
            BaseController.WriteLine("save hold");
            Thread.Sleep(1000);
            do
            {
                BaseController.WriteLine("save query");
                Thread.Sleep(2000);
                var Index = BaseController.OutputList.FindLastIndex(x => x.Contains("Data saved. Files are now ready to be copied.")) + 1;
                if (Index <= 0) continue;
                Files = BaseController.OutputList[Index].Split(", ").Select(x => x.Split(':').First()).ToList();
                if (Files.Count > 0) Saved = true;
            } while (!Saved);

            WorldName = string.Join("", Files.First().TakeWhile(x => x != '/'));
            var SourcePath = OtherController.Settings[SettingProp.ServerPath] + "\\worlds\\" + WorldName + "\\";
            var BackupPath = OtherController.Settings[SettingProp.BackupPath] + "\\" + WorldName + sDate;
            var WorldPath = OtherController.Settings[SettingProp.BackupPath] + "\\" + WorldName + "\\";
            Files = Files.Select(x => x.Replace('/', '\\').Replace(WorldName, "")).ToList();
            while (File.Exists(BackupPath + Extension + ".zip"))
                Extension = $" ({I++})";

            if (Directory.Exists(WorldPath)) Directory.Delete(WorldPath, true);
            Directory.CreateDirectory(WorldPath);
            foreach (var file in Files)
            {
                var FilePath = Path.GetDirectoryName(WorldPath + file);
                if (file.Contains('\\') && !Directory.Exists(FilePath))
                    Directory.CreateDirectory(FilePath);
                File.Copy(SourcePath + file, WorldPath + file);
            }

            BaseController.WriteLine("save resume");
            ZipFile.CreateFromDirectory(WorldPath, BackupPath + Extension + ".zip", CompressionLevel.SmallestSize, false);

            BaseController.WriteLine("say §9 Backup Completed!");
            Console.WriteLine("Backup Completed");
        }
        #endregion
        #region MK 0
        /// <summary>
        /// Backup World : MK 0
        /// </summary>
        public static void Backups()
        {
            //Var
            const int iSleep = 10;
            string sDate = " " + DateTime.Now.ToString("yyyy-MM-dd");
            string sStart = Application.StartupPath + @"\BackupPath.txt", sEnd = "";
            if (!ExFun.LoadFile(sStart, sStart, false))
            {
                OtherController.ThrowLog("Backup Path File Does Not Exist 'BK-M01'");
                return;
            }
            StreamReader tPath = new(sStart);
            //Code
            sStart = tPath.ReadLine();
            if (sStart == null) { OtherController.ThrowLog("Backup Path File Does Not Have Start Path 'BK-M02'"); return; }
            try
            {
                sEnd = string.Concat(tPath.ReadLine(), " ", sDate);
            }
            catch { OtherController.ThrowLog("Backup Path File Does Not Have End Path 'BK-M03'"); return; }
            finally { tPath.Close(); }

            Console.WriteLine("Backup Starts Now...");
            if (OtherController.ShowWindow())
            {
                SendKeys.SendWait("say §9 Starting World Backup...");
                SendKeys.SendWait("{ENTER}");
                Thread.Sleep(iSleep * 1000);
                SendKeys.SendWait("save hold");
                SendKeys.SendWait("{ENTER}");
                Thread.Sleep(iSleep * 1000);
                SendKeys.SendWait("save query");
                SendKeys.SendWait("{ENTER}");
                DirectoryCopy(sStart, sEnd, true);
                SendKeys.SendWait("save resume");
                SendKeys.SendWait("{ENTER}");
                Thread.Sleep(iSleep * 1000);
                SendKeys.SendWait("say §9 Backup Completed!");
                SendKeys.SendWait("{ENTER}");
                Console.WriteLine("Backup Completed");
            }
            else
                DirectoryCopy(sStart, sEnd, true);
            Console.WriteLine("Backup Completed");
        }

        public static void NewFile()
        {
            StreamWriter tNew = new(Application.StartupPath + @"\BackupPath.txt");
            try
            {
                Console.WriteLine("New File Created.");
                Console.WriteLine("Type In Path Of Folder To Backup...");
                tNew.WriteLine(Console.ReadLine());
                Console.WriteLine("Type In Path Where The Backup Must Be Placed...");
                tNew.Write(Console.ReadLine());
                Console.WriteLine("Name Of Backup File...");
                var File = Console.ReadLine();
                tNew.WriteLine("\\" + (string.IsNullOrWhiteSpace(File) ? "Backup" : File));
            }
            catch (Exception e) { OtherController.ThrowLog(e.Message + " 'BK-NF01'"); }
            finally { tNew.Close(); }
        }

        private static void DirectoryCopy(string sourceDirName, string destDirName, bool copySubDirs)
        {
            // Get the subdirectories for the specified directory.
            DirectoryInfo dir = new(sourceDirName);

            if (!dir.Exists)
            {
                OtherController.ThrowLog("Source directory does not exist or could not be found: " + sourceDirName + " BK-DIRC01");
                throw new DirectoryNotFoundException(
                    "Source directory does not exist or could not be found: " + sourceDirName);
            }

            DirectoryInfo[] dirs = dir.GetDirectories();

            // If the destination directory doesn't exist, create it.       
            Directory.CreateDirectory(destDirName);

            // Get the files in the directory and copy them to the new location.
            FileInfo[] files = dir.GetFiles();
            foreach (FileInfo file in files)
            {
                string tempPath = Path.Combine(destDirName, file.Name);
                file.CopyTo(tempPath, false);
            }

            // If copying subdirectories, copy them and their contents to new location.
            if (copySubDirs)
            {
                foreach (DirectoryInfo subdir in dirs)
                {
                    string tempPath = Path.Combine(destDirName, subdir.Name);
                    DirectoryCopy(subdir.FullName, tempPath, copySubDirs);
                }
            }
        }
        #endregion
    }
}