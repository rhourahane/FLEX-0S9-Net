using System.IO;

public class OS9_ID_SECTOR
{
    public byte[] cTOT = new byte[3];      // Total Number of sector on media
    public byte[] cTKS = new byte[1];      // Number of tracks
    public byte[] cMAP = new byte[2];      // Number of bytes in allocation map
    public byte[] cBIT = new byte[2];      // Number of sectors per cluster
    public byte[] cDIR = new byte[3];      // Starting sector of root directory
    public byte[] cOWN = new byte[2];      // Owners user number
    public byte[] cATT = new byte[1];      // Disk attributes
    public byte[] cDSK = new byte[2];      // Disk Identification
    public byte[] cFMT = new byte[1];      // Disk Format: density, number of sides
    public byte[] cSPT = new byte[2];      // Number of sectors per track
    public byte[] cRES = new byte[2];      // Reserved for future use
    public byte[] cBT  = new byte[3];       // Starting sector of bootstrap file
    public byte[] cBSZ = new byte[2];      // Size of bootstrap file (in bytes)
    public byte[] cDAT = new byte[5];      // Time of creation Y:M:D:H:M
    public byte[] cNAM = new byte[32];     // Volume name (last char has sign bit set)

    public byte[] cLSS = new byte[2];       // logical sector size at offset 0x68

    public OS9_ID_SECTOR(Stream fs)
    {
        ReadFromStream(fs);
    }

    public int DiskSize
    {
        get
        {
            int nSectorsPerTrack = (int)cTKS[0];
            int nSectorsPerTrackZero = cSPT[1] + (cSPT[0] * 256);
            int nTotalSectors = cTOT[2] + (cTOT[1] * 256) + (cTOT[0] * 1024);

            int nDiskSize = nTotalSectors * 256;
            nDiskSize += (nSectorsPerTrack - nSectorsPerTrackZero) * 256;

            return nDiskSize;
        }
    }

    public void ReadFromStream(Stream fs)
    {
        long currentPosition = fs.Position;

        // Point to DD_TOT
        fs.Seek(0, SeekOrigin.Begin);

        cTOT[0] = (byte)fs.ReadByte();
        cTOT[1] = (byte)fs.ReadByte();
        cTOT[2] = (byte)fs.ReadByte();
        cTKS[0] = (byte)fs.ReadByte();

        // point to DD_BIT
        fs.Seek(6, SeekOrigin.Begin);

        cBIT[0] = (byte)fs.ReadByte();
        cBIT[1] = (byte)fs.ReadByte();

        // POINT to DD_FMT
        fs.Seek(16, SeekOrigin.Begin);

        cFMT[0] = (byte)fs.ReadByte();
        cSPT[0] = (byte)fs.ReadByte();
        cSPT[1] = (byte)fs.ReadByte();

        // POINT to DD_LSNSize
        fs.Seek(0x68, SeekOrigin.Begin);

        cLSS[0] = (byte)fs.ReadByte();
        cLSS[1] = (byte)fs.ReadByte();

        fs.Seek(currentPosition, SeekOrigin.Begin);
    }
}
