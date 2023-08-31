using System;
using System.Globalization;

using System.IO;

namespace FLEXNetSharp
{
    public class Ports
    {
        public Stream streamDir = null;
        public System.IO.Ports.SerialPort sp;
        public int          port;
        public int          state;
        public int          createState;
        public int          rate;
        public string       speed;
        public string       verbose;
        public string       autoMount;
        public ImageFile[]  imageFile = new ImageFile[4];
        public string       dirFilename;
        public int          currentDrive = 0;

        public int[] track = new int[4];
        public int[] sector = new int[4];

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

        public RAW_SIR ReadRAW_SIR(Stream fs)
        {
            long currentPosition = fs.Position;

            RAW_SIR systemInformationRecord = new RAW_SIR();

            if (m_lPartitionBias >= 0)
                fs.Seek(m_lPartitionBias + 0x0310 - (0x100 * SectorBias), SeekOrigin.Begin);     // fseek(m_fp, m_lPartitionBias + 0x0310 - (0x100 * m_nSectorBias), SEEK_SET);
            else
                fs.Seek(0x0210, SeekOrigin.Begin);                                                  // fseek (fs, 0x0210, SEEK_SET);

            fs.Read(systemInformationRecord.caVolumeLabel, 0, 11);                          // fread (stSystemInformationRecord.caVolumeLabel      , 1, sizeof (stSystemInformationRecord.caVolumeLabel), fs);  // $50 - $5A
            systemInformationRecord.cVolumeNumberHi     = (byte)fs.ReadByte();              // fread (&stSystemInformationRecord.cVolumeNumberHi   , 1, 1, fs);      // $5B
            systemInformationRecord.cVolumeNumberLo     = (byte)fs.ReadByte();              // fread (&stSystemInformationRecord.cVolumeNumberLo   , 1, 1, fs);      // $5C
            systemInformationRecord.cFirstUserTrack     = (byte)fs.ReadByte();              // fread (&stSystemInformationRecord.cFirstUserTrack   , 1, 1, fs);      // $5D
            systemInformationRecord.cFirstUserSector    = (byte)fs.ReadByte();              // fread (&stSystemInformationRecord.cFirstUserSector  , 1, 1, fs);      // $5E
            systemInformationRecord.cLastUserTrack      = (byte)fs.ReadByte();              // fread (&stSystemInformationRecord.cLastUserTrack    , 1, 1, fs);      // $5F
            systemInformationRecord.cLastUserSector     = (byte)fs.ReadByte();              // fread (&stSystemInformationRecord.cLastUserSector   , 1, 1, fs);      // $60
            systemInformationRecord.cTotalSectorsHi     = (byte)fs.ReadByte();              // fread (&stSystemInformationRecord.cTotalSectorsHi   , 1, 1, fs);      // $61
            systemInformationRecord.cTotalSectorsLo     = (byte)fs.ReadByte();              // fread (&stSystemInformationRecord.cTotalSectorsLo   , 1, 1, fs);      // $62
            systemInformationRecord.cMonth              = (byte)fs.ReadByte();              // fread (&stSystemInformationRecord.cMonth            , 1, 1, fs);      // $63
            systemInformationRecord.cDay                = (byte)fs.ReadByte();              // fread (&stSystemInformationRecord.cDay              , 1, 1, fs);      // $64
            systemInformationRecord.cYear               = (byte)fs.ReadByte();              // fread (&stSystemInformationRecord.cYear             , 1, 1, fs);      // $65
            systemInformationRecord.cMaxTrack           = (byte)fs.ReadByte();              // fread (&stSystemInformationRecord.cMaxTrack         , 1, 1, fs);      // $66
            systemInformationRecord.cMaxSector          = (byte)fs.ReadByte();              // fread (&stSystemInformationRecord.cMaxSector        , 1, 1, fs);      // $67

            fs.Seek(currentPosition, SeekOrigin.Begin);

            return systemInformationRecord;
        }

