using FLEX_0S9_Net;

namespace FLEXNetSharp
{
    public class DriveInfo
    {
        public SectorAccessMode Mode;
        public string MountedFilename;
        public long NumberOfBytesPerTrack;
        public long NumberOfSectorsPerTrack;
        public int NumberOfSectorsPerCluster;
        public long TotalNumberOfSectorOnMedia;
        public int LogicalSectorSize;
    }
}
