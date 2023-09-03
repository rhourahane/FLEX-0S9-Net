using System;
using System.Globalization;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Xml;

using System.IO;
using System.IO.Ports;
using System.Reflection;

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

enum CONNECTION_STATE
{
    NOT_CONNECTED = -1,
    SYNCRONIZING,
    CONNECTED,
    GET_REQUESTED_MOUNT_DRIVE,
    GET_READ_DRIVE,
    GET_WRITE_DRIVE,
    GET_MOUNT_DRIVE,
    GET_CREATE_DRIVE,
    GET_TRACK,
    GET_SECTOR,
    RECEIVING_SECTOR,
    GET_CRC,
    MOUNT_GETFILENAME,
    DELETE_GETFILENAME,
    DIR_GETFILENAME,
    CD_GETFILENAME,
    DRIVE_GETFILENAME,
    SENDING_DIR,
    CREATE_GETPARAMETERS,
    WAIT_ACK,
    PROCESSING_MOUNT,
    PROCESSING_DIR,
    PROCESSING_LIST
};

enum CREATE_STATE
{
    GET_CREATE_PATH = 0,
    GET_CREATE_NAME,
    GET_CREATE_VOLUME,
    GET_CREATE_TRACK_COUNT,
    GET_CREATE_SECTOR_COUNT,
    CREATE_THE_IMAGE
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
        public int      mode;
        public string   MountedFilename;
        public int      NumberOfTracks;
        public byte[]   cNumberOfSectorsPerTrack = new byte[1];
        public long     NumberOfBytesPerTrack;
        public long     NumberOfSectorsPerTrack;
        public int      NumberOfSectorsPerCluster;
        public long     TotalNumberOfSectorOnMedia;
        public int      LogicalSectorSize;
    }

    public class ImageFile
    {
        public string     Name;
        public bool       readOnly;
        public Stream     stream;
        public DriveInfo  driveInfo = new DriveInfo();
        public FileFormat fileFormat;
        public bool trackAndSectorAreTrackAndSector = true;
    }

    class Program
    {
        //int             nNumberOfPorts;
        //int             FocusWindow = -1;
        //int             Done        = 0;
        //int             Helping     = 1;

        public static bool verboseOutput;

        static string shutDown;
        static bool done = false;

        //long            NextStatusUpdate = 0;

        static List<Ports> listPorts = new List<Ports>();
        static ArrayList ports = new ArrayList();

        static CultureInfo ci = new CultureInfo("en-us");

        static void ParseConfigFile()
        {
            string ApplicationPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            XmlDocument doc = new XmlDocument();
            doc.Load(Path.Combine(ApplicationPath, "fnconfig.xml"));

            foreach (XmlNode node in doc.DocumentElement.ChildNodes)
            {
                if (node.Name == "Ports")
                {
                    foreach (XmlNode portNode in node.ChildNodes)
                    {
                        Ports p = new Ports();
                        p.defaultStartDirectory = Directory.GetCurrentDirectory();
                        p.port = Convert.ToInt32(portNode.Attributes["num"].Value);
                        foreach (XmlNode paramters in portNode.ChildNodes)
                        {
                            if (paramters.Name == "Rate")
                            {
                                p.rate = Convert.ToInt32(paramters.InnerText);
                            }
                            else if (paramters.Name == "CpuSpeed")
                            {
                                p.speed = paramters.InnerText;
                            }
                            else if (paramters.Name == "Verbose")
                            {
                                p.verbose = paramters.InnerText;
                            }
                            else if (paramters.Name == "AutoMount")
                            {
                                p.autoMount = paramters.InnerText;
                            }
                            else if (paramters.Name == "DefaultDirectory")
                            {
                                p.defaultStartDirectory = paramters.InnerText;
                            }
                            else if (paramters.Name == "ImageFiles")
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
                                listPorts[i].speed = p.speed;
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


        // Waiting for Sync Byte

        static void StateConnectionStateNotConnected(Ports serialPort, int c)
        {
            if (c == 0x55)
            {
                serialPort.SetState((int)CONNECTION_STATE.SYNCRONIZING);
                serialPort.WriteByte((byte)0x55);
            }
        }

        // send ack to sync

        static void StateConnectionStateSynchronizing(Ports serialPort, int c)
        {
            if (c != 0x55)
            {
                if (c == 0xAA)
                {
                    serialPort.WriteByte((byte)0xAA);
                    serialPort.SetState((int)CONNECTION_STATE.CONNECTED);
                }
                else
                {
                    serialPort.SetState((int)CONNECTION_STATE.NOT_CONNECTED);
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

                serialPort.SetState((int)CONNECTION_STATE.SYNCRONIZING);
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
                serialPort.SetState((int)CONNECTION_STATE.GET_TRACK);
            }
            else if (c == 'R')      // used by FLEX to write a sector to the PC
            {
                // 'R'eceive Sector Request

                if (serialPort.imageFile[serialPort.currentDrive] == null)
                    serialPort.imageFile[serialPort.currentDrive] = new ImageFile();

                serialPort.imageFile[serialPort.currentDrive].trackAndSectorAreTrackAndSector = true;
                serialPort.imageFile[serialPort.currentDrive].driveInfo.mode = (int)SECTOR_ACCESS_MODE.R_MODE;
                serialPort.SetState((int)CONNECTION_STATE.GET_TRACK);
            }
            else if (c == 'E')
            {
                // Exit

                serialPort.SetState((int)CONNECTION_STATE.NOT_CONNECTED);
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
                serialPort.SetState((int)CONNECTION_STATE.MOUNT_GETFILENAME);
            }
            else if (c == 'D')
            {
                // Delete file command

                serialPort.commandFilename = "";
                serialPort.SetState((int)CONNECTION_STATE.DELETE_GETFILENAME);
            }
            else if (c == 'A')
            {
                // Dir file command

                serialPort.commandFilename = "";
                serialPort.SetState((int)CONNECTION_STATE.DIR_GETFILENAME);
            }
            else if (c == 'I')
            {
                // List Directories file command

                serialPort.dirFilename = "dirtxt" + serialPort.port.ToString() + serialPort.currentDrive.ToString() + ".txt";

                if (serialPort.streamDir != null)
                {
                    serialPort.streamDir.Close();
                    serialPort.streamDir = null;
                    File.Delete(serialPort.dirFilename);
                }
                serialPort.streamDir = File.Open(serialPort.dirFilename, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);

                // Get the drive and volume information

                System.IO.DriveInfo driveInfo = new System.IO.DriveInfo(Directory.GetDirectoryRoot(serialPort.currentWorkingDirectory));
                long availableFreeSpace = driveInfo.AvailableFreeSpace;
                string driveName = driveInfo.Name;
                string volumeLabel = driveInfo.VolumeLabel;

                byte[] volumeBuffer = Encoding.ASCII.GetBytes("\r\n Volume in Drive " + driveName + " is " + volumeLabel + "\r\n");
                serialPort.streamDir.Write(volumeBuffer, 0, volumeBuffer.Length);
                Console.WriteLine("\r\n Volume in Drive " + driveName + " is " + volumeLabel);

                byte[] wrkDirBuffer = Encoding.ASCII.GetBytes(serialPort.currentWorkingDirectory + "\r\n\r\n");
                serialPort.streamDir.Write(wrkDirBuffer, 0, wrkDirBuffer.Length);
                Console.WriteLine(serialPort.currentWorkingDirectory + "\r\n");

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

                    string fileInfoLine = file;
                    fileInfoLine = fileInfoLine.Replace(serialPort.currentWorkingDirectory + @"\", ""); // get rid of path info
                    fileInfoLine = fileInfoLine.PadRight(maxFilenameSize, ' ');                         // pad to proper length
                    fileInfoLine = fileInfoLine + "    <DIR>   " +
                                                  fCreation.Month.ToString("00") + "/" + fCreation.Day.ToString("00") + "/" + fCreation.Year.ToString("0000") +
                                                  " " +
                                                  fCreation.Hour.ToString("00") + ":" + fCreation.Minute.ToString("00") + ":" + fCreation.Second.ToString("00") +
                                                  "\r\n";
                    if (fileInfoLine.Length > 0)
                    {
                        fileCount += 1;
                        byte[] bArray = Encoding.ASCII.GetBytes(fileInfoLine);
                        serialPort.streamDir.Write(bArray, 0, bArray.Length);
                    }
                }

                byte[] fileCountBuffer = Encoding.ASCII.GetBytes("    " + fileCount.ToString() + " files\r\n");
                serialPort.streamDir.Write(fileCountBuffer, 0, fileCountBuffer.Length);

                byte[] freeSpaceBuffer = Encoding.ASCII.GetBytes("        " + availableFreeSpace.ToString() + " bytes free\r\n\r\n");
                serialPort.streamDir.Write(freeSpaceBuffer, 0, freeSpaceBuffer.Length);
                serialPort.streamDir.Close();

                try
                {
                    serialPort.streamDir = File.Open(serialPort.dirFilename, FileMode.Open, FileAccess.Read, FileShare.Read);
                    serialPort.WriteByte((byte)'\r', false);
                    serialPort.WriteByte((byte)'\n', false);
                    serialPort.SetState((int)CONNECTION_STATE.SENDING_DIR);
                }
                catch
                {
                    File.Delete(serialPort.dirFilename);

                    serialPort.WriteByte((byte)0x06);
                    serialPort.SetState((int)CONNECTION_STATE.CONNECTED);
                }
            }
            else if (c == 'P')
            {
                // Change Directory

                serialPort.commandFilename = "";
                serialPort.SetState((int)CONNECTION_STATE.CD_GETFILENAME);
            }
            else if (c == 'V')
            {
                // Change Drive (and optionally the directory)

                serialPort.commandFilename = "";
                serialPort.SetState((int)CONNECTION_STATE.DRIVE_GETFILENAME);
            }
            else if (c == 'C')
            {
                // Create a drive image

                serialPort.createFilename = "";
                serialPort.createPath = "";
                serialPort.createVolumeNumber = "";
                serialPort.createTrackCount = "";
                serialPort.createSectorCount = "";

                serialPort.SetState((int)CONNECTION_STATE.CREATE_GETPARAMETERS);
                serialPort.createState = (int)CREATE_STATE.GET_CREATE_PATH;
            }

            // now the Extended multi drive versions - these are what makes FLEXNet different from NETPC.
            //
            //      NETPC only allowed one drive to be mounted at a time. FLEXnet allows all four FLEX
            //      drives to be remote by providing mount points withinh FLEXNet for 4 image files per
            //      serial port.

            else if (c == 's')
            {
                // 's'end Sector Request with drive

                serialPort.SetState((int)CONNECTION_STATE.GET_READ_DRIVE);
            }
            else if (c == 'r')
            {
                // 'r'eceive Sector Request with drive

                serialPort.SetState((int)CONNECTION_STATE.GET_WRITE_DRIVE);
            }
            else if (c == ('s' | 0x80))     // used by OS9 to read a sector from the PC - track and sector are LBN
            {
                // 'S'end Sector Request

                if (serialPort.imageFile[serialPort.currentDrive] == null)
                    serialPort.imageFile[serialPort.currentDrive] = new ImageFile();

                serialPort.imageFile[serialPort.currentDrive].trackAndSectorAreTrackAndSector = false;

                serialPort.SetState((int)CONNECTION_STATE.GET_READ_DRIVE);
            }
            else if (c == ('r' | 0x80))      // used by OS9 to write a sector to the PC- track and sector are LBN
            {
                // 'R'eceive Sector Request

                if (serialPort.imageFile[serialPort.currentDrive] == null)
                    serialPort.imageFile[serialPort.currentDrive] = new ImageFile();

                serialPort.imageFile[serialPort.currentDrive].trackAndSectorAreTrackAndSector = false;

                serialPort.SetState((int)CONNECTION_STATE.GET_WRITE_DRIVE);
            }
            else if (c == 'm')      // Mount drive image with drive
            {
                serialPort.commandFilename = "";
                serialPort.SetState((int)CONNECTION_STATE.GET_MOUNT_DRIVE);
            }
            else if (c == 'd')      // Report which disk image is mounted to requested drive
            {
                serialPort.SetState((int)CONNECTION_STATE.GET_REQUESTED_MOUNT_DRIVE);
            }
            else if (c == 'c')      // Create a drive image
            {
                serialPort.createFilename = "";
                serialPort.createPath = "";
                serialPort.createVolumeNumber = "";
                serialPort.createTrackCount = "";
                serialPort.createSectorCount = "";

                serialPort.SetState((int)CONNECTION_STATE.GET_CREATE_DRIVE);
                serialPort.createState = (int)CREATE_STATE.GET_CREATE_PATH;
            }

            else                    // Unknown - command - go back to (int)CONNECTION_STATE.CONNECTED
            {
                if (serialPort.state != (int)CONNECTION_STATE.CONNECTED)
                    serialPort.SetState((int)CONNECTION_STATE.CONNECTED);

                if (c != 0x20)
                {
                    serialPort.SetAttribute((int)CONSOLE_ATTRIBUTE.REVERSE_ATTRIBUTE);
                    Console.Write("\n State is reset to CONNECTED - Unknown command recieved [" + c.ToString("X2", ci) + "]");
                    serialPort.SetAttribute((int)CONSOLE_ATTRIBUTE.NORMAL_ATTRIBUTE);
                }
            }
        }

        // 'd' command recieved - Report which disk image is mounted to requested drive 

        static void StateConnectionStateGetRequestedMountDrive(Ports serialPort, int c)
        {
            // Report which disk image is mounted to requested drive

            serialPort.currentDrive = c;

            serialPort.SetAttribute((int)CONSOLE_ATTRIBUTE.REVERSE_ATTRIBUTE);
            Console.Write(serialPort.currentWorkingDirectory);
            Console.Write("\r");
            Console.Write("\n");
            serialPort.SetAttribute((int)CONSOLE_ATTRIBUTE.NORMAL_ATTRIBUTE);

            if (serialPort.imageFile[serialPort.currentDrive] == null)
                serialPort.imageFile[serialPort.currentDrive] = new ImageFile();

            serialPort.sp.Write(serialPort.imageFile[serialPort.currentDrive].driveInfo.MountedFilename);

            serialPort.WriteByte(0x0D, false);
            serialPort.WriteByte(0x06);

            serialPort.SetState((int)CONNECTION_STATE.CONNECTED);
        }

        // 's'end Sector Request with drive - this state gets the drive number

        static void StateConnectionStateGetReadDrive(Ports serialPort, int c)
        {
            serialPort.currentDrive = c;

            if (serialPort.imageFile[serialPort.currentDrive] == null)
                serialPort.imageFile[serialPort.currentDrive] = new ImageFile();

            serialPort.imageFile[serialPort.currentDrive].driveInfo.mode = (int)SECTOR_ACCESS_MODE.S_MODE;
            serialPort.SetState((int)CONNECTION_STATE.GET_TRACK);
        }

        static void StateConnectionStateGetWriteDrive(Ports serialPort, int c)
        {
            serialPort.currentDrive = c;

            if (serialPort.imageFile[serialPort.currentDrive] == null)
                serialPort.imageFile[serialPort.currentDrive] = new ImageFile();

            serialPort.imageFile[serialPort.currentDrive].driveInfo.mode = (int)SECTOR_ACCESS_MODE.R_MODE;
            serialPort.SetState((int)CONNECTION_STATE.GET_TRACK);
        }

        static void StateConnectionStateGetMountDrive(Ports serialPort, int c)
        {
            serialPort.currentDrive = c;
            serialPort.SetState((int)CONNECTION_STATE.MOUNT_GETFILENAME);
        }

        static void StateConnectionStateGetCreateDrive(Ports serialPort, int c)
        {
            serialPort.currentDrive = c;
            serialPort.SetState((int)CONNECTION_STATE.CREATE_GETPARAMETERS);
        }

        static void StateConnectionStateGetTrack(Ports serialPort, int c)
        {
            serialPort.track[serialPort.currentDrive] = c;
            serialPort.SetState((int)CONNECTION_STATE.GET_SECTOR);
        }

        static void StateConnectionStateGetSector(Ports serialPort, int c)
        {
            serialPort.sector[serialPort.currentDrive] = c;

            if (serialPort.imageFile[serialPort.currentDrive] == null)
                serialPort.imageFile[serialPort.currentDrive] = new ImageFile();

            if (serialPort.imageFile[serialPort.currentDrive].driveInfo.mode == (int)SECTOR_ACCESS_MODE.S_MODE)
            {
                Console.WriteLine("\r\nState is SENDING_SECTOR");
                serialPort.SendSector();
                serialPort.SetState((int)CONNECTION_STATE.WAIT_ACK);
            }
            else
            {
                serialPort.sectorIndex = 0;
                serialPort.calculatedCRC = 0;
                serialPort.SetState((int)CONNECTION_STATE.RECEIVING_SECTOR);
            }
        }

        static void StateConnectionStateRecievingSector(Ports serialPort, int c)
        {
            serialPort.sectorBuffer[serialPort.sectorIndex++] = (byte)c;
            serialPort.calculatedCRC += (int)c;

            if (serialPort.sectorIndex >= 256)
            {
                serialPort.checksumIndex = 0;
                serialPort.SetState((int)CONNECTION_STATE.GET_CRC);
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
                serialPort.SetState((int)CONNECTION_STATE.CONNECTED);
            }
        }

        static void StateConnectionStateMountGetFilename(Ports serialPort, int c)
        {
            if (serialPort.imageFile[serialPort.currentDrive] == null)
                serialPort.imageFile[serialPort.currentDrive] = new ImageFile();

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
                serialPort.SetState((int)CONNECTION_STATE.CONNECTED);
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

        static void StateConnectionStateDirGetFilename(Ports serialPort, int c)
        {
            if (c != 0x0d)
                serialPort.commandFilename += (char)c;
            else
            {
                if (serialPort.commandFilename.Length == 0)
                    serialPort.commandFilename = "*.DSK";
                else
                    serialPort.commandFilename += ".DSK";

                serialPort.dirFilename = "dirtxt" + serialPort.port.ToString() + serialPort.currentDrive.ToString() + ".txt";

                if (serialPort.streamDir != null)
                {
                    serialPort.streamDir.Close();
                    serialPort.streamDir = null;
                    File.Delete(serialPort.dirFilename);
                }

                // get the list of files in the current working directory

                string[] files = Directory.GetFiles(serialPort.currentWorkingDirectory, "*.DSK", SearchOption.TopDirectoryOnly);

                serialPort.streamDir = File.Open(serialPort.dirFilename, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);

                System.IO.DriveInfo driveInfo = new System.IO.DriveInfo(Directory.GetDirectoryRoot(serialPort.currentWorkingDirectory));
                long availableFreeSpace = driveInfo.AvailableFreeSpace;
                string driveName = driveInfo.Name;
                string volumeLabel = driveInfo.VolumeLabel;

                byte[] volumeBuffer = Encoding.ASCII.GetBytes("\r\n Volume in Drive " + driveName + " is " + volumeLabel + "\r\n");
                serialPort.streamDir.Write(volumeBuffer, 0, volumeBuffer.Length);

                byte[] buffer = Encoding.ASCII.GetBytes(serialPort.currentWorkingDirectory + "\r\n\r\n");
                serialPort.streamDir.Write(buffer, 0, buffer.Length);

                // first get the max filename size

                int maxFilenameSize = 0;
                foreach (string file in files)
                {
                    if (file.Length > maxFilenameSize)
                        maxFilenameSize = file.Length;
                }
                maxFilenameSize = maxFilenameSize - serialPort.currentWorkingDirectory.Length;

                int fileCount = 0;
                //foreach (string file in files)
                //{
                //    string filename = file + "\r\n";
                //    filename = filename.Replace(@"\", "/");
                //    serialPort.currentWorkingDirectory.Replace(@"\", "/");
                //    filename = filename.Replace(serialPort.currentWorkingDirectory + "/", "");

                //    byte[] bArray = Encoding.ASCII.GetBytes(filename);
                //    serialPort.streamDir.Write(bArray, 0, bArray.Length);
                //}
                //serialPort.streamDir.Close();
                foreach (string file in files)
                {
                    FileInfo fi = new FileInfo(file);
                    DateTime fCreation = fi.CreationTime;

                    string fileInfoLine = file;
                    fileInfoLine = fileInfoLine.Replace(serialPort.currentWorkingDirectory + @"\", ""); // get rid of path info
                    fileInfoLine = fileInfoLine.PadRight(maxFilenameSize, ' ');                         // pad to proper length
                    fileInfoLine = fileInfoLine + "    " +
                                                  fCreation.Month.ToString("00") + "/" + fCreation.Day.ToString("00") + "/" + fCreation.Year.ToString("0000") +
                                                  " " +
                                                  fCreation.Hour.ToString("00") + ":" + fCreation.Minute.ToString("00") + ":" + fCreation.Second.ToString("00") +
                                                  "\r\n";
                    if (fileInfoLine.Length > 0)
                    {
                        fileCount += 1;
                        byte[] bArray = Encoding.ASCII.GetBytes(fileInfoLine);
                        serialPort.streamDir.Write(bArray, 0, bArray.Length);
                    }
                }

                byte[] fileCountBuffer = Encoding.ASCII.GetBytes("    " + fileCount.ToString() + " files\r\n");
                serialPort.streamDir.Write(fileCountBuffer, 0, fileCountBuffer.Length);

                byte[] freeSpaceBuffer = Encoding.ASCII.GetBytes("        " + availableFreeSpace.ToString() + " bytes free\r\n\r\n");
                serialPort.streamDir.Write(freeSpaceBuffer, 0, freeSpaceBuffer.Length);

                serialPort.streamDir.Close();

                // --------------------------------------

                serialPort.streamDir = File.Open(serialPort.dirFilename, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (serialPort.streamDir != null)
                {
                    serialPort.WriteByte((byte)'\r', false);
                    serialPort.WriteByte((byte)'\n', false);
                    serialPort.SetState((int)CONNECTION_STATE.SENDING_DIR);
                }
                else
                {
                    serialPort.WriteByte(0x06);
                    File.Delete(serialPort.dirFilename);
                    serialPort.SetState((int)CONNECTION_STATE.CONNECTED);
                }
            }
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
                catch
                {
                    status = 0x15;
                }

                serialPort.WriteByte(status);
                serialPort.SetState((int)CONNECTION_STATE.CONNECTED);
            }
        }

        static void StateConnectionStateDriveGetFilename(Ports serialPort, int c)
        {
            if (c != 0x0d)
                serialPort.commandFilename += (char)c;
            else
            {
                //int nNumDrives;
                //int nCurDrive;

                byte status = 0x00;

                //_dos_setdrive (serialPort.commandFilename[0] - 0x40, &nNumDrives);
                //_dos_getdrive (&nCurDrive);

                //if (nCurDrive = (serialPort.commandFilename[0] - 0x40))
                //{
                try
                {
                    Directory.SetCurrentDirectory(serialPort.commandFilename);
                    status = 0x06;

                    serialPort.currentWorkingDirectory = Directory.GetCurrentDirectory();
                    serialPort.currentWorkingDirectory.TrimEnd('\\');
                }
                catch
                {
                    status = 0x15;
                }
                //}
                //else
                //    status = 0x15;

                //if ((!lastActivityWasServer && !lastActivityWasClient) || lastActivityWasClient)
                //{
                //    Console.WriteLine();
                //    Console.Write("SERVER: ");
                //}
                //lastActivityWasServer = true;
                //lastActivityWasClient = false;

                serialPort.SetAttribute((int)CONSOLE_ATTRIBUTE.REVERSE_ATTRIBUTE);
                Console.Write(status.ToString("X2", ci) + " ");
                serialPort.SetAttribute((int)CONSOLE_ATTRIBUTE.NORMAL_ATTRIBUTE);

                serialPort.SetState((int)CONNECTION_STATE.CONNECTED);
            }
        }

        static void StateConnectionStateSendingDir(Ports serialPort, int c)
        {
            if (c == ' ')
            {
                string line = "";
                int buffer = 0x00;

                while ((buffer = serialPort.streamDir.ReadByte()) != -1)
                {
                    if (buffer != (int)'\n')
                        line += (char)buffer;
                    else
                        break;
                }
                serialPort.WriteByte((byte)'\r', false);
                serialPort.sp.Write(line);
                serialPort.WriteByte((byte)'\n', false);

                if (buffer == -1)
                {
                    serialPort.streamDir.Close();
                    serialPort.streamDir = null;
                    File.Delete(serialPort.dirFilename);

                    serialPort.WriteByte(0x06);
                    serialPort.SetState((int)CONNECTION_STATE.CONNECTED);
                }
            }
            else if (c == 0x1b)
            {
                serialPort.WriteByte((byte)'\r', false);
                serialPort.WriteByte((byte)'\n', false);

                serialPort.streamDir.Close();
                serialPort.streamDir = null;
                File.Delete(serialPort.dirFilename);

                serialPort.WriteByte(0x06);
                serialPort.SetState((int)CONNECTION_STATE.CONNECTED);
            }
        }

        static void StateConnectionStateCreateGetParameters(Ports serialPort, int c)
        {
            if (c != 0x0d)
            {
                switch (serialPort.createState)
                {
                    case (int)CREATE_STATE.GET_CREATE_PATH:
                        serialPort.createPath += (char)c;
                        break;
                    case (int)CREATE_STATE.GET_CREATE_NAME:
                        serialPort.createFilename += (char)c;
                        break;
                    case (int)CREATE_STATE.GET_CREATE_VOLUME:
                        serialPort.createVolumeNumber += (char)c;
                        break;
                    case (int)CREATE_STATE.GET_CREATE_TRACK_COUNT:
                        serialPort.createTrackCount += (char)c;
                        break;
                    case (int)CREATE_STATE.GET_CREATE_SECTOR_COUNT:
                        serialPort.createSectorCount += (char)c;
                        break;
                    default:
                        serialPort.SetState((int)CONNECTION_STATE.CONNECTED);
                        break;
                }
            }
            else
            {
                if (serialPort.createState != (int)CREATE_STATE.GET_CREATE_SECTOR_COUNT)
                {
                    serialPort.createState++;
                    serialPort.SetState((int)CONNECTION_STATE.CREATE_GETPARAMETERS);
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
                    serialPort.SetState((int)CONNECTION_STATE.CONNECTED);
                }
            }
        }

        static void StateConnectionStateWaitACK(Ports serialPort, int c)
        {
            //*StatusLine << '\n' << "State is WAIT_ACK";

            if (c == 0x06)
                serialPort.SetState((int)CONNECTION_STATE.CONNECTED);
            //else if (c == 's')
            //{
            //    // 's'end Sector Request with drive

            //    serialPort.SetState((int)CONNECTION_STATE.GET_READ_DRIVE);
            //}
            else
                serialPort.SetState((int)CONNECTION_STATE.CONNECTED);
        }

        static void InitializeFromConfigFile()
        {

            ParseConfigFile();

            foreach (Ports serialPort in listPorts)
            {
                int nIndex = 0;

                if (serialPort.sp != null)
                {
                    // if we come in here with a non-null value for serialPort.sp that means that we have a COM port open for this logical serial port
                    // we must close it before we can open it again or open another.

                    serialPort.sp.Close();
                }

                if (serialPort.sp == null)
                    serialPort.sp = new SerialPort("COM" + serialPort.port.ToString(), serialPort.rate, Parity.None, 8, StopBits.One);

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

                serialPort.SetState((int)CONNECTION_STATE.NOT_CONNECTED);

                foreach (ImageFile imageFile in serialPort.imageFile)
                {
                    if (imageFile != null)
                    {
                        serialPort.MountImageFile(imageFile.Name + ".DSK", nIndex);
                    }
                    nIndex++;
                }

                Console.WriteLine(string.Format("COM{0} parameters:", serialPort.port));
                Console.WriteLine(string.Format("    Rate:              {0}", serialPort.rate));
                Console.WriteLine(string.Format("    CpuSpeed:          {0}", serialPort.speed));
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
                if (Console.KeyAvailable)
                {
                    HandleCommand();
                }

                foreach (Ports serialPort in listPorts)
                {
                    try
                    {
                        if (serialPort.sp.BytesToRead > 0)
                        {
                            int c = serialPort.sp.ReadByte();

                            if (((serialPort.state != (int)CONNECTION_STATE.RECEIVING_SECTOR) && (serialPort.state != (int)CONNECTION_STATE.GET_CRC)))
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

                            switch (serialPort.state)
                            {
                                case (int)CONNECTION_STATE.NOT_CONNECTED:               StateConnectionStateNotConnected            (serialPort, c); break;
                                case (int)CONNECTION_STATE.SYNCRONIZING:                StateConnectionStateSynchronizing           (serialPort, c); break;
                                case (int)CONNECTION_STATE.CONNECTED:                   StateConnectionStateConnected               (serialPort, c); break;
                                case (int)CONNECTION_STATE.GET_REQUESTED_MOUNT_DRIVE:   StateConnectionStateGetRequestedMountDrive  (serialPort, c); break;
                                case (int)CONNECTION_STATE.GET_READ_DRIVE:              StateConnectionStateGetReadDrive            (serialPort, c); break;
                                case (int)CONNECTION_STATE.GET_WRITE_DRIVE:             StateConnectionStateGetWriteDrive           (serialPort, c); break;
                                case (int)CONNECTION_STATE.GET_MOUNT_DRIVE:             StateConnectionStateGetMountDrive           (serialPort, c); break;
                                case (int)CONNECTION_STATE.GET_CREATE_DRIVE:            StateConnectionStateGetCreateDrive          (serialPort, c); break;
                                case (int)CONNECTION_STATE.GET_TRACK:                   StateConnectionStateGetTrack                (serialPort, c); break;
                                case (int)CONNECTION_STATE.GET_SECTOR:                  StateConnectionStateGetSector               (serialPort, c); break;
                                case (int)CONNECTION_STATE.RECEIVING_SECTOR:            StateConnectionStateRecievingSector         (serialPort, c); break;
                                case (int)CONNECTION_STATE.GET_CRC:                     StateConnectionStateGetCRC                  (serialPort, c); break;
                                case (int)CONNECTION_STATE.MOUNT_GETFILENAME:           StateConnectionStateMountGetFilename        (serialPort, c); break;
                                case (int)CONNECTION_STATE.DELETE_GETFILENAME:          StateConnectionStateDeleteGetFilename       (serialPort, c); break;
                                case (int)CONNECTION_STATE.DIR_GETFILENAME:             StateConnectionStateDirGetFilename          (serialPort, c); break;
                                case (int)CONNECTION_STATE.CD_GETFILENAME:              StateConnectionStateCDGetFilename           (serialPort, c); break;
                                case (int)CONNECTION_STATE.DRIVE_GETFILENAME:           StateConnectionStateDriveGetFilename        (serialPort, c); break;
                                case (int)CONNECTION_STATE.SENDING_DIR:                 StateConnectionStateSendingDir              (serialPort, c); break;
                                case (int)CONNECTION_STATE.CREATE_GETPARAMETERS:        StateConnectionStateCreateGetParameters     (serialPort, c); break;
                                case (int)CONNECTION_STATE.WAIT_ACK:                    StateConnectionStateWaitACK                 (serialPort, c); break;
                                default:
                                    serialPort.SetState((int)CONNECTION_STATE.NOT_CONNECTED);
                                    //sprintf (szHexTemp, "%02X", c);
                                    //*StatusLine << '\n' << "State is reset to NOT_CONNECTED - Unknown STATE " << szHexTemp;
                                    break;
                            }
                        }
                    }
                    catch { }
                }
            }
        }

        static void HandleCommand()
        {
            ConsoleKeyInfo ki = Console.ReadKey();
            if ((ki.Modifiers & ConsoleModifiers.Alt) == ConsoleModifiers.Alt)
            {
                switch (ki.Key)
                {
                    case ConsoleKey.F1:                 // re-initialize the connection parameters from config file
                        InitializeFromConfigFile();
                        return;

                    case ConsoleKey.F2:
                        return;

                    case ConsoleKey.F3:
                        return;

                    case ConsoleKey.F4:
                        return;

                    case ConsoleKey.F5:
                        return;

                    case ConsoleKey.F6:
                        //status = (RS232Error) clsConnection[FocusWindow].rsPort->Rts( !clsConnection[FocusWindow].rsPort->Rts() );
                        //*StatusLine << "Toggle RTS returns: ";
                        //if ( status >= 0 ) 
                        //{
                        //    *StatusLine << itoa( status, buffer, 10 );
                        //    return;
                        //}
                        break;

                    case ConsoleKey.F7:
                        //status = (RS232Error) clsConnection[FocusWindow].rsPort->Dtr( !clsConnection[FocusWindow].rsPort->Dtr() );
                        //*StatusLine << "Toggle Dtr returns: ";
                        //if ( status >= 0 ) 
                        //{
                        //    *StatusLine << itoa( status, buffer, 10 );
                        //    return;
                        //}
                        break;

                    case ConsoleKey.F8:
                        //status = (RS232Error) clsConnection[FocusWindow].rsPort->FlushRXBuffer();
                        //*StatusLine << "Flush RX Buffer returns: ";
                        break;

                    case ConsoleKey.F9:
                        //status = (RS232Error) clsConnection[FocusWindow].rsPort->FlushTXBuffer();
                        //*StatusLine << "Flush TX Buffer returns: ";
                        break;

                    case ConsoleKey.F10:
                        done = true;
                        return;

                    default:
                        return;
                }
            }
            else if ((ki.Modifiers & ConsoleModifiers.Control) == ConsoleModifiers.Control)
            {
                done = false;
            }
            else if ((ki.Modifiers & ConsoleModifiers.Shift) == ConsoleModifiers.Shift)
            {
                done = false;
            }
            else
            {
                switch (ki.Key)
                {
                    case ConsoleKey.F1:
                        //Helping = !Helping;
                        //UserWindow->Clear();
                        //if (Helping)
                        //    DrawHelp();
                        return;

                    case ConsoleKey.F2:
                        //do
                        //    FocusWindow = ++FocusWindow % WINDOW_COUNT;
                        //while (clsConnection[FocusWindow].twWindows == 0);
                        //if (!Helping)
                        //    UserWindow->Clear();
                        //clsConnection[FocusWindow].twWindows->Goto();
                        return;

                    case ConsoleKey.F3:
                        //clsConnection[FocusWindow].nReading = !clsConnection[FocusWindow].nReading;
                        //*StatusLine << "Window "
                        //            << itoa(FocusWindow, buffer, 10);
                        //*StatusLine << " nReading flag is "
                        //            << itoa(clsConnection[FocusWindow].nReading, buffer, 10);
                        return;

                    case ConsoleKey.F4:
                        //ReadLine("New baud rate:", buffer, 10);
                        //if (buffer[0] != 0x00)
                        //{
                        //    status = clsConnection[FocusWindow].rsPort->Set(atol(buffer));
                        //    *StatusLine << "Set baud rate to "
                        //                << ltoa(atol(buffer), buffer, 10)
                        //                << " returns status of: ";
                        //    if (status == RS232_SUCCESS)
                        //        *clsConnection[FocusWindow].twWindows << "baud rate changed to : " << buffer << "\n";
                        //}
                        //else
                        //{
                        //    *clsConnection[FocusWindow].twWindows << "baud rate unchanged \n";
                        //    return;
                        //}
                        break;

                    case ConsoleKey.F5:
                        //ReadLine("New parity:", buffer, 10);
                        //if (buffer[0] != 0x00)
                        //{
                        //    status = clsConnection[FocusWindow].rsPort->Set(UNCHANGED, buffer[0]);
                        //    *StatusLine << "Set parity to "
                        //                << buffer[0]
                        //                << " returns status of: ";
                        //}
                        //else
                        //{
                        //    *clsConnection[FocusWindow].twWindows << "parity unchanged \n";
                        //    return;
                        //}
                        break;

                    case ConsoleKey.F6:
                        //ReadLine("New word length:", buffer, 10);
                        //if (buffer[0] != 0x00)
                        //{
                        //    status = clsConnection[FocusWindow].rsPort->Set(UNCHANGED, UNCHANGED, atoi(buffer));
                        //    *StatusLine << "Set word length to "
                        //                << itoa(atoi(buffer), buffer, 10)
                        //                << " returns status of: ";
                        //}
                        //else
                        //{
                        //    *clsConnection[FocusWindow].twWindows << "word length unchanged \n";
                        //    return;
                        //}
                        break;

                    case ConsoleKey.F7:
                        //ReadLine("New stop bits:", buffer, 10);
                        //if (buffer[0] != 0x00)
                        //{
                        //    status = clsConnection[FocusWindow].rsPort->Set(UNCHANGED,
                        //                        UNCHANGED,
                        //                        UNCHANGED,
                        //                        atoi(buffer));
                        //    *StatusLine << "Set stop bits to "
                        //                << itoa(atoi(buffer), buffer, 10)
                        //                << " returns status of: ";
                        //}
                        //else
                        //{
                        //    *clsConnection[FocusWindow].twWindows << "stop bits unchanged \n";
                        //    return;
                        //}
                        break;

                    case ConsoleKey.F8:
                        //clsConnection[nPort].g_displaySectorData = !clsConnection[nPort].g_displaySectorData;
                        //*StatusLine << "Sector Display is : " << (clsConnection[nPort].g_displaySectorData ? "ON" : "OFF") << " Status: ";
                        verboseOutput = !verboseOutput;
                        Console.WriteLine(string.Format("verbose is {0}", verboseOutput ? "on" : "off"));
                        return;

                    case ConsoleKey.F9:
                        //clsConnection[nPort].nDisplayOutputBytes = !clsConnection[nPort].nDisplayOutputBytes;
                        //*StatusLine << "Output Display is : " << (clsConnection[nPort].nDisplayOutputBytes ? "ON" : "OFF") << " Status: ";
                        return;

                    case ConsoleKey.F10:
                        foreach (Ports serialPort in listPorts)
                        {
                            serialPort.SetState((int)CONNECTION_STATE.NOT_CONNECTED);
                            Console.WriteLine("Serial Port " + serialPort.port.ToString() + " is reset to NOT CONNECTED");
                        }
                        return;

                }
            }
            //    *StatusLine << clsConnection[FocusWindow].rsPort->ErrorName( status );
        }

        static void Main(string[] args)
        {
            InitializeFromConfigFile();
            ProcessRequests();
        }
    }
}
