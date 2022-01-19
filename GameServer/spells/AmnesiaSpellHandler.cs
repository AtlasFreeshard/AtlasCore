/*
 * DAWN OF LIGHT - The first free open source DAoC server emulator
 * 
 * This program is free software; you can redistribute it and/or
 * modify it under the terms of the GNU General Public License
 * as published by the Free Software Foundation; either version 2
 * of the License, or (at your option) any later version.
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 * 
 * You should have received a copy of the GNU General Public License
 * along with this program; if not, write to the Free Software
 * Foundation, Inc., 59 Temple Place - Suite 330, Boston, MA  02111-1307, USA.
 *
 */
using System;
using System.Collections;
using DOL.AI.Brain;
using DOL.GS.Effects;
using DOL.GS.PacketHandler;
using DOL.Language;

namespace DOL.GS.Spells
{
	/// <summary>
	/// 
	/// </summary>
	[SpellHandlerAttribute("Amnesia")]
	public class AmnesiaSpellHandler : SpellHandler
	{
		/// <summary>
		/// Execute direct damage spell
		/// </summary>
		/// <param name="target"></param>
		public override void FinishSpellCast(GameLiving target)
		{
			m_caster.Mana -= PowerCost(target);
			base.FinishSpellCast(target);
		}

		/// <summary>
		/// execute non duration spell effect on target
		/// </summary>
		/// <param name="target"></param>
		/// <param name="effectiveness"></param>
		public override void OnDirectEffect(GameLiving target, double effectiveness)
		{
			base.OnDirectEffect(target, effectiveness);
			if (target == null || !target.IsAlive)
				return;

			/// [Atlas - Takii] This is a silly change by a silly person because disallowing Amnesia while MoC'd has never been a thing in this game.
			//if (Caster.EffectList.GetOfType<MasteryofConcentrationEffect>() != null)
 			//	return;

			//have to do it here because OnAttackedByEnemy is not called to not get aggro
			//if (target.Realm == 0 || Caster.Realm == 0)
				//target.LastAttackedByEnemyTickPvE = GameLoop.GameLoopTime;
			//else target.LastAttackedByEnemyTickPvP = GameLoop.GameLoopTime;
			SendEffectAnimation(target, 0, false, 1);

			if (target is GamePlayer)
			{
				((GamePlayer)target).styleComponent.NextCombatStyle = null;
				((GamePlayer)target).styleComponent.NextCombatBackupStyle = null;
			}
			target.StopCurrentSpellcast(); //stop even if MoC or QC
			target.rangeAttackComponent.RangeAttackTarget = null;
			if(target is GamePlayer)
				target.TargetObject = null;
			
            //MessageToLiving(target, LanguageMgr.GetTranslation((target as GamePlayer).Client, "Amnesia.MessageToTarget", null), eChatType.CT_Spell);
            //MessageToCaster(LanguageMgr.GetTranslation((m_caster as GamePlayer).Client, "Amnesia.MessageToArea", target.GetName(0, true)), eChatType.CT_Spell);
            //Message.SystemToArea(target, LanguageMgr.GetTranslation("Amnesia.MessageToArea", target.Name), eChatType.CT_Spell, target, m_caster);
            
            // "Your mind goes blank and you forget what you were doing!"
            // "{0} forgets what they were doing!"
            OnSpellStartsMsg(target, true, true, true);
            //MessageToCaster(Util.MakeSentence(Spell.Message2, target.GetName(0, true)), eChatType.CT_Spell);
            // "{0} forgets what they were doing!"
            //Message.SystemToArea(target, Util.MakeSentence(Spell.Message2, target.GetName(0, true)), eChatType.CT_Spell, target);

            /*
            GameSpellEffect effect;
            effect = SpellHandler.FindEffectOnTarget(target, "Mesmerize");
            if (effect != null)
            {
                effect.Cancel(false);
                return;
            }*/

            if (target.effectListComponent.ContainsEffectForEffectType(eEffect.Pulse))
            {
	            EffectListService.TryCancelFirstEffectOfTypeOnTarget(target, eEffect.Pulse);
            }

			if (target is GameNPC)
			{
				GameNPC npc = (GameNPC)target;
				IOldAggressiveBrain aggroBrain = npc.Brain as IOldAggressiveBrain;
				if (aggroBrain != null)
				{
					if (Util.Chance(Spell.AmnesiaChance) && npc.TargetObject != null && npc.TargetObject is GameLiving living)
					{
						aggroBrain.ClearAggroList();
						aggroBrain.AddToAggroList(living, 1);
					}
						
				}
			}
		}

		/// <summary>
		/// When spell was resisted
		/// </summary>
		/// <param name="target">the target that resisted the spell</param>
		protected override void OnSpellResisted(GameLiving target)
		{
			base.OnSpellResisted(target);
			if (Spell.CastTime == 0)
			{
				// start interrupt even for resisted instant amnesia
				target.StartInterruptTimer(target.SpellInterruptDuration, AttackData.eAttackType.Spell, Caster);
			}
		}

		// constructor
		public AmnesiaSpellHandler(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) {}
	}
}