        public UniFLEX_SIR ReadUNIFLEX_SIR(Stream fs)
        {
            long currentPosition = fs.Position;

            UniFLEX_SIR drive_SIR = new UniFLEX_SIR();

            fs.Seek(512, SeekOrigin.Begin);         // fseek (fs, 512, SEEK_SET);

            drive_SIR.m_supdt[0]    = (byte)fs.ReadByte();  // fread (&drive_SIR.m_supdt,  1,   1, fs);    //rmb 1       sir update flag                                         0x0200        -> 00 
            drive_SIR.m_swprot[0]   = (byte)fs.ReadByte();  // fread (&drive_SIR.m_swprot, 1,   1, fs);    //rmb 1       mounted read only flag                                  0x0201        -> 00 
            drive_SIR.m_slkfr[0]    = (byte)fs.ReadByte();  // fread (&drive_SIR.m_slkfr,  1,   1, fs);    //rmb 1       lock for free list manipulation                         0x0202        -> 00 
            drive_SIR.m_slkfdn[0]   = (byte)fs.ReadByte();  // fread (&drive_SIR.m_slkfdn, 1,   1, fs);    //rmb 1       lock for fdn list manipulation                          0x0203        -> 00 

            fs.Read(drive_SIR.m_sintid, 0, 4);              // fread (&drive_SIR.m_sintid, 1,   4, fs);    //rmb 4       initializing system identifier                          0x0204        -> 00 

            fs.Read(drive_SIR.m_scrtim, 0, 4);              // fread (&drive_SIR.m_scrtim, 1,   4, fs);    //rmb 4       creation time                                           0x0208        -> 11 44 F3 FC
            fs.Read(drive_SIR.m_sutime, 0, 4);              // fread (&drive_SIR.m_sutime, 1,   4, fs);    //rmb 4       date of last update                                     0x020C        -> 11 44 F1 51
            fs.Read(drive_SIR.m_sszfdn, 0, 2);              // fread (&drive_SIR.m_sszfdn, 1,   2, fs);    //rmb 2       size in blocks of fdn list                              0x0210        -> 00 4A          = 74
            fs.Read(drive_SIR.m_ssizfr, 0, 3);              // fread (&drive_SIR.m_ssizfr, 1,   3, fs);    //rmb 3       size in blocks of volume                                0x0212        -> 00 08 1F       = 2079
            fs.Read(drive_SIR.m_sfreec, 0, 3);              // fread (&drive_SIR.m_sfreec, 1,   3, fs);    //rmb 3       total free blocks                                       0x0215        -> 00 04 9C       = 
            fs.Read(drive_SIR.m_sfdnc , 0, 2);              // fread (&drive_SIR.m_sfdnc,  1,   2, fs);    //rmb 2       free fdn count                                          0x0218        -> 01 B0
            fs.Read(drive_SIR.m_sfname, 0, 14);             // fread (&drive_SIR.m_sfname, 1,  14, fs);    //rmb 14      file system name                                        0x021A        -> 00 00 00 00 00 00 00 00 00 00 00 00 00 00 
            fs.Read(drive_SIR.m_spname, 0, 14);             // fread (&drive_SIR.m_spname, 1,  14, fs);    //rmb 14      file system pack name                                   0x0228        -> 00 00 00 00 00 00 00 00 00 00 00 00 00 00 
            fs.Read(drive_SIR.m_sfnumb, 0, 2);              // fread (&drive_SIR.m_sfnumb, 1,   2, fs);    //rmb 2       file system number                                      0x0236        -> 00 00
            fs.Read(drive_SIR.m_sflawc, 0, 2);              // fread (&drive_SIR.m_sflawc, 1,   2, fs);    //rmb 2       flawed block count                                      0x0238        -> 00 00


            drive_SIR.m_sdenf[0] = (byte)fs.ReadByte();     // fread (&drive_SIR.m_sdenf,  1,   1, fs);    //rmb 1       density flag - 0=single                                 0x023A        -> 01
            drive_SIR.m_ssidf[0] = (byte)fs.ReadByte();     // fread (&drive_SIR.m_ssidf,  1,   1, fs);    //rmb 1       side flag - 0=single                                    0x023B        -> 01

            fs.Read(drive_SIR.m_sswpbg, 0, 3);              // fread (&drive_SIR.m_sswpbg, 1,   3, fs);    //rmb 3       swap starting block number                              0x023C        -> 00 08 20
            fs.Read(drive_SIR.m_sswpsz, 0, 2);              // fread (&drive_SIR.m_sswpsz, 1,   2, fs);    //rmb 2       swap block count                                        0x023F        -> 01 80

            drive_SIR.m_s64k[0] = (byte)fs.ReadByte();      // fread (&drive_SIR.m_s64k,   1,   1, fs);    //rmb 1       non-zero if swap block count is multiple of 64K         0x0241        -> 00

            fs.Read(drive_SIR.m_swinc, 0, 11);              // fread (&drive_SIR.m_swinc,  1,  11, fs);    //rmb 11      Winchester configuration info                           0x0242        -> 00 00 00 00 00 00 2A 00 99 00 9A
            fs.Read(drive_SIR.m_sspare, 0, 11);             // fread (&drive_SIR.m_sspare, 1,  11, fs);    //rmb 11      spare bytes - future use                                0x024D        -> 00 9B 00 9C 00 9D 00 9E 00 9F 00

            drive_SIR.m_snfdn[0] = (byte)fs.ReadByte();     // fread (&drive_SIR.m_snfdn,  1,   1, fs);    //rmb 1       number of in core fdns                                  0x0278        -> A0     *snfdn * 2 = 320

            fs.Read(drive_SIR.m_scfdn, 0, 169);             // fread (&drive_SIR.m_scfdn,  1, 160, fs);    //rmb CFDN*2  in core free fdns                                       0x0279        variable (*snfdn * 2)

            drive_SIR.m_snfree[0] = (byte)fs.ReadByte();    // fread (&drive_SIR.m_snfree, 1,   1, fs);    //rmb 1       number of in core free blocks                           0x03B9        -> 03

            fs.Read(drive_SIR.m_sfree, 0, 300);             // fread (&drive_SIR.m_sfree,  1, 300, fs);    //rmb         CDBLKS*DSKADS in core free blocks                       0x03BA        -> 

            fs.Seek(currentPosition, SeekOrigin.Begin);

            return drive_SIR;
        }

