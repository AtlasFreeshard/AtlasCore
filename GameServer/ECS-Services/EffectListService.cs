using DOL.GS.Spells;
using System.Collections.Generic;
using System;
using System.Numerics;
using ECS.Debug;

namespace DOL.GS
{
    public static class EffectListService
    {
        private const string ServiceName = "EffectListService";

        static EffectListService()
        {
            //This should technically be the world manager
            EntityManager.AddService(typeof(EffectListService));
        }

        public static void Tick(long tick)
        {
            Diagnostics.StartPerfCounter(ServiceName);

            foreach (var living in EntityManager.GetLivingByComponent(typeof(EffectListComponent)))
            {
                HandleEffects(tick, living);
            }

            Diagnostics.StopPerfCounter(ServiceName);               
        }

        private static void HandleEffects(long tick, GameLiving living)
        {
            if (living?.effectListComponent?.Effects.Count > 0)
            {
                foreach (var effects in living.effectListComponent.Effects.Values)
                {
                    foreach (var effect in effects)
                    {
                        if (!effect.Owner.IsAlive)
                        {
                            EffectService.RequestCancelEffect(effect);
                            continue;
                        }

                        if (tick > effect.ExpireTick && !effect.SpellHandler.Spell.IsConcentration)
                        {
                            if (effect.EffectType == eEffect.Pulse && effect.SpellHandler.Caster.LastPulseCast == effect.SpellHandler.Spell)
                            {
                                if (effect.SpellHandler.Spell.IsHarmful)
                                {
                                    ((SpellHandler)effect.SpellHandler).SendCastAnimation();

                                }
                                effect.SpellHandler.StartSpell(null);
                                effect.ExpireTick += effect.PulseFreq;
                            }
                            else
                            {
                                if (effect.EffectType == eEffect.Bleed)
                                    effect.Owner.TempProperties.removeProperty(StyleBleeding.BLEED_VALUE_PROPERTY);

                                if (effect.SpellHandler.Spell.IsPulsing && effect.SpellHandler.Caster.LastPulseCast == effect.SpellHandler.Spell &&
                                    effect.ExpireTick >= effect.LastTick + effect.PulseFreq)
                                {
                                    //Add time to effect to make sure the spell refreshes instead of cancels
                                    effect.ExpireTick += GameLoop.TickRate;
                                    effect.LastTick = GameLoop.GameLoopTime;
                                }
                                else
                                {
                                    EffectService.RequestCancelEffect(effect);
                                }
                            }
                        }
                        if (effect.EffectType == eEffect.DamageOverTime || effect.EffectType == eEffect.Bleed)
                        {
                            if (effect.LastTick == 0)
                            {
                                EffectService.OnEffectPulse(effect);
                                effect.LastTick = GameLoop.GameLoopTime;
                            }
                            else if (tick > effect.PulseFreq + effect.LastTick)
                            {
                                EffectService.OnEffectPulse(effect);
                                effect.LastTick += effect.PulseFreq;
                            }
                        }
                        if (effect.SpellHandler.Spell.SpellType == (byte)eSpellType.SpeedDecrease)
                        {
                            if (tick > effect.NextTick)
                            {
                                double factor = 2.0 - (effect.Duration - effect.GetRemainingTimeForClient()) / (double)(effect.Duration >> 1);
                                if (factor < 0) factor = 0;
                                else if (factor > 1) factor = 1;

                                effect.Owner.BuffBonusMultCategory1.Set((int)eProperty.MaxSpeed, effect.SpellHandler, 1.0 - effect.SpellHandler.Spell.Value * factor * 0.01);

                                UnbreakableSpeedDecreaseSpellHandler.SendUpdates(effect.Owner);
                                effect.NextTick += effect.TickInterval;
                                if (factor <= 0)
                                    effect.ExpireTick = GameLoop.GameLoopTime - 1;
                            }
                        }
                        if (effect.SpellHandler.Spell.SpellType == (byte)eSpellType.HealOverTime && tick > effect.NextTick)
                        {
                            (effect.SpellHandler as HoTSpellHandler).OnDirectEffect(effect.Owner, effect.Effectiveness);
                            effect.NextTick += effect.PulseFreq;
                        }
                        if (effect.SpellHandler.Spell.SpellType == (byte)eSpellType.Confusion && tick > effect.NextTick)
                        {
                            if ((effect.SpellHandler as ConfusionSpellHandler).targetList.Count > 0)
                            {
                                GameNPC npc = effect.Owner as GameNPC;
                                npc.StopAttack();
                                npc.StopCurrentSpellcast();

                                GameLiving target = (effect.SpellHandler as ConfusionSpellHandler).targetList[Util.Random((effect.SpellHandler as ConfusionSpellHandler).targetList.Count - 1)] as GameLiving;

                                npc.StartAttack(target);
                            }
                        }
                        if (effect.SpellHandler.Spell.IsConcentration && tick > effect.NextTick)
                        {
                            if (!effect.SpellHandler.Caster.
                                IsWithinRadius(effect.Owner,
                                effect.SpellHandler.Spell.SpellType != (byte)eSpellType.EnduranceRegenBuff ? ServerProperties.Properties.BUFF_RANGE > 0 ? ServerProperties.Properties.BUFF_RANGE : 5000 : effect.SpellHandler.Spell.Range)
                                && !effect.IsDisabled)
                            {
                                EffectService.RequestDisableEffect(effect, true);
                            }
                            else if (effect.SpellHandler.Caster.IsWithinRadius(effect.Owner,
                                effect.SpellHandler.Spell.SpellType != (byte)eSpellType.EnduranceRegenBuff ? ServerProperties.Properties.BUFF_RANGE > 0 ? ServerProperties.Properties.BUFF_RANGE : 5000 : effect.SpellHandler.Spell.Range)
                                && effect.IsDisabled)
                            {
                                bool isBest = false;
                                if (effects.Count > 1)
                                {
                                    foreach (var eff in effects)
                                        if (effect.SpellHandler.Spell.Value > eff.SpellHandler.Spell.Value)
                                        {
                                            isBest = true;
                                            break;
                                        }
                                        else
                                            isBest = false;
                                }
                                if (isBest)
                                    EffectService.RequestDisableEffect(effect, false);
                            }

                            effect.NextTick += effect.PulseFreq;
                        }
                        if (effect.SpellHandler.Spell.SpellType == (byte)eSpellType.SpeedDecrease)
                        {
                            if (tick > effect.NextTick)
                            {
                                double factor = 2.0 - (effect.Duration - effect.GetRemainingTimeForClient()) / (double)(effect.Duration >> 1);
                                if (factor < 0) factor = 0;
                                else if (factor > 1) factor = 1;

                                effect.Owner.BuffBonusMultCategory1.Set((int)eProperty.MaxSpeed, effect.SpellHandler, 1.0 - effect.SpellHandler.Spell.Value * factor * 0.01);

                                UnbreakableSpeedDecreaseSpellHandler.SendUpdates(effect.Owner);
                                effect.NextTick += effect.TickInterval;
                                if (factor <= 0)
                                    effect.ExpireTick = GameLoop.GameLoopTime - 1;
                            }
                        }
                        if (effect.SpellHandler.Spell.SpellType == (byte)eSpellType.HealOverTime && tick > effect.NextTick)
                        {
                            (effect.SpellHandler as HoTSpellHandler).OnDirectEffect(effect.Owner, effect.Effectiveness);
                            effect.NextTick += effect.PulseFreq;
                        }
                        if (effect.SpellHandler.Spell.SpellType == (byte)eSpellType.Confusion && tick > effect.NextTick)
                        {
                            if ((effect.SpellHandler as ConfusionSpellHandler).targetList.Count > 0)
                            {
                                GameNPC npc = effect.Owner as GameNPC;
                                npc.StopAttack();
                                npc.StopCurrentSpellcast();

                                GameLiving target = (effect.SpellHandler as ConfusionSpellHandler).targetList[Util.Random((effect.SpellHandler as ConfusionSpellHandler).targetList.Count - 1)] as GameLiving;

                                npc.StartAttack(target);
                            }
                        }
                    }
                }
            }
        }
    }
}