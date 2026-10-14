#Requires -Version 5.1
<#
.SYNOPSIS
Asks Corsair iCUE, through the same SDK library Ember uses, whether it lets a program connect, and prints its answer.
Run it once from a normal PowerShell window and once from an "Run as administrator" one, and compare.

.DESCRIPTION
Changes nothing: it connects, waits a few seconds, lists how many devices iCUE offers, and disconnects.
The library is the one Ember ships (in the iCUE folder next to Ember.App.exe); pass -DllPath to use another copy.
#>
[CmdletBinding()]
param(
    [string]$DllPath = (Join-Path $env:ProgramFiles 'Ember\App\iCUE\iCUESDK.x64_2019.dll'),
    [int]$Seconds = 8
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path $DllPath)) { throw "Cannot find $DllPath. Is Ember installed?" }

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class IcueProbe
{
    [DllImport("kernel32", CharSet = CharSet.Unicode)] static extern IntPtr LoadLibrary(string path);
    [DllImport("kernel32")] static extern IntPtr GetProcAddress(IntPtr module, string name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void StateHandler(IntPtr context, IntPtr changed);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int ConnectFn(IntPtr handler, IntPtr context);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int DisconnectFn();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int GetDevicesFn(IntPtr filter, int max, IntPtr devices, out int size);

    public static int State;
    public static string Versions = "";
    public static int Final; // the session state just before disconnecting
    static readonly StateHandler Keep = (c, e) =>
    {
        State = Marshal.ReadInt32(e);
        Versions = string.Format("iCUE {0}.{1}.{2}, SDK server {3}.{4}.{5}",
            Marshal.ReadInt32(e, 28), Marshal.ReadInt32(e, 32), Marshal.ReadInt32(e, 36),
            Marshal.ReadInt32(e, 16), Marshal.ReadInt32(e, 20), Marshal.ReadInt32(e, 24));
    };

    static T Get<T>(IntPtr lib, string name) { return (T)(object)Marshal.GetDelegateForFunctionPointer(GetProcAddress(lib, name), typeof(T)); }

    public static string Run(string dll, int seconds, out int devices)
    {
        devices = -1;
        var lib = LoadLibrary(dll);
        if (lib == IntPtr.Zero) return "could not load the library";
        var rc = Get<ConnectFn>(lib, "CorsairConnect")(Marshal.GetFunctionPointerForDelegate(Keep), IntPtr.Zero);
        var end = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < end && State != 6) System.Threading.Thread.Sleep(200);
        if (State == 6)
        {
            var filter = Marshal.AllocHGlobal(4); Marshal.WriteInt32(filter, -1);
            var buf = Marshal.AllocHGlobal(396 * 64);
            int size;
            var grc = Get<GetDevicesFn>(lib, "CorsairGetDevices")(filter, 64, buf, out size);
            devices = grc == 0 ? size : -1;
        }
        Final = State;
        var result = "CorsairConnect returned " + rc + "; " + Versions;
        Get<DisconnectFn>(lib, "CorsairDisconnect")();
        return result;
    }
}
'@

$names = @{ 0 = 'invalid'; 1 = 'closed'; 2 = 'connecting'; 3 = 'timed out (iCUE not answering)'; 4 = 'REFUSED (iCUE does not allow this program)'; 5 = 'lost'; 6 = 'CONNECTED' }
$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
Write-Host "Running as administrator: $admin"
Write-Host "Library: $DllPath"
$devices = 0
$text = [IcueProbe]::Run($DllPath, $Seconds, [ref]$devices)
Write-Host "Result: session $($names[[IcueProbe]::Final])"
Write-Host $text
if ($devices -ge 0) { Write-Host "iCUE offers $devices device(s) to the SDK." }
