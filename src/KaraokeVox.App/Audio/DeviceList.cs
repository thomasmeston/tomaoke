using NAudio.CoreAudioApi;

namespace KaraokeVox.App.Audio;

public static class DeviceList
{
    public static (IReadOnlyList<DeviceChoice> Inputs, IReadOnlyList<DeviceChoice> Outputs, string? DefaultInputId, string? DefaultOutputId) Query()
    {
        using var enumerator = new MMDeviceEnumerator();
        return (
            List(enumerator, DataFlow.Capture),
            List(enumerator, DataFlow.Render),
            TryDefault(enumerator, DataFlow.Capture, Role.Console),
            TryDefault(enumerator, DataFlow.Render, Role.Multimedia));
    }

    static IReadOnlyList<DeviceChoice> List(MMDeviceEnumerator enumerator, DataFlow flow)
    {
        var devices = new List<DeviceChoice>();
        foreach (MMDevice device in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
        {
            using (device)
            {
                string name = device.FriendlyName;
                bool bluetooth = name.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase)
                    || device.ID.Contains("BTHENUM", StringComparison.OrdinalIgnoreCase);
                bool handsFree = name.Contains("Hands-Free", StringComparison.OrdinalIgnoreCase);
                devices.Add(new DeviceChoice(device.ID, name, bluetooth, handsFree));
            }
        }
        return devices;
    }

    static string? TryDefault(MMDeviceEnumerator enumerator, DataFlow flow, Role role)
    {
        try
        {
            using MMDevice device = enumerator.GetDefaultAudioEndpoint(flow, role);
            return device.ID;
        }
        catch
        {
            return null;
        }
    }
}