        public uint ConvertToInt16(byte[] value)
        {
            return (uint)(value[0] * 256) + (uint)(value[1]);
        }

        public uint ConvertToInt24(byte[] value)
        {
            return (uint)(value[0] * 256 * 256) + (uint)(value[1] * 256) + (uint)(value[2]);
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
                long fileLength = fs.Length;        // int fd = _fileno (fs);   long fileLength = _filelength(fd);

                // First Check for OS9 format

                OS9_ID_SECTOR stIDSector = new OS9_ID_SECTOR();

                fs.Seek(0, SeekOrigin.Begin);

                stIDSector.cTOT[0] = (byte)fs.ReadByte();         // fread (&stIDSector.cTOT[0], 1, 1, fs);   // Total Number of sector on media
                stIDSector.cTOT[1] = (byte)fs.ReadByte();         // fread (&stIDSector.cTOT[1], 1, 1, fs);
                stIDSector.cTOT[2] = (byte)fs.ReadByte();         // fread (&stIDSector.cTOT[2], 1, 1, fs);
                stIDSector.cTKS[0] = (byte)fs.ReadByte();         // fread (&stIDSector.cTKS[0], 1, 1, fs);   // Sectors Per Track (not track 0)

                fs.Seek(16, SeekOrigin.Begin);                    // fseek(fs, 16, SEEK_SET);

                stIDSector.cFMT[0] = (byte)fs.ReadByte();         // fread (&stIDSector.cFMT[0], 1, 1, fs);     // Disk Format Byte
                stIDSector.cSPT[0] = (byte)fs.ReadByte();         // fread (&stIDSector.cSPT[0], 1, 1, fs);     // Sectors per track on track 0 high byte
                stIDSector.cSPT[1] = (byte)fs.ReadByte();         // fread (&stIDSector.cSPT[1], 1, 1, fs);     // Sectors per track on track 0 low  byte

                // There's no point in going any further if the file size if not right

                int nSectorsPerTrack = (int)stIDSector.cTKS[0];
                int nSectorsPerTrackZero = stIDSector.cSPT[1] + (stIDSector.cSPT[0] * 256);
                int nTotalSectors = stIDSector.cTOT[2] + (stIDSector.cTOT[1] * 256) + (stIDSector.cTOT[0] * 1024);

                long nDiskSize = (long)(nTotalSectors * 256);
                nDiskSize += (long)((nSectorsPerTrack - nSectorsPerTrackZero) * 256);

                if (nDiskSize == (fileLength & 0xFFFFFF00))
                {
                    ff = FileFormat.fileformat_OS9;
                    SectorBias = 0;
                    m_lPartitionBias = 0;
                }
                else
                {
                    int nMaxSector;
                    int nMaxTrack;

                    RAW_SIR stSystemInformationRecord = ReadRAW_SIR(fs);

                    nMaxSector = stSystemInformationRecord.cMaxSector;
                    nMaxTrack = stSystemInformationRecord.cMaxTrack;
                    nTotalSectors = stSystemInformationRecord.cTotalSectorsHi * 256 + stSystemInformationRecord.cTotalSectorsLo;

                    nDiskSize = (long)(nMaxTrack + 1) * (long)nMaxSector * (long)256;   // Track is 0 based, sector is 1 based

                    if (nDiskSize == (fileLength & 0xFFFFFF00))
                    {
                        ff = FileFormat.fileformat_FLEX;
                        SectorBias = 1;
                        m_lPartitionBias = 0;
                    }
                    else
                    {
                        UniFLEX_SIR drive_SIR = ReadUNIFLEX_SIR(fs);

                        uint nFDNSize = ConvertToInt16(drive_SIR.m_sszfdn);
                        uint nVolumeSize = ConvertToInt24(drive_SIR.m_ssizfr);
                        uint nSwapSize = ConvertToInt16(drive_SIR.m_sswpsz);

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
                                uint nInfoSize = 0;

                                fs.Seek(-2, SeekOrigin.End);            // (fs, -2, SEEK_END);
                                fs.Read(cInfoSize, 0, 2);               // fread (cInfoSize, 1, 2, fs);

                                nInfoSize = ConvertToInt16(cInfoSize);
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

        public void SetState(int newState)
        {
            state = newState;
            string statusLine = "";

            switch (newState)
            {
                case (int)CONNECTION_STATE.NOT_CONNECTED:               statusLine = "State is NOT_CONNECTED"; break;
                case (int)CONNECTION_STATE.SYNCRONIZING:                statusLine = "State is SYNCRONIZING"; break;
                case (int)CONNECTION_STATE.CONNECTED:                   statusLine = "\nState is CONNECTED"; break;
                case (int)CONNECTION_STATE.GET_REQUESTED_MOUNT_DRIVE:   statusLine = "State is GET_REQUESTED_MOUNT_DRIVE"; break;
                case (int)CONNECTION_STATE.GET_READ_DRIVE:              statusLine = "State is GET_DRIVE"; break;
                case (int)CONNECTION_STATE.GET_WRITE_DRIVE:             statusLine = "State is GET_DRIVE"; break;
                case (int)CONNECTION_STATE.GET_MOUNT_DRIVE:             statusLine = "State is GET_MOUNT_DRIVE"; break;
                case (int)CONNECTION_STATE.GET_CREATE_DRIVE:            statusLine = "State is GET_CREATE_DRIVE"; break;
                case (int)CONNECTION_STATE.GET_TRACK:                   statusLine = "State is GET_TRACK"; break;
                case (int)CONNECTION_STATE.GET_SECTOR:                  statusLine = "State is GET_SECTOR"; break;
                case (int)CONNECTION_STATE.RECEIVING_SECTOR:            statusLine = "State is RECEIVING_SECTOR"; break;
                case (int)CONNECTION_STATE.GET_CRC:                     statusLine = "State is GET_CRC"; break;
                case (int)CONNECTION_STATE.MOUNT_GETFILENAME:           statusLine = "State is MOUNT_GETFILENAME"; break;
                case (int)CONNECTION_STATE.WAIT_ACK:                    statusLine = "State is WAIT_ACK"; break;
                case (int)CONNECTION_STATE.PROCESSING_MOUNT:            statusLine = "State is PROCESSING_MOUNT"; break;
                case (int)CONNECTION_STATE.PROCESSING_DIR:              statusLine = "State is PROCESSING_DIR"; break;
                case (int)CONNECTION_STATE.PROCESSING_LIST:             statusLine = "State is PROCESSING_LIST"; break;
                case (int)CONNECTION_STATE.DELETE_GETFILENAME:          statusLine = "State is DELETE_GETFILENAME"; break;
                case (int)CONNECTION_STATE.DIR_GETFILENAME:             statusLine = "State is DIR_GETFILENAME"; break;
                case (int)CONNECTION_STATE.CD_GETFILENAME:              statusLine = "State is CD_GETFILENAME"; break;
                case (int)CONNECTION_STATE.DRIVE_GETFILENAME:           statusLine = "State is DRIVE_GETFILENAME"; break;
                case (int)CONNECTION_STATE.SENDING_DIR:                 statusLine = "State is SENDING_DIR"; break;

                case (int)CONNECTION_STATE.CREATE_GETPARAMETERS:
                    switch (createState)
                    {
                        case (int)CREATE_STATE.GET_CREATE_PATH:
                            statusLine = "State is CREATE_GETPARAMETERS GET_CREATE_PATH";
                            break;
                        case (int)CREATE_STATE.GET_CREATE_NAME:
                            statusLine = "State is CREATE_GETPARAMETERS GET_CREATE_NAME";
                            break;
                        case (int)CREATE_STATE.GET_CREATE_VOLUME:
                            statusLine = "State is CREATE_GETPARAMETERS GET_CREATE_VOLUME";
                            break;
                        case (int)CREATE_STATE.GET_CREATE_TRACK_COUNT:
                            statusLine = "State is CREATE_GETPARAMETERS GET_CREATE_TRACK_COUNT";
                            break;
                        case (int)CREATE_STATE.GET_CREATE_SECTOR_COUNT:
                            statusLine = "State is CREATE_GETPARAMETERS GET_CREATE_SECTOR_COUNT";
                            break;
                        case (int)CREATE_STATE.CREATE_THE_IMAGE:
                            statusLine = "State is CREATE_GETPARAMETERS CREATE_THE_IMAGE";
                            break;
                    }
                    break;

                default: statusLine = "State is UNKNOWN - [" + newState.ToString("X2") + "]"; break;
            }

            Console.WriteLine("\n" + statusLine);
        }

        public byte MountImageFile(string fileName, int nDrive)
        {
            byte c = 0x06;
            string Message = "";

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
                catch
                {
                    try
                    {
                        imageFile[nDrive].stream = File.Open(fileName, FileMode.Open, FileAccess.Read, FileShare.Read);
                        imageFile[nDrive].fileFormat = GetFileFormat(imageFile[nDrive].stream);
                        imageFile[nDrive].readOnly = true;
                    }
                    catch
                    {
                        c = 0x15;
                    }
                }

                if (c == 0x06)
                {
                    if (fileToLoad.Substring(1, 1) == ":")
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
                                OS9_ID_SECTOR stIDSector = new OS9_ID_SECTOR();

                                // Point to DD_TOT

                                imageFile[nDrive].stream.Seek(0, SeekOrigin.Begin);

                                stIDSector.cTOT[0] = (byte)imageFile[nDrive].stream.ReadByte();         // fread (&stIDSector.cTOT[0], 1, 1, fs);   // Total Number of sector on media
                                stIDSector.cTOT[1] = (byte)imageFile[nDrive].stream.ReadByte();         // fread (&stIDSector.cTOT[1], 1, 1, fs);
                                stIDSector.cTOT[2] = (byte)imageFile[nDrive].stream.ReadByte();         // fread (&stIDSector.cTOT[2], 1, 1, fs);
                                stIDSector.cTKS[0] = (byte)imageFile[nDrive].stream.ReadByte();         // fread (&stIDSector.cTKS[0], 1, 1, fs);   // Sectors Per Track (not track 0)

                                // point to DD_BIT

                                imageFile[nDrive].stream.Seek(6, SeekOrigin.Begin);                    // fseek(fs, 6, SEEK_SET);

                                stIDSector.cBIT[0] = (byte)imageFile[nDrive].stream.ReadByte();         // fread (&stIDSector.cBIT[0], 1, 1, fs);     // cluster size high byte
                                stIDSector.cBIT[1] = (byte)imageFile[nDrive].stream.ReadByte();         // fread (&stIDSector.cBIT[1], 1, 1, fs);     // cluster size low  byte

                                // POINT to DD_FMT

                                imageFile[nDrive].stream.Seek(16, SeekOrigin.Begin);                    // fseek(fs, 0x10, SEEK_SET);

                                stIDSector.cFMT[0] = (byte)imageFile[nDrive].stream.ReadByte();         // fread (&stIDSector.cFMT[0], 1, 1, fs);     // Disk Format Byte
                                stIDSector.cSPT[0] = (byte)imageFile[nDrive].stream.ReadByte();         // fread (&stIDSector.cSPT[0], 1, 1, fs);     // Sectors per track on track 0 high byte
                                stIDSector.cSPT[1] = (byte)imageFile[nDrive].stream.ReadByte();         // fread (&stIDSector.cSPT[1], 1, 1, fs);     // Sectors per track on track 0 low  byte

                                // POINT to DD_LSNSize

                                imageFile[nDrive].stream.Seek(0x68, SeekOrigin.Begin);                    // fseek(fs, 0x68, SEEK_SET);

                                stIDSector.cLSS[0] = (byte)imageFile[nDrive].stream.ReadByte();
                                stIDSector.cLSS[1] = (byte)imageFile[nDrive].stream.ReadByte();

                                // There's no point in going any further if the file size if not right

                                int nSectorsPerTrack        = stIDSector.cSPT[1] + (stIDSector.cSPT[0] * 256);
                                int nSectorsPerTrackZero    = stIDSector.cSPT[1] + (stIDSector.cSPT[0] * 256);
                                int nTotalSectors           = stIDSector.cTOT[2] + (stIDSector.cTOT[1] * 256) + (stIDSector.cTOT[0] * 1024);
                                int nClusterSize            = stIDSector.cBIT[1] + (stIDSector.cBIT[0] * 256);
                                int nLogicalSectorSize      = stIDSector.cLSS[1] + (stIDSector.cLSS[0] * 256);

                                long nDiskSize = (long)(nTotalSectors * 256);
                                nDiskSize += (long)((nSectorsPerTrack - nSectorsPerTrackZero) * 256);

                                imageFile[nDrive].driveInfo.NumberOfSectorsPerTrack     = nSectorsPerTrack;
                                imageFile[nDrive].driveInfo.TotalNumberOfSectorOnMedia  = nTotalSectors;
                                imageFile[nDrive].driveInfo.NumberOfBytesPerTrack       = nSectorsPerTrack * (nLogicalSectorSize + 1) * 256;
                                imageFile[nDrive].driveInfo.NumberOfSectorsPerCluster   = nClusterSize;
                                imageFile[nDrive].driveInfo.LogicalSectorSize           = nLogicalSectorSize;       // 0 = 256 bytes per sector
                            }
                            break;

                        default:
                            break;
                    }

                    if (fileToLoad.Substring(1, 1) == ":")
                        imageFile[nDrive].driveInfo.MountedFilename = fileToLoad;
                    else
                        imageFile[nDrive].driveInfo.MountedFilename = currentWorkingDirectory + "/" + fileToLoad;

                    imageFile[nDrive].driveInfo.MountedFilename = imageFile[nDrive].driveInfo.MountedFilename.ToUpper();
                }
                else
                {
                    if (fileToLoad.Substring(1, 1) == ":")
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
                            lOffsetToStartOfTrack = ((long)track[currentDrive] * imageFile[currentDrive].driveInfo.NumberOfBytesPerTrack);

                            if (sector[currentDrive] == 0)
                                lOffsetFromTrackStartToSector = (long)sector[currentDrive] * 256L;
                            else
                                lOffsetFromTrackStartToSector = (long)(sector[currentDrive] - 1) * 256L;

                            lSectorOffset = lOffsetToStartOfTrack + lOffsetFromTrackStartToSector;

                            Console.Write("[" + lSectorOffset.ToString("X8") + "]");
                        }
                        break;

                    case FileFormat.fileformat_OS9:     
                        {
                            // we need to convert the track and sector as LBN to Track and Sector to be able to calculate the offset used to access the diskette image sector

                            int lbn = (track[currentDrive] * 256) + sector[currentDrive];

                            try
                            {
                                int _track = lbn / (int)imageFile[currentDrive].driveInfo.NumberOfSectorsPerTrack;
                                int _sector = lbn % (int)imageFile[currentDrive].driveInfo.NumberOfSectorsPerTrack;
                                if (lbn <= imageFile[currentDrive].driveInfo.NumberOfSectorsPerTrack)
                                    _sector = lbn;

                                // now we can access the diskette iamge with T/S calcualted from LBN

                                lOffsetToStartOfTrack = ((long)_track * imageFile[currentDrive].driveInfo.NumberOfBytesPerTrack);

                                if (sector[currentDrive] == 0)
                                    lOffsetFromTrackStartToSector = (long)_sector * 256L;
                                else
                                    lOffsetFromTrackStartToSector = (long)(_sector - 1) * 256L;

                                lSectorOffset = lOffsetToStartOfTrack + lOffsetFromTrackStartToSector;
                            }
                            catch (Exception e)
                            {
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

                lSectorOffset = ((imageFile[currentDrive].driveInfo.LogicalSectorSize + 1) * 256) * (track[currentDrive] * 256 + sector[currentDrive]);
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
                catch
                {
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
                        try
                        {
                            Stream fp = File.Open(fullFilename, FileMode.Open, FileAccess.Read, FileShare.Read);
                            cStatus = 0x15;     // file already exists
                            fp.Close();
                        }
                        catch
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
                            catch
                            {
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
