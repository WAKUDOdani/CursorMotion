namespace CursorMotion;

// NVIDIA recording has reproducibly crashed with unrestricted native frames.
// The compact contract is a device requirement, not a profile's opt-out switch.
internal static class NativeCursorLimits
{
    // 128px is the locally confirmed NVIDIA recording transport. A smaller
    // visible glyph inside a 256px bitmap does not satisfy this contract.
    internal const int NativeSide = 256;
    internal const int NvidiaSide = 128;
    internal const int NvidiaGlyphSide = 80;
    internal static int Side(uint vendorId) => RequiresCompact(vendorId) ? NvidiaSide : NativeSide;
    internal static bool RequiresCompact(uint vendorId) => vendorId == 0x10DE;
    internal static bool UseCompact(bool requested, uint vendorId) => requested || RequiresCompact(vendorId);
}
