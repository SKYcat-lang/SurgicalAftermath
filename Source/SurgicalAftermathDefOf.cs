using RimWorld;
using Verse;

namespace SurgicalAftermath;

[DefOf]
public static class SurgicalAftermathDefOf
{
    public static HediffDef SA_SurgicalWound;
    public static HediffDef SA_PostSurgicalAftereffect;

    static SurgicalAftermathDefOf()
    {
        DefOfHelper.EnsureInitializedInCtor(typeof(SurgicalAftermathDefOf));
    }
}
