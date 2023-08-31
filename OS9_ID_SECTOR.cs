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

    // ...

    public byte[] cLSS = new byte[2];       // logical sector size at offset 0x68
}
