namespace Np2ptpGui.Models;

public enum NdjsonEventKind
{
    Progress,
    Result,
    Status,
    Error,
}

public sealed record NdjsonEvent
{
    public required NdjsonEventKind Kind { get; init; }
    public required string Op { get; init; }

    // progress (pack: bytes; get/fetch: chunks, optional phase)
    public long? BytesDone { get; init; }
    public long? BytesTotal { get; init; }
    public int? ChunksDone { get; init; }
    public int? ChunksTotal { get; init; }
    public string? Phase { get; init; }

    // result
    public string? Root { get; init; }
    public string? Path { get; init; }
    public int? ChunksNew { get; init; }
    public int? ChunksFetched { get; init; }
    public int? ChunksDeduped { get; init; }

    // status (serve)
    public int? Peers { get; init; }
    public string? Tracker { get; init; }
    public long? BytesServed { get; init; }
    public long? BytesReceived { get; init; }

    // error
    public string? Message { get; init; }
}
