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
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using Atlas.DataLayer.Models;
using DOL.Language;
using DOL.GS.PacketHandler;

namespace DOL.GS
{
	public class SpellCrafting : AdvancedCraftingSkill
	{
        protected override String Profession
        {
            get
            {
                return "CraftersProfession.Spellcrafter";
            }
        }

        private static readonly int[,] itemMaxBonusLevel =  // taken from mythic Spellcraft calculator
		{
			{0,1,1,1,1,1,1},
			{1,1,1,1,1,2,2},
			{1,1,1,2,2,2,2},
			{1,1,2,2,2,3,3},
			{1,2,2,2,3,3,4},
			{1,2,2,3,3,4,4},
			{2,2,3,3,4,4,5},
			{2,3,3,4,4,5,5},
			{2,3,3,4,5,5,6},
			{2,3,4,4,5,6,7},
			{2,3,4,5,6,6,7},
			{3,4,4,5,6,7,8},
			{3,4,5,6,6,7,9},
			{3,4,5,6,7,8,9},
			{3,4,5,6,7,8,10},
			{3,5,6,7,8,9,10},
			{4,5,6,7,8,10,11},
			{4,5,6,8,9,10,12},
			{4,6,7,8,9,11,12},
			{4,6,7,8,10,11,13},
			{4,6,7,9,10,12,13},
			{5,6,8,9,11,12,14},
			{5,7,8,10,11,13,15},
			{5,7,9,10,12,13,15},
			{5,7,9,10,12,14,16},
			{5,8,9,11,12,14,16},
			{6,8,10,11,13,15,17},
			{6,8,10,12,13,15,18},
			{6,8,10,12,14,16,18},
			{6,9,11,12,14,16,19},
			{6,9,11,13,15,17,20},
			{7,9,11,13,15,17,20},
			{7,10,12,14,16,18,21},
			{7,10,12,14,16,19,21},
			{7,10,12,14,17,19,22},
			{7,10,13,15,17,20,23},
			{8,11,13,15,17,20,23},
			{8,11,13,16,18,21,24},
			{8,11,14,16,18,21,24},
			{8,11,14,16,19,22,25},
			{8,12,14,17,19,22,26},
			{9,12,15,17,20,23,26},
			{9,12,15,18,20,23,27},
			{9,13,15,18,21,24,27},
			{9,13,16,18,21,24,28},
			{9,13,16,19,22,25,29},
			{10,13,16,19,22,25,29},
			{10,14,17,20,23,26,30},
			{10,14,17,20,23,27,31},
			{10,14,17,20,23,27,31},
			{10,15,18,21,24,28,32},
		};

        private static readonly int[] OCStartPercentages = { 0, 10, 20, 30, 50, 70 };
        private static readonly int[] ItemQualOCModifiers = { 0, 0, 6, 8, 10, 18, 26 };

		public SpellCrafting()
		{
			Icon = 0x0D;
			Name = LanguageMgr.GetTranslation(ServerProperties.Properties.SERV_LANGUAGE, 
				"Crafting.Name.Evocation");
			eSkill = eCraftingSkill.SpellCrafting;
		}

		#region Classic craft functions
		public override void GainCraftingSkillPoints(GamePlayer player, Recipe recipe)
		{
			if (Util.Chance(CalculateChanceToGainPoint(player, recipe.Level)))
			{
				player.GainCraftingSkill(eCraftingSkill.SpellCrafting, 1);

				if (player.GetCraftingSkillValue(eCraftingSkill.GemCutting) < subSkillCap)
	                player.GainCraftingSkill(eCraftingSkill.GemCutting, 1);

				player.Out.SendUpdateCraftingSkills();
			}
		}

		#endregion

		#region Requirement check

		/// <summary>
		/// This function is called when player accept the combine
		/// </summary>
		/// <param name="player"></param>
		/// <param name="item"></param>
		public override bool IsAllowedToCombine(GamePlayer player, InventoryItem item)
		{
			if (!base.IsAllowedToCombine(player, item)) return false;

			if (((InventoryItem)player.TradeWindow.TradeItems[0]).Model == 525) // first ingredient to combine is Dust => echantement
			{
				if (item.ItemTemplate.Level < 15)
				{
					player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language,
						"SpellCrafting.IsAllowedToCombine.NoEnchanted"), eChatType.CT_System,
						eChatLoc.CL_SystemWindow);

					return false;
				}

