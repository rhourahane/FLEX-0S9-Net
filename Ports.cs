﻿using System;
using System.Globalization;

using System.IO;

namespace FLEXNetSharp
{
    public class Ports
    {
        public Stream streamDir = null;
        public System.IO.Ports.SerialPort sp;
        public string       port;
        private CONNECTION_STATE state;
        public CREATE_STATE createState;
        public int          rate;
        public string       speed;
        public string       verbose;
        public string       autoMount;
        public ImageFile[]  imageFile = new ImageFile[4];
        public string       dirFilename;
        public int          currentDrive = 0;

        public int sectorIndex = 0;
        public int calculatedCRC = 0;

        public int checksumIndex = 0;
        public int checksum = 0;

        public byte[] sectorBuffer = new byte[1024];        // allow for up to 1024 byte sectors

        public string currentWorkingDirectory;
        public string commandFilename;
        public string createFilename;
        public string createPath;
        public string createVolumeNumber;
        public string createTrackCount;
        public string createSectorCount;

        public string defaultStartDirectory = "";

        public bool g_displaySectorData;

        CultureInfo ci = new CultureInfo("en-us");

        private long m_lPartitionBias = -1;

        public const int sizeofVolumeLabel = 11;
        public const int sizeofDirEntry = 24;
        public const int sizeofSystemInformationRecord = 24;

        private int m_nSectorBias;

        public int SectorBias
        {
            get { return m_nSectorBias; }
            set { m_nSectorBias = value; }
        }

        public int ConvertMSBToInt16(byte[] value)
        {
            return (value[0] * 256) + value[1];
        }

        public int ConvertMSBToInt24(byte[] value)
        {
            return (value[0] * 256 * 256) + (value[1] * 256) + value[2];
        }

        /// <summary>
        /// Determine the file format OS9 - FLEX or UniFLEX
        /// </summary>
        /// <param name="fs"></param>
        /// <returns></returns>
        public FileFormat GetFileFormat(Stream fs)
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

        public void WriteByte(byte byteToWrite)
        {
            WriteByte(byteToWrite, true);
        }

        public void WriteByte(byte byteToWrite, bool displayOnScreen)
        {
            byte[] byteBuffer = new byte[1];
            byteBuffer[0] = byteToWrite;
            sp.Write(byteBuffer, 0, 1);

            if (displayOnScreen)
            {
                SetAttribute((int)CONSOLE_ATTRIBUTE.REVERSE_ATTRIBUTE);
                Console.Write(byteToWrite.ToString("X2", ci) + " ");
                SetAttribute((int)CONSOLE_ATTRIBUTE.NORMAL_ATTRIBUTE);
            }
        }

        public void SetAttribute(int attr)
        {
            if (attr == (int)CONSOLE_ATTRIBUTE.REVERSE_ATTRIBUTE)
            {
                Console.BackgroundColor = ConsoleColor.White;
                Console.ForegroundColor = ConsoleColor.Black;
            }
            else if (attr == (int)CONSOLE_ATTRIBUTE.NORMAL_ATTRIBUTE)
            {
                Console.BackgroundColor = ConsoleColor.Black;
                Console.ForegroundColor = ConsoleColor.White;
            }
        }

        public CONNECTION_STATE State
        {
            get => state;

            set
            {
                state = value;

                DisplayState(state);
            }
        }

        private void DisplayState(CONNECTION_STATE displayState)
        {
            switch (displayState)
            {
                case CONNECTION_STATE.NOT_CONNECTED:
                case CONNECTION_STATE.SYNCRONIZING:
                case CONNECTION_STATE.CONNECTED:
                case CONNECTION_STATE.GET_REQUESTED_MOUNT_DRIVE:
                case CONNECTION_STATE.GET_READ_DRIVE:
                case CONNECTION_STATE.GET_WRITE_DRIVE:
                case CONNECTION_STATE.GET_MOUNT_DRIVE:
                case CONNECTION_STATE.GET_CREATE_DRIVE:
                case CONNECTION_STATE.GET_TRACK:
                case CONNECTION_STATE.GET_SECTOR:
                case CONNECTION_STATE.RECEIVING_SECTOR:
                case CONNECTION_STATE.GET_CRC:
                case CONNECTION_STATE.MOUNT_GETFILENAME:
                case CONNECTION_STATE.WAIT_ACK:
                case CONNECTION_STATE.PROCESSING_MOUNT:
                case CONNECTION_STATE.PROCESSING_DIR:
                case CONNECTION_STATE.PROCESSING_LIST:
                case CONNECTION_STATE.DELETE_GETFILENAME:
                case CONNECTION_STATE.DIR_GETFILENAME:
                case CONNECTION_STATE.CD_GETFILENAME:
                case CONNECTION_STATE.DRIVE_GETFILENAME:
                case CONNECTION_STATE.SENDING_DIR:
                    Console.WriteLine($"State is {state}");
                    break;

                case CONNECTION_STATE.CREATE_GETPARAMETERS:
                    Console.WriteLine($"State is {displayState} {createState}");
                    break;

                default:
                    Console.WriteLine($"State is UNHANDLED {displayState} - {(int)state:X2}");
                    break;
            }
        }

