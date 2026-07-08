namespace Np2ptpGui.Tests.ViewModels;

using Np2ptpGui.Models;
using Np2ptpGui.ViewModels;
using Xunit;

public class OperationViewModelTests
{
    [Fact]
    public void Apply_ByteProgress_UpdatesFractionAndDetail()
    {
        var vm = new OperationViewModel("1", OperationType.Pack, "input");

        vm.Apply(new NdjsonEvent { Kind = NdjsonEventKind.Progress, Op = "pack", BytesDone = 2621440, BytesTotal = 5242880 });

        Assert.Equal(0.5, vm.ProgressFraction, precision: 3);
        Assert.Contains("2,621,440", vm.DetailText);
    }

    [Fact]
    public void Apply_ChunkProgressWithPhase_UpdatesFractionAndDetail()
    {
        var vm = new OperationViewModel("1", OperationType.Fetch, "np2ptp:x");

        vm.Apply(new NdjsonEvent { Kind = NdjsonEventKind.Progress, Op = "get", Phase = "downloading", ChunksDone = 35, ChunksTotal = 70 });

        Assert.Equal(0.5, vm.ProgressFraction, precision: 3);
        Assert.Contains("downloading", vm.DetailText);
    }

    [Fact]
    public void Apply_PackResult_MarksCompletedWithLink()
    {
        var vm = new OperationViewModel("1", OperationType.Pack, "input");

        vm.Apply(new NdjsonEvent { Kind = NdjsonEventKind.Result, Op = "pack", Root = "np2ptp:deadbeef", ChunksTotal = 70, ChunksNew = 2, BytesTotal = 5242880 });

        Assert.Equal("Completed", vm.Status);
        Assert.Equal(1.0, vm.ProgressFraction);
        Assert.Equal("np2ptp:deadbeef", vm.ResultLink);
    }

    [Fact]
    public void Apply_ServeStatus_UpdatesDetailWithPeerCount()
    {
        var vm = new OperationViewModel("1", OperationType.Serve, "file.nptp");

        vm.Apply(new NdjsonEvent { Kind = NdjsonEventKind.Status, Op = "serve", Peers = 3, Tracker = "https://x", BytesServed = 100, BytesReceived = 0 });

        Assert.Contains("peers: 3", vm.DetailText);
    }

    [Fact]
    public void Apply_Error_MarksErrorWithMessage()
    {
        var vm = new OperationViewModel("1", OperationType.Fetch, "np2ptp:x");

        vm.Apply(new NdjsonEvent { Kind = NdjsonEventKind.Error, Op = "fetch", Message = "download failed: request to peer failed" });

        Assert.Equal("Error", vm.Status);
        Assert.Equal("download failed: request to peer failed", vm.ErrorMessage);
        Assert.True(vm.IsError);
    }

    [Fact]
    public void Apply_RaisesPropertyChangedForStatus()
    {
        var vm = new OperationViewModel("1", OperationType.Fetch, "np2ptp:x");
        var raised = false;
        vm.PropertyChanged += (_, e) => raised |= e.PropertyName == nameof(OperationViewModel.Status);

        vm.Apply(new NdjsonEvent { Kind = NdjsonEventKind.Error, Op = "fetch", Message = "boom" });

        Assert.True(raised);
    }
}
