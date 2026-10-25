using System.Runtime.InteropServices;

// Оголошення Core Audio (mmdeviceapi.h, endpointvolume.h, audiopolicy.h, audioclient.h, devicetopology.h).
// Порядок методів у кожному інтерфейсі — це таблиця методів COM: міняти його не можна, навіть для методів,
// які модуль не викликає. BOOL у COM-методах — 4 байти, тому всюди [MarshalAs(UnmanagedType.Bool)]
// (без цього .NET передав би 2-байтовий VARIANT_BOOL).
namespace Kastom.Modules.Audio.CoreAudio.Interop;

internal enum EDataFlow
{
    Render = 0,
    Capture = 1,
    All = 2,
}

internal enum ERole
{
    Console = 0,
    Multimedia = 1,
    Communications = 2,
}

internal enum AudioSessionState
{
    Inactive = 0,
    Active = 1,
    Expired = 2,
}

internal enum AudioSessionDisconnectReason
{
    DeviceRemoval = 0,
    ServerShutdown = 1,
    FormatChanged = 2,
    SessionLogoff = 3,
    SessionDisconnected = 4,
    ExclusiveModeOverride = 5,
}

internal static class ComConstants
{
    public const int DeviceStateActive = 0x1;
    public const int ClsctxAll = 0x17;
    public const int ClsctxInprocServer = 0x1;
    public const int StgmRead = 0x0;
    public const int AudclntShareModeShared = 0;
    public const int PartTypeSubunit = 1;

    public const int SOk = 0;
    public const int SFalse = 1;

    /// <summary>Пристрій зник або змінився — об'єкти треба створити заново.</summary>
    public const int AudclntEDeviceInvalidated = unchecked((int)0x88890004);

    /// <summary>Звукова служба Windows не працює (перезапуск Audiosrv).</summary>
    public const int AudclntEServiceNotRunning = unchecked((int)0x88890010);

    public const int RpcEDisconnected = unchecked((int)0x80010108);
    public const int RpcSServerUnavailable = unchecked((int)0x800706BA);
    public const int ENotFound = unchecked((int)0x80070490);

    public static readonly Guid IidAudioEndpointVolume = new("5CDF2C82-841E-4546-9722-0CF74078229A");
    public static readonly Guid IidAudioMeterInformation = new("C02216F6-8C67-4B5B-9D00-D008E73E0064");
    public static readonly Guid IidAudioSessionManager2 = new("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");
    public static readonly Guid IidAudioClient = new("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
    public static readonly Guid IidDeviceTopology = new("2A07407E-6497-4A18-9787-32F79BD0D98F");
    public static readonly Guid IidAudioVolumeLevel = new("7FB7B48F-531D-44A2-BCB3-5AD5A134B3DC");

    /// <summary>KSNODETYPE_VOLUME: вузол гучності в топології драйвера (так виглядає й підсилення мікрофона).</summary>
    public static readonly Guid KsNodeTypeVolume = new("3A5ACC00-C557-11D0-8A2B-00A0C9255AC1");

    /// <summary>Зникла сама звукова служба (перезапуск Audiosrv): усі об'єкти треба створити заново.</summary>
    public static bool IsServiceGone(int hresult) =>
        hresult is AudclntEServiceNotRunning or RpcEDisconnected or RpcSServerUnavailable;

    /// <summary>Зник чи змінився один пристрій (висмикнули USB, змінили формат): досить перелічити пристрої знову.</summary>
    public static bool IsDeviceGone(int hresult) => hresult is AudclntEDeviceInvalidated or ENotFound;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PropertyKey(Guid formatId, int propertyId)
{
    public Guid FormatId = formatId;
    public int PropertyId = propertyId;

    public readonly bool Is(PropertyKey other) => FormatId == other.FormatId && PropertyId == other.PropertyId;
}

/// <summary>
/// PROPVARIANT: тип і 16 байтів значення. Розмір — 24 байти на x64 і 16 на x86, як у Windows.
/// Рядки й інші виділені значення звільняє <see cref="NativeMethods.PropVariantClear"/>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PropVariant
{
    public const ushort VtEmpty = 0;
    public const ushort VtBool = 11;
    public const ushort VtLpwstr = 31;

    public ushort ValueType;
    private ushort _reserved1;
    private ushort _reserved2;
    private ushort _reserved3;
    public IntPtr Value1;
    public IntPtr Value2;

    public readonly string? AsString() => ValueType == VtLpwstr && Value1 != IntPtr.Zero ? Marshal.PtrToStringUni(Value1) : null;

    public readonly bool? AsBool() => ValueType == VtBool ? unchecked((short)Value1.ToInt64()) != 0 : null;

    public static PropVariant FromBool(bool value) =>
        new() { ValueType = VtBool, Value1 = new IntPtr(value ? 0xFFFF : 0) }; // VARIANT_TRUE = -1 у 16 бітах
}

internal static class PropertyKeys
{
    /// <summary>PKEY_Device_FriendlyName: «Навушники (HyperX Cloud II)».</summary>
    public static readonly PropertyKey DeviceFriendlyName = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);

    /// <summary>PKEY_Device_DeviceDesc: «Навушники».</summary>
    public static readonly PropertyKey DeviceDescription = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 2);

