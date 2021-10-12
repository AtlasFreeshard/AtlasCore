using System.Reflection;
using System.Collections;
using DOL.GS;
using DOL.GS.PacketHandler;
using DOL.GS.Effects;
using System.Collections.Generic;

namespace DOL.GS.RealmAbilities
{

    public class AtlasOF_SoldiersBarricade : SoldiersBarricadeAbility
    {
        public AtlasOF_SoldiersBarricade(Atlas.DataLayer.Models.Ability dba, int level) : base(dba, level) { }

        public override int MaxLevel { get { return 1; } }
        public override int CostForUpgrade(int level) { return 10; }
        public override int GetReUseDelay(int level) { return 1800; } // 30 mins
        protected override int GetArmorFactorAmount() { return 200; }

        public override void AddEffectsInfo(IList<string> list)
        {
            list.Add(string.Format("Value: {0} AF", GetArmorFactorAmount()));
        }
    }
}