				lock (player.TradeWindow.Sync)
				{
					foreach (InventoryItem materialToCombine in player.TradeWindow.TradeItems)
					{
						if (materialToCombine.Model != 525)
						{
							player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language,
								"SpellCrafting.IsAllowedToCombine.FalseMaterial",
								materialToCombine.Name),
								eChatType.CT_System, eChatLoc.CL_SystemWindow);

							return false;
						}
					}
				}
			}
			else
			{
				if (item.Bonuses.Count >= 4)
                {
					player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "SpellCrafting.IsAllowedToCombine.AlreadyImbued", item.Name), eChatType.CT_System, eChatLoc.CL_SystemWindow);
					return false;
				}

				lock (player.TradeWindow.Sync)
				{
					var bonusCount = item.Bonuses.Count;
					for (int i = 0; i < player.TradeWindow.ItemsCount; i++)
					{
						InventoryItem currentItem = (InventoryItem)player.TradeWindow.TradeItems[i];

						if (currentItem.ItemTemplate.ObjectType != (int)eObjectType.SpellcraftGem)
						{
							player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "SpellCrafting.IsAllowedToCombine.FalseItem"), eChatType.CT_System, eChatLoc.CL_SystemWindow);
							return false;
						}

						if (item.Bonuses.Any(x => x.BonusType == currentItem.Bonuses.First().BonusType))
						{
							player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "SpellCrafting.IsAllowedToCombine.NoSameBonus"), eChatType.CT_System, eChatLoc.CL_SystemWindow);
							return false;
						}

						if (bonusCount > 4)
						{
							player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "SpellCrafting.IsAllowedToCombine.DifferentTypes", item.Name), eChatType.CT_System, eChatLoc.CL_SystemWindow);
							return false;
						}
						bonusCount++;
					}
				}

				int bonusLevel = GetTotalImbuePoints(player, item);
				if (bonusLevel > player.GetCraftingSkillValue(eCraftingSkill.SpellCrafting) / 20)
				{
					player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "SpellCrafting.IsAllowedToCombine.NotEnoughSkill"), eChatType.CT_System, eChatLoc.CL_SystemWindow);
					return false;
				}

				if (bonusLevel - GetItemMaxImbuePoints(item) > 6)
				{
					player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "SpellCrafting.IsAllowedToCombine.NoMoreLevels", item.Name), eChatType.CT_System, eChatLoc.CL_SystemWindow);
					return false;
				}
			}

			return true;
		}

		#endregion

		#region Apply magical effect

		/// <summary>
		/// Apply all needed magical bonus to the item
		/// </summary>
		/// <param name="player"></param>
		/// <param name="item"></param>
		/// <returns></returns>
		protected override void ApplyMagicalEffect(GamePlayer player, InventoryItem item)
		{
			if (item == null || player.TradeWindow.TradeItems[0] == null) return; // be sure at least one item in each side

			if (((InventoryItem)player.TradeWindow.TradeItems[0]).Model == 525) // Echant item
			{
				ApplyMagicalDusts(player, item);
			}
			else if (((InventoryItem)player.TradeWindow.TradeItems[0]).ItemTemplate.ObjectType == (int)eObjectType.SpellcraftGem) // Spellcraft item
			{
				ApplySpellcraftGems(player, item);
			}

			if (item.ItemTemplate is ItemUnique)
			{
				GameServer.Instance.SaveDataObject(item);
				GameServer.Instance.SaveDataObject(item.ItemTemplate as ItemUnique);
			}
			else
			{
				ChatUtil.SendErrorMessage(player, "Spellcrafting error: Item was not an ItemUnique, crafting changes not saved to DB!");
				log.ErrorFormat("Spellcrafting error: Item {0} was not an ItemUnique for player {1}, crafting changes not saved to DB!", item.Id, player.Name);
			}
		}

		/// <summary>
		/// Apply all magical dust
		/// </summary>
		/// <param name="player"></param>
		/// <param name="item"></param>
		/// <returns></returns>
		private void ApplyMagicalDusts(GamePlayer player, InventoryItem item)
		{
			int spellCrafterLevel = player.GetCraftingSkillValue(eCraftingSkill.SpellCrafting);

			int bonusCap;
			if (spellCrafterLevel < 300 || item.ItemTemplate.Level < 15) bonusCap = 0;
			else if (spellCrafterLevel < 400 || item.ItemTemplate.Level < 20) bonusCap = 5;
			else if (spellCrafterLevel < 500 || item.ItemTemplate.Level < 25) bonusCap = 10;
			else if (spellCrafterLevel < 600 || item.ItemTemplate.Level < 30) bonusCap = 15;
			else if (spellCrafterLevel < 700 || item.ItemTemplate.Level < 35) bonusCap = 20;
			else if (spellCrafterLevel < 800 || item.ItemTemplate.Level < 40) bonusCap = 25;
			else if (spellCrafterLevel < 900 || item.ItemTemplate.Level < 45) bonusCap = 30;
			else bonusCap = 35;

			player.Inventory.BeginChanges();

			int bonusMod = item.ItemTemplate.ItemBonus;
			lock (player.TradeWindow.Sync)
			{
				foreach (InventoryItem gemme in player.TradeWindow.TradeItems)
				{
					switch (gemme.Name)
					{
						case "coppery imbued dust": bonusMod += 2; break;
						case "coppery enchanted dust": bonusMod += 4; break;
						case "coppery glowing dust": bonusMod += 6; break;
						case "coppery ensorcelled dust": bonusMod += 8; break;

						case "silvery imbued dust": bonusMod += 10; break;
						case "silvery enchanted dust": bonusMod += 12; break;
						case "silvery glowing dust": bonusMod += 14; break;
						case "silvery ensorcelled dust": bonusMod += 16; break;

						case "golden imbued dust": bonusMod += 18; break;
						case "golden enchanted dust": bonusMod += 20; break;
						case "golden glowing dust": bonusMod += 22; break;
						case "golden ensorcelled dust": bonusMod += 24; break;

						case "mithril imbued dust": bonusMod += 26; break;
						case "mithril enchanted dust": bonusMod += 28; break;
						case "mithril glowing dust": bonusMod += 30; break;
						case "mithril ensorcelled dust": bonusMod += 32; break;

						case "platinum imbued dust": bonusMod += 34; break;
						case "platinum enchanted dust": bonusMod += 36; break;
						case "platinum glowing dust": bonusMod += 38; break;
						case "platinum ensorcelled dust": bonusMod += 40; break;

					}
					player.Inventory.RemoveCountFromStack(gemme, 1);
					InventoryLogging.LogInventoryAction(player, "(craft)", eInventoryActionType.Craft, gemme.ItemTemplate);
				}
			}

			item.ItemTemplate.ItemBonus = Math.Min(bonusMod, bonusCap);

			player.Inventory.CommitChanges();
		}

		/// <summary>
		/// Apply all spellcraft gems bonus
		/// </summary>
		/// <param name="player"></param>
		/// <param name="item"></param>
		/// <returns></returns>
		private void ApplySpellcraftGems(GamePlayer player, InventoryItem item)
		{
			int maxBonusLevel = GetItemMaxImbuePoints(item);
			int bonusLevel = GetTotalImbuePoints(player, item);

			int sucessChances = 100;
			if (bonusLevel > maxBonusLevel) sucessChances = CalculateChanceToOverchargeItem(player, item, maxBonusLevel, bonusLevel);
			int destroyChance = 100 - CalculateChanceToPreserveItem(player, item, maxBonusLevel, bonusLevel);


			GamePlayer tradePartner = player.TradeWindow.Partner;

			if (Util.Chance(sucessChances))
			{
				lock (player.TradeWindow.Sync)
				{
					player.Inventory.BeginChanges();
					foreach (InventoryItem gem in (ArrayList)player.TradeWindow.TradeItems.Clone())
					{
						var bonus = item.Bonuses.FirstOrDefault(x => x.BonusOrder == 1);

						if (item.Bonuses.Count < 4)
                        {
							bonus = new ItemBonus()
							{
								BonusOrder = item.Bonuses.Count + 1,
								BonusType = gem.Bonuses.First().BonusType,
								BonusValue = gem.Bonuses.First().BonusValue
							};
                        }
						player.Inventory.RemoveCountFromStack(gem, 1);
						InventoryLogging.LogInventoryAction(player, "(craft)", eInventoryActionType.Craft, gem.ItemTemplate);
					}
					player.Inventory.CommitChanges();
				}

				if (tradePartner != null)
				{
					tradePartner.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "SpellCrafting.ApplySpellcraftGems.ImbuedItem", player.Name), eChatType.CT_Important, eChatLoc.CL_SystemWindow);
				}
				player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "SpellCrafting.ApplySpellcraftGems.ImbuedItem", player.Name), eChatType.CT_Important, eChatLoc.CL_SystemWindow);
			}
			else if (Util.Chance(destroyChance))
			{
				player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "SpellCrafting.ApplySpellcraftGems.PowerExplodes"), eChatType.CT_Important, eChatLoc.CL_SystemWindow);

				lock (player.TradeWindow.Sync)
				{
					player.Inventory.BeginChanges();
                    // Luhz Crafting Update:
                    // The base item is no longer lost when spellcrafting explodes - only gems are destroyed.
                    foreach (InventoryItem gem in (ArrayList)player.TradeWindow.TradeItems.Clone())
                    {
                        if (gem.ItemTemplate.ObjectType == (int)eObjectType.SpellcraftGem)
						{
                            player.Inventory.RemoveCountFromStack(gem, 1);
							InventoryLogging.LogInventoryAction(player, "(craft)", eInventoryActionType.Craft, gem.ItemTemplate);
						}
                    }
                    player.Inventory.CommitChanges();
				}

				player.Emote(eEmote.SpellGoBoom);
				player.Health = 0;
				player.Die(player); // On official you take damages
				player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "SpellCrafting.ApplySpellcraftGems.Failed"), eChatType.CT_Important, eChatLoc.CL_SystemWindow);

				if (tradePartner != null)
				{
					if (Util.Chance(40))
					{
						tradePartner.Emote(eEmote.SpellGoBoom);
						tradePartner.Health = 0;
						tradePartner.Die(player);
					}
					tradePartner.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "SpellCrafting.ApplySpellcraftGems.Failed"), eChatType.CT_Important, eChatLoc.CL_SystemWindow);
				}
			}
			else
			{
				player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "SpellCrafting.ApplySpellcraftGems.Failed"), eChatType.CT_Important, eChatLoc.CL_SystemWindow);
				if (tradePartner != null)
				{
					tradePartner.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "SpellCrafting.ApplySpellcraftGems.Failed"), eChatType.CT_Important, eChatLoc.CL_SystemWindow);
				}
			}
		}

		#endregion

		# region Show informations to player

		/// <summary>
		/// Shaw to player all infos about the current spellcraft
		/// </summary>
		/// <param name="player"></param>
		/// <param name="item"></param>
		public virtual void ShowSpellCraftingInfos(GamePlayer player, InventoryItem item)
		{
			if (((InventoryItem)player.TradeWindow.TradeItems[0]).Model == 525) return; // check if applying dust

			ArrayList spellcraftInfos = new ArrayList(10);

			int totalItemCharges = GetItemMaxImbuePoints(item);
			int totalGemmesCharges = GetTotalImbuePoints(player, item);

			spellcraftInfos.Add(LanguageMgr.GetTranslation(player.Client.Account.Language, "SpellCrafting.ShowSpellCraftingInfos.TotalImbue", item.Name, totalItemCharges));
			spellcraftInfos.Add(LanguageMgr.GetTranslation(player.Client.Account.Language, "SpellCrafting.ShowSpellCraftingInfos.CurrentBonus", item.Name));

			foreach (var bonus in item.Bonuses.Where(x => x.BonusOrder <= 4))
            {
				spellcraftInfos.Add("\t" + SkillBase.GetPropertyName((eProperty)bonus.BonusType) + ": " + bonus.BonusValue + " " + ((bonus.BonusType >= (int)eProperty.Resist_First && bonus.BonusType <= (int)eProperty.Resist_Last) ? "%" : " pts"));
			}
			
			spellcraftInfos.Add(LanguageMgr.GetTranslation(player.Client.Account.Language, "SpellCrafting.ShowSpellCraftingInfos.GemBonuses"));
			lock (player.TradeWindow.Sync)
			{
				for (int i = 0; i < player.TradeWindow.ItemsCount; i++)
				{
					InventoryItem currentGem = (InventoryItem)player.TradeWindow.TradeItems[i];
					var bonus = currentGem.Bonuses.First();
					spellcraftInfos.Add("\t" + currentGem.Name + " - " + SkillBase.GetPropertyName((eProperty)bonus.BonusType) + ": (" + GetGemImbuePoints(bonus.BonusType, bonus.BonusValue) + ") " + bonus.BonusValue + " " + ((bonus.BonusType >= (int)eProperty.Resist_First && bonus.BonusType <= (int)eProperty.Resist_Last) ? "%" : "pts"));
				}
			}
			spellcraftInfos.Add(LanguageMgr.GetTranslation(player.Client.Account.Language, "SpellCrafting.ShowSpellCraftingInfos.ImbueCapacity", totalGemmesCharges, totalItemCharges));

			if (totalGemmesCharges > totalItemCharges)
			{
				int maxBonusLevel = GetItemMaxImbuePoints(item);
				int bonusLevel = GetTotalImbuePoints(player, item);
				spellcraftInfos.Add("\t");
				spellcraftInfos.Add(LanguageMgr.GetTranslation(player.Client.Account.Language, "SpellCrafting.ShowSpellCraftingInfos.Penality", GetOverchargePenality(totalItemCharges, totalGemmesCharges)));
				spellcraftInfos.Add("\t");
				spellcraftInfos.Add(LanguageMgr.GetTranslation(player.Client.Account.Language, "SpellCrafting.ShowSpellCraftingInfos.Success", CalculateChanceToOverchargeItem(player, item, maxBonusLevel, bonusLevel)));
			}

			GamePlayer partner = player.TradeWindow.Partner;
			foreach (string line in spellcraftInfos)
			{
				player.Out.SendMessage(line, eChatType.CT_Important, eChatLoc.CL_SystemWindow);
				if (partner != null) partner.Out.SendMessage(line, eChatType.CT_Important, eChatLoc.CL_SystemWindow);
			}

			if (totalGemmesCharges > totalItemCharges)
			{
				player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "SpellCrafting.ShowSpellCraftingInfos.Modified"), eChatType.CT_System, eChatLoc.CL_SystemWindow);
			}
		}

		# endregion

		#region Calcul functions

		/// <summary>
        /// Get the sucess chance to overcharge the item
		/// </summary>
		/// <param name="player"></param>
		/// <param name="item"></param>
		/// <param name="maxBonusLevel"></param>
		/// <param name="bonusLevel"></param>
		/// <returns></returns>
		protected int CalculateChanceToOverchargeItem(GamePlayer player, InventoryItem item, int maxBonusLevel, int bonusLevel)
		{
            // Luhz Crafting Update:
            // Overcharge success rate is now dependent only on item quality, points overcharged, and spellcrafter skill.
            // This formula is taken from Kort's Spellcrafting Calculator, which appears to be correct (for 1.87). Much Thanks!
            if(bonusLevel - maxBonusLevel > 5) return 0;
            if(bonusLevel - maxBonusLevel < 0) return 100;

            try {
            int success = 34 + ItemQualOCModifiers[item.ItemTemplate.Quality - 94];
            success -= OCStartPercentages[bonusLevel-maxBonusLevel];
            int skillbonus = (player.GetCraftingSkillValue(eCraftingSkill.SpellCrafting) / 10);
            if (skillbonus > 100) 
                skillbonus = 100;
            success += skillbonus;
            int fudgefactor = (int)(100.0 * ((skillbonus / 100.0 - 1.0) * (OCStartPercentages[bonusLevel-maxBonusLevel] / 200.0)));
            success += fudgefactor;
            if (success > 100)
                success = 100;
            return success;
            }
            catch(Exception)
            {
                // Array access exception: someone is trying to imbue 100 < item.Quality < 94
                // Probably a GM or someone testing; I suppose we should just let them.
                return 100;
            }

            
		}

		/// <summary>
		/// Get the maximum bonus level the item can hold
		/// </summary>
		/// <param name="item"></param>
		/// <returns></returns>
		protected int GetItemMaxImbuePoints(InventoryItem item)
		{
			if (item.ItemTemplate.Level > 51) return 32;
			if (item.ItemTemplate.Level < 1) return 0;
            // Luhz Crafting Update:
            // All items have MP level imbue points
            return itemMaxBonusLevel[item.ItemTemplate.Level - 1, 100 - 94];
		}

		/// <summary>
		/// Get gems bonus level
		/// </summary>
		/// <param name="player">player spellcrafting</param>
		/// <param name="item">item to calculate imbues for</param>
		/// <returns></returns>
		protected int GetTotalImbuePoints(GamePlayer player, InventoryItem item)
		{
			int totalGemBonus = 0;
			int biggerGemCharge = 0;
			foreach (var bonus in item.Bonuses.Where(x => x.BonusOrder <= 3))
			{
				var currentBonusImbuePoint = GetGemImbuePoints(bonus.BonusType, bonus.BonusValue);
				totalGemBonus += currentBonusImbuePoint;

				if (currentBonusImbuePoint > biggerGemCharge) 
					biggerGemCharge = currentBonusImbuePoint;
			}			

			lock (player.TradeWindow.Sync)
			{
				for (int i = 0; i < player.TradeWindow.TradeItems.Count; i++)
				{
					InventoryItem currentGem = (InventoryItem)player.TradeWindow.TradeItems[i];
					int currentGemCharge = GetGemImbuePoints(currentGem.Bonuses.First().BonusType, currentGem.Bonuses.First().BonusValue);
					if (currentGemCharge > biggerGemCharge) biggerGemCharge = currentGemCharge;

					totalGemBonus += currentGemCharge;
				}
			}
			totalGemBonus += biggerGemCharge;
			totalGemBonus /= 2;

			return (int)Math.Floor((double)totalGemBonus);
		}

		/// <summary>
		/// Get how much the gem use like bonus point
		/// </summary>
		/// <param name="bonusType"></param>
		/// <param name="bonusValue"></param>
		/// <returns></returns>
		protected int GetGemImbuePoints(int bonusType, int bonusValue)
		{
			int gemBonus;
			if (bonusType <= (int)eProperty.Stat_Last) gemBonus = (int)(((bonusValue - 1) * 2 / 3) + 1); //stat
			else if (bonusType == (int)eProperty.MaxMana) gemBonus = (int)((bonusValue * 2) - 2); //mana
			else if (bonusType == (int)eProperty.MaxHealth) gemBonus = (int)(bonusValue / 4); //HP
			else if (bonusType <= (int)eProperty.Resist_Last) gemBonus = (int)((bonusValue * 2) - 2);//resist
			else if (bonusType <= (int)eProperty.Skill_Last) gemBonus = (int)((bonusValue - 1) * 5);//skill
			else gemBonus = 1;// focus
			if (gemBonus < 1) gemBonus = 1;

			return gemBonus;
		}

		/// <summary>
		/// Get the % overcharge penality
		/// </summary>
		/// <param name="maxBonusLevel"></param>
		/// <param name="bonusLevel"></param>
		/// <returns></returns>
		protected int GetOverchargePenality(int maxBonusLevel, int bonusLevel)
		{
			int diff = bonusLevel - maxBonusLevel;
			if (diff == 1) return -10;
			else if (diff == 2) return -20;
			else if (diff == 3) return -30;
			else if (diff == 4) return -50;
			else return -70;
		}

		/// <summary>
        /// Get the chance to preserve item while overcharging
		/// </summary>
		/// <param name="player"></param>
		/// <param name="item"></param>
		/// <param name="maxBonusLevel"></param>
		/// <param name="bonusLevel"></param>
		/// <returns></returns>
		protected int CalculateChanceToPreserveItem(GamePlayer player, InventoryItem item, int maxBonusLevel, int bonusLevel)
		{
			int baseChance = 0;

			double gemModifier = 0;
			lock (player.TradeWindow.Sync)
			{
				foreach (InventoryItem gem in player.TradeWindow.TradeItems)
					gemModifier += (gem.ItemTemplate.Quality - 92) * 1.8;
			}

			gemModifier = gemModifier * item.ItemTemplate.Quality / 100;

			int intSkill = player.GetCraftingSkillValue(eCraftingSkill.SpellCrafting);
			int intSkillMod;
			if (intSkill < 50) { intSkillMod = -50; }
			else if (intSkill < 100) { intSkillMod = -45; }
			else if (intSkill < 150) { intSkillMod = -40; }
			else if (intSkill < 200) { intSkillMod = -35; }
			else if (intSkill < 250) { intSkillMod = -30; }
			else if (intSkill < 300) { intSkillMod = -25; }
			else if (intSkill < 350) { intSkillMod = -20; }
			else if (intSkill < 400) { intSkillMod = -15; }
			else if (intSkill < 450) { intSkillMod = -10; }
			else if (intSkill < 500) { intSkillMod = -5; }
			else if (intSkill < 550) { intSkillMod = 0; }
			else if (intSkill < 600) { intSkillMod = 5; }
			else if (intSkill < 650) { intSkillMod = 10; }
			else if (intSkill < 700) { intSkillMod = 15; }
			else if (intSkill < 750) { intSkillMod = 20; }
			else if (intSkill < 800) { intSkillMod = 25; }
			else if (intSkill < 850) { intSkillMod = 30; }
			else if (intSkill < 900) { intSkillMod = 35; }
			else if (intSkill < 950) { intSkillMod = 40; }
			else if (intSkill < 1000) { intSkillMod = 45; }
			else { intSkillMod = 50; }

			int finalChances = (int)(intSkillMod + gemModifier + baseChance);
			if (finalChances > 100) finalChances = 100;
			else if (finalChances < 0) finalChances = 0;

			return finalChances;
		}

		#endregion

	}
}