    /// <summary>PKEY_DeviceClass_IconPath: "%windir%\system32\mmres.dll,-3010".</summary>
    public static readonly PropertyKey DeviceIconPath = new(new Guid("259abffc-50a7-47ce-af08-68c9a7d73366"), 12);

    /// <summary>«Прослуховувати цей пристрій» (VT_BOOL), ключ із сторінки «Прослуховування» властивостей мікрофона.</summary>
    public static readonly PropertyKey ListenEnabled = new(new Guid("24dbb0fc-9311-4b3d-9cf0-18ff155639d4"), 1);

    /// <summary>«Відтворювати через цей пристрій» (VT_LPWSTR, id пристрою виводу; порожньо — за замовчуванням).</summary>
    public static readonly PropertyKey ListenTarget = new(new Guid("24dbb0fc-9311-4b3d-9cf0-18ff155639d4"), 0);
}

[ComImport]
[Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal class MMDeviceEnumeratorComObject;

[ComImport]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    IMMDeviceCollection EnumAudioEndpoints(EDataFlow dataFlow, int stateMask);

    [PreserveSig]
    int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice endpoint);

    IMMDevice GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id);

    [PreserveSig]
    int RegisterEndpointNotificationCallback(IMMNotificationClient client);

    [PreserveSig]
    int UnregisterEndpointNotificationCallback(IMMNotificationClient client);
}

[ComImport]
[Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceCollection
{
    int GetCount();

    IMMDevice Item(int index);
}

[ComImport]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    [return: MarshalAs(UnmanagedType.IUnknown)]
    object Activate(ref Guid iid, int clsCtx, IntPtr activationParams);

    IPropertyStore OpenPropertyStore(int stgmAccess);

    [return: MarshalAs(UnmanagedType.LPWStr)]
    string GetId();

    int GetState();
}

[ComImport]
[Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    int GetCount();

    PropertyKey GetAt(int index);

    [PreserveSig]
    int GetValue(ref PropertyKey key, out PropVariant value);

    [PreserveSig]
    int SetValue(ref PropertyKey key, ref PropVariant value);

    [PreserveSig]
    int Commit();
}

[ComImport]
[Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMNotificationClient
{
    [PreserveSig]
    int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int newState);

    [PreserveSig]
    int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string deviceId);

    [PreserveSig]
    int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string deviceId);

    [PreserveSig]
    int OnDefaultDeviceChanged(EDataFlow flow, ERole role, [MarshalAs(UnmanagedType.LPWStr)] string? defaultDeviceId);

    [PreserveSig]
    int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, PropertyKey key);
}

