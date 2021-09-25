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
using log4net;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Atlas.DataLayer.Models;

namespace DOL.GS.PacketHandler
{
	[PacketLib(182, GameClient.eClientVersion.Version182)]
	public class PacketLib182 : PacketLib181
	{
		/// <summary>
		/// Defines a logger for this class.
		/// </summary>
		private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

		/// <summary>
		/// Constructs a new PacketLib for Version 1.82 clients
		/// </summary>
		/// <param name="client">the gameclient this lib is associated with</param>
		public PacketLib182(GameClient client)
			: base(client)
		{
		}

		protected override void SendInventorySlotsUpdateRange(ICollection<int> slots, eInventoryWindowType windowType)
		{
			using (GSTCPPacketOut pak = new GSTCPPacketOut(GetPacketCode(eServerPackets.InventoryUpdate)))
			{
				pak.WriteByte((byte)(slots == null ? 0 : slots.Count));
				pak.WriteByte((byte)((m_gameClient.Player.IsCloakHoodUp ? 0x01 : 0x00) | (int)m_gameClient.Player.rangeAttackComponent.ActiveQuiverSlot)); //bit0 is hood up bit4 to 7 is active quiver
				pak.WriteByte((byte)m_gameClient.Player.VisibleActiveWeaponSlots);
				pak.WriteByte((byte)windowType);
				if (slots != null)
				{
					foreach (int updatedSlot in slots)
					{
						if (updatedSlot >= (int)eInventorySlot.Consignment_First && updatedSlot <= (int)eInventorySlot.Consignment_Last)
							pak.WriteByte((byte)(updatedSlot - (int)eInventorySlot.Consignment_First + (int)eInventorySlot.HousingInventory_First));
						else
							pak.WriteByte((byte)(updatedSlot));
	
						InventoryItem item = null;
						item = m_gameClient.Player.Inventory.GetItem((eInventorySlot)updatedSlot);
	
						if (item == null)
						{
							pak.Fill(0x00, 19);
							continue;
						}
						pak.WriteByte((byte)item.ItemTemplate.Level);
	
						int value1; // some object types use this field to display count
						int value2; // some object types use this field to display count
						switch (item.ItemTemplate.ObjectType)
						{
							case (int)eObjectType.Arrow:
							case (int)eObjectType.Bolt:
							case (int)eObjectType.Poison:
							case (int)eObjectType.GenericItem:
								value1 = item.Count;
								value2 = item.ItemTemplate.SPD_ABS;
								break;
							case (int)eObjectType.Thrown:
								value1 = item.ItemTemplate.DPS_AF;
								value2 = item.Count;
								break;
							case (int)eObjectType.Instrument:
								value1 = (item.ItemTemplate.DPS_AF == 2 ? 0 : item.ItemTemplate.DPS_AF);
								value2 = 0;
								break; // unused
							case (int)eObjectType.Shield:
								value1 = item.ItemTemplate.TypeDamage;
								value2 = item.ItemTemplate.DPS_AF;
								break;
							case (int)eObjectType.AlchemyTincture:
							case (int)eObjectType.SpellcraftGem:
								value1 = 0;
								value2 = 0;
								/*
								must contain the quality of gem for spell craft and think same for tincture
								*/
								break;
							case (int)eObjectType.HouseWallObject:
							case (int)eObjectType.HouseFloorObject:
							case (int)eObjectType.GardenObject:
								value1 = 0;
								value2 = item.ItemTemplate.SPD_ABS;
								/*
								Value2 byte sets the width, only lower 4 bits 'seem' to be used (so 1-15 only)
	
								The byte used for "Hand" (IE: Mini-delve showing a weapon as Left-Hand
								usabe/TwoHanded), the lower 4 bits store the height (1-15 only)
								*/
								break;
	
							default:
								value1 = item.ItemTemplate.DPS_AF;
								value2 = item.ItemTemplate.SPD_ABS;
								break;
						}
						pak.WriteByte((byte)value1);
						pak.WriteByte((byte)value2);
	
						if (item.ItemTemplate.ObjectType == (int)eObjectType.GardenObject)
							pak.WriteByte((byte)(item.ItemTemplate.DPS_AF));
						else
							pak.WriteByte((byte)(item.ItemTemplate.Hand << 6));
						pak.WriteByte((byte)((item.ItemTemplate.TypeDamage > 3 ? 0 : item.ItemTemplate.TypeDamage << 6) | item.ItemTemplate.ObjectType));
						pak.WriteShort((ushort)item.ItemTemplate.Weight);
						pak.WriteByte(item.ConditionPercent); // % of con
						pak.WriteByte(item.DurabilityPercent); // % of dur
						pak.WriteByte((byte)item.ItemTemplate.Quality); // % of qua
						pak.WriteByte((byte)item.ItemTemplate.ItemBonus); // % bonus
						pak.WriteShort((ushort)item.Model);
						pak.WriteByte((byte)item.Extension);
						int flag = 0;
						if (item.Emblem != 0)
						{
							pak.WriteShort((ushort)item.Emblem);
							flag |= (item.Emblem & 0x010000) >> 16; // = 1 for newGuildEmblem
						}
						else
							pak.WriteShort((ushort)item.Color);
	//						flag |= 0x01; // newGuildEmblem
						flag |= 0x02; // enable salvage button

						// Enable craft button if the item can be modified and the player has alchemy or spellcrafting
						eCraftingSkill skill = CraftingMgr.GetCraftingSkill(item);
						switch (skill)
						{
							case eCraftingSkill.ArmorCrafting:
							case eCraftingSkill.Fletching:
							case eCraftingSkill.Tailoring:
							case eCraftingSkill.WeaponCrafting:
								if (m_gameClient.Player.CraftingSkills.ContainsKey(eCraftingSkill.Alchemy)
									|| m_gameClient.Player.CraftingSkills.ContainsKey(eCraftingSkill.SpellCrafting))
									flag |= 0x04; // enable craft button
								break;

							default:
								break;
						}

						ushort icon1 = 0;
						ushort icon2 = 0;
						string spell_name1 = "";
						string spell_name2 = "";
						if (item.ItemTemplate.ObjectType != (int)eObjectType.AlchemyTincture)
						{
							SpellLine chargeEffectsLine = SkillBase.GetSpellLine(GlobalSpellsLines.Item_Effects);
	
							if (chargeEffectsLine != null)
							{
								foreach (var itemSpell in item.Spells)
                                {
									Spell spell = SkillBase.FindSpell(itemSpell.SpellID, chargeEffectsLine);
									if (spell != null)
									{
										flag |= 0x08;
										icon1 = spell.Icon;
										spell_name1 = spell.Name; // or best spl.Name ?
									}
								}
							}
						}
						pak.WriteByte((byte)flag);
						if ((flag & 0x08) == 0x08)
						{
							pak.WriteShort((ushort)icon1);
							pak.WritePascalString(spell_name1);
						}
						if ((flag & 0x10) == 0x10)
						{
							pak.WriteShort((ushort)icon2);
							pak.WritePascalString(spell_name2);
						}
						pak.WriteByte((byte)item.ItemTemplate.Effect);
						string name = item.Name;
						if (item.Count > 1)
							name = item.Count + " " + name;
	                    if (item.SellPrice > 0)
	                    {
							if (ServerProperties.Properties.CONSIGNMENT_USE_BP)
	                            name += "[" + item.SellPrice.ToString() + " BP]";
	                        else
	                            name += "[" + Money.GetString(item.SellPrice) + "]";
	                    }
						pak.WritePascalString(name);
					}
				}
				SendTCP(pak);
			}
		}
	}
}
