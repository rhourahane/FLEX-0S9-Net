﻿using FLEX_0S9_Net;
using System;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;

namespace FLEXNetSharp
{
    internal class Ports
    {
        private const int MaxSectorSize = 1024;
        private const int MaxImageFiles = 4;

        private readonly ImageFile[] imageFile = new ImageFile[MaxImageFiles];
        private readonly byte[] sectorBuffer = new byte[MaxSectorSize];        // allow for up to 1024 byte sectors

        private StreamReader streamDir = null;
        private SerialPort sp;
        private string port;
        private ConnectionState state = ConnectionState.NotConnected;
        private CreateState createState;
        private int rate;
        private bool verbose;
        private bool autoMount;
        private int currentDrive = 0;

        private int sectorIndex = 0;
        private int calculatedCRC = 0;

        private int checksumIndex = 0;
        private int checksum = 0;

        private string currentWorkingDirectory;
        private StringBuilder commandFilename;
        private string createFilename;
        private string createPath;
        private string createVolumeNumber;
        private string createTrackCount;
        private string createSectorCount;
        private bool autoShutdown;
        private long m_lPartitionBias = -1;
        private int m_nSectorBias;

        public Ports(PortParameters parameters)
        {
            if (string.IsNullOrEmpty(parameters.Device))
            {
                port = $"COM{parameters.Num}";
            }
            else
            {
                port = parameters.Device;
            }

            rate = parameters.Rate;
            autoShutdown = parameters.AutoShutdown;
            verbose = parameters.Verbose;
            autoMount = parameters.AutoMount;
            currentWorkingDirectory = parameters.DefaultDirectory;

            for (int index = 0; index != MaxImageFiles; ++index)
            {
                imageFile[index] = new ImageFile();
                if (parameters.ImageFiles.Length > index)
                {
                    if (!string.IsNullOrEmpty(parameters.ImageFiles[index]))
                    {
                        imageFile[index].Name = parameters.ImageFiles[index];
                    }
                }
            }
        }

        public void DisplayConfiguration()
        {
            Console.WriteLine("Port:{0}", port);
            Console.WriteLine("Parameters:");
            Console.WriteLine("    Rate:              {0}", rate);
            Console.WriteLine("    Verbose:           {0}", verbose);
            Console.WriteLine("    AutoMount:         {0}", autoMount);
            Console.WriteLine("    DefaultDirectory   {0}", currentWorkingDirectory);
            Console.WriteLine("    ImageFiles");
            int count = 0;
            foreach (var file in imageFile)
            {
                Console.WriteLine("        {0} - {1}", count++, file.Name);
            }
        }

        public void Open()
        {
            sp = new SerialPort(port, rate, Parity.None, 8, StopBits.One);
            sp.ReadBufferSize = MaxSectorSize;
            sp.WriteBufferSize = MaxSectorSize;
            sp.DtrEnable = true;
            sp.RtsEnable = true;

            try
            {
                sp.Open();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                try
                {
                    sp.Close();
                    sp.Open();
                }
                catch (Exception e1)
                {
                    Console.WriteLine(e1.Message);
                }
            }

            State = ConnectionState.NotConnected;

            int index = 0;
            foreach (var image in imageFile)
            {
                if (!string.IsNullOrEmpty(image.Name))
                {
                    MountImageFile(image.Name, index);
                }
                ++index;
            }
        }

        private int SectorBias
        {
            get { return m_nSectorBias; }
            set { m_nSectorBias = value; }
        }

        private int ConvertMSBToInt16(byte[] value)
        {
            return (value[0] * 256) + value[1];
        }

        private int ConvertMSBToInt24(byte[] value)
        {
            return (value[0] * 256 * 256) + (value[1] * 256) + value[2];
        }

        /// <summary>
        /// Determine the file format OS9 - FLEX or UniFLEX
        /// </summary>
        /// <param name="fs"></param>
        /// <returns></returns>
        private FileFormat GetFileFormat(Stream fs)
        {
            FileFormat ff = FileFormat.fileformat_UNKNOWN;

            if (fs != null)
            {
                long currentPosition = fs.Position;
                long fileLength = fs.Length;

                // First Check for OS9 format
                OS9_ID_SECTOR stIDSector = new OS9_ID_SECTOR(fs);

                // There's no point in going any further if the file size if not right
                int nDiskSize = stIDSector.DiskSize;

                if (nDiskSize == (fileLength & 0xFFFFFF00))
                {
                    ff = FileFormat.fileformat_OS9;
                    SectorBias = 0;
                    m_lPartitionBias = 0;
                }
                else
                {
                    RAW_SIR stSystemInformationRecord = new RAW_SIR(fs, m_lPartitionBias, m_nSectorBias);

                    nDiskSize = stSystemInformationRecord.DiskSize;

                    if (nDiskSize == (fileLength & 0xFFFFFF00))
                    {
                        ff = FileFormat.fileformat_FLEX;
                        SectorBias = 1;
                        m_lPartitionBias = 0;
                    }
                    else
                    {
                        UniFLEX_SIR drive_SIR = new UniFLEX_SIR(fs);

                        int nVolumeSize = ConvertMSBToInt24(drive_SIR.m_ssizfr);
                        int nSwapSize = ConvertMSBToInt16(drive_SIR.m_sswpsz);

                        nDiskSize = (nVolumeSize + nSwapSize + 1) * 512;
                        if (nDiskSize == (fileLength & 0xFFFFFF00))
                        {
                            ff = FileFormat.fileformat_UniFLEX;
                        }
                        else
                        {
                            // see if this an IDE drive with multiple partitions
                            if ((fileLength % 256) > 0)
                            {
                                // could be - get the drive info and see if it makes sence
                                byte[] cInfoSize = new byte[2];

                                fs.Seek(-2, SeekOrigin.End);
                                fs.Read(cInfoSize, 0, 2);

                                int nInfoSize = ConvertMSBToInt16(cInfoSize);
                                if (nInfoSize == (fileLength % 256))
                                {
                                    ff = FileFormat.fileformat_FLEX_IDE;
                                    SectorBias = 0;
                                    m_lPartitionBias = 0;
                                }
                            }
                        }
                    }
                    fs.Seek(currentPosition, SeekOrigin.Begin);
                }
            }

            return ff;
        }

