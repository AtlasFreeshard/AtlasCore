using System.Reflection;
using System.Collections;
using DOL.GS;
using DOL.GS.PacketHandler;
using DOL.GS.Effects;
using DOL.GS.Spells;

namespace DOL.GS.RealmAbilities
{
    public class AtlasOF_ReflexAttack : TimedRealmAbility
    {
        public AtlasOF_ReflexAttack(Atlas.DataLayer.Models.Ability dba, int level) : base(dba, level) { }

        public const int duration = 30000; // 30 seconds
        public override int MaxLevel { get { return 1; } }
        public override int GetReUseDelay(int level) { return 1800; } // 30 mins
        public override int CostForUpgrade(int level) { return 14; }

        public override void Execute(GameLiving living)
        {
            if (CheckPreconditions(living, DEAD | SITTING | MEZZED | STUNNED)) return;

            new AtlasOF_ReflexAttackEffect(duration).Start(living);
            DisableSkill(living);
        }
    }
}
