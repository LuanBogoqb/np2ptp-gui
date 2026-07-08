namespace Np2ptpGui.Services;

using System.Text.Json;
using Np2ptpGui.Models;

public static class NdjsonParser
{
    public static bool TryParse(string line, out NdjsonEvent? evt)
    {
        evt = null;
        if (string.IsNullOrWhiteSpace(line)) return false;

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            return false;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (!root.TryGetProperty("event", out var eventProp)) return false;
            if (!root.TryGetProperty("op", out var opProp)) return false;

            if (eventProp.ValueKind is not JsonValueKind.String) return false;
            if (opProp.ValueKind is not JsonValueKind.String) return false;

            var kind = eventProp.GetString() switch
            {
                "progress" => NdjsonEventKind.Progress,
                "result" => NdjsonEventKind.Result,
                "status" => NdjsonEventKind.Status,
                "error" => NdjsonEventKind.Error,
                _ => (NdjsonEventKind?)null,
            };
            if (kind is null) return false;

            evt = new NdjsonEvent
            {
                Kind = kind.Value,
                Op = opProp.GetString() ?? "",
                BytesDone = GetLong(root, "bytes_done"),
                BytesTotal = GetLong(root, "bytes_total"),
                ChunksDone = GetInt(root, "chunks_done"),
                ChunksTotal = GetInt(root, "chunks_total"),
                Phase = GetString(root, "phase"),
                Root = GetString(root, "root"),
                Path = GetString(root, "path"),
                ChunksNew = GetInt(root, "chunks_new"),
                ChunksFetched = GetInt(root, "chunks_fetched"),
                ChunksDeduped = GetInt(root, "chunks_deduped"),
                Peers = GetInt(root, "peers"),
                Tracker = GetString(root, "tracker"),
                BytesServed = GetLong(root, "bytes_served"),
                BytesReceived = GetLong(root, "bytes_received"),
                Message = GetString(root, "message"),
            };
            return true;
        }
    }

    private static long? GetLong(JsonElement root, string name) =>
        root.TryGetProperty(name, out var p) && p.ValueKind is JsonValueKind.Number ? p.GetInt64() : null;

    private static int? GetInt(JsonElement root, string name) =>
        root.TryGetProperty(name, out var p) && p.ValueKind is JsonValueKind.Number ? p.GetInt32() : null;

    private static string? GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var p) && p.ValueKind is JsonValueKind.String ? p.GetString() : null;
}