        private readonly byte[] serialBuffer = new byte[8];
        private void WriteByte(byte byteToWrite, bool displayOnScreen = true)
        {
            serialBuffer[0] = byteToWrite;
            sp.Write(serialBuffer, 0, 1);

            if (displayOnScreen)
            {
                SetAttribute((int)ConsoleAttribute.Reverse);
                Console.Write("{0:X2} ", byteToWrite);
                SetAttribute((int)ConsoleAttribute.Normal);
            }
        }

        private void SetAttribute(int attr)
        {
            if (attr == (int)ConsoleAttribute.Reverse)
            {
                Console.BackgroundColor = ConsoleColor.White;
                Console.ForegroundColor = ConsoleColor.Black;
            }
            else if (attr == (int)ConsoleAttribute.Normal)
            {
                Console.BackgroundColor = ConsoleColor.Black;
                Console.ForegroundColor = ConsoleColor.White;
            }
        }

        private ConnectionState State
        {
            get => state;

            set
            {
                state = value;

                DisplayState(state);
            }
        }

        private void DisplayState(ConnectionState displayState)
        {
            switch (displayState)
            {
                case ConnectionState.NotConnected:
                case ConnectionState.Syncronizing:
                case ConnectionState.Connected:
                case ConnectionState.GetRequestedMountDrive:
                case ConnectionState.GetReadDrive:
                case ConnectionState.GetWriteDrive:
                case ConnectionState.GetMountDrive:
                case ConnectionState.GetCreateDrive:
                case ConnectionState.GetTrack:
                case ConnectionState.GetSector:
                case ConnectionState.ReceivingSector:
                case ConnectionState.GetCrc:
                case ConnectionState.MountGetfilename:
                case ConnectionState.WaitAck:
                case ConnectionState.DeleteGetfilename:
                case ConnectionState.DirGetfilename:
                case ConnectionState.CdGetfilename:
                case ConnectionState.DriveGetfilename:
                case ConnectionState.SendingDir:
                    Console.WriteLine($"State is {state}");
                    break;

                case ConnectionState.CreateGetParameters:
                    Console.WriteLine($"State is {displayState} {createState}");
                    break;

                default:
                    Console.WriteLine($"State is UNHANDLED {displayState} - {(int)state:X2}");
                    break;
            }
        }


        private void StateConnectionStateNotConnected(int c)
        {
            if (c == 0x55)
            {
                State = ConnectionState.Syncronizing;
                WriteByte(0x55);
            }
        }

        // send ack to sync

        private void StateConnectionStateSynchronizing(int c)
        {
            if (c != 0x55)
            {
                if (c == 0xAA)
                {
                    WriteByte(0xAA);
                    State = ConnectionState.Connected;
                }
                else
                {
                    State = ConnectionState.NotConnected;
                }
            }
        }

        private void StateConnectionStateDirGetFilename(int c)
        {
            if (c != 0x0d)
                commandFilename.Append((char)c);
            else
            {
                if (commandFilename.Length == 0)
                {
                    commandFilename = new StringBuilder("*.DSK");
                }
                else
                {
                    commandFilename.Append(".DSK");
                }

                if (streamDir != null)
                {
                    streamDir.Dispose();
                    streamDir = null;
                }

                // get the list of files in the current working directory
                var memoryStream = new MemoryStream();
                using (var stringStream = new StreamWriter(memoryStream, Encoding.ASCII, -1, true))
                {
                    System.IO.DriveInfo driveInfo = new System.IO.DriveInfo(Directory.GetDirectoryRoot(currentWorkingDirectory));
                    long availableFreeSpace = driveInfo.AvailableFreeSpace;
                    string driveName = driveInfo.Name;
                    string volumeLabel = driveInfo.VolumeLabel;

                    stringStream.Write("\r\nVolume in Drive {0} is {1}\r\n", driveName, volumeLabel);
                    stringStream.Write("{0}\r\n\r\n", currentWorkingDirectory);

                    string[] files = Directory.GetFiles(currentWorkingDirectory, commandFilename.ToString(), SearchOption.TopDirectoryOnly);

                    // first get the max filename size
                    int maxFilenameSize = files.Max(f => f.Length);
                    maxFilenameSize = maxFilenameSize - currentWorkingDirectory.Length;

                    int fileCount = 0;
                    foreach (string file in files)
                    {
                        FileInfo fi = new FileInfo(file);
                        DateTime fCreation = fi.CreationTime;

                        string fileInfoLine = string.Format("{0}    {1:MM/dd/yyyy HH:mm:ss}\r\n", Path.GetFileName(file).PadRight(maxFilenameSize), fCreation);
                        if (fileInfoLine.Length > 0)
                        {
                            fileCount += 1;
                            stringStream.Write(fileInfoLine);
                        }
                    }

                    stringStream.Write("\r\n");
                    stringStream.Write("    {0} files\r\n", fileCount);
                    stringStream.Write("    {0} bytes free\r\n", availableFreeSpace);
                }

                memoryStream.Seek(0, SeekOrigin.Begin);
                streamDir = new StreamReader(memoryStream, Encoding.ASCII);
                if (streamDir != null)
                {
                    WriteByte((byte)'\r');
                    WriteByte((byte)'\n');
                    State = ConnectionState.SendingDir;
                }
                else
                {
                    WriteByte(0x06);
                    State = ConnectionState.Connected;
                }
            }
        }

