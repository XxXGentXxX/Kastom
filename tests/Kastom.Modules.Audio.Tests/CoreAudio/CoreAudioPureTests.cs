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

    [Fact]
    public void DisconnectCodesAreRecognised()
    {
        Assert.True(ComConstants.IsDisconnect(ComConstants.AudclntEServiceNotRunning));
        Assert.True(ComConstants.IsDisconnect(ComConstants.AudclntEDeviceInvalidated));
        Assert.True(ComConstants.IsDisconnect(ComConstants.RpcEDisconnected));
        Assert.False(ComConstants.IsDisconnect(unchecked((int)0x80070057)));
        Assert.True(Com.IsDisconnect(new AudioCommandException("x", new COMException("x", ComConstants.AudclntEServiceNotRunning))));
        Assert.False(Com.IsDisconnect(new AudioCommandException("x")));
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
