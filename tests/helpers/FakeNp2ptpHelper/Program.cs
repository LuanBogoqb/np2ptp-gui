using System;
using System.Threading;

var scenario = Environment.GetEnvironmentVariable("FAKE_NP2PTP_SCENARIO") ?? "pack-ok";

switch (scenario)
{
    case "pack-ok":
        Console.WriteLine("""{"event":"progress","op":"pack","bytes_done":1048576,"bytes_total":5242880}""");
        Thread.Sleep(50);
        Console.WriteLine("""{"event":"progress","op":"pack","bytes_done":5242880,"bytes_total":5242880}""");
        Console.WriteLine("""{"event":"result","op":"pack","root":"np2ptp:deadbeef","chunks_total":70,"chunks_new":2,"bytes_total":5242880}""");
        return 0;

    case "fetch-ok":
        Console.WriteLine("""{"event":"progress","op":"fetch","chunks_done":5,"chunks_total":10}""");
        Thread.Sleep(150);
        Console.WriteLine("""{"event":"result","op":"fetch","path":"C:\\downloads\\file.bin","chunks_total":10,"chunks_new":10}""");
        return 0;

    case "fetch-error":
        Console.WriteLine("""{"event":"error","op":"fetch","message":"download failed: request to peer failed"}""");
        return 1;

    case "serve-ok":
        var stopRequested = new ManualResetEventSlim(false);
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stopRequested.Set(); };
        Console.WriteLine("""{"event":"status","op":"serve","peers":0,"tracker":"https://tracker.example","bytes_served":0,"bytes_received":0}""");
        stopRequested.Wait(TimeSpan.FromSeconds(30));
        return 0;

    default:
        Console.WriteLine($"unknown scenario: {scenario}");
        return 2;
}
