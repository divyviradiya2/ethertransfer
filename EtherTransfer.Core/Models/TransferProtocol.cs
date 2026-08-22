namespace EtherTransfer.Core.Models;

public static class ProtocolMessageTypes
{
    public const string TransferRequest = "TRANSFER_REQUEST";
    public const string TransferResponse = "TRANSFER_RESPONSE";
}

public class BaseProtocolMessage
{
    public string Type { get; set; } = string.Empty;
}

public class TransferRequestMessage : BaseProtocolMessage
{
    public TransferRequestMessage()
    {
        Type = ProtocolMessageTypes.TransferRequest;
    }

    public string SenderName { get; set; } = string.Empty;
    public int TotalFiles { get; set; }
    public long TotalSize { get; set; }
    public bool ContainsFolders { get; set; }
    public int PayloadFolderCount { get; set; }
    public int PayloadFileCount { get; set; }
    public System.Collections.Generic.List<string> RootElementNames { get; set; } = new();
}

public class TransferResponseMessage : BaseProtocolMessage
{
    public TransferResponseMessage()
    {
        Type = ProtocolMessageTypes.TransferResponse;
    }

    public bool Accepted { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class FileItemMetadata
{
    public string RelativePath { get; set; } = string.Empty;
    public string RootName { get; set; } = string.Empty;
    public long Size { get; set; }
}

public class FileChecksumMessage : BaseProtocolMessage
{
    public FileChecksumMessage() { Type = "FILE_CHECKSUM"; }
    public string Sha256 { get; set; } = string.Empty;
}

public class FileSkipMessage : BaseProtocolMessage
{
    public FileSkipMessage() { Type = "FILE_SKIP"; }
    public string RelativePath { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}
