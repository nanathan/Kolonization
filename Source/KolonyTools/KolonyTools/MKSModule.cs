using System;
using System.CodeDom;
using System.Collections.Generic;
using System.Linq;
using Kolonization;
using USITools;


namespace KolonyTools
{
    public class MKSModule : PartModule
    {
        private double lastCheck;
        private double checkTime = 5f;
        private bool rewardsChecked;

        [KSPField]
        public bool calculateEfficiency = true;

        [KSPField] 
        public string efficiencyPart = "";

        [KSPField] 
        public int workSpace = 0;

        [KSPField] 
        public int livingSpace = 0;

        [KSPField] 
        public bool hasGenerators = true;

        [KSPField] 
        public string PrimarySkill = "Engineer";

        [KSPField]
        public string SecondarySkill = "Scientist";
        
        [KSPField(guiActive = true, guiName = "Efficiency")]
        public string efficiency = "Unknown";

        [KSPField] 
        public float CrewBonus = 0.1f;

        [KSPField] 
        public float MaxEfficiency = 2.5f;

        private bool _showGUI = true;
        private int _numConverters;
        private int _numCrew;
        private float _efficiencyRate;
        private const int COLONY_RANGE = 100;
        private const int EFF_RANGE = 500;


        private void EfficiencySetup()
        {
            _efficiencyRate = GetEfficiency();
        }

        public bool ShowGUI
        {
            get
            {
                return _showGUI;
            }

            set
            {
                _showGUI = value;
                //Hide/show MKSModule gui
                if (Fields["Efficiency"] != null)
                    Fields["Efficiency"].guiActive = _showGUI;
            }
        }

        private float GetEfficiency()
        {
            try
            {
                //Efficiency is based on various factors.  These come in three
                //categories: 
                //  * Part
                //  * Vessel
                //  * Colony
                //  - Vessel Workspaces         [numWorkspaces]
                //  - 25% Vessel Crew Capacity  [numWorkSpaces]
                //  - Vessel MKS Module count   [numModules]
                //  - Part Crew                 [modKerbalFactor]   (0.05 - 3.75 per Kerbal)
                //  - Vessel crew               [numWeightedKerbals]
                //  - Colony efficiency parts   [added to eff]
                //          Bonus equal to 100 * number of units - 1
                //  - Colony Living Space/Kerbal Happiness

                float numWorkspaces = GetKolonyWorkspaces(vessel);
                //Plus 25% of Crew Cap as low efficiency workspaces
                numWorkspaces += vessel.GetCrewCapacity()*.25f; 
                //Number of active modules
                var numModules = GetActiveKolonyModules(vessel);
                //Kerbals in the module
                float modKerbalFactor = part.protoModuleCrew.Sum(k => GetKerbalFactor(k));
                modKerbalFactor *= GetCrewHappiness();
                //Kerbals in the ship
                float numWeightedKerbals = vessel.GetVesselCrew().Sum(k => GetKerbalFactor(k));
                numWeightedKerbals *= GetCrewHappiness();
                //Worst case, 25% (if crewed).  Uncrewed vessels will be at 0%
                //You need crew for these things, no robo ships.
                float eff = .0f;
                if (vessel.GetCrewCount() > 0)
                {
                    float WorkSpaceKerbalRatio = numWorkspaces / vessel.GetCrewCount();
                    if (WorkSpaceKerbalRatio > 3) WorkSpaceKerbalRatio = 3;
                    //A module gets 100% bonus from Kerbals inside of it,
                    //in addition to a 10% bonus for Kerbals in the entire station.
                    float WorkUnits = WorkSpaceKerbalRatio * modKerbalFactor;
                    WorkUnits += WorkSpaceKerbalRatio*numWeightedKerbals*CrewBonus;
                    eff = WorkUnits / numModules;
                    if (eff > MaxEfficiency) eff = MaxEfficiency;
                    if (eff < .25) eff = .25f;
                }

                //Add in efficiencyParts 
                if (efficiencyPart != "")
                {
                    var validEffParts = new List<EffPart>();
                    var effPartBits = efficiencyPart.Split(',')
                        .Select(effPartName => effPartName.Trim().Replace('_', '.')).ToArray();

                    for(int i = 0; i < effPartBits.Count(); i +=2)
                    {
                        validEffParts.Add(new EffPart
                            {
                                Name = effPartBits[i],
                                Multiplier = float.Parse(effPartBits[i+1])
                            });
                    }

                    var effParts = 0f;
                    foreach (var vep in validEffParts)
                    {
                        var vList = LogisticsTools.GetNearbyVessels(EFF_RANGE, true, vessel, true);
                        var effPartList = new List<Part>();
                        foreach (var v in vList)
                        {
                            var nameWhenRootPart = vep.Name + " (" + v.GetName() + ")";
                            var pList = v.Parts.Where(p => p.name == vep.Name || p.name == nameWhenRootPart);
                            effPartList.AddRange(pList);
                        }

                        foreach (var ep in effPartList)
                        {
                            var mod = ep.FindModuleImplementing<USIAnimation>();
                            if (mod == null)
                            {
                                effParts += vep.Multiplier;
                            }
                            else
                            {
                                if (mod.isDeployed)
                                    effParts += vep.Multiplier;
                            }
                        }
                    }
                    eff += effParts;
                    if (eff < 0.25)  
                        eff = 0.25f;  //We can go as low as 25% as these are almost mandatory.
                }

                if (!calculateEfficiency)
                {
                    eff = 1f;
                    efficiency = String.Format("100% [Fixed]");
                }

                efficiency = String.Format("{0}%", Math.Round((eff * 100), 1));

                //DEBUG DATA
                //DEBUG

                return eff;
            }
            catch (Exception ex)
            {
                print(String.Format("[MKS] - ERROR in GetEfficiency - {0}", ex.Message));
                return 1f;
            }
        }

