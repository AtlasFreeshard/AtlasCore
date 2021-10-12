using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Atlas.DataLayer.Models;
using DOL.GS.Spells;
using DOL.AI;
using DOL.GS.Effects;
using DOL.AI.Brain;

namespace DOL.GS.Scripts
{
    public class OFAssistant : GameNPC
    {
        public override bool AddToWorld()
        {
            if (!(base.AddToWorld()))
                return false;

            Level = 100;
            Flags |= GameNPC.eFlags.PEACE;

            if (Realm == eRealm.None)
                Realm = eRealm.Albion;

            switch (Realm)
            {
                case eRealm.Albion:
                    {
                        Name = "Master Elementalist";
                        Model = 61;
                        LoadEquipmentTemplateFromDatabase("master_elementalist");

                    }
                    break;
                case eRealm.Hibernia:
                    {
                        Name = "Seoltoir";
                        Model = 342;
                        LoadEquipmentTemplateFromDatabase("seoltoir");
                    }
                    break;
                case eRealm.Midgard:
                    {
                        Name = "Master Runemaster";
                        Model = 153;
                        LoadEquipmentTemplateFromDatabase("master_runemaster");
                    }
                    break;

            }

            SetOwnBrain(new AssistantTeleporterBrain());

            return true;
        }
        public void CastEffect()
        {
            foreach (GamePlayer player in GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE))
            {
                player.Out.SendSpellCastAnimation(this, 4468, 50);
            }
        }
    }
    public class OFTeleporter : GameNPC
    {
        //Re-Port every 45 seconds.
        public const int ReportInterval = 45;
        //RvR medallions
        public const string HadrianID = "hadrian_necklace";
        public const string EmainID = "emain_necklace";
        public const string OdinID = "odin_necklace";
        public const string HomeID = "home_necklace";
        
        //BG medallions
        public const string AbermenaiID = "abermenai_necklace";
        public const string ThidrankiID = "thidranki_necklace";
        public const string MurdaigeanID = "murdaigean_necklace";
        public const string CaledoniaID = "caledonia_necklace";
        
        //Other medallions
        public const string DarknessFallsID = "df_necklace";

        private IList<OFAssistant> m_ofAssistants;
        public IList<OFAssistant> Assistants
        {
            get { return m_ofAssistants; }
            set { m_ofAssistants = value; }
        }

        private Atlas.DataLayer.Models.Spell m_buffSpell;
        private Spell m_portSpell;

        public Spell PortSpell
        {
            get
            {
                m_buffSpell = new Atlas.DataLayer.Models.Spell();
                m_buffSpell.ClientEffect = 4468;
                m_buffSpell.CastTime = 5;
                m_buffSpell.Icon = 4468;
                m_buffSpell.Duration = ReportInterval;
                m_buffSpell.Target = "Self";
                m_buffSpell.Type = "ArmorFactorBuff";
                m_buffSpell.Name = "TELEPORTER_EFFECT";
                m_buffSpell.RecastDelay = ReportInterval;
                m_portSpell = new Spell(m_buffSpell, 0);
                return m_portSpell;
            }
            set { m_portSpell = value; }
        }
        public void StartTeleporting()
        {
            bool cast = CastSpell(PortSpell, SkillBase.GetSpellLine(GlobalSpellsLines.Mob_Spells), false);

            if (Assistants == null) {
                Assistants = new List<OFAssistant>();
            }

            if (Assistants.Count < 5) {
                //cache our assistants on first run
                foreach (GameNPC assistant in GetNPCsInRadius(5000)) {
                    if (assistant is OFAssistant) {
                        Assistants.Add(assistant as OFAssistant);
                        Console.WriteLine($"Adding assistant {assistant}");
                    }
                }

                Console.WriteLine(Assistants.ToString());
            }

            if (cast) {
                foreach(OFAssistant assi in Assistants) {
                    assi.CastEffect();
                }
            }
        }
        public override void OnAfterSpellCastSequence(ISpellHandler handler)
        {
            base.OnAfterSpellCastSequence(handler);

            InventoryItem medallion = null;

            foreach (GamePlayer player in GetPlayersInRadius(300))
            {
                GameLocation PortLocation = null;
                medallion = player.Inventory.GetItem(eInventorySlot.Mythical);

                switch (player.Realm)
                {
                    case eRealm.Albion:
                        {
                            if (medallion != null)
                            {

                                switch (medallion.ItemTemplate.KeyName)
                                {
                                    case OdinID: PortLocation = new GameLocation("Odin Alb", 100, 596364, 631509, 5971); break;
                                    case EmainID: PortLocation = new GameLocation("Emain Alb", 200, 475835, 343661, 4080); break;
                                    case HomeID: PortLocation = new GameLocation("Home Alb", 1, 583908, 486608, 2184); break;
                                    case DarknessFallsID: PortLocation = new GameLocation("DF Alb", 249, 31670, 27908, 22893); break;
                                    
                                }
                            }
                        }
                        break;
                    case eRealm.Midgard:
                        {
                            if (medallion != null)
                            {
                                switch (medallion.ItemTemplate.KeyName)
                                {
                                    case HadrianID: PortLocation = new GameLocation("Hadrian Mid", 1, 655200, 293217, 4879); break;
                                    case EmainID: PortLocation = new GameLocation("Emain Mid", 200, 474107, 295199, 3871); break;
                                    case HomeID: PortLocation = new GameLocation("Home Mid", 100, 765381, 673533, 5736); break;
                                    case AbermenaiID: PortLocation = new GameLocation("Abermenai Mid", 253, 53568, 23643, 4530); break;
                                    case ThidrankiID: PortLocation = new GameLocation("Thidranki Mid", 252, 53568, 23643, 4530); break;
                                    case MurdaigeanID: PortLocation = new GameLocation("Murdaigean Mid", 251, 53568, 23643, 4530); break;
                                    case CaledoniaID: PortLocation = new GameLocation("Caledonia Mid", 250, 53568, 23643, 4530); break;
                                    case DarknessFallsID: PortLocation = new GameLocation("DF Mid", 249, 18584, 18887, 22892); break;
                                }
                            }
                        }
                        break;
                    case eRealm.Hibernia:
                        {
                            if (medallion != null)
                            {
                                switch (medallion.ItemTemplate.KeyName)
                                {
                                    case OdinID: PortLocation = new GameLocation("Odin Hib", 100, 596055, 581400, 6031); break;
                                    case HadrianID: PortLocation = new GameLocation("Hadrian Hib", 1, 605743, 293676, 4839); break;
                                    case HomeID: PortLocation = new GameLocation("Home Hib", 200, 334335, 420404, 5184); break;
                                    case AbermenaiID: PortLocation = new GameLocation("Abermenai Hib", 253, 17367, 18248, 4320); break;
                                    case ThidrankiID: PortLocation = new GameLocation("Thidranki Hib", 252, 17367, 18248, 4320); break;
                                    case MurdaigeanID: PortLocation = new GameLocation("Murdaigean Hib", 251, 17367, 18248, 4320); break;
                                    case CaledoniaID: PortLocation = new GameLocation("Caledonia Hib", 250, 17367, 18248, 4320); break;
                                    case DarknessFallsID: PortLocation = new GameLocation("DF Hib", 249, 46385, 40298, 21357); break;
                                }
                            }
                        }
                        break;
                }

                //Move the player to the designated port location.
                if (PortLocation != null)
                {
                    //Remove the Necklace.
                    player.Inventory.RemoveItem(medallion);
                    player.MoveTo(PortLocation);
                }
            }
        }
        public override bool AddToWorld()
        {
            if (!(base.AddToWorld()))
                return false;

            if (Realm == eRealm.None)
                Realm = eRealm.Albion;
            
            Level = 100;
            
            switch (Realm)
            {
                case eRealm.Albion:
                    {
                        Name = "Master Visur";
                        Model = 63; 
                        LoadEquipmentTemplateFromDatabase("visur");

                    }
                    break;
                case eRealm.Hibernia:
                    {
                        Name = "Glasny";
                        Model = 342;
                        LoadEquipmentTemplateFromDatabase("glasny");
                    }
                    break;
                case eRealm.Midgard:
                    {
                        Name = "Stor Gothi";
                        Model = 153;
                        LoadEquipmentTemplateFromDatabase("stor_gothi");
                    }
                    break;
            }

            SetOwnBrain(new MainTeleporterBrain());

            return true;
        }
    }
    public class MainTeleporterBrain : StandardMobBrain
    {
        public override void Think()
        {
            OFTeleporter teleporter = Body as OFTeleporter;

            GameSpellEffect effect = null;

            foreach (GameSpellEffect activeEffect in teleporter.EffectList)
            {
                if (activeEffect.Name == "TELEPORTER_EFFECT")
                {
                    effect = activeEffect;
                }
            }

            if (effect != null || teleporter.IsCasting)
                return;

            teleporter.StartTeleporting();
        }
    }

    public class AssistantTeleporterBrain : StandardMobBrain {
        public override void Think() {
            //do nothing
        }
    }
}
