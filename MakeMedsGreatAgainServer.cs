using System.Collections.Generic;
using System.Linq;
using SPTarkov.Server.Core.Models.Enums;
using System.Reflection;
using System.Threading.Tasks;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Utils;
using Range = SemanticVersioning.Range;

namespace MakeMedsGreatAgain;

public record ModMetadata : AbstractModMetadata
{
    public override string ModGuid { get; init; } = "com.vinihns.makeMedsGreatAgain";
    public override string Name { get; init; } = "Make Meds Great Again";
    public override string Author { get; init; } = "ViniHNS";
    public override List<string>? Contributors { get; init; }
    public override SemanticVersioning.Version Version { get; init; } = new("1.2.5");
    public override SemanticVersioning.Range SptVersion { get; init; } = new("~4.0.0");


    public override List<string>? Incompatibilities { get; init; }
    public override Dictionary<string, Range>? ModDependencies { get; init; } = new()
    {
        { "com.wtt.commonlib", new Range("2.0.15") }

    };
    public override string? Url { get; init; } = "https://github.com/viniHNS/Make-Meds-Great-Again-Client";
    public override bool? IsBundleMod { get; init; } = false;
    public override string License { get; init; } = "MIT";
}

[Injectable(TypePriority = OnLoadOrder.PostDBModLoader + 1)]
public class MakeMedsGreatAgain(
    ISptLogger<MakeMedsGreatAgain> logger,
    ModHelper modHelper, DatabaseService databaseService,
    WTTServerCommonLib.WTTServerCommonLib wttCommon)
    : IOnLoad
{
    public async Task OnLoad()
    {
        var assembly = Assembly.GetExecutingAssembly();

        await wttCommon.CustomItemServiceExtended.CreateCustomItems(assembly);

        var pathToMod = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
        var config = modHelper.GetJsonDataFromFile<ModConfig>(pathToMod, "config/config.json");

        if (config == null)
        {
            logger.Error("Failed to load MakeMedsGreatAgain config!");
            return;
        }

        if (config.debug)
        {
            logger.Info("MakeMedsGreatAgain: Config loaded successfully. Debug mode enabled.");
        }

        var globals = databaseService.GetTables();

        if (globals == null)
        {
            logger.Error("Critical error: Database tables are null. Cannot proceed.");
            return;
        }

        void UpdateItemHP(string itemId, int hp)
        {
            if (globals.Templates.Items.TryGetValue(itemId, out var item) && item != null)
            {
                item.Properties.MaxHpResource = hp;
                if (config.debug) logger.Info($"MakeMedsGreatAgain: Updated HP for {itemId} to {hp}");
            }
            else if (config.debug)
            {
                logger.Warning($"MakeMedsGreatAgain: Could not find item {itemId} to update HP");
            }
        }

        void UpdateBleedingFractureEffects(string itemId, bool canHealHeavyBleeding, int heavyBleedingCost, bool canHealFracture, int fractureCost, bool canHealLightBleeding, int lightBleedingCost)
        {
            if (globals.Templates.Items.TryGetValue(itemId, out var item) && item != null)
            {
                // Ensure the item has EffectsDamage dictionary
                if (item.Properties.EffectsDamage == null)
                {
                    item.Properties.EffectsDamage = new Dictionary<DamageEffectType, EffectsDamageProperties>();
                }

                // Helper to add or remove effect
                void UpdateEffect(DamageEffectType type, bool enabled, int cost, int delay = 0, int duration = 0, int fadeOut = 0)
                {
                    // Double check Properties just in case
                    if (item.Properties.EffectsDamage == null) return;

                    if (enabled)
                    {
                        var effect = new EffectsDamageProperties
                        {
                            Cost = cost,
                            Delay = delay,
                            Duration = duration,
                            FadeOut = fadeOut
                        };

                        if (item.Properties.EffectsDamage.ContainsKey(type))
                        {
                            item.Properties.EffectsDamage[type] = effect;
                        }
                        else
                        {
                            item.Properties.EffectsDamage.Add(type, effect);
                        }
                        if (config.debug) logger.Info($"MakeMedsGreatAgain: Enabled effect {type} for {itemId} with cost {cost}");
                    }
                    else
                    {
                        if (item.Properties.EffectsDamage.ContainsKey(type))
                        {
                            item.Properties.EffectsDamage.Remove(type);
                            if (config.debug) logger.Info($"MakeMedsGreatAgain: Removed effect {type} from {itemId}");
                        }
                    }
                }

                // Heavy Bleeding
                UpdateEffect(DamageEffectType.HeavyBleeding, canHealHeavyBleeding, heavyBleedingCost);

                // Light Bleeding
                UpdateEffect(DamageEffectType.LightBleeding, canHealLightBleeding, lightBleedingCost);

                // Fracture
                UpdateEffect(DamageEffectType.Fracture, canHealFracture, fractureCost);

                // Contusion 
                UpdateEffect(DamageEffectType.Contusion, true, 0);

                // RadExposure
                UpdateEffect(DamageEffectType.RadExposure, true, 0);
            }
            else if (config.debug)
            {
                logger.Warning($"MakeMedsGreatAgain: Could not find item {itemId} to update effects");
            }
        }

        void UpdateSurgeryEffect(string itemId, bool enabled, int surgeryCost)
        {
            if (globals.Templates.Items.TryGetValue(itemId, out var item) && item != null)
            {
                if (enabled)
                {
                    // Ensure the item has EffectsDamage dictionary
                    if (item.Properties.EffectsDamage == null)
                    {
                        item.Properties.EffectsDamage = new Dictionary<DamageEffectType, EffectsDamageProperties>();
                    }

                    // Define the DestroyedPart effect structure
                    var destroyedPartEffect = new EffectsDamageProperties
                    {
                        Delay = 0,
                        Duration = (int)((dynamic)item.Properties).MedUseTime,
                        FadeOut = 0,
                        HealthPenaltyMax = 45,
                        HealthPenaltyMin = 25
                    };

                    // Add or update the "DestroyedPart" effect
                    if (item.Properties.EffectsDamage.ContainsKey(DamageEffectType.DestroyedPart))
                    {
                        item.Properties.EffectsDamage[DamageEffectType.DestroyedPart] = destroyedPartEffect;
                    }
                    else
                    {
                        item.Properties.EffectsDamage.Add(DamageEffectType.DestroyedPart, destroyedPartEffect);
                    }

                    destroyedPartEffect.Cost = surgeryCost;

                    if (config.debug) logger.Info($"MakeMedsGreatAgain: Enabled Surgery (DestroyedPart) for {itemId} with cost {surgeryCost}");
                }
                else
                {
                    // If disabled, remove the effect if it exists
                    if (item.Properties.EffectsDamage != null && item.Properties.EffectsDamage.ContainsKey(DamageEffectType.DestroyedPart))
                    {
                        item.Properties.EffectsDamage.Remove(DamageEffectType.DestroyedPart);
                        if (config.debug) logger.Info($"MakeMedsGreatAgain: Removed Surgery (DestroyedPart) from {itemId}");
                    }
                }
            }
            else if (config.debug)
            {
                logger.Warning($"MakeMedsGreatAgain: Could not find item {itemId} to update surgery effect");
            }
        }

        // Helper function to update item usage
        void UpdateItemUsage(string itemId, int configUsage, int defaultUsage)
        {
            if (globals.Templates.Items.TryGetValue(itemId, out var item))
            {
                if (configUsage > 0)
                {
                    item.Properties.MaxHpResource = configUsage;
                    if (config.debug) logger.Info($"MakeMedsGreatAgain: Updated Usage (HP) for {itemId} to {configUsage}");
                }
                else if (configUsage == 0)
                {
                    item.Properties.MaxHpResource = defaultUsage;
                    if (config.debug) logger.Info($"MakeMedsGreatAgain: Reset Usage (HP) for {itemId} to default {defaultUsage}");
                }
            }
            else if (config.debug)
            {
                logger.Warning($"MakeMedsGreatAgain: Could not find item {itemId} to update usage");
            }
        }

        // Caloc-B
        UpdateItemUsage(config.calocID, config.calocUsage, 3);
        // Army Bandage
        UpdateItemUsage(config.armyBandageID, config.armyBandageUsage, 2);
        // Analgin
        UpdateItemUsage(config.analginPainkillersID, config.analginPainkillersUsage, 4);
        // Augmentin
        UpdateItemUsage(config.augmentinID, config.augmentinUsage, 1);
        // Ibuprofen
        UpdateItemUsage(config.ibuprofenID, config.ibuprofenUsage, 12);
        // Vaseline
        UpdateItemUsage(config.vaselinID, config.vaselinUsage, 6);
        // Golden Star
        UpdateItemUsage(config.goldenStarID, config.goldenStarUsage, 10);
        // Alu Splint
        UpdateItemUsage(config.aluminiumSplintID, config.aluminiumSplintUsage, 5);
        // Surv12
        UpdateItemUsage(config.survivalKitID, config.survivalKitUsage, 15);
        // CMS
        UpdateItemUsage(config.cmsID, config.cmsUsage, 5);

        // Grizzly
        if (config.grizzlyChanges)
        {
            UpdateItemHP(config.grizzlyID, config.grizzlyHP);
            UpdateSurgeryEffect(config.grizzlyID, config.grizzlyCanDoSurgery, config.grizzlySurgeryCost);
            UpdateBleedingFractureEffects(config.grizzlyID, config.grizzlyCanHealHeavyBleeding, config.grizzlyHeavyBleedingHealCost, config.grizzlyCanHealFractures, config.grizzlyFractureHealCost, config.grizzlyCanHealLightBleeding, config.grizzlyLightBleedingHealCost);
        }

        // AI-2
        if (config.ai2Changes)
        {
            UpdateItemHP(config.ai2ID, config.ai2HP);
            UpdateSurgeryEffect(config.ai2ID, config.ai2CanDoSurgery, config.ai2SurgeryCost);
            UpdateBleedingFractureEffects(config.ai2ID, config.ai2CanHealHeavyBleeding, config.ai2HeavyBleedingHealCost, config.ai2CanHealFractures, config.ai2FractureHealCost, config.ai2CanHealLightBleeding, config.ai2LightBleedingHealCost);
        }

        // Car First Aid Kit
        if (config.carKitChanges)
        {
            UpdateItemHP(config.carKitID, config.carKitHP);
            UpdateSurgeryEffect(config.carKitID, config.carKitCanDoSurgery, config.carKitSurgeryCost);
            UpdateBleedingFractureEffects(config.carKitID, config.carKitCanHealHeavyBleeding, config.carKitHeavyBleedingHealCost, config.carKitCanHealFractures, config.carKitFractureHealCost, config.carKitCanHealLightBleeding, config.carKitLightBleedingHealCost);
        }

        // Salewa
        if (config.salewaChanges)
        {
            UpdateItemHP(config.salewaID, config.salewaHP);
            UpdateSurgeryEffect(config.salewaID, config.salewaCanDoSurgery, config.salewaSurgeryCost);
            UpdateBleedingFractureEffects(config.salewaID, config.salewaCanHealHeavyBleeding, config.salewaHeavyBleedingHealCost, config.salewaCanHealFractures, config.salewaFractureHealCost, config.salewaCanHealLightBleeding, config.salewaLightBleedingHealCost);
        }

        // IFAK
        if (config.ifakChanges)
        {
            UpdateItemHP(config.ifakID, config.ifakHP);
            UpdateSurgeryEffect(config.ifakID, config.ifakCanDoSurgery, config.ifakSurgeryCost);
            UpdateBleedingFractureEffects(config.ifakID, config.ifakCanHealHeavyBleeding, config.ifakHeavyBleedingHealCost, config.ifakCanHealFractures, config.ifakFractureHealCost, config.ifakCanHealLightBleeding, config.ifakLightBleedingHealCost);
        }

        // AFAK
        if (config.afakChanges)
        {
            UpdateItemHP(config.afakID, config.afakHP);
            UpdateSurgeryEffect(config.afakID, config.afakCanDoSurgery, config.afakSurgeryCost);
            UpdateBleedingFractureEffects(config.afakID, config.afakCanHealHeavyBleeding, config.afakHeavyBleedingHealCost, config.afakCanHealFractures, config.afakFractureHealCost, config.afakCanHealLightBleeding, config.afakLightBleedingHealCost);
        }


        await Task.CompletedTask;
    }

    public class ModConfig
    {
        public bool debug { get; set; } = true;
        // Simple Usage Items
        public string calocID { get; set; } = string.Empty;
        public string armyBandageID { get; set; } = string.Empty;
        public string analginPainkillersID { get; set; } = string.Empty;
        public string augmentinID { get; set; } = string.Empty;
        public string ibuprofenID { get; set; } = string.Empty;
        public string vaselinID { get; set; } = string.Empty;
        public string goldenStarID { get; set; } = string.Empty;
        public string aluminiumSplintID { get; set; } = string.Empty;
        public string survivalKitID { get; set; } = string.Empty;
        public string cmsID { get; set; } = string.Empty;
        public string grizzlyID { get; set; } = string.Empty;
        public string ai2ID { get; set; } = string.Empty;
        public string carKitID { get; set; } = string.Empty;
        public string salewaID { get; set; } = string.Empty;
        public string ifakID { get; set; } = string.Empty;
        public string afakID { get; set; } = string.Empty;

        public int calocUsage { get; set; }
        public int armyBandageUsage { get; set; }
        public int analginPainkillersUsage { get; set; }
        public int augmentinUsage { get; set; }
        public int ibuprofenUsage { get; set; }
        public int vaselinUsage { get; set; }
        public int goldenStarUsage { get; set; }
        public int aluminiumSplintUsage { get; set; }
        public int survivalKitUsage { get; set; }
        public int cmsUsage { get; set; }

        // Grizzly
        public bool grizzlyChanges { get; set; }
        public int grizzlyHP { get; set; }
        public bool grizzlyCanHealHeavyBleeding { get; set; }
        public bool grizzlyCanDoSurgery { get; set; }
        public int grizzlySurgeryCost { get; set; }
        public int grizzlyHeavyBleedingHealCost { get; set; }
        public bool grizzlyCanHealLightBleeding { get; set; }
        public int grizzlyLightBleedingHealCost { get; set; }
        public bool grizzlyCanHealFractures { get; set; }
        public int grizzlyFractureHealCost { get; set; }

        // AI-2
        public bool ai2Changes { get; set; }
        public int ai2HP { get; set; }
        public bool ai2CanHealFractures { get; set; }
        public bool ai2CanDoSurgery { get; set; }
        public int ai2SurgeryCost { get; set; }
        public int ai2FractureHealCost { get; set; }
        public bool ai2CanHealHeavyBleeding { get; set; }
        public int ai2HeavyBleedingHealCost { get; set; }
        public bool ai2CanHealLightBleeding { get; set; }
        public int ai2LightBleedingHealCost { get; set; }

        // Car First Aid Kit
        public bool carKitChanges { get; set; }
        public int carKitHP { get; set; }
        public bool carKitCanHealFractures { get; set; }
        public int carKitFractureHealCost { get; set; }
        public bool carKitCanHealHeavyBleeding { get; set; }
        public bool carKitCanDoSurgery { get; set; }
        public int carKitSurgeryCost { get; set; }
        public int carKitHeavyBleedingHealCost { get; set; }
        public bool carKitCanHealLightBleeding { get; set; }
        public int carKitLightBleedingHealCost { get; set; }

        // Salewa
        public bool salewaChanges { get; set; }
        public int salewaHP { get; set; }
        public bool salewaCanHealFractures { get; set; }
        public int salewaFractureHealCost { get; set; }
        public bool salewaCanHealHeavyBleeding { get; set; }
        public bool salewaCanDoSurgery { get; set; }
        public int salewaSurgeryCost { get; set; }
        public int salewaHeavyBleedingHealCost { get; set; }
        public bool salewaCanHealLightBleeding { get; set; }
        public int salewaLightBleedingHealCost { get; set; }

        // IFAK
        public bool ifakChanges { get; set; }
        public int ifakHP { get; set; }
        public bool ifakCanHealFractures { get; set; }
        public int ifakFractureHealCost { get; set; }
        public bool ifakCanHealHeavyBleeding { get; set; }
        public bool ifakCanDoSurgery { get; set; }
        public int ifakSurgeryCost { get; set; }
        public int ifakHeavyBleedingHealCost { get; set; }
        public bool ifakCanHealLightBleeding { get; set; }
        public int ifakLightBleedingHealCost { get; set; }

        // AFAK
        public bool afakChanges { get; set; }
        public int afakHP { get; set; }
        public bool afakCanHealFractures { get; set; }
        public int afakFractureHealCost { get; set; }
        public bool afakCanHealHeavyBleeding { get; set; }
        public bool afakCanDoSurgery { get; set; }
        public int afakSurgeryCost { get; set; }
        public int afakHeavyBleedingHealCost { get; set; }
        public bool afakCanHealLightBleeding { get; set; }
        public int afakLightBleedingHealCost { get; set; }
    }
}