        private float GetKerbalFactor(ProtoCrewMember k)
        {
            var kerbalFactor = k.experienceLevel / 2f;
            //A level 0 Kerbal is not quite zero - it.s 0.1
            if (kerbalFactor < 0.1)
                kerbalFactor = 0.1f;
            
            // Level 0 Pilot:       0.05
            // Level 0 Engineer:    0.15
            // Level 1 Pilot:       0.25
            // Level 1 Engineer:    0.75
            // Level 2 Pilot:       0.50
            // Level 2 Engineer:    1.50
            // Level 5 Pilot:       1.25
            // Level 5 engineer:    3.75

            //(0.05 - 3.75)
            if (k.experienceTrait.Title == PrimarySkill)
            {
                kerbalFactor *= 1.5f;
            }
            else if (k.experienceTrait.Title == SecondarySkill)
            {
                kerbalFactor *= 1f;
            }
            else 
            {
                kerbalFactor *= 0.5f;
            }
            return kerbalFactor;
        }

        private float GetCrewHappiness()
        {
            //Crew Happiness is a function of the ratio of living space to Kerbals.
            //These are COLONY-WIDE.
            var kShips = LogisticsTools.GetNearbyVessels(COLONY_RANGE, true, vessel, true);
            float ls = GetKolonyLivingSpace(kShips);
            //We can add in a limited number for crew capacity - 10%
            ls += GetKolonyCrewCap(kShips) * .1f;

            var totKerbs = GetKolonyInhabitants(kShips);
            var hap = 0f;
            if(totKerbs > 0)
                hap = ls/totKerbs;

            //Range is 50% - 150% for crowding and extra space.
            //This is calculated before loneliness.
            if (hap < .5f) hap = .5f;
            if (hap > 1.5f) hap = 1.5f;

            //Kerbals hate being alone.  Any fewer than five Kerbals incurs a pretty significant penalty.
            if (totKerbs < 5)
            {
                //20% - 80%
                hap *= (totKerbs * .2f);
            }
            return hap;
        }

        private float GetKolonyCrewCap(List<Vessel> vlist)
        {
            var cc = 0f;
            foreach (var v in vlist)
            {
                cc += v.GetCrewCapacity();
            }
            return cc;
        }

        private float GetKolonyInhabitants(List<Vessel> vlist)
        {
            var cc = 0f;
            foreach (var v in vlist)
            {
                cc += v.GetCrewCount();
            }
            return cc;
        }


        private int GetActiveKolonyModules(Vessel v)
        {
            try
            {
                var numMods = 0;
                var pList = v.parts.Where(p => p.Modules.Contains("ModuleResourceConverter"));
                foreach (var p in pList)
                {
                    var mods = p.Modules.OfType<ModuleResourceConverter>();
                    numMods += mods.Count(pm => pm.IsActivated);
                }
                return numMods;
            }
            catch (Exception ex)
            {
                print(String.Format("[MKS] - ERROR in GetActiveKolonyModules - {0}", ex.Message));
                return 0;
            }
        }

