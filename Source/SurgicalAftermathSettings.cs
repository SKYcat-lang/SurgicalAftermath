using UnityEngine;
using Verse;

namespace SurgicalAftermath;

public class SurgicalAftermathSettings : ModSettings
{
    public bool protectScarRemovalSurgery = true;

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref protectScarRemovalSurgery, "protectScarRemovalSurgery", defaultValue: true);
    }

    public void DoWindowContents(Rect inRect)
    {
        Listing_Standard listing = new Listing_Standard();
        listing.Begin(inRect);

        listing.CheckboxLabeled(
            "SA_Setting_ProtectScarRemoval_Label".Translate(),
            ref protectScarRemovalSurgery,
            "SA_Setting_ProtectScarRemoval_Desc".Translate());

        listing.End();
    }
}
