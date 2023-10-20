namespace FLEXNetSharp
{
    public class DriveInfo
    {
        public SECTOR_ACCESS_MODE mode;
        public string MountedFilename;
        //public int      NumberOfTracks;
        public byte[] cNumberOfSectorsPerTrack = new byte[1];
        public long NumberOfBytesPerTrack;
        public long NumberOfSectorsPerTrack;
        public int NumberOfSectorsPerCluster;
        public long TotalNumberOfSectorOnMedia;
        public int LogicalSectorSize;
    }
}
