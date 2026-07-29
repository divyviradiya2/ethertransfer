using System.Collections.Generic;

namespace EtherTransfer.Core.Models;

public enum LogLevel { Debug, Info, Warning, Error }

public record StructuredLogMessage(
    string EventId,
    string Message,
    LogLevel Level = LogLevel.Info,
    Dictionary<string, object>? Properties = null
);
