using System;
using System.Globalization;
using System.Collections.Generic;
using System.Text;
using System.Xml;

using System.IO;
using System.IO.Ports;
using System.Reflection;
using System.Linq;

enum CONSOLE_ATTRIBUTE
{
    REVERSE_ATTRIBUTE = 0,
    NORMAL_ATTRIBUTE
};

enum SECTOR_ACCESS_MODE
{
    S_MODE = 0,
    R_MODE
};
public enum FileFormat
{
    fileformat_UNKNOWN = -1,
    fileformat_OS9,
    fileformat_FLEX,
    fileformat_UniFLEX,
    fileformat_FLEX_IDE
}

namespace FLEXNetSharp
{
    public class DriveInfo
    {
        public int mode;
        public string MountedFilename;
        //public int      NumberOfTracks;
        public byte[] cNumberOfSectorsPerTrack = new byte[1];
        public long NumberOfBytesPerTrack;
        public long NumberOfSectorsPerTrack;
        public int NumberOfSectorsPerCluster;
        public long TotalNumberOfSectorOnMedia;
        public int LogicalSectorSize;
    }

    public class ImageFile
    {
        public string     Name;
        public bool       readOnly;
        public Stream     stream;
        public DriveInfo  driveInfo = new DriveInfo();
        public FileFormat fileFormat;
        public bool trackAndSectorAreTrackAndSector = true;
        public int track;
        public int sector;
    }

    class Program
    {
        public static bool verboseOutput;

        static string shutDown;
        static bool done = false;
        static List<Ports> listPorts = new List<Ports>();
        static CultureInfo ci = new CultureInfo("en-us");

        static void ParseConfigFile(string[] args)
        {
            string applicationPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string configPath;

            if (args.Length > 0)
            {
                configPath = args[0];
            }
            else
            {
                configPath = Path.Combine(applicationPath, "fnconfig.xml");
            }

            XmlDocument doc = new XmlDocument();
            doc.Load(configPath);

            foreach (XmlNode node in doc.DocumentElement.ChildNodes)
            {
                if (node.Name == "Ports")
                {
                    foreach (XmlNode portNode in node.ChildNodes)
                    {
                        Ports p = new Ports();
                        p.defaultStartDirectory = Directory.GetCurrentDirectory();
                        if (portNode.Attributes["num"] != null)
                        {
                            p.port = $"COM{portNode.Attributes["num"].Value}";
                        }
                        else
                        {
                            p.port = portNode.Attributes["device"].Value;
                        }

                        foreach (XmlNode paramters in portNode.ChildNodes)
                        {
                            switch (paramters.Name)
                            {
                                case "Rate":
                                    p.rate = Convert.ToInt32(paramters.InnerText);
                                    break;

                                case "Verbose":
                                    p.verbose = paramters.InnerText;
                                    break;

                                case "AutoMount":
                                    p.autoMount = paramters.InnerText;
                                    break;

                                case "DefaultDirectory":
                                    p.defaultStartDirectory = paramters.InnerText;
                                    break;

                                case "ImageFiles":
                                    {
                                        int index = 0;
                                        foreach (XmlNode imageFile in paramters.ChildNodes)
                                        {
                                            if (imageFile.Name == "ImageFile")
                                            {
                                                p.imageFile[index] = new ImageFile();
                                                p.imageFile[index].Name = imageFile.InnerText;
                                                index++;
                                            }
                                        }
                                    }
                                    break;
                            }
                        }

                        if (!Directory.Exists(p.defaultStartDirectory))
                        {
                            Console.WriteLine("Invalid default directory setting to {0}", applicationPath);
                            p.defaultStartDirectory = applicationPath;
                        }
                        p.currentWorkingDirectory = p.defaultStartDirectory;

                        bool portAlreadyExists = false;

                        for (int i = 0; i < listPorts.Count; i++)
                        {
                            // we already have this port in the list - just update it
                            if (listPorts[i].port == p.port)
                            {
                                portAlreadyExists = true;

                                listPorts[i].defaultStartDirectory = p.defaultStartDirectory;
                                listPorts[i].rate = p.rate;
                                listPorts[i].verbose = p.verbose;
                                listPorts[i].autoMount = p.autoMount;

                                int imageFileIndex = 0;
                                foreach (ImageFile imageFile in p.imageFile)
                                {
                                    if (listPorts[i].imageFile[imageFileIndex].Name != null && imageFile.Name != null)
                                        listPorts[i].imageFile[imageFileIndex].Name = imageFile.Name;

                                    imageFileIndex++;
                                }
                            }
                        }

                        if (!portAlreadyExists)
                            listPorts.Add(p);
                    }
                }
                else if (node.Name == "Shutdown")
                {
                    shutDown = node.InnerText;
                }
            }
        }

