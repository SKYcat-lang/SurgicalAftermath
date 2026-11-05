using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SurgicalAftermath;

public static class SurgeryAftermathUtility
{
    private static readonly HashSet<string> LowTierMedievalPartDefNames = new HashSet<string>
    {
        "PegLeg",
        "WoodenHand",
        "WoodenFoot",
        "Denture"
    };

    /// <summary>
    /// 환자의 영구 부상(흉터) 총 심각도(Severity) 합계를 계산합니다.
    /// </summary>
    public static float GetTotalPermanentInjurySeverity(Pawn pawn)
    {
        if (pawn?.health?.hediffSet?.hediffs == null)
        {
            return 0f;
        }

        float total = 0f;
        List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
        for (int i = 0; i < hediffs.Count; i++)
        {
            Hediff h = hediffs[i];
            if (h is Hediff_Injury && h.IsPermanent())
            {
                total += h.Severity;
            }
        }
        return total;
    }

    /// <summary>
    /// 레시피의 이름이나 워커 클래스가 흉터 제거 의도의 수술인지 확인합니다.
    /// </summary>
    public static bool IsScarRemovalRecipeByName(RecipeDef recipe)
    {
        if (recipe == null)
        {
            return false;
        }

        if (!recipe.defName.NullOrEmpty() &&
            recipe.defName.IndexOf("Scar", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        string workerName = recipe.workerClass?.Name;
        if (!workerName.NullOrEmpty() &&
            workerName.IndexOf("Scar", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        if (recipe.removesHediff != null &&
            recipe.removesHediff.defName.IndexOf("Scar", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// 해당 레시피가 수술 창상/후유증을 남기는 외과 수술인지 판별합니다.
    /// </summary>
    public static bool IsEligibleSurgery(RecipeDef recipe, Pawn patient, BodyPartRecord part, bool checkSurgeryFailCalled)
    {
        if (recipe?.Worker is not Recipe_Surgery)
        {
            return false;
        }

        if (patient == null || patient.Dead || !patient.RaceProps.IsFlesh)
        {
            return false;
        }

        // 안락사나 이미 자체적으로 SurgicalCut을 가하는 외과적 검사는 제외
        if (recipe.Worker is Recipe_ExecuteByCut || recipe.Worker is Recipe_SurgicalInspection)
        {
            return false;
        }

        // 바닐라 최하급 목제 보철물(의족, 의수, 나무 발, 틀니) 장착은 침습적 수술로 보지 않아 제외
        if (IsLowTierProstheticRecipe(recipe))
        {
            return false;
        }

        // CheckSurgeryFail을 거치지 않는 비침습 처치(마취, 수혈, 헤모겐 추출 등)는 기본적으로 제외하되,
        // 부위를 직접 지정하는 수술(part != null)은 포함
        if (!checkSurgeryFailCalled && part == null)
        {
            return false;
        }

        // 신체 부위를 타겟으로 하지 않는 전신 약물/제노점 이식 등 제외
        if (part == null && !recipe.targetsBodyPart)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// 목발/나무 의수/나무 발/틀니 등 최하급 보철물 장착 수술인지 확인합니다.
    /// </summary>
    public static bool IsLowTierProstheticRecipe(RecipeDef recipe)
    {
        if (recipe?.addsHediff == null)
        {
            return false;
        }

        HediffDef hediff = recipe.addsHediff;

        // 1. 바닐라 최하급 보철물 명시적 체크
        if (LowTierMedievalPartDefNames.Contains(hediff.defName))
        {
            return true;
        }

        // 2. 범용 판별: 인공 신체 부위(Hediff_AddedPart)이면서 가치 영향(priceImpact)이 없거나 통나무(WoodLog)만 재료로 쓰는 경우
        if (typeof(Hediff_AddedPart).IsAssignableFrom(hediff.hediffClass))
        {
            if (!hediff.priceImpact)
            {
                return true;
            }
            if (hediff.spawnThingOnRemoved == ThingDefOf.WoodLog)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 장기 이식, 내부 장기 적출 등 지속적인 사후 치료가 필요한 '대수술'인지 판별합니다.
    /// </summary>
    public static bool IsMajorSurgery(RecipeDef recipe, Pawn patient, BodyPartRecord originalPart)
    {
        if (recipe == null)
        {
            return false;
        }

        // 1. 자연 장기/신체 이식 수술
        if (recipe.Worker is Recipe_InstallNaturalBodyPart)
        {
            return true;
        }

        // 2. 내부 심부 장기(Heart, Lung, Kidney, Liver, Stomach, Brain, Spine 등)에 대한 모든 수술 (적출, 인공장기 이식, 질병 절제 등)
        if (originalPart != null && originalPart.depth == BodyPartDepth.Inside)
        {
            return true;
        }

        // 3. 주요 사지(팔, 다리, 어깨 등) 전체를 교체하거나 절단하는 큰 수술 (손가락/발가락 등 소부위 제외)
        if (originalPart != null && originalPart.def.GetMaxHealth(patient) >= 20f &&
            (recipe.Worker is Recipe_RemoveBodyPart || recipe.Worker is Recipe_InstallArtificialBodyPart))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// 수술 창상(Hediff_Injury)이 실제로 부여될 유효한 외부/상위 신체 부위를 찾습니다.
    /// 결손된 부위나 인공보철물로 대체된 부위, 혹은 내부 장기(Inside)인 경우 상위 자연 부위(예: 몸통)로 이동합니다.
    /// </summary>
    public static BodyPartRecord ResolveWoundTargetPart(Pawn patient, BodyPartRecord operatedPart)
    {
        BodyPartRecord current = operatedPart ?? patient.RaceProps.body.corePart;

        while (current != null)
        {
            bool isMissing = patient.health.hediffSet.PartIsMissing(current);
            bool hasArtificialPart = patient.health.hediffSet.PartOrAnyAncestorHasDirectlyAddedParts(current);
            bool isInside = current.depth == BodyPartDepth.Inside;

            if (!isMissing && !hasArtificialPart && !isInside)
            {
                return current;
            }

            current = current.parent;
        }

        // 최상위(Torso 등)까지 올라갔는데도 못 찾은 경우 corePart 반환 (결손되지 않았다면)
        BodyPartRecord core = patient.RaceProps.body.corePart;
        if (core != null && !patient.health.hediffSet.PartIsMissing(core))
        {
            return core;
        }

        return null;
    }

    /// <summary>
    /// 대수술 후유증(상태이상)이 부여될 부위를 찾습니다.
    /// </summary>
    public static BodyPartRecord ResolveAftereffectTargetPart(Pawn patient, BodyPartRecord operatedPart)
    {
        BodyPartRecord current = operatedPart ?? patient.RaceProps.body.corePart;

        while (current != null)
        {
            if (!patient.health.hediffSet.PartIsMissing(current))
            {
                return current;
            }
            current = current.parent;
        }

        return patient.RaceProps.body.corePart;
    }

    /// <summary>
    /// 수술 완료 후 수술 창상 및 대수술 후유증을 적용합니다.
    /// </summary>
    public static void ApplySurgicalAftermath(RecipeDef recipe, Pawn surgeon, Pawn patient, BodyPartRecord operatedPart, bool surgeryFailed, bool preventScar)
    {
        if (patient == null || patient.Dead)
        {
            return;
        }

        int medSkill = surgeon?.skills?.GetSkill(SkillDefOf.Medicine)?.Level ?? 8;

        // 1. 수술 창상 (SA_SurgicalWound) 적용
        BodyPartRecord woundPart = ResolveWoundTargetPart(patient, operatedPart);
        if (woundPart != null && SurgicalAftermathDefOf.SA_SurgicalWound != null)
        {
            float partHealth = patient.health.hediffSet.GetPartHealth(woundPart);
            if (partHealth > 2f)
            {
                // 기본 창상 데미지: 6~10 사이에서 집도의 의학 스킬에 따라 경감 (최소 5 이상 유지하여 흉터 임계치 4를 넘김)
                float baseSeverity = Rand.Range(6.5f, 10.5f) - (medSkill * 0.12f);
                if (surgeryFailed)
                {
                    baseSeverity += 3.0f;
                }

                // 부위가 파괴되지 않도록 최대 체력 제한
                float maxSafeSeverity = Mathf.Max(1f, partHealth - 2f);
                float finalSeverity = Mathf.Clamp(baseSeverity, 5.0f, Mathf.Min(12.0f, maxSafeSeverity));

                if (finalSeverity >= 1f)
                {
                    Hediff_Injury wound = (Hediff_Injury)HediffMaker.MakeHediff(SurgicalAftermathDefOf.SA_SurgicalWound, patient, woundPart);
                    wound.Severity = finalSeverity;

                    HediffComp_GetsPermanent permComp = wound.TryGetComp<HediffComp_GetsPermanent>();
                    if (permComp != null)
                    {
                        if (preventScar)
                        {
                            // 흉터 제거 의도의 수술 보호가 작동한 경우: 수술 창상은 남되 영구 흉터가 될 확률을 0%로 강제 고정
                            permComp.IsPermanent = false;
                            permComp.permanentDamageThreshold = 9999f;
                        }
                        else
                        {
                            // 바닐라 흉터 판정(PreFinalizeInjury) 호출 + 성공한 수술에도 높은 확률로 수술 흉터가 남도록 보정
                            permComp.PreFinalizeInjury();

                            float extraScarChance = surgeryFailed ? 0.75f : Mathf.Clamp(0.65f - (medSkill * 0.015f), 0.25f, 0.65f);
                            if (!permComp.IsPermanent && permComp.permanentDamageThreshold >= 9999f && Rand.Chance(extraScarChance))
                            {
                                if (woundPart.def.delicate)
                                {
                                    permComp.IsPermanent = true;
                                }
                                else
                                {
                                    permComp.permanentDamageThreshold = Rand.Range(1.5f, Mathf.Max(2.0f, wound.Severity * 0.5f));
                                }
                            }
                        }
                    }

                    patient.health.AddHediff(wound, woundPart);
                }
            }
        }

        // 2. 대수술(장기이식, 심부 수술 등)인 경우 지속 치료가 필요한 '수술 후유증' 상태이상 부여
        if (IsMajorSurgery(recipe, patient, operatedPart) && SurgicalAftermathDefOf.SA_PostSurgicalAftereffect != null)
        {
            BodyPartRecord effectPart = ResolveAftereffectTargetPart(patient, operatedPart);

            // 이미 같은 부위에 수술 후유증이 있다면 심각도만 보강하고, 없다면 새로 추가
            Hediff existing = patient.health.hediffSet.hediffs
                .FirstOrDefault(h => h.def == SurgicalAftermathDefOf.SA_PostSurgicalAftereffect && h.Part == effectPart);

            float initialSeverity = surgeryFailed
                ? Rand.Range(0.75f, 0.90f)
                : Mathf.Clamp(Rand.Range(0.55f, 0.75f) - (medSkill * 0.008f), 0.40f, 0.75f);

            if (existing != null)
            {
                existing.Severity = Mathf.Min(1.0f, Mathf.Max(existing.Severity, initialSeverity) + 0.15f);
            }
            else
            {
                Hediff aftereffect = HediffMaker.MakeHediff(SurgicalAftermathDefOf.SA_PostSurgicalAftereffect, patient, effectPart);
                aftereffect.Severity = initialSeverity;
                patient.health.AddHediff(aftereffect, effectPart);
            }
        }
    }
}

/// <summary>
/// 모든 수술의 실행 진입점(Bill_Medical.Notify_IterationCompleted)과 실패 판정(Recipe_Surgery.CheckSurgeryFail)을 후킹합니다.
/// </summary>
[HarmonyPatch]
public static class Patch_SurgeryAftermath
{
    private struct SurgeryContext
    {
        public bool Active;
        public bool CheckSurgeryFailCalled;
        public bool SurgeryFailed;
        public BodyPartRecord TargetPart;
        public float PermanentInjurySeverityBefore;
    }

    private static SurgeryContext currentContext;

    [HarmonyPatch(typeof(Bill_Medical), nameof(Bill_Medical.Notify_IterationCompleted))]
    [HarmonyPrefix]
    public static void Notify_IterationCompleted_Prefix(Bill_Medical __instance)
    {
        bool completable = __instance.CompletableEver;
        Pawn patient = completable ? __instance.GiverPawn : null;

        currentContext = new SurgeryContext
        {
            Active = completable,
            CheckSurgeryFailCalled = false,
            SurgeryFailed = false,
            TargetPart = __instance.Part,
            PermanentInjurySeverityBefore = patient != null ? SurgeryAftermathUtility.GetTotalPermanentInjurySeverity(patient) : 0f
        };
    }

    [HarmonyPatch(typeof(Recipe_Surgery), "CheckSurgeryFail")]
    [HarmonyPostfix]
    public static void CheckSurgeryFail_Postfix(bool __result, BodyPartRecord part)
    {
        if (currentContext.Active)
        {
            currentContext.CheckSurgeryFailCalled = true;
            currentContext.SurgeryFailed = __result;
            if (part != null && currentContext.TargetPart == null)
            {
                currentContext.TargetPart = part;
            }
        }
    }

    [HarmonyPatch(typeof(Bill_Medical), nameof(Bill_Medical.Notify_IterationCompleted))]
    [HarmonyPostfix]
    public static void Notify_IterationCompleted_Postfix(Bill_Medical __instance, Pawn billDoer)
    {
        if (!currentContext.Active)
        {
            return;
        }

        SurgeryContext ctx = currentContext;
        currentContext = default;

        Pawn patient = __instance.GiverPawn;
        RecipeDef recipe = __instance.recipe;
        BodyPartRecord part = ctx.TargetPart ?? __instance.Part;

        if (SurgeryAftermathUtility.IsEligibleSurgery(recipe, patient, part, ctx.CheckSurgeryFailCalled))
        {
            bool protectEnabled = SurgicalAftermathMod.Settings == null || SurgicalAftermathMod.Settings.protectScarRemovalSurgery;
            bool preventScar = false;

            if (protectEnabled)
            {
                float permanentSeverityAfter = SurgeryAftermathUtility.GetTotalPermanentInjurySeverity(patient);
                bool permanentInjuriesReduced = permanentSeverityAfter < ctx.PermanentInjurySeverityBefore - 0.01f;

                if (permanentInjuriesReduced || SurgeryAftermathUtility.IsScarRemovalRecipeByName(recipe))
                {
                    preventScar = true;
                }
            }

            SurgeryAftermathUtility.ApplySurgicalAftermath(recipe, billDoer, patient, part, ctx.SurgeryFailed, preventScar);
        }
    }
}
