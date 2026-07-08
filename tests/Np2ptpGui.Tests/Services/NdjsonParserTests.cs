namespace Np2ptpGui.Tests.Services;

using Np2ptpGui.Models;
using Np2ptpGui.Services;
using Xunit;

public class NdjsonParserTests
{
    [Fact]
    public void TryParse_PackProgress_ParsesBytesFields()
    {
        var line = """{"event":"progress","op":"pack","bytes_done":1048576,"bytes_total":5242880}""";

        Assert.True(NdjsonParser.TryParse(line, out var evt));
        Assert.Equal(NdjsonEventKind.Progress, evt!.Kind);
        Assert.Equal("pack", evt.Op);
        Assert.Equal(1048576L, evt.BytesDone);
        Assert.Equal(5242880L, evt.BytesTotal);
    }

    [Fact]
    public void TryParse_PackResult_ParsesRootAndChunkFields()
    {
        var line = """{"event":"result","op":"pack","root":"np2ptp:e0cf","chunks_total":70,"chunks_new":2,"bytes_total":5242880}""";

        Assert.True(NdjsonParser.TryParse(line, out var evt));
        Assert.Equal(NdjsonEventKind.Result, evt!.Kind);
        Assert.Equal("np2ptp:e0cf", evt.Root);
        Assert.Equal(70, evt.ChunksTotal);
        Assert.Equal(2, evt.ChunksNew);
        Assert.Equal(5242880L, evt.BytesTotal);
    }

    [Fact]
    public void TryParse_FetchProgress_ParsesChunkAndByteFields()
    {
        var line = """{"event":"progress","op":"fetch","chunks_done":1230,"chunks_total":36491,"bytes_done":78643200,"bytes_total":2400000000}""";

        Assert.True(NdjsonParser.TryParse(line, out var evt));
        Assert.Equal(1230, evt!.ChunksDone);
        Assert.Equal(36491, evt.ChunksTotal);
        Assert.Equal(78643200L, evt.BytesDone);
        Assert.Equal(2400000000L, evt.BytesTotal);
    }

    [Fact]
    public void TryParse_FetchResult_ParsesFetchedAndDedupedFields()
    {
        var line = """{"event":"result","op":"fetch","root":"np2ptp:abc","chunks_fetched":36491,"chunks_deduped":0,"bytes_total":2400000000}""";

        Assert.True(NdjsonParser.TryParse(line, out var evt));
        Assert.Equal("np2ptp:abc", evt!.Root);
        Assert.Equal(36491, evt.ChunksFetched);
        Assert.Equal(0, evt.ChunksDeduped);
    }

    [Fact]
    public void TryParse_GetProgress_ParsesPhaseField()
    {
        var line = """{"event":"progress","op":"get","phase":"downloading","chunks_done":40,"chunks_total":70}""";

        Assert.True(NdjsonParser.TryParse(line, out var evt));
        Assert.Equal("downloading", evt!.Phase);
        Assert.Equal(40, evt.ChunksDone);
        Assert.Equal(70, evt.ChunksTotal);
    }

    [Fact]
    public void TryParse_GetResult_ParsesPathField()
    {
        var line = """{"event":"result","op":"get","root":"np2ptp:xyz","path":"downloaded.bin","bytes_total":123,"chunks_fetched":68,"chunks_deduped":2}""";

        Assert.True(NdjsonParser.TryParse(line, out var evt));
        Assert.Equal("downloaded.bin", evt!.Path);
        Assert.Equal(68, evt.ChunksFetched);
        Assert.Equal(2, evt.ChunksDeduped);
    }

    [Fact]
    public void TryParse_ServeStatus_ParsesPeersAndByteCounters()
    {
        var line = """{"event":"status","op":"serve","peers":3,"tracker":"https://nptp.bogotec.uk","bytes_served":10485760,"bytes_received":0}""";

        Assert.True(NdjsonParser.TryParse(line, out var evt));
        Assert.Equal(NdjsonEventKind.Status, evt!.Kind);
        Assert.Equal(3, evt.Peers);
        Assert.Equal("https://nptp.bogotec.uk", evt.Tracker);
        Assert.Equal(10485760L, evt.BytesServed);
        Assert.Equal(0L, evt.BytesReceived);
    }

    [Fact]
    public void TryParse_Error_ParsesMessage()
    {
        var line = """{"event":"error","op":"fetch","message":"download failed: request to peer failed"}""";

        Assert.True(NdjsonParser.TryParse(line, out var evt));
        Assert.Equal(NdjsonEventKind.Error, evt!.Kind);
        Assert.Equal("download failed: request to peer failed", evt.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{"op":"pack"}""")]
    [InlineData("""{"event":"unknown","op":"pack"}""")]
    public void TryParse_InvalidOrUnknownInput_ReturnsFalse(string line)
    {
        Assert.False(NdjsonParser.TryParse(line, out var evt));
        Assert.Null(evt);
    }
}
