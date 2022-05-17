﻿using System;
using DOL.AI.Brain;
using DOL.Events;
using DOL.Database;
using DOL.GS;

namespace DOL.GS
{
    public class Issorden : GameEpicBoss
    {
        public Issorden() : base()
        {
        }
        public override int GetResist(eDamageType damageType)
        {
            switch (damageType)
            {
                case eDamageType.Slash: return 80; // dmg reduction for melee dmg
                case eDamageType.Crush: return 80; // dmg reduction for melee dmg
                case eDamageType.Thrust: return 80; // dmg reduction for melee dmg
                default: return 60; // dmg reduction for rest resists
            }
        }
        public override double AttackDamage(InventoryItem weapon)
        {
            return base.AttackDamage(weapon) * Strength / 100;
        }

        public override int AttackRange
        {
            get { return 350; }
            set { }
        }

        public override bool HasAbility(string keyName)
        {
            if (IsAlive && keyName == DOL.GS.Abilities.CCImmunity)
                return true;

            return base.HasAbility(keyName);
        }

        public override double GetArmorAF(eArmorSlot slot)
        {
            return 800;
        }

        public override double GetArmorAbsorb(eArmorSlot slot)
        {
            // 85% ABS is cap.
            return 0.55;
        }

        public override int MaxHealth
        {
            get { return 20000; }
        }

        public override bool AddToWorld()
        {
            INpcTemplate npcTemplate = NpcTemplateMgr.GetTemplate(60162545);
            LoadTemplate(npcTemplate);
            Strength = npcTemplate.Strength;
            Dexterity = npcTemplate.Dexterity;
            Constitution = npcTemplate.Constitution;
            Quickness = npcTemplate.Quickness;
            Piety = npcTemplate.Piety;
            Intelligence = npcTemplate.Intelligence;
            Empathy = npcTemplate.Empathy;
            IssordenBrain.BafMobs = false;
            Faction = FactionMgr.GetFactionByID(140);
            Faction.AddFriendFaction(FactionMgr.GetFactionByID(140));

            IssordenBrain sbrain = new IssordenBrain();
            SetOwnBrain(sbrain);
            LoadedFromScript = false; //load from database
            SaveIntoDatabase();
            base.AddToWorld();
            return true;
        }

        [ScriptLoadedEvent]
        public static void ScriptLoaded(DOLEvent e, object sender, EventArgs args)
        {
            GameNPC[] npcs;
            npcs = WorldMgr.GetNPCsByNameFromRegion("Issorden", 160, (eRealm) 0);
            if (npcs.Length == 0)
            {
                log.Warn("Issorden not found, creating it...");

                log.Warn("Initializing Issorden...");
                Issorden TG = new Issorden();
                TG.Name = "Issorden";
                TG.Model = 920;
                TG.Realm = 0;
                TG.Level = 78;
                TG.Size = 180;
                TG.CurrentRegionID = 160; //tuscaran glacier
                TG.MeleeDamageType = eDamageType.Crush;
                TG.RespawnInterval =
                    ServerProperties.Properties.SET_SI_EPIC_ENCOUNTER_RESPAWNINTERVAL *
                    60000; //1min is 60000 miliseconds
                TG.Faction = FactionMgr.GetFactionByID(140);
                TG.Faction.AddFriendFaction(FactionMgr.GetFactionByID(140));

                TG.X = 54583;
                TG.Y = 37745;
                TG.Z = 11435;
                IssordenBrain ubrain = new IssordenBrain();
                TG.SetOwnBrain(ubrain);
                TG.AddToWorld();
                TG.SaveIntoDatabase();
                TG.Brain.Start();
            }
            else
                log.Warn("Issorden exist ingame, remove it and restart server if you want to add by script code.");
        }
    }
}

namespace DOL.AI.Brain
{
    public class IssordenBrain : StandardMobBrain
    {
        private static readonly log4net.ILog log =
            log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public IssordenBrain()
            : base()
        {
            AggroLevel = 100;
            AggroRange = 600;
            ThinkInterval = 2000;
        }

        public static bool BafMobs = false;

        public override void Think()
        {
            if (!HasAggressionTable())
            {
                //set state to RETURN TO SPAWN
                FSM.SetCurrentState(eFSMStateType.RETURN_TO_SPAWN);
                Body.Health = Body.MaxHealth;
                BafMobs = false;
            }

            if (Body.IsOutOfTetherRange)
            {
                Body.Health = Body.MaxHealth;
                ClearAggroList();
            }
            else if (Body.InCombatInLast(30 * 1000) == false && this.Body.InCombatInLast(35 * 1000))
            {
                Body.Health = Body.MaxHealth;
            }

            if (Body.InCombat || HasAggro || Body.attackComponent.AttackState == true)
            {
                if (Body.TargetObject != null)
                {
                    if (Util.Chance(10))
                    {
                        Body.CastSpell(IssoRoot, SkillBase.GetSpellLine(GlobalSpellsLines.Mob_Spells));
                    }
                }

                if (BafMobs == false)
                {
                    foreach (GameNPC npc in WorldMgr.GetNPCsFromRegion(Body.CurrentRegionID))
                    {
                        if (npc != null)
                        {
                            if (npc.IsAlive && npc.PackageID == "IssordenBaf")
                            {
                                AddAggroListTo(
                                    npc.Brain as StandardMobBrain); // add to aggro mobs with IssordenBaf PackageID
                                BafMobs = true;
                            }
                        }
                    }
                }
            }

            base.Think();
        }

        private Spell m_IssoRoot;

        private Spell IssoRoot
        {
            get
            {
                if (m_IssoRoot == null)
                {
                    DBSpell spell = new DBSpell();
                    spell.AllowAdd = false;
                    spell.CastTime = 0;
                    spell.RecastDelay = 30;
                    spell.ClientEffect = 277;
                    spell.Icon = 277;
                    spell.Duration = 60;
                    spell.Radius = 1500;
                    spell.Value = 99;
                    spell.Name = "Issorden Root";
                    spell.TooltipId = 277;
                    spell.SpellID = 11741;
                    spell.Target = "Enemy";
                    spell.Type = "SpeedDecrease";
                    spell.Uninterruptible = true;
                    spell.MoveCast = true;
                    spell.DamageType = (int) eDamageType.Cold;
                    m_IssoRoot = new Spell(spell, 70);
                    SkillBase.AddScriptedSpell(GlobalSpellsLines.Mob_Spells, m_IssoRoot);
                }

                return m_IssoRoot;
            }
        }
    }
}