[ComImport]
[Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioEndpointVolume
{
    [PreserveSig]
    int RegisterControlChangeNotify(IAudioEndpointVolumeCallback notify);

    [PreserveSig]
    int UnregisterControlChangeNotify(IAudioEndpointVolumeCallback notify);

    [PreserveSig]
    int GetChannelCount(out int channelCount);

    [PreserveSig]
    int SetMasterVolumeLevel(float levelDb, ref Guid eventContext);

    [PreserveSig]
    int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);

    [PreserveSig]
    int GetMasterVolumeLevel(out float levelDb);

    [PreserveSig]
    int GetMasterVolumeLevelScalar(out float level);

    [PreserveSig]
    int SetChannelVolumeLevel(int channel, float levelDb, ref Guid eventContext);

    [PreserveSig]
    int SetChannelVolumeLevelScalar(int channel, float level, ref Guid eventContext);

    [PreserveSig]
    int GetChannelVolumeLevel(int channel, out float levelDb);

    [PreserveSig]
    int GetChannelVolumeLevelScalar(int channel, out float level);

    [PreserveSig]
    int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);

    [PreserveSig]
    int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);

    [PreserveSig]
    int GetVolumeStepInfo(out int step, out int stepCount);

    [PreserveSig]
    int VolumeStepUp(ref Guid eventContext);

    [PreserveSig]
    int VolumeStepDown(ref Guid eventContext);

    [PreserveSig]
    int QueryHardwareSupport(out int hardwareSupportMask);

    [PreserveSig]
    int GetVolumeRange(out float minDb, out float maxDb, out float incrementDb);
}

[ComImport]
[Guid("657804FA-D6AD-4496-8A60-352752AF4F89")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioEndpointVolumeCallback
{
    /// <summary>notifyData — AUDIO_VOLUME_NOTIFICATION_DATA: GUID, BOOL bMuted, float fMasterVolume, …</summary>
    [PreserveSig]
    int OnNotify(IntPtr notifyData);
}

[ComImport]
[Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioMeterInformation
{
    [PreserveSig]
    int GetPeakValue(out float peak);

    [PreserveSig]
    int GetMeteringChannelCount(out int channelCount);

    [PreserveSig]
    int GetChannelsPeakValues(int channelCount, IntPtr peakValues);

    [PreserveSig]
    int QueryHardwareSupport(out int hardwareSupportMask);
}

[ComImport]
[Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionManager2
{
    // IAudioSessionManager
    [PreserveSig]
    int GetAudioSessionControl(IntPtr sessionGuid, int streamFlags, out IntPtr sessionControl);

    [PreserveSig]
    int GetSimpleAudioVolume(IntPtr sessionGuid, int streamFlags, out IntPtr audioVolume);

    // IAudioSessionManager2
    IAudioSessionEnumerator GetSessionEnumerator();

    [PreserveSig]
    int RegisterSessionNotification(IAudioSessionNotification notification);

    [PreserveSig]
    int UnregisterSessionNotification(IAudioSessionNotification notification);

    [PreserveSig]
    int RegisterDuckNotification([MarshalAs(UnmanagedType.LPWStr)] string sessionId, IntPtr duckNotification);

    [PreserveSig]
    int UnregisterDuckNotification(IntPtr duckNotification);
}

[ComImport]
[Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionEnumerator
{
    int GetCount();

    IAudioSessionControl GetSession(int index);
}

[ComImport]
[Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionControl
{
    [PreserveSig]
    int GetState(out AudioSessionState state);

    [PreserveSig]
    int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);

    [PreserveSig]
    int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, ref Guid eventContext);

    [PreserveSig]
    int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);

    [PreserveSig]
    int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid eventContext);

    [PreserveSig]
    int GetGroupingParam(out Guid groupingParam);

    [PreserveSig]
    int SetGroupingParam(ref Guid groupingParam, ref Guid eventContext);

    [PreserveSig]
    int RegisterAudioSessionNotification(IAudioSessionEvents client);

    [PreserveSig]
    int UnregisterAudioSessionNotification(IAudioSessionEvents client);
}

[ComImport]
[Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionControl2
{
    // IAudioSessionControl
    [PreserveSig]
    int GetState(out AudioSessionState state);

    [PreserveSig]
    int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);

    [PreserveSig]
    int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, ref Guid eventContext);

    [PreserveSig]
    int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);

    [PreserveSig]
    int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid eventContext);

    [PreserveSig]
    int GetGroupingParam(out Guid groupingParam);

    [PreserveSig]
    int SetGroupingParam(ref Guid groupingParam, ref Guid eventContext);

    [PreserveSig]
    int RegisterAudioSessionNotification(IAudioSessionEvents client);

    [PreserveSig]
    int UnregisterAudioSessionNotification(IAudioSessionEvents client);

    // IAudioSessionControl2
    [PreserveSig]
    int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);

    [PreserveSig]
    int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);

    /// <summary>AUDCLNT_S_NO_SINGLE_PROCESS (успіх) — сеанс кількох процесів; тоді це перший із них.</summary>
    [PreserveSig]
    int GetProcessId(out int processId);

    /// <summary>S_OK — системні звуки, S_FALSE — ні.</summary>
    [PreserveSig]
    int IsSystemSoundsSession();

    [PreserveSig]
    int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
}