        private int GetKolonyWorkspaces(Vessel v)
        {
            try
            {
                var numWS = 0;
                var pList = v.parts.Where(p => p.Modules.Contains("MKSModule"));
                foreach (var p in pList)
                {
                    var mods = p.Modules.OfType<MKSModule>();
                    numWS += mods.Sum(pm => pm.workSpace);
                }
                return numWS;
            }
            catch (Exception ex)
            {
                print(String.Format("[MKS] - ERROR in GetKolonyWorkspaces - {0}", ex.Message));
                return 0;
            }
        }
        private int GetKolonyLivingSpace(List<Vessel> vList)
        {
            try
            {
                var numLS = 0;
                foreach (var v in vList)
                {
                    var pList = v.parts.Where(p => p.Modules.Contains("MKSModule"));
                    foreach (var p in pList)
                    {
                        var mods = p.Modules.OfType<MKSModule>();
                        foreach (var pm in mods)
                        {
                            if (p.Modules.Contains("USIAnimation"))
                            {
                                var am = p.Modules.OfType<USIAnimation>().First();
                                if (am.isDeployed)
                                {
                                    numLS += pm.livingSpace;
                                }
                            }
                            else
                            {
                                numLS += pm.livingSpace;
                            }
                        }
                    }
                }
                return numLS;
            }
            catch (Exception ex)
            {
                print(String.Format("[MKS] - ERROR in GetKolonyWorkspaces - {0}", ex.Message));
                return 0;
            }
        }
        
        public virtual float GetEfficiencyRate()
        {
            var curConverters = GetActiveKolonyModules(vessel);
            if (curConverters != _numConverters || part.protoModuleCrew.Count != _numCrew)
            {
                _numConverters = curConverters;
                _numCrew = part.protoModuleCrew.Count;
                EfficiencySetup();
            }
            return _efficiencyRate;
        }


        public override void OnLoad(ConfigNode node)
        {
            try
            {
                if (!hasGenerators)
                {
                    Fields["efficiency"].guiActive = false;
                }
            }
            catch (Exception ex)
            {
                print("ERROR IN MKSModuleOnLoad - " + ex.Message);
            }
        }

        private void CheckRewards()
        {
            if (!HighLogic.LoadedSceneIsFlight)
                return;

            var k = KolonizationManager.Instance.FetchLogEntry(vessel.id.ToString(), vessel.mainBody.flightGlobalsIndex);
            if (ResearchAndDevelopment.Instance != null)
            {
                if (k.Science > 1)
                {
                    ResearchAndDevelopment.Instance.AddScience((float)k.Science, TransactionReasons.ContractReward);
                    var msg = String.Format("Added {0:n2} Science", k.Science);
                    ScreenMessages.PostScreenMessage(msg, 5f, ScreenMessageStyle.UPPER_CENTER);
                    k.Science = 0d;
                }
            }
            if (Funding.Instance != null)
            {
                if (k.Funds > 1)
                {
                    Funding.Instance.AddFunds(k.Funds, TransactionReasons.ContractReward);
                    var msg = String.Format("Added {0:n2} Funds", k.Funds);
                    ScreenMessages.PostScreenMessage(msg, 5f, ScreenMessageStyle.UPPER_CENTER);
                    k.Funds = 0d;
                }
            }
            if (Reputation.Instance != null)
            {
                if (k.Rep > 1)
                {
                    Reputation.Instance.AddReputation((float) k.Rep, TransactionReasons.ContractReward);
                    var msg = String.Format("Added {0:n2} Reputation", k.Rep);
                    ScreenMessages.PostScreenMessage(msg, 5f, ScreenMessageStyle.UPPER_CENTER);
                    k.Rep = 0d;
                }
            }

            KolonizationManager.Instance.TrackLogEntry(k);            
        }

        public void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight)
                return;

            if (Math.Abs(lastCheck - Planetarium.GetUniversalTime()) < checkTime)
                return;

            if (!rewardsChecked)
            {
                CheckRewards();
                rewardsChecked = true;
            }

            lastCheck = Planetarium.GetUniversalTime();

