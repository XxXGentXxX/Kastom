using Kastom.Sdk;

namespace Kastom.Core.Tests.Runtime;

/// <summary>
/// Версія збірки Kastom.Sdk заморожена на весь контракт 1. Модуль посилається на SDK саме цієї версії, а .NET не
/// підставить збірку старішої версії: коли версія SDK ішла за лаунчером, «Звук» 1.2.0 (SDK 0.1.3) не завантажився
/// в лаунчері 0.1.2. Міняти лише разом з <see cref="KastomSdk.ContractVersion"/>.
/// </summary>
public sealed class SdkVersionTests
{
    [Fact]
    public void SdkAssemblyVersionIsFrozenForContractOne()
    {
        Assert.Equal(1, KastomSdk.ContractVersion);
        Assert.Equal(new Version(0, 1, 3, 0), typeof(IKastomModule).Assembly.GetName().Version);
    }
}
