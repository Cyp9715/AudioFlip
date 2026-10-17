namespace AudioFlip.Tests;

public sealed class DeviceReferenceTests
{
    [Fact]
    public void ChangedEndpointIdResolvesByUniqueName()
    {
        var reference = DeviceReference.Encode("A", "헤드셋 | USB");
        var device = new AudioDevice("B", "헤드셋 | USB");
        Assert.True(DeviceReference.TryResolveId(reference, [device], out var id));
        Assert.Equal(device.Id, id);
        Assert.True(DeviceReference.IsMatch(reference, device, [device]));
    }

    [Fact]
    public void SurvivingEndpointWinsOverAnotherDeviceWithTheOldName()
    {
        var reference = DeviceReference.Encode("A", "Headset");
        AudioDevice[] devices = [new("A", "Renamed headset"), new("B", "Headset")];
        Assert.True(DeviceReference.TryResolveId(reference, devices, out var id));
        Assert.Equal("A", id);
    }

    [Fact]
    public void AmbiguousNamesAreNotSelectedAfterIdChanges()
    {
        var reference = DeviceReference.Encode("A", "Headset");
        AudioDevice[] devices = [new("B", "Headset"), new("C", "Headset")];
        Assert.False(DeviceReference.TryResolveId(reference, devices, out _));
        Assert.All(devices, device => Assert.False(DeviceReference.IsMatch(reference, device, devices)));
    }

    [Fact]
    public void LegacyRawIdsStillResolve()
    {
        Assert.True(DeviceReference.TryResolveId("A", [new("A", "Headset")], out var id));
        Assert.Equal("A", id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ref|Headset|invalid")]
    [InlineData("ref|Headset")]
    public void MalformedReferencesDoNotResolve(string reference)
    {
        Assert.False(DeviceReference.TryResolveId(reference, [new("A", "Headset")], out _));
    }

    [Fact]
    public void EditingSelectionRetainsMissingDevicesAndOriginalCycleOrder()
    {
        var missing = DeviceReference.Encode("C", "Disconnected");
        var oldReference = DeviceReference.Encode("A", "Headset");
        AudioDevice[] available = [new("B", "Headset"), new("D", "Speakers")];
        var selection = DeviceReference.UpdateSelection([missing, oldReference], available, available);
        Assert.Equal(new[] { missing, DeviceReference.Encode("B", "Headset"), DeviceReference.Encode("D", "Speakers") }, selection);
        Assert.Equal(new[] { missing }, DeviceReference.UpdateSelection(selection, available, []));
    }
}