            var conEff = GetEfficiencyRate();
            UpdateKolonizationStats();
            var kBonus = Math.Max(KolonizationSetup.Instance.Config.MinBaseBonus,GetPlanetaryBonus());
            kBonus += KolonizationSetup.Instance.Config.StartingBaseBonus;
            conEff *= (float)kBonus;
            
            foreach (var con in part.FindModulesImplementing<ModuleResourceConverter>())
            {
                con.EfficiencyBonus = conEff;
            }
        }

        private double GetPlanetaryBonus()
        {
            var thisBodyInfo = KolonizationManager.Instance.KolonizationInfo.Where(k=>k.BodyIndex == vessel.mainBody.flightGlobalsIndex);
            var bonus = thisBodyInfo.Sum(k=>k.GeologyResearch);
            if(PrimarySkill == "Pilot")
                bonus = thisBodyInfo.Sum(k=>k.KolonizationResearch);
            else if (PrimarySkill == "Scientist")
                bonus = thisBodyInfo.Sum(k=>k.BotanyResearch);
            
            bonus = Math.Sqrt(bonus);
            bonus /= KolonizationSetup.Instance.Config.EfficiencyMultiplier;
            return bonus;
        }



        private void UpdateKolonizationStats()
        {
            
            //No kolonization on Kerbin!
            if (vessel.mainBody == FlightGlobals.GetHomeBody())
                return;

            var k = KolonizationManager.Instance.FetchLogEntry(vessel.id.ToString(), vessel.mainBody.flightGlobalsIndex);

            if (Planetarium.GetUniversalTime() - k.LastUpdate < checkTime)
            {
                return;
            }

            var numPilots = GetVesselCrewByTrait("Pilot");
            var numEngineers = GetVesselCrewByTrait("Engineer");
            var numScientists = GetVesselCrewByTrait("Scientist");

            var elapsedTime = Planetarium.GetUniversalTime() - k.LastUpdate;
            var orbitMod = 1f;
            if(!vessel.LandedOrSplashed)
                orbitMod = KolonizationSetup.Instance.Config.OrbitMultiplier;

            var scienceBase = numScientists * elapsedTime * orbitMod;
            var repBase = numPilots * elapsedTime * orbitMod;
            var fundsBase = numEngineers * elapsedTime * orbitMod;
            
            k.LastUpdate = Planetarium.GetUniversalTime();
            k.BotanyResearch += scienceBase;
            k.KolonizationResearch += repBase;
            k.GeologyResearch += fundsBase;
            
            
            var mult = vessel.mainBody.scienceValues.RecoveryValue;
            var science = scienceBase*KolonizationSetup.Instance.Config.ScienceMultiplier*mult;
            var rep = repBase*KolonizationSetup.Instance.Config.RepMultiplier*mult;
            var funds = fundsBase*KolonizationSetup.Instance.Config.FundsMultiplier*mult;

            k.Science += science;
            k.Funds += funds;
            k.Rep += rep;
            KolonizationManager.Instance.TrackLogEntry(k);
            
            //Update the hab bonus
            var thisBodyInfo = KolonizationManager.Instance.KolonizationInfo.Where(b=>b.BodyIndex == vessel.mainBody.flightGlobalsIndex);
            var  habBonus = thisBodyInfo.Sum(b=>b.KolonizationResearch);
            habBonus = Math.Sqrt(habBonus);
            habBonus /= KolonizationSetup.Instance.Config.EfficiencyMultiplier;
            USI_GlobalBonuses.Instance.SaveHabBonus(vessel.mainBody.flightGlobalsIndex,habBonus);

            //Update the drill bonus
            foreach (var d in vessel.FindPartModulesImplementing<BaseDrill>())
            {
                var geoBonus = thisBodyInfo.Sum(b => b.GeologyResearch);
                geoBonus = Math.Sqrt(habBonus);
                geoBonus /= KolonizationSetup.Instance.Config.EfficiencyMultiplier;
                d.EfficiencyBonus = (float)geoBonus;
            }
        }



        private double GetVesselCrewByTrait(string trait)
        {
            var crew = vessel.GetVesselCrew().Where(c => c.experienceTrait.Title == trait);
            return crew.Count();
        }

        private struct EffPart
        {
            public string Name { get; set; }
            public float Multiplier { get; set; }
        }
    }
}
