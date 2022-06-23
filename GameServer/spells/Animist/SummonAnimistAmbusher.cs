using System;
using DOL.Events;
using DOL.GS.PacketHandler;
using DOL.AI.Brain;
using DOL.GS.Effects;
using DOL.Language;

namespace DOL.GS.Spells
{
	/// <summary>
	/// Summon a fnf animist pet.
	/// </summary>
	[SpellHandler("SummonAnimistAmbusher")]
	public class SummonAnimistAmbusher : SummonTheurgistPet
	{
		public SummonAnimistAmbusher(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }
		
		protected override void AddHandlers()
		{
			GameEventMgr.AddHandler(m_pet, GameLivingEvent.Dying, OnPetDying);
			base.AddHandlers();
		}
		
		protected override void RemoveHandlers()
		{
			GameEventMgr.RemoveHandler(m_pet, GameLivingEvent.Dying, OnPetDying);
			base.AddHandlers();
		}

		public override bool CheckBeginCast(GameLiving selectedTarget)
		{
			Region rgn = WorldMgr.GetRegion(Caster.CurrentRegion.ID);

			if (rgn?.GetZone(Caster.GroundTarget.X, Caster.GroundTarget.Y) != null)
				return base.CheckBeginCast(selectedTarget);
			if (Caster is GamePlayer)
				MessageToCaster(LanguageMgr.GetTranslation((Caster as GamePlayer).Client, "SummonAnimistFnF.CheckBeginCast.NoGroundTarget"), eChatType.CT_SpellResisted);
			return false;

		}
		
		public override void ApplyEffectOnTarget(GameLiving target, double effectiveness)
		{
			
			if (target == null)
				target = (m_pet.Brain as ForestheartAmbusherBrain).CalculateNextAttackTarget();
			base.ApplyEffectOnTarget(target, effectiveness);
			
			m_pet.TempProperties.setProperty("target", target);
			(m_pet.Brain as IOldAggressiveBrain).AddToAggroList(target, 1);
			(m_pet.Brain as ForestheartAmbusherBrain).Think();
			// SetBrainToOwner(m_pet.Brain as ForestheartAmbusherBrain);


			Caster.PetCount++;
		}
		
		protected override void GetPetLocation(out int x, out int y, out int z, out ushort heading, out Region region)
		{
			x = Caster.GroundTarget.X;
			y = Caster.GroundTarget.Y;
			z = Caster.GroundTarget.Z;
			region = Caster.CurrentRegion;
			
			heading = Caster.Heading;
		}

		/// <summary>
		/// [Ganrod] Nidel: Can remove TurretFNF
		/// </summary>
		/// <param name="e"></param>
		/// <param name="sender"></param>
		/// <param name="arguments"></param>
		protected override void OnNpcReleaseCommand(DOLEvent e, object sender, EventArgs arguments)
		{

		}

		/// <summary>
		/// When an applied effect expires.
		/// Duration spells only.
		/// </summary>
		/// <param name="effect">The expired effect</param>
		/// <param name="noMessages">true, when no messages should be sent to player and surrounding</param>
		/// <returns>immunity duration in milliseconds</returns>
		public override int OnEffectExpires(GameSpellEffect effect, bool noMessages)
		{
			Caster.PetCount--;

			return base.OnEffectExpires(effect, noMessages);
		}

		protected override IControlledBrain GetPetBrain(GameLiving owner)
		{
			return new ForestheartAmbusherBrain(owner);
		}
		
		protected override GamePet GetGamePet(INpcTemplate template)
		{
			return new TheurgistPet(template);
		}

		/// <summary>
		/// Do not trigger SubSpells
		/// </summary>
		/// <param name="target"></param>
		public override void CastSubSpells(GameLiving target)
		{
		}

		private void OnPetDying(DOLEvent e, object sender, EventArgs arguments)
		{
			if (e != GameLivingEvent.Dying || sender is not TheurgistPet)
				return;
			var pet = sender as TheurgistPet;
			if (pet.Brain is not  ForestheartAmbusherBrain)
				return;
			var player = pet?.Owner as GamePlayer;
			if (player == null)
				return;

			AtlasOF_ForestheartAmbusherECSEffect effect = (AtlasOF_ForestheartAmbusherECSEffect)EffectListService.GetEffectOnTarget(player, eEffect.ForestheartAmbusher);

			effect?.Cancel(false);
			
		}
	}
}
