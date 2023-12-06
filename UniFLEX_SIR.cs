using System.IO;

public class UniFLEX_SIR
{
    public byte[] m_supdt  = new byte[1];        //rmb 1       sir update flag                                         0x0200        -> 00 
    public byte[] m_swprot = new byte[1];        //rmb 1       mounted read only flag                                  0x0201        -> 00 
    public byte[] m_slkfr  = new byte[1];        //rmb 1       lock for free list manipulation                         0x0202        -> 00 
    public byte[] m_slkfdn = new byte[1];        //rmb 1       lock for fdn list manipulation                          0x0203        -> 00 
    public byte[] m_sintid = new byte[4];        //rmb 4       initializing system identifier                          0x0204        -> 00 
    public byte[] m_scrtim = new byte[4];        //rmb 4       creation time                                           0x0208        -> 11 44 F3 FC
    public byte[] m_sutime = new byte[4];        //rmb 4       date of last update                                     0x020C        -> 11 44 F1 51
    public byte[] m_sszfdn = new byte[2];        //rmb 2       size in blocks of fdn list                              0x0210        -> 00 4A          = 74
    public byte[] m_ssizfr = new byte[3];        //rmb 3       size in blocks of volume                                0x0212        -> 00 08 1F       = 2079
    public byte[] m_sfreec = new byte[3];        //rmb 3       total free blocks                                       0x0215        -> 00 04 9C       = 
    public byte[] m_sfdnc  = new byte[2];        //rmb 2       free fdn count                                          0x0218        -> 01 B0
    public byte[] m_sfname = new byte[14];       //rmb 14      file system name                                        0x021A        -> 00 00 00 00 00 00 00 00 00 00 00 00 00 00 
    public byte[] m_spname = new byte[14];       //rmb 14      file system pack name                                   0x0228        -> 00 00 00 00 00 00 00 00 00 00 00 00 00 00 
    public byte[] m_sfnumb = new byte[2];        //rmb 2       file system number                                      0x0236        -> 00 00
    public byte[] m_sflawc = new byte[2];        //rmb 2       flawed block count                                      0x0238        -> 00 00
    public byte[] m_sdenf  = new byte[1];        //rmb 1       density flag - 0=single                                 0x023A        -> 01
    public byte[] m_ssidf  = new byte[1];        //rmb 1       side flag - 0=single                                    0x023B        -> 01
    public byte[] m_sswpbg = new byte[3];        //rmb 3       swap starting block number                              0x023C        -> 00 08 20
    public byte[] m_sswpsz = new byte[2];        //rmb 2       swap block count                                        0x023F        -> 01 80
    public byte[] m_s64k   = new byte[1];        //rmb 1       non-zero if swap block count is multiple of 64K         0x0241        -> 00
    public byte[] m_swinc  = new byte[11];       //rmb 11      Winchester configuration info                           0x0242        -> 00 00 00 00 00 00 2A 00 99 00 9A
    public byte[] m_sspare = new byte[11];       //rmb 11      spare bytes - future use                                0x024D        -> 00 9B 00 9C 00 9D 00 9E 00 9F 00
    public byte[] m_snfdn  = new byte[1];        //rmb 1       number of in core fdns                                  0x0258        -> A0           *snfdn * 2 = 320
    public byte[] m_scfdn  = new byte[512];      //rmb CFDN*2  in core free fdns                                       0x0259        variable (*snfdn * 2)
    public byte[] m_snfree = new byte[1];        //rmb 1       number of in core free blocks                           0x03B9        -> 03
    public byte[] m_sfree  = new byte[16384];    //rmb         CDBLKS*DSKADS in core free blocks                       0x03BA        -> 

    public UniFLEX_SIR(Stream fs)
    {
        ReadFromStream(fs);
    }

    public void ReadFromStream(Stream fs)
    {
        long currentPosition = fs.Position;

        fs.Seek(512, SeekOrigin.Begin);

        m_supdt[0] = (byte)fs.ReadByte();
        m_swprot[0] = (byte)fs.ReadByte();
        m_slkfr[0] = (byte)fs.ReadByte();
        m_slkfdn[0] = (byte)fs.ReadByte();

        fs.Read(m_sintid, 0, 4);

        fs.Read(m_scrtim, 0, 4);
        fs.Read(m_sutime, 0, 4);
        fs.Read(m_sszfdn, 0, 2);
        fs.Read(m_ssizfr, 0, 3);
        fs.Read(m_sfreec, 0, 3);
        fs.Read(m_sfdnc, 0, 2);
        fs.Read(m_sfname, 0, 14);
        fs.Read(m_spname, 0, 14);
        fs.Read(m_sfnumb, 0, 2);
        fs.Read(m_sflawc, 0, 2);

        m_sdenf[0] = (byte)fs.ReadByte();
        m_ssidf[0] = (byte)fs.ReadByte();

        fs.Read(m_sswpbg, 0, 3);
        fs.Read(m_sswpsz, 0, 2);

        m_s64k[0] = (byte)fs.ReadByte();

        fs.Read(m_swinc, 0, 11);
        fs.Read(m_sspare, 0, 11);

        m_snfdn[0] = (byte)fs.ReadByte();

        fs.Read(m_scfdn, 0, 169);

        m_snfree[0] = (byte)fs.ReadByte();

        fs.Read(m_sfree, 0, 300);

        fs.Seek(currentPosition, SeekOrigin.Begin);
    }

}