        private void StateConnectionStateSendingDir(int c)
        {
            if (c == ' ')
            {
                string line = streamDir.ReadLine();
                if (line != null)
                {
                    WriteByte((byte)'\r', false);
                    sp.Write(line);
                    WriteByte((byte)'\n', false);
                }
                else
                {
                    streamDir.Dispose();
                    streamDir = null;

                    WriteByte(0x06);
                    State = ConnectionState.Connected;
                }
            }
            else if (c == 0x1b)
            {
                WriteByte((byte)'\r', false);
                WriteByte((byte)'\n', false);

                streamDir.Dispose();
                streamDir = null;

                WriteByte(0x06);
                State = ConnectionState.Connected;
            }
        }

        private void StateConnectionStateGetRequestedMountDrive(int c)
        {
            // Report which disk image is mounted to requested drive
            currentDrive = c;

            SetAttribute((int)ConsoleAttribute.Reverse);
            Console.WriteLine(currentWorkingDirectory);
            SetAttribute((int)ConsoleAttribute.Normal);

            if (imageFile[currentDrive].driveInfo.MountedFilename != null)
            {
                sp.Write(imageFile[currentDrive].driveInfo.MountedFilename);
            }

            WriteByte(0x0D, false);
            WriteByte(0x06);

            State = ConnectionState.Connected;
        }

        private void StateConnectionStateGetReadDrive(int c)
        {
            currentDrive = c;

            if (imageFile[currentDrive] == null)
                imageFile[currentDrive] = new ImageFile();

            imageFile[currentDrive].driveInfo.Mode = SectorAccessMode.S_MODE;
            State = ConnectionState.GetTrack;
        }
        private void StateConnectionStateGetWriteDrive(int c)
        {
            currentDrive = c;

            imageFile[currentDrive].driveInfo.Mode = SectorAccessMode.R_MODE;
            State = ConnectionState.GetTrack;
        }

        private void StateConnectionStateGetMountDrive(int c)
        {
            currentDrive = c;
            State = ConnectionState.MountGetfilename;
        }

        private void StateConnectionStateGetTrack(int c)
        {
            imageFile[currentDrive].track = c;
            State = ConnectionState.GetSector;
        }

        private void StateConnectionStateGetSector(int c)
        {
            imageFile[currentDrive].sector = c;

            if (imageFile[currentDrive].driveInfo.Mode == SectorAccessMode.S_MODE)
            {
                Console.WriteLine("\r\nState is SENDING_SECTOR");
                SendSector();
                State = ConnectionState.WaitAck;
            }
            else
            {
                sectorIndex = 0;
                calculatedCRC = 0;
                State = ConnectionState.ReceivingSector;
            }
        }

        private void StateConnectionStateGetCreateDrive(int c)
        {
            currentDrive = c;
            State = ConnectionState.CreateGetParameters;
        }

        private void StateConnectionStateRecievingSector(int c)
        {
            sectorBuffer[sectorIndex++] = (byte)c;
            calculatedCRC += c;

            if (sectorIndex >= 256)
            {
                checksumIndex = 0;
                State = ConnectionState.GetCrc;
            }
        }

        private void StateConnectionStateCDGetFilename(int c)
        {
            if (c != 0x0d)
                commandFilename.Append((char)c);
            else
            {
                byte status;
                try
                {
                    Directory.SetCurrentDirectory(commandFilename.ToString());
                    status = 0x06;

                    currentWorkingDirectory = Directory.GetCurrentDirectory();
                    currentWorkingDirectory.TrimEnd('\\');
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    status = 0x15;
                }

                WriteByte(status);
                State = ConnectionState.Connected;
            }
        }

        private void StateConnectionStateGetCRC(int c)
        {
            if (checksumIndex++ == 0)
                checksum = c * 256;
            else
            {
                checksum += c;

                byte status = WriteSector();
                WriteByte(status);
                State = ConnectionState.Connected;
            }
        }

        private void StateConnectionStateWaitACK(int c)
        {
            if (c == 0x06)
            {
                State = ConnectionState.Connected;
            }
            else
            {
                State = ConnectionState.Connected;
            }
        }

        private void StateConnectionStateMountGetFilename(int c)
        {
            if (c != 0x0d)
            {
                // just add the character to the filename
                commandFilename.Append((char)c);
            }
            else
            {
                commandFilename.Append(".DSK");

                // this should close any file that is currently open for this port/drive
                if (imageFile[currentDrive] != null)
                {
                    if (imageFile[currentDrive].stream != null)
                    {
                        imageFile[currentDrive].stream.Close();
                        imageFile[currentDrive].stream = null;
                    }
                }

                // Now mount the new file

                byte status = 0x06;
                if (commandFilename.Length > 0)
                {
                    Console.WriteLine();
                    status = MountImageFile(commandFilename.ToString(), currentDrive);
                }

                WriteByte(status);

                byte cMode = (byte)'W';
                if (imageFile[currentDrive] != null)
                {
                    if (imageFile[currentDrive].readOnly)
                    {
                        cMode = (byte)'R';
                    }
                }
                WriteByte(cMode);
                State = ConnectionState.Connected;
            }
        }

        private void StateConnectionStateDriveGetFilename(int c)
        {
            if (c != 0x0d)
            {
                commandFilename.Append((char)c);
            }
            else
            {
                byte status;

                try
                {
                    Directory.SetCurrentDirectory(commandFilename.ToString());
                    status = 0x06;

                    currentWorkingDirectory = Directory.GetCurrentDirectory();
                    currentWorkingDirectory.TrimEnd('\\');
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    status = 0x15;
                }

                SetAttribute((int)ConsoleAttribute.Reverse);
                Console.Write("{0:X2} ", status);
                SetAttribute((int)ConsoleAttribute.Normal);

                State = ConnectionState.Connected;
            }
        }

