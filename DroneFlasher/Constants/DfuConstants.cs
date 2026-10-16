namespace DroneFlasher.Constants;

public static class DfuConstants
{
    public const ushort StmDfuVid = 0x0483;
    public const ushort StmDfuPid = 0xDF11;

    public const byte DFU_DETACH = 0x00;
    public const byte DFU_DNLOAD = 0x01;
    public const byte DFU_GETSTATUS = 0x03;
    public const byte DFU_CLRSTATUS = 0x04;

    public const byte DFU_STATE_APP_IDLE = 0;
    public const byte DFU_STATE_DFU_IDLE = 2;
    public const byte DFU_STATE_DNLOAD_SYNC = 3;
    public const byte DFU_STATE_DNLOAD_BUSY = 4;
    public const byte DFU_STATE_DNLOAD_IDLE = 5;
    public const byte DFU_STATE_MANIFEST = 6;
    public const byte DFU_STATE_ERROR = 10;

    public const int TransferBlockSize = 2048;
    public const uint DefaultBaseAddress = 0x08000000;
}