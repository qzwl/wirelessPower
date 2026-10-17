namespace BatteryTray.Hid.Protocols;

internal interface IProtocolAdapter
{
    string Name { get; }
    int Priority { get; }
    bool Matches(DeviceGroup group);
    PeripheralReading? TryRead(DeviceGroup group);
}
