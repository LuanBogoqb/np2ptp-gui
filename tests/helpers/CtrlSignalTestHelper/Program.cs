using System;
using System.Threading;

var received = new ManualResetEventSlim(false);
ConsoleSpecialKey? key = null;

Console.CancelKeyPress += (_, e) =>
{
    key = e.SpecialKey;
    e.Cancel = true;
    received.Set();
};

if (received.Wait(TimeSpan.FromSeconds(10)))
{
    Console.WriteLine($"SIGNAL:{key}");
    return 0;
}

Console.WriteLine("TIMEOUT");
return 99;
