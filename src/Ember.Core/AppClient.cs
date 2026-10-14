using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Ember.Core;

/// <summary>
/// Sends one request to the running Ember app on its pipe and returns the reply. Before trusting the answer it checks that
/// the process serving the pipe is Ember.App in the same sign-in session. Failures become a plain reply, never an exception.
/// </summary>
[SupportedOSPlatform("windows")]
public static class AppClient
{
    public static async Task<AppReply> SendAsync(AppRequest request, string pipeName = EmberNames.AppPipeName, bool verifyServer = true, CancellationToken ct = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            try { await pipe.ConnectAsync(500, timeout.Token).ConfigureAwait(false); }
            catch (TimeoutException) { return new AppReply(false, "Ember is not running.", null); }
            if (verifyServer && !IsEmberApp(pipe.SafePipeHandle)) return new AppReply(false, "That is not the Ember app.", null);
            await pipe.WriteAsync(Encoding.UTF8.GetBytes(AppProtocol.Encode(request) + "\n"), timeout.Token).ConfigureAwait(false);
            await pipe.FlushAsync(timeout.Token).ConfigureAwait(false);
            var line = await LineReader.ReadLineAsync(new BufferedStream(pipe), AppProtocol.MaxLineBytes, timeout.Token).ConfigureAwait(false);
            return line is not null && AppProtocol.TryDecodeReply(line, out var reply) && reply is not null
                ? reply
                : new AppReply(false, "Ember did not answer.", null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new AppReply(false, "Ember did not answer in time.", null);
        }
        catch (Exception)
        {
            return new AppReply(false, "Ember is not running.", null);
        }
    }

    static bool IsEmberApp(SafePipeHandle handle)
    {
        try
        {
            if (!GetNamedPipeServerProcessId(handle, out var pid)) return false;
            using var server = Process.GetProcessById((int)pid);
            using var me = Process.GetCurrentProcess();
            return server.ProcessName.Equals("Ember.App", StringComparison.OrdinalIgnoreCase) && server.SessionId == me.SessionId;
        }
        catch (Exception)
        {
            return false;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverProcessId);
}
