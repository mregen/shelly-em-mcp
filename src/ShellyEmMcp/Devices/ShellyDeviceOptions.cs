// SPDX-License-Identifier: MIT

namespace ShellyEmMcp.Devices;

/// <summary>
/// One configured Shelly device: a friendly name and its LAN host/IP. Password is only relevant if
/// the device has local admin auth (Shelly Gen2 digest auth) enabled - uncommon for LAN-only setups
/// and not currently used by <see cref="ShellyRpcClient"/>; the field is reserved for when digest
/// auth support is added and can be verified against a password-protected device.
/// </summary>
public sealed record ShellyDeviceOptions(string Name, string Host, string? Password = null);
