using System;
using System.Collections.Generic;
using System.Xml;
using System.IO;
using System.IO.Ports;
using System.Reflection;

enum CONSOLE_ATTRIBUTE
{
    REVERSE_ATTRIBUTE = 0,
    NORMAL_ATTRIBUTE
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
                                Console.Write("{0:X2} ", c);
                            }

                            switch (serialPort.State)
                            {
                                case CONNECTION_STATE.NOT_CONNECTED:
                                    serialPort.StateConnectionStateNotConnected(c);
                                    break;

                                case CONNECTION_STATE.SYNCRONIZING:
                                    serialPort.StateConnectionStateSynchronizing(c);
                                    break;

                                case CONNECTION_STATE.CONNECTED:
                                    serialPort.StateConnectionStateConnected(c);
                                    break;

                                case CONNECTION_STATE.GET_REQUESTED_MOUNT_DRIVE:
                                    serialPort.StateConnectionStateGetRequestedMountDrive(c);
                                    break;

                                case CONNECTION_STATE.GET_READ_DRIVE:
                                    serialPort.StateConnectionStateGetReadDrive(c);
                                    break;

                                case CONNECTION_STATE.GET_WRITE_DRIVE:
                                    serialPort.StateConnectionStateGetWriteDrive(c);
                                    break;

                                case CONNECTION_STATE.GET_MOUNT_DRIVE:
                                    serialPort.StateConnectionStateGetMountDrive(c);
                                    break;

                                case CONNECTION_STATE.GET_CREATE_DRIVE:
                                    serialPort.StateConnectionStateGetCreateDrive(c);
                                    break;

                                case CONNECTION_STATE.GET_TRACK:
                                    serialPort.StateConnectionStateGetTrack(c);
                                    break;

                                case CONNECTION_STATE.GET_SECTOR:
                                    serialPort.StateConnectionStateGetSector(c);
                                    break;

                                case CONNECTION_STATE.RECEIVING_SECTOR:
                                    serialPort.StateConnectionStateRecievingSector(c);
                                    break;

                                case CONNECTION_STATE.GET_CRC:
                                    serialPort.StateConnectionStateGetCRC(c);
                                    break;

                                case CONNECTION_STATE.MOUNT_GETFILENAME:
                                    serialPort.StateConnectionStateMountGetFilename(c);
                                    break;

                                case CONNECTION_STATE.DELETE_GETFILENAME:
                                    serialPort.StateConnectionStateDeleteGetFilename(c);
                                    break;

                                case CONNECTION_STATE.DIR_GETFILENAME:
                                    serialPort.StateConnectionStateDirGetFilename(c);
                                    break;

                                case CONNECTION_STATE.CD_GETFILENAME:
                                    serialPort.StateConnectionStateCDGetFilename(c);
                                    break;

                                case CONNECTION_STATE.DRIVE_GETFILENAME:
                                    serialPort.StateConnectionStateDriveGetFilename(c);
                                    break;

                                case CONNECTION_STATE.SENDING_DIR:
                                    serialPort.StateConnectionStateSendingDir(c);
                                    break;

                                case CONNECTION_STATE.CREATE_GETPARAMETERS:
                                    serialPort.StateConnectionStateCreateGetParameters(c);
                                    break;

                                case CONNECTION_STATE.WAIT_ACK:
                                    serialPort.StateConnectionStateWaitACK(c);
                                    break;

                                default:
                                    serialPort.State = CONNECTION_STATE.NOT_CONNECTED;
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
