namespace CursorMotion;

// NVIDIA recording has reproducibly crashed with unrestricted native frames.
// The compact contract is a device requirement, not a profile's opt-out switch.
internal static class NativeCursorLimits
{
    internal static bool RequiresCompact(uint vendorId) => vendorId == 0x10DE;
    internal static bool UseCompact(bool requested, uint vendorId) => requested || RequiresCompact(vendorId);
}
