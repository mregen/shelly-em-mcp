// SPDX-License-Identifier: MIT

namespace ShellyEmMcp.Devices;

internal static class ShellyDeviceLookup
{
    public static ShellyDeviceOptions? Find(IReadOnlyList<ShellyDeviceOptions> devices, string name) =>
        devices.FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));
}
