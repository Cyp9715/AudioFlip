namespace AudioFlip.Tests;

using System.Reflection;
using System.Runtime.InteropServices;
using AudioFlip.Interop;

public sealed class InteropTests
{
    [Theory]
    [InlineData(typeof(IMMDevice))]
    [InlineData(typeof(IMMDeviceCollection))]
    [InlineData(typeof(IMMDeviceEnumerator))]
    [InlineData(typeof(IPropertyStore))]
    [InlineData(typeof(IPolicyConfig))]
    public void HResultMethodsPreserveTheirNativeSignatures(Type type)
    {
        Assert.True(type.IsImport);
        Assert.All(type.GetMethods(), method =>
            Assert.True(method.GetMethodImplementationFlags().HasFlag(MethodImplAttributes.PreserveSig), method.Name));
    }

    [Fact]
    public void PropVariantHasEnoughSpaceForTheNativeUnion()
    {
        Assert.Equal(IntPtr.Size == 8 ? 24 : 16, Marshal.SizeOf<PropVariant>());
    }
}
