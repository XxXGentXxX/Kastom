using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Kastom.Modules.Audio.CoreAudio;
using Kastom.Modules.Audio.CoreAudio.Interop;
using Kastom.Modules.Audio.Model;
using Kastom.Sdk.Services;

namespace Kastom.Modules.Audio.Tests.CoreAudio;

/// <summary>Частини шару Core Audio, які не звертаються до звукової служби (у тестах її не чіпаємо взагалі).</summary>
public sealed class CoreAudioPureTests
{
    private const string RenderId = "{0.0.0.00000000}.{8c1f2a3b-0000-4000-8000-123456789abc}";

    [Fact]
    public void DeviceIdsRoundTripLikeEarTrumpet()
    {
        var packed = DeviceIds.Pack(RenderId, EDataFlow.Render);
        Assert.Equal(@"\\?\SWD#MMDEVAPI#" + RenderId + "#{e6327cad-dcec-4949-ae8a-991e976a79d2}", packed);
        Assert.Equal(RenderId, DeviceIds.Unpack(packed));
        Assert.EndsWith("#{2eef81be-33fa-4800-9670-1cd474972c3f}", DeviceIds.Pack(RenderId, EDataFlow.Capture));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{0.0.0.00000000}.{guid}")]
    [InlineData(@"\\?\SWD#MMDEVAPI#")]
    public void UnpackRejectsForeignStrings(string? packed) => Assert.Null(DeviceIds.Unpack(packed));

    [Fact]
    public void NativeStructsHaveWindowsLayout()
    {
        Assert.Equal(20, Marshal.SizeOf<PropertyKey>());
        Assert.Equal(IntPtr.Size == 8 ? 24 : 16, Marshal.SizeOf<PropVariant>());
        Assert.True(PropVariant.FromBool(true).AsBool());
        Assert.False(PropVariant.FromBool(false).AsBool());
        Assert.Null(new PropVariant().AsBool());
        Assert.Null(new PropVariant().AsString());
    }

    [Theory]
    [InlineData(17f, 20f)]
    [InlineData(14f, 10f)]
    [InlineData(-5f, 0f)]
    [InlineData(99f, 30f)]
    public void BoostRoundsToDriverStep(float requested, float expected) =>
        Assert.Equal(expected, MicBoostControl.Quantize(requested, 0, 30, 10));

    [Fact]
    public void BoostWithoutStepOnlyClamps() => Assert.Equal(12.5f, MicBoostControl.Quantize(12.5f, 0, 20, 0));

    [Theory]
    [InlineData("Microphone Boost", true)]
    [InlineData("Mic Boost", true)]
    [InlineData("Підсилення мікрофона", true)]
    [InlineData("Microphone", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void BoostIsFoundByNodeName(string? name, bool expected) => Assert.Equal(expected, MicTopology.IsBoostName(name));

    // Перші шість — у тому вигляді, як їх віддала Windows на справжньому ПК (2026-10-25): з номером з'єднувача
    // попереду. Номери екземплярів пристроїв замінено вигаданими.
    [Theory]
    [InlineData(@"{2}.\\?\bthhfenum#bthhfpaudio#8&1a2b3c4d&1&97#{6994ad04-93ef-11d0-a3cc-00a0c9223196}\voice", true)]
    [InlineData(@"{2}.\\?\bthenum#{0000110b-0000-1000-8000-00805f9b34fb}_vid&0002054c_pid&0d58#7&2#{6994ad04-93ef-11d0-a3cc-00a0c9223196}\wave", true)]
    [InlineData(@"{2}.\\?\usb#vid_17a0&pid_0303&mi_00#6&5e6f7a8b&1&0000#{6994ad04-93ef-11d0-a3cc-00a0c9223196}\global", false)]
    [InlineData(@"{2}.\\?\hdaudio#func_01&ven_10ec&dev_0897#4&2#{6994ad04-93ef-11d0-a3cc-00a0c9223196}\ewave", false)]
    [InlineData(@"{2}.\\?\root#unnamed_device#0001#{6994ad04-93ef-11d0-a3cc-00a0c9223196}", false)]
    [InlineData(@"{2}.\\?\root#media#0000#{6994ad04-93ef-11d0-a3cc-00a0c9223196}\vbaudio", false)]
    [InlineData(@"\\?\BTHLEDEVICE#{...}", true)]
    [InlineData(null, false)]
    public void BluetoothIsRecognisedByFilterPath(string? path, bool expected) => Assert.Equal(expected, MicTopology.IsBluetoothPath(path));

    [Fact]
    public void ExitedProcessIsNotRunning()
    {
        // Відкритий дескриптор не дає Windows віддати цей номер процесу іншому, поки триває перевірка.
        using var process = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit 0") { CreateNoWindow = true, UseShellExecute = false })!;
        process.WaitForExit();
        Assert.True(ProcessInfo.IsRunning(Environment.ProcessId));
        Assert.False(ProcessInfo.IsRunning(process.Id));
    }

    [Fact]
    public void DisconnectCodesAreRecognised()
    {
        Assert.True(ComConstants.IsServiceGone(ComConstants.AudclntEServiceNotRunning));
        Assert.True(ComConstants.IsServiceGone(ComConstants.RpcEDisconnected));
        Assert.False(ComConstants.IsServiceGone(ComConstants.AudclntEDeviceInvalidated));
        Assert.False(ComConstants.IsServiceGone(unchecked((int)0x80070057)));

        // Зник один пристрій — це не зникнення служби: досить перелічити пристрої.
        Assert.True(ComConstants.IsDeviceGone(ComConstants.AudclntEDeviceInvalidated));
        Assert.False(ComConstants.IsDeviceGone(ComConstants.AudclntEServiceNotRunning));

        Assert.True(Com.IsServiceGone(new AudioCommandException("x", new COMException("x", ComConstants.AudclntEServiceNotRunning))));
        Assert.True(Com.IsDeviceGone(new AudioCommandException("x", new COMException("x", ComConstants.AudclntEDeviceInvalidated))));
        Assert.False(Com.IsServiceGone(new AudioCommandException("x")));
        Assert.False(Com.IsDeviceGone(new AudioCommandException("x")));
    }

    [Fact]
    public void ProcessInfoDescribesCurrentProcess()
    {
        var path = ProcessInfo.GetPath(Environment.ProcessId);
        Assert.NotNull(path);
        Assert.Equal(Environment.ProcessPath, path, ignoreCase: true);
        Assert.Equal(Path.GetFileNameWithoutExtension(path)!.ToLowerInvariant(), ProcessInfo.GetProcessName(path, Environment.ProcessId));
        Assert.Null(ProcessInfo.GetPath(0));
        Assert.Equal(Process.GetCurrentProcess().ProcessName.ToLowerInvariant(), ProcessInfo.GetProcessName(null, Environment.ProcessId));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("  ", null)]
    [InlineData(" Discord ", "Discord")]
    public void DisplayNamePassesPlainText(string? name, string? expected) => Assert.Equal(expected, ProcessInfo.ResolveDisplayName(name));

    [Fact]
    public async Task BackendBeforeStartTouchesNothing()
    {
        using var backend = new CoreAudioBackend(new NullLog());
        Assert.False(backend.Current.IsAvailable);
        Assert.Equal(BackendCapabilities.None, backend.Capabilities);
        await Assert.ThrowsAsync<AudioCommandException>(() => backend.SetDeviceMuteAsync(RenderId, true));
        backend.BeginMetering().Dispose();
        await backend.StopAsync();
    }

    private sealed class NullLog : IModuleLogger
    {
        public void Info(string message)
        {
        }

        public void Warn(string message)
        {
        }

        public void Error(string message, Exception? exception = null)
        {
        }
    }
}