        public byte MountImageFile(string fileName, int nDrive)
        {
            byte c = 0x06;
            try
            {
                Directory.SetCurrentDirectory(currentWorkingDirectory);

                string fileToLoad = fileName;

                try
                {
                    if (imageFile[nDrive] == null)
                        imageFile[nDrive] = new ImageFile();

                    imageFile[nDrive].stream = File.Open(fileName, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
                    imageFile[nDrive].fileFormat = GetFileFormat(imageFile[nDrive].stream);
                    imageFile[nDrive].readOnly = false;
                }
                catch (UnauthorizedAccessException)
                {
                    // Unable to open file for read/write try readonly
                    try
                    {
                        imageFile[nDrive].stream = File.Open(fileName, FileMode.Open, FileAccess.Read, FileShare.Read);
                        imageFile[nDrive].fileFormat = GetFileFormat(imageFile[nDrive].stream);
                        imageFile[nDrive].readOnly = true;

                        Console.WriteLine("Mounting {0} readonly", fileName);
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
                            imageFile[nDrive].stream.Read(imageFile[nDrive].driveInfo.cNumberOfSectorsPerTrack, 0, 1);
                            imageFile[nDrive].driveInfo.NumberOfSectorsPerTrack = Convert.ToInt16(imageFile[nDrive].driveInfo.cNumberOfSectorsPerTrack[0]);
                            imageFile[nDrive].driveInfo.NumberOfBytesPerTrack = imageFile[nDrive].driveInfo.NumberOfSectorsPerTrack * 256;
                            imageFile[nDrive].driveInfo.NumberOfBytesPerTrack = (long)(imageFile[nDrive].driveInfo.NumberOfSectorsPerTrack * 256L);
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

                                long nDiskSize = (long)(nTotalSectors * 256);
                                nDiskSize += (long)((nSectorsPerTrack - nSectorsPerTrackZero) * 256);

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

                Message = Message.Replace("/", @"\");
                Console.WriteLine(Message);
            }
            catch (Exception e)
            {
                Console.WriteLine(string.Format("Unable to open directory: {0} due to exception {1} - please change your configuration file", currentWorkingDirectory, e));
            }

            return (c);
        }

        public long GetSectorOffset()
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
                                lOffsetFromTrackStartToSector = (long)imageFile[currentDrive].sector * 256L;
                            else
                                lOffsetFromTrackStartToSector = (long)(imageFile[currentDrive].sector - 1) * 256L;

                            lSectorOffset = lOffsetToStartOfTrack + lOffsetFromTrackStartToSector;

                            Console.Write("[" + lSectorOffset.ToString("X8") + "]");
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

                                lOffsetToStartOfTrack = ((long)_track * imageFile[currentDrive].driveInfo.NumberOfBytesPerTrack);

                                if (imageFile[currentDrive].sector == 0)
                                    lOffsetFromTrackStartToSector = (long)_sector * 256L;
                                else
                                    lOffsetFromTrackStartToSector = (long)(_sector - 1) * 256L;

                                lSectorOffset = lOffsetToStartOfTrack + lOffsetFromTrackStartToSector;
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine(e);
                                string message = e.Message;
                            }
                            Console.Write("[" + lSectorOffset.ToString("X8") + "]");
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
                Console.Write("[" + lSectorOffset.ToString("X8") + "]");
            }

            return (lSectorOffset);
        }

        public byte WriteSector()
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


        public void SendCharToFLEX(byte c, bool displayOnScreen)
        {
            WriteByte(c, displayOnScreen);
        }

        public void SendCharToFLEX(string str, bool displayOnScreen)
        {
            if (displayOnScreen)
            {
                SetAttribute((int)CONSOLE_ATTRIBUTE.REVERSE_ATTRIBUTE);
                Console.Write(str);
                SetAttribute((int)CONSOLE_ATTRIBUTE.NORMAL_ATTRIBUTE);
            }

            sp.Write(str);
        }

        public void SendSector()
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
                    if (Program.verboseOutput)
                    {
                        // cause new line after every 32 characters
                        if (nIndex % 32 == 0)
                        {
                            Console.WriteLine();
                        }
                    }

                    //WriteByte(sectorBuffer[nIndex], g_displaySectorData);
                    WriteByte(sectorBuffer[nIndex], Program.verboseOutput);
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

        public byte CreateImageFile()
        {
            byte cStatus = 0x15;

            string fullFilename = createPath + "/" + createFilename + ".DSK";

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
                                                    sectorBuffer[0] = (byte)0x00;
                                                    sectorBuffer[1] = (byte)0x00;
                                                }
                                                else
                                                {
                                                    sectorBuffer[0] = (byte)(track + 1);
                                                    sectorBuffer[1] = (byte)1;
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

                                                    char[] cArray = createFilename.ToCharArray();
                                                    for (int i = 0; i < cArray.Length && i < 11; i++)
                                                    {
                                                        sectorBuffer[16 + i] = (byte)cArray[i];
                                                    }

                                                    sectorBuffer[27] = (byte)(volumeNumber / 256);
                                                    sectorBuffer[28] = (byte)(volumeNumber % 256);
                                                    sectorBuffer[29] = (byte)0x01;                  // first user track
                                                    sectorBuffer[30] = (byte)0x01;                  // first user sector
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
                                                        sectorBuffer[0] = (byte)0x00;
                                                        sectorBuffer[1] = (byte)0x00;
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
    }
}