        private void StateConnectionStateCreateGetParameters(int c)
        {
            if (c != 0x0d)
            {
                switch (createState)
                {
                    case CreateState.GetCreatePath:
                        createPath += (char)c;
                        break;
                    case CreateState.GetCreateName:
                        createFilename += (char)c;
                        break;
                    case CreateState.GetCreateVolume:
                        createVolumeNumber += (char)c;
                        break;
                    case CreateState.GetCreateTrackCount:
                        createTrackCount += (char)c;
                        break;
                    case CreateState.GetCreateSectorCount:
                        createSectorCount += (char)c;
                        break;
                    default:
                        State = ConnectionState.Connected;
                        break;
                }
            }
            else
            {
                if (createState != CreateState.GetCreateSectorCount)
                {
                    createState++;
                    State = ConnectionState.CreateGetParameters;
                }
                else
                {
                    string fullFilename = createPath + "/" + createFilename + ".DSK";

                    byte status;
                    Console.Write("\nCreating Image File {0}", fullFilename);
                    status = CreateImageFile();
                    WriteByte(status);

                    // Cannot automount because we do not know what drive to mount image too.
                    //
                    //if (autoMount.ToUpper() == "T" || autoMount.ToUpper() == "Y")
                    //{
                    //    if (createPath ==  ".")
                    //    {
                    //        if (imageFile[currentDrive].stream != null)
                    //        {
                    //            imageFile[currentDrive].stream.Close();
                    //            imageFile[currentDrive].stream = null;
                    //        }

                    //        MountImageFile(currentWorkingDirectory + "/" + createFilename + ".DSK", currentDrive);
                    //    }
                    //    else
                    //    {
                    //        try
                    //        {
                    //            Directory.SetCurrentDirectory(createPath);
                    //            if (imageFile[currentDrive].stream != null)
                    //            {
                    //                imageFile[currentDrive].stream.Close();
                    //                imageFile[currentDrive].stream = null;
                    //            }

                    //            MountImageFile(createPath + "/" + createFilename + ".DSK", currentDrive);
                    //        }
                    //        catch
                    //        {
                    //        }
                    //    }
                    //}
                    State = ConnectionState.Connected;
                }
            }
        }
        private void StateConnectionStateDeleteGetFileName(int c)
        {
            //    if (c != 0x0d)
            //        commandFilename[nCommandFilenameIndex++] = c;
            //    else
            //    {
            //        strcpy (szFileToDelete, currentWorkingDirectory );
            //        if ((strlen ((char*)commandFilename) + strlen (szFileToDelete)) < 126)
            //        {
            //            strcat (szFileToDelete, "/");
            //            strcat (szFileToDelete, (char*)commandFilename);

            //            int nStatus = -1;

            //            // do not attempt to delete if the file is mounted

            //            for (int i = 0; i < (int) strlen (szFileToDelete); i++)
            //            {
            //                if (szFileToDelete[i] >= 'A' && szFileToDelete[i] <= 'Z')
            //                    szFileToDelete[i] = szFileToDelete[i] & 0x5F;
            //            }

            //            if (strcmp (szMountedFilename[nCurrentDrive], szFileToDelete) != 0)
            //            {
            //                // see if the file can be opened exclusively in r/w mode, if
            //                // it can - we can delete it, otherwise we fail attempting to
            //                // delete it so do not attempt

            //                FILE *x = fopen (szFileToDelete, "r+b");
            //                if (x != NULL)
            //                {
            //                    // we were able to open it - close it and delete it

            //                    fclose (x);
            //                    nStatus = unlink (szFileToDelete);
            //                }
            //                else
            //                    *twWindows << "\n" << "attempted to delete open image" << "\n";
            //            }
            //            else
            //                *twWindows << "\n" << "attempted to delete mounted image" << "\n";


            //            if (nStatus == 0)
            //            {
            //                *twWindows << "image deleted" << "\n";
            //                rsPort->Write (0x06);
            //            }
            //            else
            //            {
            //                *twWindows << "unable to delete image" << "\n";
            //                rsPort->Write (0x15);
            //            }
            //        }
            //        else
            //        {
            //            *twWindows << "\n" << "attempted to delete with image name > 128 characters" << "\n";
            //            rsPort->Write (0x15);
            //        }

            //        SetState  (CONNECTED);
            //    }
        }

