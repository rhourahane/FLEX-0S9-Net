using System;
using System.IO;
// [Serializable()]
public class RAW_SIR
{
    public const int sizeofVolumeLabel = 11;
    public const int sizeofDirEntry = 24;
    public const int sizeofSystemInformationRecord = 24;

    public byte[] caVolumeLabel = new byte[sizeofVolumeLabel];    // $50 - $5A
    public byte cVolumeNumberHi;                    // $5B
    public byte cVolumeNumberLo;                    // $5C
    public byte cFirstUserTrack;                    // $5D
    public byte cFirstUserSector;                   // $5E
    public byte cLastUserTrack;                     // $5F
    public byte cLastUserSector;                    // $60
    public byte cTotalSectorsHi;                    // $61
    public byte cTotalSectorsLo;                    // $62
    public byte cMonth;                             // $63
    public byte cDay;                               // $64
    public byte cYear;                              // $65
    public byte cMaxTrack;                          // $66
    public byte cMaxSector;                         // $67


    public RAW_SIR(Stream fs, long partitionBias, int sectorBias)
    {
        ReadFromStream(fs, partitionBias, sectorBias);
    }

    public void ReadFromStream(Stream fs, long partitionBias, int sectorBias)
    {
        long currentPosition = fs.Position;

        if (partitionBias >= 0)
            fs.Seek(partitionBias + 0x0310 - (0x100 * sectorBias), SeekOrigin.Begin);
        else
            fs.Seek(0x0210, SeekOrigin.Begin);

        fs.Read(caVolumeLabel, 0, 11);
        cVolumeNumberHi = (byte)fs.ReadByte();
        cVolumeNumberLo = (byte)fs.ReadByte();
        cFirstUserTrack = (byte)fs.ReadByte();
        cFirstUserSector = (byte)fs.ReadByte();
        cLastUserTrack = (byte)fs.ReadByte();
        cLastUserSector = (byte)fs.ReadByte();
        cTotalSectorsHi = (byte)fs.ReadByte();
        cTotalSectorsLo = (byte)fs.ReadByte();
        cMonth = (byte)fs.ReadByte();
        cDay = (byte)fs.ReadByte();
        cYear = (byte)fs.ReadByte();
        cMaxTrack = (byte)fs.ReadByte();
        cMaxSector = (byte)fs.ReadByte();

        fs.Seek(currentPosition, SeekOrigin.Begin);
    }

}
