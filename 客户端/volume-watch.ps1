param(
  [Parameter(Mandatory=$true)][string]$LogDir,
  [Parameter(Mandatory=$true)][string]$StopFile
)
$ErrorActionPreference = 'Stop'
$jsonAsm = @'
using System;
using System.Runtime.InteropServices;

[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
class MMDeviceEnumeratorComObject { }

[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDeviceEnumerator {
  int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
  int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
  int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
  int RegisterEndpointNotificationCallback(IntPtr client);
  int UnregisterEndpointNotificationCallback(IntPtr client);
}

[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDevice {
  int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object interfacePointer);
  int OpenPropertyStore(int access, out IntPtr properties);
  int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
  int GetState(out int state);
}

[ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioEndpointVolume {
  int RegisterControlChangeNotify(IntPtr notify);
  int UnregisterControlChangeNotify(IntPtr notify);
  int GetChannelCount(out uint count);
  int SetMasterVolumeLevel(float level, Guid context);
  int SetMasterVolumeLevelScalar(float level, Guid context);
  int GetMasterVolumeLevel(out float level);
  int GetMasterVolumeLevelScalar(out float level);
  int SetChannelVolumeLevel(uint channel, float level, Guid context);
  int SetChannelVolumeLevelScalar(uint channel, float level, Guid context);
  int GetChannelVolumeLevel(uint channel, out float level);
  int GetChannelVolumeLevelScalar(uint channel, out float level);
  int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, Guid context);
  int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
  int GetVolumeStepInfo(out uint step, out uint stepCount);
  int VolumeStepUp(Guid context);
  int VolumeStepDown(Guid context);
  int QueryHardwareSupport(out uint mask);
  int GetVolumeRange(out float min, out float max, out float inc);
}

public static class VolumeProbe {
  public static string Read() {
    IMMDeviceEnumerator en = (IMMDeviceEnumerator)(new MMDeviceEnumeratorComObject());
    IMMDevice dev;
    if (en.GetDefaultAudioEndpoint(0, 0, out dev) != 0 || dev == null) return "{\"error\":\"no_default_output\"}";
    string id;
    dev.GetId(out id);
    Guid iid = typeof(IAudioEndpointVolume).GUID;
    object obj;
    if (dev.Activate(ref iid, 23, IntPtr.Zero, out obj) != 0 || obj == null) return "{\"error\":\"activate_failed\"}";
    IAudioEndpointVolume vol = (IAudioEndpointVolume)obj;
    float scalar;
    bool mute;
    vol.GetMasterVolumeLevelScalar(out scalar);
    vol.GetMute(out mute);
    return "{\"volume\":" + Math.Round(scalar * 100.0, 1).ToString(System.Globalization.CultureInfo.InvariantCulture)
      + ",\"mute\":" + (mute ? "true" : "false")
      + ",\"device\":" + Quote(id) + "}";
  }
  static string Quote(string s) {
    if (s == null) return "null";
    return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
  }
}
'@
Add-Type -TypeDefinition $jsonAsm
New-Item -ItemType Directory -Force $LogDir | Out-Null

function Get-LogFile {
  $dirs = @(Get-ChildItem -LiteralPath $LogDir -Directory -Filter 'backroom-*' -ErrorAction SilentlyContinue | Sort-Object Name)
  if ($dirs.Count -gt 0) { return (Join-Path $dirs[$dirs.Count - 1].FullName 'volume.jsonl') }
  return (Join-Path $LogDir 'volume.jsonl')
}

$lastInfo = ''
while (-not (Test-Path -LiteralPath $StopFile)) {
  try {
    $info = [VolumeProbe]::Read()
    if ($info -ne $lastInfo) {
      $line = '{"time":"' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + '",' + $info.TrimStart('{')
      $f = Get-LogFile
      [System.IO.File]::AppendAllText($f, $line + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))
      $lastInfo = $info
    }
  } catch { }
  Start-Sleep -Milliseconds 400
}