        // this is the main loop while we are connected waiting for commands
        static void StateConnectionStateConnected(Ports serialPort, int c)
        {
            if (c == 'U')
            {
                serialPort.SetAttribute((int)CONSOLE_ATTRIBUTE.REVERSE_ATTRIBUTE);
                Console.Write(string.Format("{0} ", c.ToString("X2")));
                serialPort.SetAttribute((int)CONSOLE_ATTRIBUTE.NORMAL_ATTRIBUTE);

                serialPort.State = CONNECTION_STATE.SYNCRONIZING;
                serialPort.WriteByte((byte)0x55);
            }
            else if (c == '?')
            {
                // Query Current Directory

                serialPort.SetAttribute((int)CONSOLE_ATTRIBUTE.REVERSE_ATTRIBUTE);
                Console.Write(serialPort.currentWorkingDirectory);
                serialPort.SetAttribute((int)CONSOLE_ATTRIBUTE.NORMAL_ATTRIBUTE);
                Console.Write("\n");

                serialPort.sp.Write(serialPort.currentWorkingDirectory);
                serialPort.WriteByte((byte)0x0D, false);
                serialPort.WriteByte((byte)0x06);
            }
            else if (c == 'S')      // used by FLEX to read a sector from the PC
            {
                // 'S'end Sector Request

                if (serialPort.imageFile[serialPort.currentDrive] == null)
                    serialPort.imageFile[serialPort.currentDrive] = new ImageFile();

                serialPort.imageFile[serialPort.currentDrive].trackAndSectorAreTrackAndSector = true;
                serialPort.imageFile[serialPort.currentDrive].driveInfo.mode = (int)SECTOR_ACCESS_MODE.S_MODE;
                serialPort.State = CONNECTION_STATE.GET_TRACK;
            }
            else if (c == 'R')      // used by FLEX to write a sector to the PC
            {
                // 'R'eceive Sector Request

                if (serialPort.imageFile[serialPort.currentDrive] == null)
                    serialPort.imageFile[serialPort.currentDrive] = new ImageFile();

                serialPort.imageFile[serialPort.currentDrive].trackAndSectorAreTrackAndSector = true;
                serialPort.imageFile[serialPort.currentDrive].driveInfo.mode = (int)SECTOR_ACCESS_MODE.R_MODE;
                serialPort.State = CONNECTION_STATE.GET_TRACK;
            }
            else if (c == 'E')
            {
                // Exit

                serialPort.State = CONNECTION_STATE.NOT_CONNECTED;
                serialPort.WriteByte((byte)0x06);
                if (shutDown == "T")
                    done = true;
            }
            else if (c == 'Q')          // Quick Check for active connection
            {
                serialPort.WriteByte((byte)0x06);
            }
            else if (c == 'M')          // Mount drive image
            {
                serialPort.commandFilename = "";
                serialPort.State = CONNECTION_STATE.MOUNT_GETFILENAME;
            }
            else if (c == 'D')
            {
                // Delete file command

                serialPort.commandFilename = "";
                serialPort.State = CONNECTION_STATE.DELETE_GETFILENAME;
            }
            else if (c == 'A')
            {
                // Dir file command

                serialPort.commandFilename = "";
                serialPort.State = CONNECTION_STATE.DIR_GETFILENAME;
            }
            else if (c == 'I')
            {
                // List Directories file command

                serialPort.dirFilename = "dirtxt" + serialPort.port.ToString() + serialPort.currentDrive.ToString() + ".txt";

                if (serialPort.streamDir != null)
                {
                    serialPort.streamDir.Dispose();
                    serialPort.streamDir = null;
                    File.Delete(serialPort.dirFilename);
                }

                using (var fileStream = File.Open(serialPort.dirFilename, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
                using (var stringWriter = new StreamWriter(fileStream, Encoding.ASCII))
                {
                    // Get the drive and volume information
                    System.IO.DriveInfo driveInfo = new System.IO.DriveInfo(Directory.GetDirectoryRoot(serialPort.currentWorkingDirectory));
                    long availableFreeSpace = driveInfo.AvailableFreeSpace;
                    string driveName = driveInfo.Name;
                    string volumeLabel = driveInfo.VolumeLabel;

                    var topLine = string.Format("\r\nVolume in Drive {0} is {1}", driveName, volumeLabel);
                    stringWriter.Write(topLine);
                    Console.WriteLine(topLine);

                    stringWriter.Write(serialPort.currentWorkingDirectory + "\r\n\r\n");
                    Console.WriteLine(serialPort.currentWorkingDirectory);

                    // get the list of directories in the current working directory
                    string[] files = Directory.GetDirectories(serialPort.currentWorkingDirectory);

                    int maxFilenameSize = 0;
                    foreach (string file in files)
                    {
                        if (file.Length > maxFilenameSize)
                            maxFilenameSize = file.Length;
                    }
                    maxFilenameSize = maxFilenameSize - serialPort.currentWorkingDirectory.Length;

                    int fileCount = 0;
                    foreach (string file in files)
                    {
                        FileInfo fi = new FileInfo(file);
                        DateTime fCreation = fi.CreationTime;

                        string fileInfoLine = string.Format("{0}    <DIR>   {1:MM/dd/yyyy HH:mm:ss}\r\n", Path.GetFileName(file).PadRight(maxFilenameSize), fCreation);
                        if (fileInfoLine.Length > 0)
                        {
                            fileCount += 1;
                            stringWriter.Write(fileInfoLine);
                        }
                    }

                    stringWriter.Write("\r\n");
                    stringWriter.Write("    {0} files\r\n", fileCount);
                    stringWriter.Write("    {0} bytes free\r\n", availableFreeSpace);
                }


                try
                {
                    serialPort.streamDir = File.OpenText(serialPort.dirFilename);
                    serialPort.WriteByte((byte)'\r', false);
                    serialPort.WriteByte((byte)'\n', false);
                    serialPort.State = CONNECTION_STATE.SENDING_DIR;
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);

                    File.Delete(serialPort.dirFilename);

                    serialPort.WriteByte((byte)0x06);
                    serialPort.State = CONNECTION_STATE.CONNECTED;
                }
            }
            else if (c == 'P')
            {
                // Change Directory

                serialPort.commandFilename = "";
                serialPort.State = CONNECTION_STATE.CD_GETFILENAME;
            }
            else if (c == 'V')
            {
                // Change Drive (and optionally the directory)

                serialPort.commandFilename = "";
                serialPort.State = CONNECTION_STATE.DRIVE_GETFILENAME;
            }
            else if (c == 'C')
            {
                // Create a drive image

                serialPort.createFilename = "";
                serialPort.createPath = "";
                serialPort.createVolumeNumber = "";
                serialPort.createTrackCount = "";
                serialPort.createSectorCount = "";

                serialPort.State = CONNECTION_STATE.CREATE_GETPARAMETERS;
                serialPort.createState = CREATE_STATE.GET_CREATE_PATH;
            }

            // now the Extended multi drive versions - these are what makes FLEXNet different from NETPC.
            //
            //      NETPC only allowed one drive to be mounted at a time. FLEXnet allows all four FLEX
            //      drives to be remote by providing mount points withinh FLEXNet for 4 image files per
            //      serial port.

            else if (c == 's')
            {
                // 's'end Sector Request with drive

                serialPort.State = CONNECTION_STATE.GET_READ_DRIVE;
            }
            else if (c == 'r')
            {
                // 'r'eceive Sector Request with drive

                serialPort.State = CONNECTION_STATE.GET_WRITE_DRIVE;
            }
            else if (c == ('s' | 0x80))     // used by OS9 to read a sector from the PC - track and sector are LBN
            {
                // 'S'end Sector Request

                if (serialPort.imageFile[serialPort.currentDrive] == null)
                    serialPort.imageFile[serialPort.currentDrive] = new ImageFile();

                serialPort.imageFile[serialPort.currentDrive].trackAndSectorAreTrackAndSector = false;

                serialPort.State = CONNECTION_STATE.GET_READ_DRIVE;
            }
            else if (c == ('r' | 0x80))      // used by OS9 to write a sector to the PC- track and sector are LBN
            {
                // 'R'eceive Sector Request

                if (serialPort.imageFile[serialPort.currentDrive] == null)
                    serialPort.imageFile[serialPort.currentDrive] = new ImageFile();

                serialPort.imageFile[serialPort.currentDrive].trackAndSectorAreTrackAndSector = false;

                serialPort.State = CONNECTION_STATE.GET_WRITE_DRIVE;
            }
            else if (c == 'm')      // Mount drive image with drive
            {
                serialPort.commandFilename = "";
                serialPort.State = CONNECTION_STATE.GET_MOUNT_DRIVE;
            }
            else if (c == 'd')      // Report which disk image is mounted to requested drive
            {
                serialPort.State = CONNECTION_STATE.GET_REQUESTED_MOUNT_DRIVE;
            }
            else if (c == 'c')      // Create a drive image
            {
                serialPort.createFilename = "";
                serialPort.createPath = "";
                serialPort.createVolumeNumber = "";
                serialPort.createTrackCount = "";
                serialPort.createSectorCount = "";

                serialPort.State = CONNECTION_STATE.GET_CREATE_DRIVE;
                serialPort.createState = CREATE_STATE.GET_CREATE_PATH;
            }

            else                    // Unknown - command - go back to (int)CONNECTION_STATE.CONNECTED
            {
                if (serialPort.State != CONNECTION_STATE.CONNECTED)
                    serialPort.State = CONNECTION_STATE.CONNECTED;

                if (c != 0x20)
                {
                    serialPort.SetAttribute((int)CONSOLE_ATTRIBUTE.REVERSE_ATTRIBUTE);
                    Console.Write("\n State is reset to CONNECTED - Unknown command recieved [" + c.ToString("X2", ci) + "]");
                    serialPort.SetAttribute((int)CONSOLE_ATTRIBUTE.NORMAL_ATTRIBUTE);
                }
            }
        }

        // 's'end Sector Request with drive - this state gets the drive number

        static void StateConnectionStateGetReadDrive(Ports serialPort, int c)
        {
            serialPort.currentDrive = c;

            if (serialPort.imageFile[serialPort.currentDrive] == null)
                serialPort.imageFile[serialPort.currentDrive] = new ImageFile();

            serialPort.imageFile[serialPort.currentDrive].driveInfo.mode = (int)SECTOR_ACCESS_MODE.S_MODE;
            serialPort.State = CONNECTION_STATE.GET_TRACK;
        }

        static void StateConnectionStateGetWriteDrive(Ports serialPort, int c)
        {
            serialPort.currentDrive = c;

            if (serialPort.imageFile[serialPort.currentDrive] == null)
                serialPort.imageFile[serialPort.currentDrive] = new ImageFile();

            serialPort.imageFile[serialPort.currentDrive].driveInfo.mode = (int)SECTOR_ACCESS_MODE.R_MODE;
            serialPort.State = CONNECTION_STATE.GET_TRACK;
        }

        static void StateConnectionStateGetMountDrive(Ports serialPort, int c)
        {
            serialPort.currentDrive = c;
            serialPort.State = CONNECTION_STATE.MOUNT_GETFILENAME;
        }

        static void StateConnectionStateGetCreateDrive(Ports serialPort, int c)
        {
            serialPort.currentDrive = c;
            serialPort.State = CONNECTION_STATE.CREATE_GETPARAMETERS;
        }

        static void StateConnectionStateRecievingSector(Ports serialPort, int c)
        {
            serialPort.sectorBuffer[serialPort.sectorIndex++] = (byte)c;
            serialPort.calculatedCRC += (int)c;

            if (serialPort.sectorIndex >= 256)
            {
                serialPort.checksumIndex = 0;
                serialPort.State = CONNECTION_STATE.GET_CRC;
            }
        }

        static void StateConnectionStateGetCRC(Ports serialPort, int c)
        {
            if (serialPort.checksumIndex++ == 0)
                serialPort.checksum = (int)c * 256;
            else
            {
                serialPort.checksum += (int)c;

                byte status = serialPort.WriteSector();
                serialPort.WriteByte(status);
                serialPort.State = CONNECTION_STATE.CONNECTED;
            }
        }

        static void StateConnectionStateMountGetFilename(Ports serialPort, int c)
        {
            if (serialPort.imageFile[serialPort.currentDrive] == null)
                serialPort.imageFile[serialPort.currentDrive] = new ImageFile();

            Console.WriteLine(serialPort.commandFilename);

            if (c != 0x0d)
            {
                // just add the character to the filename
                serialPort.commandFilename += (char)c;
            }
            else
            {
                serialPort.commandFilename += ".DSK";

                // this should close any file that is currently open for this port/drive
                if (serialPort.imageFile[serialPort.currentDrive] != null)
                {
                    if (serialPort.imageFile[serialPort.currentDrive].stream != null)
                    {
                        serialPort.imageFile[serialPort.currentDrive].stream.Close();
                        serialPort.imageFile[serialPort.currentDrive].stream = null;
                    }
                }

                // Now mount the new file

                byte status = 0x06;
                if (serialPort.commandFilename.Length > 0)
                {
                    Console.WriteLine();
                    status = serialPort.MountImageFile(serialPort.commandFilename, serialPort.currentDrive);
                }

                serialPort.WriteByte(status);

                byte cMode = (byte)'W';
                if (serialPort.imageFile[serialPort.currentDrive] != null)
                {
                    if (serialPort.imageFile[serialPort.currentDrive].readOnly)
                    {
                        cMode = (byte)'R';
                    }
                }
                serialPort.WriteByte(cMode);
                serialPort.State = CONNECTION_STATE.CONNECTED;
            }
        }

        static void StateConnectionStateDeleteGetFilename(Ports serialPort, int c)
        {
            //    if (c != 0x0d)
            //        serialPort.commandFilename[serialPort.nCommandFilenameIndex++] = c;
            //    else
            //    {
            //        strcpy (serialPort.szFileToDelete, serialPort.currentWorkingDirectory );
            //        if ((strlen ((char*)serialPort.commandFilename) + strlen (serialPort.szFileToDelete)) < 126)
            //        {
            //            strcat (serialPort.szFileToDelete, "/");
            //            strcat (serialPort.szFileToDelete, (char*)serialPort.commandFilename);

            //            int nStatus = -1;

            //            // do not attempt to delete if the file is mounted

            //            for (int i = 0; i < (int) strlen (serialPort.szFileToDelete); i++)
            //            {
            //                if (serialPort.szFileToDelete[i] >= 'A' && serialPort.szFileToDelete[i] <= 'Z')
            //                    serialPort.szFileToDelete[i] = serialPort.szFileToDelete[i] & 0x5F;
            //            }

            //            if (strcmp (serialPort.szMountedFilename[serialPort.nCurrentDrive], serialPort.szFileToDelete) != 0)
            //            {
            //                // see if the file can be opened exclusively in r/w mode, if
            //                // it can - we can delete it, otherwise we fail attempting to
            //                // delete it so do not attempt

            //                FILE *x = fopen (serialPort.szFileToDelete, "r+b");
            //                if (x != NULL)
            //                {
            //                    // we were able to open it - close it and delete it

            //                    fclose (x);
            //                    nStatus = unlink (serialPort.szFileToDelete);
            //                }
            //                else
            //                    *serialPort.twWindows << "\n" << "attempted to delete open image" << "\n";
            //            }
            //            else
            //                *serialPort.twWindows << "\n" << "attempted to delete mounted image" << "\n";


            //            if (nStatus == 0)
            //            {
            //                *serialPort.twWindows << "image deleted" << "\n";
            //                rsPort->Write (0x06);
            //            }
            //            else
            //            {
            //                *serialPort.twWindows << "unable to delete image" << "\n";
            //                rsPort->Write (0x15);
            //            }
            //        }
            //        else
            //        {
            //            *serialPort.twWindows << "\n" << "attempted to delete with image name > 128 characters" << "\n";
            //            rsPort->Write (0x15);
            //        }

            //        serialPort.SetState  (CONNECTED);
            //    }
        }

        static void StateConnectionStateCDGetFilename(Ports serialPort, int c)
        {
            if (c != 0x0d)
                serialPort.commandFilename += (char)c;
            else
            {
                byte status = 0x00;

                try
                {
                    Directory.SetCurrentDirectory(serialPort.commandFilename);
                    status = 0x06;

                    serialPort.currentWorkingDirectory = Directory.GetCurrentDirectory();
                    serialPort.currentWorkingDirectory.TrimEnd('\\');
                }
                catch (Exception e)  
                {
                    Console.WriteLine(e);
                    status = 0x15;
                }

                serialPort.WriteByte(status);
                serialPort.State = CONNECTION_STATE.CONNECTED;
            }
        }

        static void StateConnectionStateDriveGetFilename(Ports serialPort, int c)
        {
            if (c != 0x0d)
                serialPort.commandFilename += (char)c;
            else
            {
                byte status = 0x00;

                try
                {
                    Directory.SetCurrentDirectory(serialPort.commandFilename);
                    status = 0x06;

                    serialPort.currentWorkingDirectory = Directory.GetCurrentDirectory();
                    serialPort.currentWorkingDirectory.TrimEnd('\\');
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    status = 0x15;
                }

                serialPort.SetAttribute((int)CONSOLE_ATTRIBUTE.REVERSE_ATTRIBUTE);
                Console.Write(status.ToString("X2", ci) + " ");
                serialPort.SetAttribute((int)CONSOLE_ATTRIBUTE.NORMAL_ATTRIBUTE);

                serialPort.State = CONNECTION_STATE.CONNECTED;
            }
        }

        static void StateConnectionStateCreateGetParameters(Ports serialPort, int c)
        {
            if (c != 0x0d)
            {
                switch (serialPort.createState)
                {
                    case CREATE_STATE.GET_CREATE_PATH:
                        serialPort.createPath += (char)c;
                        break;
                    case CREATE_STATE.GET_CREATE_NAME:
                        serialPort.createFilename += (char)c;
                        break;
                    case CREATE_STATE.GET_CREATE_VOLUME:
                        serialPort.createVolumeNumber += (char)c;
                        break;
                    case CREATE_STATE.GET_CREATE_TRACK_COUNT:
                        serialPort.createTrackCount += (char)c;
                        break;
                    case CREATE_STATE.GET_CREATE_SECTOR_COUNT:
                        serialPort.createSectorCount += (char)c;
                        break;
                    default:
                        serialPort.State = CONNECTION_STATE.CONNECTED;
                        break;
                }
            }
            else
            {
                if (serialPort.createState != CREATE_STATE.GET_CREATE_SECTOR_COUNT)
                {
                    serialPort.createState++;
                    serialPort.State = CONNECTION_STATE.CREATE_GETPARAMETERS;
                }
                else
                {
                    string fullFilename = serialPort.createPath + "/" + serialPort.createFilename + ".DSK";

                    byte status;
                    Console.Write("\n" + "Creating Image File " + fullFilename);
                    status = serialPort.CreateImageFile();
                    serialPort.WriteByte(status);

                    // Cannot automount because we do not know what drive to mount image too.
                    //
                    //if (serialPort.autoMount.ToUpper() == "T" || serialPort.autoMount.ToUpper() == "Y")
                    //{
                    //    if (serialPort.createPath ==  ".")
                    //    {
                    //        if (serialPort.imageFile[serialPort.currentDrive].stream != null)
                    //        {
                    //            serialPort.imageFile[serialPort.currentDrive].stream.Close();
                    //            serialPort.imageFile[serialPort.currentDrive].stream = null;
                    //        }

                    //        serialPort.MountImageFile(serialPort.currentWorkingDirectory + "/" + serialPort.createFilename + ".DSK", serialPort.currentDrive);
                    //    }
                    //    else
                    //    {
                    //        try
                    //        {
                    //            Directory.SetCurrentDirectory(serialPort.createPath);
                    //            if (serialPort.imageFile[serialPort.currentDrive].stream != null)
                    //            {
                    //                serialPort.imageFile[serialPort.currentDrive].stream.Close();
                    //                serialPort.imageFile[serialPort.currentDrive].stream = null;
                    //            }

                    //            serialPort.MountImageFile(serialPort.createPath + "/" + serialPort.createFilename + ".DSK", serialPort.currentDrive);
                    //        }
                    //        catch
                    //        {
                    //        }
                    //    }
                    //}
                    serialPort.State = CONNECTION_STATE.CONNECTED;
                }
            }
        }

        static void InitializeFromConfigFile(string[] args)
        {

            ParseConfigFile(args);

            foreach (Ports serialPort in listPorts)
            {
                int nIndex = 0;

                if (serialPort.sp != null)
                {
                    // if we come in here with a non-null value for serialPort.sp that means that we have a COM port open for this logical serial port
                    // we must close it before we can open it again or open another.
                    serialPort.sp.Dispose();
                }

                if (serialPort.sp == null)
                {
                    serialPort.sp = new SerialPort(serialPort.port, serialPort.rate, Parity.None, 8, StopBits.One);
                }

                serialPort.sp.ReadBufferSize = 32768;
                serialPort.sp.WriteBufferSize = 32768;
                try
                {
                    serialPort.sp.Open();
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                    try
                    {
                        serialPort.sp.Close();
                        serialPort.sp.Open();
                    }
                    catch (Exception e1)
                    {
                        Console.WriteLine(e1.Message);
                    }
                }

                serialPort.State = CONNECTION_STATE.NOT_CONNECTED;

                foreach (ImageFile imageFile in serialPort.imageFile)
                {
                    if (imageFile != null)
                    {
                        serialPort.MountImageFile(imageFile.Name + ".DSK", nIndex);
                    }
                    nIndex++;
                }

                Console.WriteLine(string.Format("{0} parameters:", serialPort.port));
                Console.WriteLine(string.Format("    Rate:              {0}", serialPort.rate));
                Console.WriteLine(string.Format("    Verbose:           {0}", serialPort.verbose));
                Console.WriteLine(string.Format("    AutoMount:         {0}", serialPort.autoMount));
                Console.WriteLine(string.Format("    DefaultDirectory   {0}", serialPort.defaultStartDirectory));
                Console.WriteLine(string.Format("    ImageFiles"));

                for (int imageFileIndex = 0; imageFileIndex < serialPort.imageFile.Length; imageFileIndex++)
                {
                    if (serialPort.imageFile[imageFileIndex] == null)
                        serialPort.imageFile[imageFileIndex] = new ImageFile();

                    Console.WriteLine(string.Format("        {0} - {1}", imageFileIndex, serialPort.imageFile[imageFileIndex].Name));
                }
                Console.WriteLine(string.Format("    Current Working Directory: {0}", serialPort.currentWorkingDirectory));

                serialPort.sp.DtrEnable = true;
                serialPort.sp.RtsEnable = true;
            }
        }

        static void ProcessRequests()
        {
            while (!done)
            {
                foreach (Ports serialPort in listPorts)
                {
                    try
                    {
                        if (serialPort.sp.BytesToRead > 0)
                        {
                            int c = serialPort.sp.ReadByte();

                            if ((serialPort.State != CONNECTION_STATE.RECEIVING_SECTOR) &&
                                (serialPort.State != CONNECTION_STATE.GET_CRC))
                            {
                                //if ((!lastActivityWasServer && !lastActivityWasClient) || lastActivityWasServer)
                                //{
                                //    Console.WriteLine();
                                //    Console.Write("CLIENT: ");
                                //}

                                Console.Write(c.ToString("X2", ci) + " ");

                                //lastActivityWasServer = false;
                                //lastActivityWasClient = true;
                            }

                            switch (serialPort.State)
                            {
                                case CONNECTION_STATE.NOT_CONNECTED:
                                    serialPort.StateConnectionStateNotConnected(c);
                                    break;

                                case CONNECTION_STATE.SYNCRONIZING:
                                    serialPort.StateConnectionStateSynchronizing(c);
                                    break;

                                case CONNECTION_STATE.CONNECTED:                   StateConnectionStateConnected               (serialPort, c); break;
                                case CONNECTION_STATE.GET_REQUESTED_MOUNT_DRIVE:
                                    serialPort.StateConnectionStateGetRequestedMountDrive(c);
                                    break;

                                case CONNECTION_STATE.GET_READ_DRIVE:              StateConnectionStateGetReadDrive            (serialPort, c); break;
                                case CONNECTION_STATE.GET_WRITE_DRIVE:             StateConnectionStateGetWriteDrive           (serialPort, c); break;
                                case CONNECTION_STATE.GET_MOUNT_DRIVE:             StateConnectionStateGetMountDrive           (serialPort, c); break;
                                case CONNECTION_STATE.GET_CREATE_DRIVE:            StateConnectionStateGetCreateDrive          (serialPort, c); break;
                                case CONNECTION_STATE.GET_TRACK:
                                    serialPort.StateConnectionStateGetTrack(c);
                                    break;

                                case CONNECTION_STATE.GET_SECTOR:
                                    serialPort.StateConnectionStateGetSector(c);
                                    break;

                                case CONNECTION_STATE.RECEIVING_SECTOR:            StateConnectionStateRecievingSector         (serialPort, c); break;
                                case CONNECTION_STATE.GET_CRC:                     StateConnectionStateGetCRC                  (serialPort, c); break;
                                case CONNECTION_STATE.MOUNT_GETFILENAME:           StateConnectionStateMountGetFilename        (serialPort, c); break;
                                case CONNECTION_STATE.DELETE_GETFILENAME:          StateConnectionStateDeleteGetFilename       (serialPort, c); break;
                                case CONNECTION_STATE.DIR_GETFILENAME:
                                    serialPort.StateConnectionStateDirGetFilename(c);
                                    break;

                                case CONNECTION_STATE.CD_GETFILENAME:
                                    serialPort.StateConnectionStateCDGetFilename(c);
                                    break;

                                case CONNECTION_STATE.DRIVE_GETFILENAME:           StateConnectionStateDriveGetFilename        (serialPort, c); break;
                                case CONNECTION_STATE.SENDING_DIR:
                                    serialPort.StateConnectionStateSendingDir(c);
                                    break;

                                case CONNECTION_STATE.CREATE_GETPARAMETERS:        StateConnectionStateCreateGetParameters     (serialPort, c); break;
                                case CONNECTION_STATE.WAIT_ACK:
                                    serialPort.StateConnectionStateWaitACK(c);
                                    break;

                                default:
                                    serialPort.State = CONNECTION_STATE.NOT_CONNECTED   ;
                                    //sprintf (szHexTemp, "%02X", c);
                                    //*StatusLine << '\n' << "State is reset to NOT_CONNECTED - Unknown STATE " << szHexTemp;
                                    break;
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                    }
                }
            }
        }

        static void Main(string[] args)
        {
            InitializeFromConfigFile(args);
            ProcessRequests();
        }
    }
}
