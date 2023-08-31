using System;
// [Serializable()]
public class RAW_SIR
{
    public const int sizeofVolumeLabel = 11;
    public const int sizeofDirEntry = 24;
    public const int sizeofSystemInformationRecord = 24;

    public RAW_SIR()
    {
        Array.Clear(caVolumeLabel, 0, caVolumeLabel.Length);
        cVolumeNumberHi  = 0x00;
        cVolumeNumberLo  = 0x00;
        cFirstUserTrack  = 0x00;
        cFirstUserSector = 0x00;
        cLastUserTrack   = 0x00;
        cLastUserSector  = 0x00;
        cTotalSectorsHi  = 0x00;
        cTotalSectorsLo  = 0x00;
        cMonth           = 0x00;
        cDay             = 0x00;
        cYear            = 0x00;
        cMaxTrack        = 0x00;
        cMaxSector       = 0x00;
    }

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
}
