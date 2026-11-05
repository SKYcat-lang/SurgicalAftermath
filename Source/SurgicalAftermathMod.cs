using HarmonyLib;
using UnityEngine;
using Verse;

namespace SurgicalAftermath;

public class SurgicalAftermathMod : Mod
{
    public static SurgicalAftermathSettings Settings { get; private set; }

    public SurgicalAftermathMod(ModContentPack content) : base(content)
    {
        Settings = GetSettings<SurgicalAftermathSettings>();
        var harmony = new Harmony("gahee.surgicalaftermath");
        harmony.PatchAll();
        Log.Message("[SurgicalAftermath] 수술 후유증 모드가 로드되었습니다.");
    }

    public override string SettingsCategory()
    {
        return "SA_SettingsCategory".Translate();
    }

    public override void DoSettingsWindowContents(Rect inRect)
    {
        Settings.DoWindowContents(inRect);
    }
}
