namespace CursorMotion;

// NVIDIA recording has reproducibly crashed with unrestricted native frames.
// The compact contract is a device requirement, not a profile's opt-out switch.
internal static class NativeCursorLimits
{
    // Smaller diagnostic frames discarded the user's accepted whole-cursor
    // motion. Keep full effects inside the bounded 256px native publication.
    internal const int NativeSide = 256;
    internal static bool RequiresCompact(uint vendorId) => vendorId == 0x10DE;
    internal static bool UseCompact(bool requested, uint vendorId) => requested || RequiresCompact(vendorId);
}