        private byte MountImageFile(string fileName, int nDrive)
        {
            byte c = 0x06;
            try
            {
                Directory.SetCurrentDirectory(currentWorkingDirectory);

                string fileToLoad = fileName;
                if (string.IsNullOrEmpty(Path.GetExtension(fileName)))
                {
                    fileToLoad += ".DSK";
                }

                try
                {
                    imageFile[nDrive].stream = File.Open(fileToLoad, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
                    imageFile[nDrive].fileFormat = GetFileFormat(imageFile[nDrive].stream);
                    imageFile[nDrive].readOnly = false;
                }
                catch (UnauthorizedAccessException)
                {
                    // Unable to open file for read/write try readonly
                    try
                    {
                        imageFile[nDrive].stream = File.Open(fileToLoad, FileMode.Open, FileAccess.Read, FileShare.Read);
                        imageFile[nDrive].fileFormat = GetFileFormat(imageFile[nDrive].stream);
                        imageFile[nDrive].readOnly = true;

                        Console.WriteLine("Mounting {0} readonly", fileToLoad);
                    }
                    catch (Exception eIn)
                    {
                        Console.WriteLine(eIn);
                        c = 0x15;
                    }
                }
                catch (FileNotFoundException fn)
                {
                    Console.WriteLine("Failed to find file {0} file not mounted", fn.FileName);
                    c = 0x15;
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    c = 0x15;
                }

                string Message;
                bool isPathFullyQualified = Path.IsPathFullyQualified(fileToLoad);
                if (c == 0x06)
                {
                    if (isPathFullyQualified)
                    {
                        Message = string.Format("Loaded imagefile {0} from directory {1}", fileToLoad.PadRight(16), Directory.GetParent(fileToLoad));
                    }
                    else
                    {
                        Message = string.Format("Loaded imagefile {0} from directory {1}", fileToLoad.PadRight(16), currentWorkingDirectory);
                    }

                    imageFile[nDrive].driveInfo = new DriveInfo();

                    switch (imageFile[nDrive].fileFormat)
                    {
                        case FileFormat.fileformat_FLEX:
                            imageFile[nDrive].stream.Seek(512 + 39, SeekOrigin.Begin);
                            imageFile[nDrive].driveInfo.NumberOfSectorsPerTrack = imageFile[nDrive].stream.ReadByte();
                            imageFile[nDrive].driveInfo.NumberOfBytesPerTrack = imageFile[nDrive].driveInfo.NumberOfSectorsPerTrack * 256;
                            imageFile[nDrive].driveInfo.LogicalSectorSize = 0; //set sector size = 256 bytes for FLEX.
                            break;

                        case FileFormat.fileformat_OS9:
                            {
                                OS9_ID_SECTOR stIDSector = new OS9_ID_SECTOR(imageFile[nDrive].stream);

                                // There's no point in going any further if the file size if not right

                                int nSectorsPerTrack = stIDSector.cSPT[1] + (stIDSector.cSPT[0] * 256);
                                int nSectorsPerTrackZero = stIDSector.cSPT[1] + (stIDSector.cSPT[0] * 256);
                                int nTotalSectors = stIDSector.cTOT[2] + (stIDSector.cTOT[1] * 256) + (stIDSector.cTOT[0] * 1024);
                                int nClusterSize = stIDSector.cBIT[1] + (stIDSector.cBIT[0] * 256);
                                int nLogicalSectorSize = stIDSector.cLSS[1] + (stIDSector.cLSS[0] * 256);

                                long nDiskSize = nTotalSectors * 256;
                                nDiskSize += (nSectorsPerTrack - nSectorsPerTrackZero) * 256;

                                imageFile[nDrive].driveInfo.NumberOfSectorsPerTrack = nSectorsPerTrack;
                                imageFile[nDrive].driveInfo.TotalNumberOfSectorOnMedia = nTotalSectors;
                                imageFile[nDrive].driveInfo.NumberOfBytesPerTrack = nSectorsPerTrack * (nLogicalSectorSize + 1) * 256;
                                imageFile[nDrive].driveInfo.NumberOfSectorsPerCluster = nClusterSize;
                                imageFile[nDrive].driveInfo.LogicalSectorSize = nLogicalSectorSize;       // 0 = 256 bytes per sector
                            }
                            break;

                        default:
                            break;
                    }

                    if (isPathFullyQualified)
                        imageFile[nDrive].driveInfo.MountedFilename = fileToLoad;
                    else
                        imageFile[nDrive].driveInfo.MountedFilename = Path.Combine(currentWorkingDirectory, fileToLoad);

                    imageFile[nDrive].driveInfo.MountedFilename = imageFile[nDrive].driveInfo.MountedFilename.ToUpper();
                    imageFile[nDrive].Name = fileToLoad;
                }
                else
                {
                    if (isPathFullyQualified)
                    {
                        Message = string.Format("Unable to load {0} from directory {1}", fileToLoad.PadRight(16), Directory.GetParent(fileToLoad));
                    }
                    else
                    {
                        Message = string.Format("Unable to load {0} from directory {1}", fileToLoad.PadRight(16), currentWorkingDirectory);
                    }
                }

                Console.WriteLine(Message);
            }
            catch (Exception e)
            {
                Console.WriteLine("Unable to open directory: {0} due to exception {1} - please change your configuration file", currentWorkingDirectory, e);
            }

            return (c);
        }

        private long GetSectorOffset()
        {
            long lSectorOffset = 0;
            long lOffsetToStartOfTrack;
            long lOffsetFromTrackStartToSector;

            if (imageFile[currentDrive].trackAndSectorAreTrackAndSector)
            {
                // actual track and sector are in the track[currentDrive] and sector[currentDrive] variables

                // This is FLEX asking -
                //
                // we need to check if the diskette is an OS9 or FLEX diskette image
                //  
                //      Since this is FLEX requesting the sector - the data in the track and sector are actual track and sector - not LBN
                //
                //          -   if the diskette image format is FLEX - proceed as we normally would
                //          -   if the diskette image format is OS9  - we need to convert the track and sector to an LBN
                //              using the information in the driveInfo data of the imageFIle class

                switch (imageFile[currentDrive].fileFormat)
                {
                    case FileFormat.fileformat_FLEX:    
                        {
                            lOffsetToStartOfTrack = (imageFile[currentDrive].track * imageFile[currentDrive].driveInfo.NumberOfBytesPerTrack);

                            if (imageFile[currentDrive].sector == 0)
                                lOffsetFromTrackStartToSector = imageFile[currentDrive].sector * 256L;
                            else
                                lOffsetFromTrackStartToSector = (imageFile[currentDrive].sector - 1) * 256L;

                            lSectorOffset = lOffsetToStartOfTrack + lOffsetFromTrackStartToSector;

                            Console.Write("[{0:X8}]", lSectorOffset.ToString("X8"));
                        }
                        break;

                    case FileFormat.fileformat_OS9:     
                        {
                            // we need to convert the track and sector as LBN to Track and Sector to be able to calculate the offset used to access the diskette image sector

                            int lbn = (imageFile[currentDrive].track * 256) + imageFile[currentDrive].sector;

                            try
                            {
                                int _track = lbn / (int)imageFile[currentDrive].driveInfo.NumberOfSectorsPerTrack;
                                int _sector = lbn % (int)imageFile[currentDrive].driveInfo.NumberOfSectorsPerTrack;
                                if (lbn <= imageFile[currentDrive].driveInfo.NumberOfSectorsPerTrack)
                                    _sector = lbn;

                                // now we can access the diskette iamge with T/S calcualted from LBN

                                lOffsetToStartOfTrack = (_track * imageFile[currentDrive].driveInfo.NumberOfBytesPerTrack);

                                if (imageFile[currentDrive].sector == 0)
                                    lOffsetFromTrackStartToSector = _sector * 256L;
                                else
                                    lOffsetFromTrackStartToSector = (_sector - 1) * 256L;

                                lSectorOffset = lOffsetToStartOfTrack + lOffsetFromTrackStartToSector;
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine(e);
                                string message = e.Message;
                            }
                            Console.Write("[{0:X8}]", lSectorOffset);
                        }
                        break;
                }
            }
            else
            {
                // track[currentDrive] and sector[currentDrive] variables contain the LBN to retrieve

                // This is OS9 asking
                //
                // we need to check if the diskette is an OS9 or FLEX diskette image
                //  
                //      Since this is OS9 requesting the sector 
                //          -   if the diskette image format is FLEX - convert the LBN to T/S 
                //              using data in the driveInfo data of the imageFIle class
                //          -   if the diskette image format is OS9  - just use the LBN to calculate the offset
                //              
                switch (imageFile[currentDrive].fileFormat)
                {
                    case FileFormat.fileformat_FLEX:
                        break;

                    case FileFormat.fileformat_OS9:
                        break;
                }

                lSectorOffset = ((imageFile[currentDrive].driveInfo.LogicalSectorSize + 1) * 256) * (imageFile[currentDrive].track * 256 + imageFile[currentDrive].sector);
                Console.Write("[{0:X8}]", lSectorOffset);
            }

            return (lSectorOffset);
        }

        private byte WriteSector()
        {
            byte status = 0x15;

            if (calculatedCRC == checksum)
            {
                long lSectorOffset = GetSectorOffset();

                imageFile[currentDrive].stream.Seek(lSectorOffset, SeekOrigin.Begin);
                try
                {
                    int bytesPerSector = (imageFile[currentDrive].driveInfo.LogicalSectorSize + 1) * 256;
                    imageFile[currentDrive].stream.Write(sectorBuffer, 0, bytesPerSector);
                    status = 0x06;
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            }
            else
            {
                sectorIndex = 0;
                calculatedCRC = 0;
                checksumIndex = 0;
                checksum = 0;
            }

            return (status);
        }

        private void SendSector()
        {
            int checksum = 0;

            long lSectorOffset = GetSectorOffset();

            if (imageFile[currentDrive].stream != null)
            {
                int bytesPerSector = (imageFile[currentDrive].driveInfo.LogicalSectorSize + 1) * 256;

                imageFile[currentDrive].stream.Seek(lSectorOffset, SeekOrigin.Begin);
                imageFile[currentDrive].stream.Read(sectorBuffer, 0, bytesPerSector);

                for (int nIndex = 0; nIndex < bytesPerSector; nIndex++)
                {
                    if (verbose)
                    {
                        // cause new line after every 32 characters
                        if (nIndex % 32 == 0)
                        {
                            Console.WriteLine();
                        }
                    }

                    WriteByte(sectorBuffer[nIndex], verbose);
                    checksum += (char)(sectorBuffer[nIndex] & 0xFF);
                }

                WriteByte((byte)((checksum / 256) & 0xFF));
                WriteByte((byte)((checksum % 256) & 0xFF));
            }
            else
            {
                int bytesPerSector = (imageFile[currentDrive].driveInfo.LogicalSectorSize + 1) * 256;
                for (int nIndex = 0; nIndex < bytesPerSector; nIndex++)
                {
                    WriteByte(0x00);
                    checksum += (char)(sectorBuffer[nIndex] & 0xFF);
                }
                WriteByte((byte)((checksum / 256) & 0xFF));
                WriteByte((byte)((checksum % 256) & 0xFF));
            }
        }

        private byte CreateImageFile()
        {
            byte cStatus = 0x15;

            if (string.IsNullOrWhiteSpace(createPath))
            {
                createPath = currentWorkingDirectory;
            }

            string fullFilename = Path.Combine(createPath, createFilename) + ".DSK";

            int track;
            int sector;

            int volumeNumber = Convert.ToInt32(createVolumeNumber);
            int numberOfTracks = Convert.ToInt32(createTrackCount);
            int numberOfSectors = Convert.ToInt32(createSectorCount);

            if ((numberOfTracks > 0) && (numberOfTracks <= 256))
            {
                if ((numberOfSectors > 0) && (numberOfSectors <= 255))
                {
                    // total number of user sectors = number tracks minus one times the number of sectors
                    // because track 0 is not for users
                    int nTotalSectors = (numberOfTracks - 1) * numberOfSectors;

                    DateTime now = DateTime.Now;

                    if (nTotalSectors > 0)
                    {
                        if (File.Exists(fullFilename))
                        {
                            cStatus = 0x15;     // file already exists
                        }
                        else
                        {
                            // file does not yet exist - create it
                            try
                            {
                                Stream fp = File.Open(fullFilename, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                                for (track = 0; track < numberOfTracks; track++)
                                {
                                    for (sector = 0; sector < numberOfSectors; sector++)
                                    {
                                        if (track > 0)
                                        {
                                            if (sector == (numberOfSectors - 1))
                                            {
                                                if (track == (numberOfTracks - 1))
                                                {
                                                    sectorBuffer[0] = 0x00;
                                                    sectorBuffer[1] = 0x00;
                                                }
                                                else
                                                {
                                                    sectorBuffer[0] = (byte)(track + 1);
                                                    sectorBuffer[1] = 1;
                                                }
                                            }
                                            else
                                            {
                                                sectorBuffer[0] = (byte)track;
                                                sectorBuffer[1] = (byte)(sector + 2);
                                            }
                                        }
                                        else
                                        {
                                            switch (sector)
                                            {
                                                case 0:
                                                    break;
                                                case 1:
                                                    break;
                                                case 2:

                                                    var volumeLabel = createFilename.PadRight(RAW_SIR.sizeofVolumeLabel);
                                                    for (int i = 0; i < volumeLabel.Length && i < 11; i++)
                                                    {
                                                        sectorBuffer[16 + i] = (byte)volumeLabel[i];
                                                    }

                                                    sectorBuffer[27] = (byte)(volumeNumber / 256);
                                                    sectorBuffer[28] = (byte)(volumeNumber % 256);
                                                    sectorBuffer[29] = 0x01;                  // first user track
                                                    sectorBuffer[30] = 0x01;                  // first user sector
                                                    sectorBuffer[31] = (byte)(numberOfTracks - 1);  // last user track
                                                    sectorBuffer[32] = (byte)numberOfSectors;       // last user sector
                                                    sectorBuffer[33] = (byte)(nTotalSectors / 256);
                                                    sectorBuffer[34] = (byte)(nTotalSectors % 256);
                                                    sectorBuffer[35] = (byte)now.Month;             // month
                                                    sectorBuffer[36] = (byte)now.Day;               // day
                                                    sectorBuffer[37] = (byte)(now.Year - 100);      // year (make Y2K compatible)
                                                    sectorBuffer[38] = (byte)(numberOfTracks - 1);  // max track
                                                    sectorBuffer[39] = (byte)numberOfSectors;       // max sector
                                                    break;

                                                case 3:
                                                    {
                                                        int bytesPerSector = (imageFile[currentDrive].driveInfo.LogicalSectorSize + 1) * 256;
                                                        for (int i = 0; i < bytesPerSector; i++)
                                                            sectorBuffer[i] = 0x00;
                                                    }
                                                    break;

                                                default:
                                                    if (sector == (numberOfSectors - 1))
                                                    {
                                                        sectorBuffer[0] = 0x00;
                                                        sectorBuffer[1] = 0x00;
                                                    }
                                                    else
                                                    {
                                                        sectorBuffer[0] = (byte)track;
                                                        sectorBuffer[1] = (byte)(sector + 2);
                                                    }
                                                    break;
                                            }
                                        }
                                        fp.Write(sectorBuffer, 0, 256);
                                    }
                                }

                                cStatus = 0x06;
                                fp.Close();
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine(e);
                                cStatus = 0x15;     // could not create file
                            }
                        }
                    }
                    else
                        cStatus = 0x15;     // total number of sectors not > 0
                }
                else
                    cStatus = 0x15;     // too many sectors
            }
            else
                cStatus = 0x15;     // too many tracks

            return (cStatus);
        }

        private void ListDirectorys()
        {
            if (streamDir != null)
            {
                streamDir.Dispose();
                streamDir = null;
            }

            var memstream = new MemoryStream();
            using (var stringWriter = new StreamWriter(memstream, Encoding.ASCII, -1, true))
            {
                // Get the drive and volume information
                System.IO.DriveInfo driveInfo = new System.IO.DriveInfo(Directory.GetDirectoryRoot(currentWorkingDirectory));
                long availableFreeSpace = driveInfo.AvailableFreeSpace;
                string driveName = driveInfo.Name;
                string volumeLabel = driveInfo.VolumeLabel;

                var topLine = string.Format("\r\nVolume in Drive {0} is {1}", driveName, volumeLabel);
                stringWriter.Write(topLine);
                Console.WriteLine(topLine);

                stringWriter.Write(currentWorkingDirectory + "\r\n\r\n");
                Console.WriteLine(currentWorkingDirectory);

                // get the list of directories in the current working directory
                string[] files = Directory.GetDirectories(currentWorkingDirectory);

                int maxFilenameSize = 0;
                foreach (string file in files)
                {
                    if (file.Length > maxFilenameSize)
                        maxFilenameSize = file.Length;
                }
                maxFilenameSize = maxFilenameSize - currentWorkingDirectory.Length;

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
                memstream.Seek(0, SeekOrigin.Begin);
                streamDir = new StreamReader(memstream, Encoding.ASCII);

                WriteByte((byte)'\r', false);
                WriteByte((byte)'\n', false);
                State = ConnectionState.SendingDir;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);

                WriteByte(0x06);
                State = ConnectionState.Connected;
            }
        }

        private bool StateConnectionStateConnected(int c)
        {
            bool done = false;

            if (c == 'U')
            {
                SetAttribute((int)ConsoleAttribute.Reverse);
                Console.Write("{0:X2} ", c);
                SetAttribute((int)ConsoleAttribute.Normal);

                State = ConnectionState.Syncronizing;
                WriteByte(0x55);
            }
            else if (c == '?')
            {
                // Query Current Directory
                SetAttribute((int)ConsoleAttribute.Reverse);
                Console.Write(currentWorkingDirectory);
                SetAttribute((int)ConsoleAttribute.Normal);
                Console.Write("\n");

                sp.Write(currentWorkingDirectory);
                WriteByte(0x0D, false);
                WriteByte(0x06);
            }
            else if (c == 'S')      // used by FLEX to read a sector from the PC
            {
                // 'S'end Sector Request
                imageFile[currentDrive].trackAndSectorAreTrackAndSector = true;
                imageFile[currentDrive].driveInfo.Mode = SectorAccessMode.S_MODE;
                State = ConnectionState.GetTrack;
            }
            else if (c == 'R')      // used by FLEX to write a sector to the PC
            {
                // 'R'eceive Sector Request
                imageFile[currentDrive].trackAndSectorAreTrackAndSector = true;
                imageFile[currentDrive].driveInfo.Mode = SectorAccessMode.R_MODE;
                State = ConnectionState.GetTrack;
            }
            else if (c == 'E')
            {
                // Exit
                State = ConnectionState.NotConnected;
                WriteByte(0x06);
                if (autoShutdown)
                {
                    Console.WriteLine("Recieved exit from client shuting down");
                    done = true;
                }
            }
            else if (c == 'Q')          // Quick Check for active connection
            {
                WriteByte(0x06);
            }
            else if (c == 'M')          // Mount drive image
            {
                commandFilename = new StringBuilder();
                State = ConnectionState.MountGetfilename;
            }
            else if (c == 'D')
            {
                // Delete file command

                commandFilename = new StringBuilder();
                State = ConnectionState.DeleteGetfilename;
            }
            else if (c == 'A')
            {
                // Dir file command

                commandFilename = new StringBuilder();
                State = ConnectionState.DirGetfilename;
            }
            else if (c == 'I')
            {
                ListDirectorys();
            }
            else if (c == 'P')
            {
                // Change Directory

                commandFilename = new StringBuilder();
                State = ConnectionState.CdGetfilename;
            }
            else if (c == 'V')
            {
                // Change Drive (and optionally the directory)
                commandFilename = new StringBuilder();
                State = ConnectionState.DriveGetfilename;
            }
            else if (c == 'C')
            {
                // Create a drive image

                createFilename = "";
                createPath = "";
                createVolumeNumber = "";
                createTrackCount = "";
                createSectorCount = "";

                State = ConnectionState.CreateGetParameters;
                createState = CreateState.GetCreatePath;
            }

            // now the Extended multi drive versions - these are what makes FLEXNet different from NETPC.
            //
            //      NETPC only allowed one drive to be mounted at a time. FLEXnet allows all four FLEX
            //      drives to be remote by providing mount points withinh FLEXNet for 4 image files per
            //      serial port.

            else if (c == 's')
            {
                // 's'end Sector Request with drive

                State = ConnectionState.GetReadDrive;
            }
            else if (c == 'r')
            {
                // 'r'eceive Sector Request with drive

                State = ConnectionState.GetWriteDrive;
            }
            else if (c == ('s' | 0x80))     // used by OS9 to read a sector from the PC - track and sector are LBN
            {
                // 'S'end Sector Request
                imageFile[currentDrive].trackAndSectorAreTrackAndSector = false;

                State = ConnectionState.GetReadDrive;
            }
            else if (c == ('r' | 0x80))      // used by OS9 to write a sector to the PC- track and sector are LBN
            {
                // 'R'eceive Sector Request
                imageFile[currentDrive].trackAndSectorAreTrackAndSector = false;

                State = ConnectionState.GetWriteDrive;
            }
            else if (c == 'm')      // Mount drive image with drive
            {
                commandFilename = new StringBuilder();
                State = ConnectionState.GetMountDrive;
            }
            else if (c == 'd')      // Report which disk image is mounted to requested drive
            {
                State = ConnectionState.GetRequestedMountDrive;
            }
            else if (c == 'c')      // Create a drive image
            {
                createFilename = "";
                createPath = "";
                createVolumeNumber = "";
                createTrackCount = "";
                createSectorCount = "";

                State = ConnectionState.GetCreateDrive;
                createState = CreateState.GetCreatePath;
            }
            else                    // Unknown - command - go back to (int)CONNECTION_STATE.CONNECTED
            {
                if (State != ConnectionState.Connected)
                    State = ConnectionState.Connected;

                if (c != 0x20)
                {
                    SetAttribute((int)ConsoleAttribute.Reverse);
                    Console.Write("\n State is reset to CONNECTED - Unknown command recieved [{0:X2}]", c);
                    SetAttribute((int)ConsoleAttribute.Normal);
                }
            }

            return done;
        }

        public void ProcessRequests()
        {
            bool done = false;
            while (!done)
            {
                try
                {
                    int c = sp.ReadByte();
                    if (c == -1)
                    {
                        // The serial port has been closed so stop the loop and let the thread die.
                        done = true;
                        continue;
                    }

                    if ((State != ConnectionState.ReceivingSector) &&
                        (State != ConnectionState.GetCrc))
                    {
                        Console.Write("{0:X2} ", c);
                    }

                    switch (State)
                    {
                        case ConnectionState.NotConnected:
                            StateConnectionStateNotConnected(c);
                            break;

                        case ConnectionState.Syncronizing:
                            StateConnectionStateSynchronizing(c);
                            break;

                        case ConnectionState.Connected:
                            done = StateConnectionStateConnected(c);
                            break;

                        case ConnectionState.GetRequestedMountDrive:
                            StateConnectionStateGetRequestedMountDrive(c);
                            break;

                        case ConnectionState.GetReadDrive:
                            StateConnectionStateGetReadDrive(c);
                            break;

                        case ConnectionState.GetWriteDrive:
                            StateConnectionStateGetWriteDrive(c);
                            break;

                        case ConnectionState.GetMountDrive:
                            StateConnectionStateGetMountDrive(c);
                            break;

                        case ConnectionState.GetCreateDrive:
                            StateConnectionStateGetCreateDrive(c);
                            break;

                        case ConnectionState.GetTrack:
                            StateConnectionStateGetTrack(c);
                            break;

                        case ConnectionState.GetSector:
                            StateConnectionStateGetSector(c);
                            break;

                        case ConnectionState.ReceivingSector:
                            StateConnectionStateRecievingSector(c);
                            break;

                        case ConnectionState.GetCrc:
                            StateConnectionStateGetCRC(c);
                            break;

                        case ConnectionState.MountGetfilename:
                            StateConnectionStateMountGetFilename(c);
                            break;

                        case ConnectionState.DeleteGetfilename:
                            StateConnectionStateDeleteGetFileName(c);
                            break;

                        case ConnectionState.DirGetfilename:
                            StateConnectionStateDirGetFilename(c);
                            break;

                        case ConnectionState.CdGetfilename:
                            StateConnectionStateCDGetFilename(c);
                            break;

                        case ConnectionState.DriveGetfilename:
                            StateConnectionStateDriveGetFilename(c);
                            break;

                        case ConnectionState.SendingDir:
                            StateConnectionStateSendingDir(c);
                            break;

                        case ConnectionState.CreateGetParameters:
                            StateConnectionStateCreateGetParameters(c);
                            break;

                        case ConnectionState.WaitAck:
                            StateConnectionStateWaitACK(c);
                            break;

                        default:
                            State = ConnectionState.NotConnected;
                            //sprintf (szHexTemp, "%02X", c);
                            //*StatusLine << '\n' << "State is reset to NOT_CONNECTED - Unknown STATE " << szHexTemp;
                            break;
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine("Terminating connection due to error below");
                    Console.WriteLine(e);
                    done = true;
                }
            }
        }
    }
}