[ComImport]
[Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISimpleAudioVolume
{
    [PreserveSig]
    int SetMasterVolume(float level, ref Guid eventContext);

    [PreserveSig]
    int GetMasterVolume(out float level);

    [PreserveSig]
    int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);

    [PreserveSig]
    int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
}

[ComImport]
[Guid("24918ACC-64B3-37C1-8CA9-74A66E9957A8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionEvents
{
    [PreserveSig]
    int OnDisplayNameChanged([MarshalAs(UnmanagedType.LPWStr)] string newDisplayName, ref Guid eventContext);

    [PreserveSig]
    int OnIconPathChanged([MarshalAs(UnmanagedType.LPWStr)] string newIconPath, ref Guid eventContext);

    [PreserveSig]
    int OnSimpleVolumeChanged(float newVolume, [MarshalAs(UnmanagedType.Bool)] bool newMute, ref Guid eventContext);

    [PreserveSig]
    int OnChannelVolumeChanged(int channelCount, IntPtr newChannelVolumes, int changedChannel, ref Guid eventContext);

    [PreserveSig]
    int OnGroupingParamChanged(ref Guid newGroupingParam, ref Guid eventContext);

    [PreserveSig]
    int OnStateChanged(AudioSessionState newState);

    [PreserveSig]
    int OnSessionDisconnected(AudioSessionDisconnectReason disconnectReason);
}

[ComImport]
[Guid("641DD20B-4D41-49CC-ABA3-174B9477BB08")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionNotification
{
    [PreserveSig]
    int OnSessionCreated(IAudioSessionControl newSession);
}

[ComImport]
[Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioClient
{
    [PreserveSig]
    int Initialize(int shareMode, int streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr audioSessionGuid);

    [PreserveSig]
    int GetBufferSize(out int bufferFrames);

    [PreserveSig]
    int GetStreamLatency(out long latency);

    [PreserveSig]
    int GetCurrentPadding(out int paddingFrames);

    [PreserveSig]
    int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closestMatch);

    [PreserveSig]
    int GetMixFormat(out IntPtr deviceFormat);

    [PreserveSig]
    int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);

    [PreserveSig]
    int Start();

    [PreserveSig]
    int Stop();

    [PreserveSig]
    int Reset();

    [PreserveSig]
    int SetEventHandle(IntPtr eventHandle);

    [PreserveSig]
    int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
}

[ComImport]
[Guid("2A07407E-6497-4A18-9787-32F79BD0D98F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDeviceTopology
{
    [PreserveSig]
    int GetConnectorCount(out int count);

    [PreserveSig]
    int GetConnector(int index, out IConnector connector);

    [PreserveSig]
    int GetSubunitCount(out int count);

    [PreserveSig]
    int GetSubunit(int index, out IntPtr subunit);

    [PreserveSig]
    int GetPartById(int id, out IPart part);

    [PreserveSig]
    int GetDeviceId([MarshalAs(UnmanagedType.LPWStr)] out string deviceId);

    [PreserveSig]
    int GetSignalPath(IPart partFrom, IPart partTo, [MarshalAs(UnmanagedType.Bool)] bool rejectMixedPaths, out IPartsList parts);
}

[ComImport]
[Guid("9c2c4058-23f5-41de-877a-df3af236a09e")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IConnector
{
    [PreserveSig]
    int GetConnectorType(out int type);

    [PreserveSig]
    int GetDataFlow(out int flow);

    [PreserveSig]
    int ConnectTo(IConnector connectTo);

    [PreserveSig]
    int Disconnect();

    [PreserveSig]
    int IsConnected([MarshalAs(UnmanagedType.Bool)] out bool connected);

    [PreserveSig]
    int GetConnectedTo(out IConnector connectedTo);

    [PreserveSig]
    int GetConnectorIdConnectedTo([MarshalAs(UnmanagedType.LPWStr)] out string connectorId);

    [PreserveSig]
    int GetDeviceIdConnectedTo([MarshalAs(UnmanagedType.LPWStr)] out string deviceId);
}

[ComImport]
[Guid("AE2DE0E4-5BCA-4F2D-AA46-5D13F8FDB3A9")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPart
{
    [PreserveSig]
    int GetName([MarshalAs(UnmanagedType.LPWStr)] out string name);

    [PreserveSig]
    int GetLocalId(out int id);

    [PreserveSig]
    int GetGlobalId([MarshalAs(UnmanagedType.LPWStr)] out string globalId);

    [PreserveSig]
    int GetPartType(out int partType);

    [PreserveSig]
    int GetSubType(out Guid subType);

    [PreserveSig]
    int GetControlInterfaceCount(out int count);

    [PreserveSig]
    int GetControlInterface(int index, out IntPtr controlInterface);

    [PreserveSig]
    int EnumPartsIncoming(out IPartsList parts);

    [PreserveSig]
    int EnumPartsOutgoing(out IPartsList parts);

    [PreserveSig]
    int GetTopologyObject(out IDeviceTopology topology);

    [PreserveSig]
    int Activate(int clsContext, ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object controlInterface);

    [PreserveSig]
    int RegisterControlChangeCallback(ref Guid iid, IntPtr notify);

    [PreserveSig]
    int UnregisterControlChangeCallback(IntPtr notify);
}

[ComImport]
[Guid("6DAA848C-5EB0-45CC-AEA5-998A2CDA1FFB")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPartsList
{
    [PreserveSig]
    int GetCount(out int count);

    [PreserveSig]
    int GetPart(int index, out IPart part);
}

/// <summary>IAudioVolumeLevel = IPerChannelDbLevel без власних методів.</summary>
[ComImport]
[Guid("7FB7B48F-531D-44A2-BCB3-5AD5A134B3DC")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioVolumeLevel
{
    [PreserveSig]
    int GetChannelCount(out int channels);

    [PreserveSig]
    int GetLevelRange(int channel, out float minLevelDb, out float maxLevelDb, out float stepping);

    [PreserveSig]
    int GetLevel(int channel, out float levelDb);

    [PreserveSig]
    int SetLevel(int channel, float levelDb, ref Guid eventContext);

    [PreserveSig]
    int SetLevelUniform(float levelDb, ref Guid eventContext);

    [PreserveSig]
    int SetLevelAllChannels(IntPtr levelsDb, int channels, ref Guid eventContext);
}

internal static unsafe partial class NativeMethods
{
    public const int CoinitMultithreaded = 0x0;
    public const uint ProcessQueryLimitedInformation = 0x1000;

    [LibraryImport("ole32.dll")]
    public static partial int CoInitializeEx(IntPtr reserved, int coInit);

    [LibraryImport("ole32.dll")]
    public static partial void CoUninitialize();

    [LibraryImport("ole32.dll")]
    public static partial int CoCreateInstance(in Guid clsid, IntPtr outer, int clsContext, in Guid iid, out IntPtr instance);

    [LibraryImport("ole32.dll")]
    public static partial int PropVariantClear(ref PropVariant value);

    [LibraryImport("ole32.dll")]
    public static partial void CoTaskMemFree(IntPtr memory);

    [LibraryImport("combase.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int WindowsCreateString(string source, int length, out IntPtr hstring);

    [LibraryImport("combase.dll")]
    public static partial int WindowsDeleteString(IntPtr hstring);

    [LibraryImport("combase.dll")]
    public static partial IntPtr WindowsGetStringRawBuffer(IntPtr hstring, out int length);

    [LibraryImport("combase.dll")]
    public static partial int RoGetActivationFactory(IntPtr activatableClassId, in Guid iid, out IntPtr factory);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial IntPtr OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(IntPtr handle);

    [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool QueryFullProcessImageName(IntPtr process, int flags, char* exeName, ref int size);

    [LibraryImport("shlwapi.dll", EntryPoint = "SHLoadIndirectString", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int SHLoadIndirectString(string source, char* output, int outputSize, IntPtr reserved);
}
