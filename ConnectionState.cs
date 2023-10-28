namespace FLEX_0S9_Net
{
    public enum ConnectionState
    {
        NotConnected = -1,
        Syncronizing,
        Connected,
        GetRequestedMountDrive,
        GetReadDrive,
        GetWriteDrive,
        GetMountDrive,
        GetCreateDrive,
        GetTrack,
        GetSector,
        ReceivingSector,
        GetCrc,
        MountGetfilename,
        DeleteGetfilename,
        DirGetfilename,
        CdGetfilename,
        DriveGetfilename,
        SendingDir,
        CreateGetParameters,
        WaitAck,
    };
}