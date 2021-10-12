using System.Reflection;
using System.Collections;
using DOL.GS;
using DOL.GS.PacketHandler;
using DOL.GS.Effects;
using Atlas.DataLayer.Models;

namespace DOL.GS.RealmAbilities
{
	public class BarrierOfFortitudeAbility : TimedRealmAbility
	{
		public BarrierOfFortitudeAbility(Atlas.DataLayer.Models.Ability dba, int level) : base(dba, level) { }

        /// [Atlas - Takii] Remove the "BoF/SB don't stack" rule from NF by giving them unique names.
        //public const string BofBaSb = "RA_DAMAGE_DECREASE";
        public const string BofBaSb = "RA_ATLAS_BOF";

		int m_range = 1500;
		int m_duration = 30;
		int m_value = 0;

		public override void Execute(GameLiving living)
		{
			GamePlayer player = living as GamePlayer;
			if (CheckPreconditions(living, DEAD | SITTING | MEZZED | STUNNED)) return;
			if (player.TempProperties.getProperty(BofBaSb, false))
			{
				player.Out.SendMessage("You already an effect of that type!", eChatType.CT_SpellResisted, eChatLoc.CL_SystemWindow);
				return;
			}

			m_value = GetAbsorbAmount();

			DisableSkill(living);
			ArrayList targets = new ArrayList();
			if (player.Group == null)
				targets.Add(player);
			else
			{
				foreach (GamePlayer grpplayer in player.Group.GetPlayersInTheGroup())
				{
					if (player.IsWithinRadius(grpplayer, m_range ) && grpplayer.IsAlive)
						targets.Add(grpplayer);
				}
			}
			bool success;
			foreach (GamePlayer target in targets)
			{
				//send spelleffect
				if (!target.IsAlive) continue;
				success = !target.TempProperties.getProperty(BofBaSb, false);
				foreach (GamePlayer visPlayer in target.GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE))
					visPlayer.Out.SendSpellEffectAnimation(player, target, 7016, 0, false, CastSuccess(success));
				if (success)
				{
					if (target != null)
						new BarrierOfFortitudeEffect().Start(target, m_duration, m_value);
				}
			}

		}
		private byte CastSuccess(bool success)
		{
			if (success)
				return 1;
			else
				return 0;
		}
		public override int GetReUseDelay(int level)
		{
			return 600;
		}

        protected virtual int GetAbsorbAmount()
        {
			if (ServerProperties.Properties.USE_NEW_ACTIVES_RAS_SCALING)
            {
                switch (Level)
                {
                    case 1: return 10;
                    case 2: return 15;
                    case 3: return 20;
                    case 4: return 30;
                    case 5: return 40;
                }
            }
            else
            {
                switch (Level)
                {
                    case 1: return 10;;
                    case 2: return 20;
                    case 3: return 40;
                }
            }
			return 0;
        }
    }
}
