namespace EtherTransfer.Core.Models;

/// <summary>
/// Contains all the JSON metadata models for the Transfer Protocol.
/// </summary>

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

/// <summary>
/// Sent by the sender after streaming a file's bytes. Contains the SHA-256 hash
/// computed incrementally during the read so the receiver can verify integrity.
/// </summary>
public class FileChecksumMessage : BaseProtocolMessage
{
    public FileChecksumMessage() { Type = "FILE_CHECKSUM"; }
    public string Sha256 { get; set; } = string.Empty;
}

/// <summary>
/// Sent by the sender when a file cannot be read (locked, deleted, permission denied).
/// The receiver must skip this file without aborting the entire transfer.
/// </summary>
public class FileSkipMessage : BaseProtocolMessage
{
    public FileSkipMessage() { Type = "FILE_SKIP"; }
    public string RelativePath { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}
