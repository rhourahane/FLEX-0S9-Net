using FLEX_0S9_Net;
using System.IO;

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
}
