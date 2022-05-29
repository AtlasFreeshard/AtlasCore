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
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using DOL.AI;
using DOL.AI.Brain;
using DOL.Database;
using DOL.Events;
using DOL.GS;
using DOL.GS.Effects;
using DOL.GS.Housing;
using DOL.GS.Keeps;
using DOL.GS.Movement;
using DOL.GS.PacketHandler;
using DOL.GS.Quests;
using DOL.GS.Spells;
using DOL.GS.Styles;
using DOL.GS.Utils;
using DOL.Language;
using DOL.GS.ServerProperties;
using FiniteStateMachine;

namespace DOL.GS
{
	/// <summary>
	/// This class is the baseclass for all Non Player Characters like
	/// Monsters, Merchants, Guards, Steeds ...
	/// </summary>
	public class GameNPC : GameLiving, ITranslatableObject
	{
		public static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

		/// <summary>
		/// Constant for determining if already at a point
		/// </summary>
		/// <remarks>
		/// This helps to reduce the turning of an npc while fighting or returning to a spawn
		/// Tested - min distance for mob sticking within combat range to player is 25
		/// </remarks>
		public const int CONST_WALKTOTOLERANCE = 25;

		private int m_databaseLevel;

		
		#region Formations/Spacing

		//Space/Offsets used in formations
		// Normal = 1
		// Big = 2
		// Huge = 3
		private byte m_formationSpacing = 1;

		/// <summary>
		/// The Minions's x-offset from it's commander
		/// </summary>
		public byte FormationSpacing
		{
			get { return m_formationSpacing; }
			set
			{
				//BD range values vary from 1 to 3.  It is more appropriate to just ignore the
				//incorrect values than throw an error since this isn't a very important area.
				if (value > 0 && value < 4)
					m_formationSpacing = value;
			}
		}

		/// <summary>
		/// Used for that formation type if a GameNPC has a formation
		/// </summary>
		public enum eFormationType
		{
			// M = owner
			// x = following npcs
			//Line formation
			// M x x x
			Line,
			//Triangle formation
			//		x
			// M x
			//		x
			Triangle,
			//Protect formation
			//		 x
			// x  M
			//		 x
			Protect,
		}

		private eFormationType m_formation = eFormationType.Line;
		/// <summary>
		/// How the minions line up with the commander
		/// </summary>
		public eFormationType Formation
		{
			get { return m_formation; }
			set { m_formation = value; }
		}

		#endregion

		#region Sizes/Properties
		/// <summary>
		/// Holds the size of the NPC
		/// </summary>
		protected byte m_size;
		/// <summary>
		/// Gets or sets the size of the npc
		/// </summary>
		public byte Size
		{
			get { return m_size; }
			set
			{
				m_size = value;
				if (ObjectState == eObjectState.Active)
				{
					foreach (GamePlayer player in GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE))
						player.Out.SendModelAndSizeChange(this, Model, value);
					//					BroadcastUpdate();
				}
			}
		}

		public virtual LanguageDataObject.eTranslationIdentifier TranslationIdentifier
		{
			get { return LanguageDataObject.eTranslationIdentifier.eNPC; }
		}

		/// <summary>
		/// Holds the translation id.
		/// </summary>
		protected string m_translationId = "";

		/// <summary>
		/// Gets or sets the translation id.
		/// </summary>
		public string TranslationId
		{
			get { return m_translationId; }
			set { m_translationId = (value == null ? "" : value); }
		}

		/// <summary>
		/// Gets or sets the model of this npc
		/// </summary>
		public override ushort Model
		{
			get { return base.Model; }
			set
			{
				base.Model = value;
				if (ObjectState == eObjectState.Active)
				{
					foreach (GamePlayer player in GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE))
						player.Out.SendModelChange(this, Model);
				}
			}
		}

		/// <summary>
		/// Gets or sets the heading of this NPC
		/// </summary>
		public override ushort Heading
		{
			get { return base.Heading; }
			set
			{
				if (IsTurningDisabled)
					return;
				ushort oldHeading = base.Heading;
				base.Heading = value;
				if (base.Heading != oldHeading)
					BroadcastUpdate();
			}
		}

		/// <summary>
		/// Gets or sets the level of this NPC
		/// </summary>
		public override byte Level
		{
			get { return base.Level; }
			set
			{
				bool bMaxHealth = (m_health == MaxHealth);

				if (Level != value)
				{
					if (Level < 1 && ObjectState == eObjectState.Active)
					{
						// This is a newly created NPC, so notify nearby players of its creation
						foreach (GamePlayer player in GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE))
						{
							player.Out.SendNPCCreate(this);
							if (m_inventory != null)
								player.Out.SendLivingEquipmentUpdate(this);
						}
					}

					base.Level = value;
					AutoSetStats();  // Recalculate stats when level changes
				}
				else
					base.Level = value;

				if (bMaxHealth)
					m_health = MaxHealth;
			}
		}

		/// <summary>
		/// Auto set stats based on DB entry, npcTemplate, and level.
		/// </summary>
		public virtual void AutoSetStats()
		{
			AutoSetStats(null);
		}

		/// <summary>
		/// Auto set stats based on DB entry, npcTemplate, and level.
		/// </summary>
		/// <param name="dbMob">Mob DB entry to load stats from, retrieved from DB if null</param>
		public virtual void AutoSetStats(Mob dbMob = null)
		{
			// Don't set stats for mobs until their level is set
			if (Level < 1)
				return;

			GameNPC npc = this;
			Mob mob = dbMob;
			if (mob == null && !string.IsNullOrEmpty(InternalID))
				// This should only happen when a GM command changes level on a mob with no npcTemplate,
				mob = GameServer.Database.FindObjectByKey<Mob>(InternalID);
			if (mob != null && mob.NPCTemplateID != -1)
				NPCTemplate = NpcTemplateMgr.GetTemplate(mob.NPCTemplateID);

			var mobTypeName = npc?.GetType().FullName;
			var isBoss = (mob != null && (npc.GetType().IsSubclassOf(typeof(GameEpicBoss)) ||
			                                      npc.GetType() == typeof(GameEpicBoss) || 
			                                      npc.GetType().IsSubclassOf(typeof(GameDragon))));
			var isEpic = (mob != null && npc.GetType().FullName == "DOL.GS.GameEpicNPC");
			
			// Multipliers, base levels, and placeholder vars
			const double regMultiplier = 1; // Backup multiplier value for regular mob stats
			const double epicMultiplier = 2;
			const double bossMultiplier = 5;
			const short mobBase = 29; // Static variable to autoset stat bases, where needed

			// Mob stats must be set above 0 in order to scale with level
			// If stats are 0, then first set to stat base
			// This is intended for '/mob levela', which forces stats to scale with level
			// NOTE: We should just avoid using anything other than NPC templates
			// Individual autoset values for base stats
			short autoStr = (Properties.MOB_AUTOSET_STR_BASE > 0) ? Properties.MOB_AUTOSET_STR_BASE : mobBase;
			short autoCon = (Properties.MOB_AUTOSET_CON_BASE > 0) ? Properties.MOB_AUTOSET_CON_BASE : mobBase;
			short autoDex = (Properties.MOB_AUTOSET_DEX_BASE > 0) ? Properties.MOB_AUTOSET_DEX_BASE : mobBase;
			short autoQui = (Properties.MOB_AUTOSET_QUI_BASE > 0) ? Properties.MOB_AUTOSET_QUI_BASE : mobBase;
			short autoInt = (Properties.MOB_AUTOSET_INT_BASE > 0) ? Properties.MOB_AUTOSET_INT_BASE : mobBase;
			short autoEmp = (Properties.MOB_AUTOSET_EMP_BASE > 0) ? Properties.MOB_AUTOSET_EMP_BASE : mobBase;
			short autoCha = (Properties.MOB_AUTOSET_CHA_BASE > 0) ? Properties.MOB_AUTOSET_CHA_BASE : mobBase;
			short autoPie = (Properties.MOB_AUTOSET_PIE_BASE > 0) ? Properties.MOB_AUTOSET_PIE_BASE : mobBase;
			
			// Individual autoset values for base stat multipliers
			double multStr = (Properties.MOB_AUTOSET_STR_MULTIPLIER > 0) ? Properties.MOB_AUTOSET_STR_MULTIPLIER : (short)regMultiplier; // Server property value
			double multCon = (Properties.MOB_AUTOSET_CON_MULTIPLIER > 0) ? Properties.MOB_AUTOSET_CON_MULTIPLIER : (short)regMultiplier; // Server property value
			double multDex = (Properties.MOB_AUTOSET_DEX_MULTIPLIER > 0) ? Properties.MOB_AUTOSET_DEX_MULTIPLIER : (short)regMultiplier; // Server property value
			double multQui = (Properties.MOB_AUTOSET_QUI_MULTIPLIER > 0) ? Properties.MOB_AUTOSET_QUI_MULTIPLIER : (short)regMultiplier; // Server property value
			double multInt = (Properties.MOB_AUTOSET_INT_MULTIPLIER > 0) ? Properties.MOB_AUTOSET_INT_MULTIPLIER : (short)regMultiplier; // Server property value
			double multEmp = (Properties.MOB_AUTOSET_EMP_MULTIPLIER > 0) ? Properties.MOB_AUTOSET_EMP_MULTIPLIER : (short)regMultiplier; // Server property value
			double multCha = (Properties.MOB_AUTOSET_CHA_MULTIPLIER > 0) ? Properties.MOB_AUTOSET_CHA_MULTIPLIER : (short)regMultiplier; // Server property value
			double multPie = (Properties.MOB_AUTOSET_PIE_MULTIPLIER > 0) ? Properties.MOB_AUTOSET_PIE_MULTIPLIER : (short)regMultiplier; // Server property value
			
			if (isEpic)
			{
				autoStr = (short)(Properties.MOB_AUTOSET_STR_BASE * epicMultiplier);
				autoCon = (short)(Properties.MOB_AUTOSET_CON_BASE * epicMultiplier);
				autoDex = (short)(Properties.MOB_AUTOSET_DEX_BASE * epicMultiplier);
				autoQui = (short)(Properties.MOB_AUTOSET_QUI_BASE * epicMultiplier);
				autoInt = (short)(Properties.MOB_AUTOSET_INT_BASE * epicMultiplier);
				autoEmp = (short)(Properties.MOB_AUTOSET_EMP_BASE * epicMultiplier);
				autoCha = (short)(Properties.MOB_AUTOSET_CHA_BASE * epicMultiplier);
				autoPie = (short)(Properties.MOB_AUTOSET_PIE_BASE * epicMultiplier);
				
				multStr = (Properties.MOB_AUTOSET_STR_MULTIPLIER * epicMultiplier);
				multCon = (Properties.MOB_AUTOSET_CON_MULTIPLIER * epicMultiplier);
				multDex = (Properties.MOB_AUTOSET_DEX_MULTIPLIER * epicMultiplier);
				multQui = (Properties.MOB_AUTOSET_QUI_MULTIPLIER * epicMultiplier);
				multInt = (Properties.MOB_AUTOSET_INT_MULTIPLIER * epicMultiplier);
				multEmp = (Properties.MOB_AUTOSET_EMP_MULTIPLIER * epicMultiplier);
				multCha = (Properties.MOB_AUTOSET_CHA_MULTIPLIER * epicMultiplier);
				multPie = (Properties.MOB_AUTOSET_PIE_MULTIPLIER * epicMultiplier);
			}
			if (isBoss)
			{
				autoStr = (short)(Properties.MOB_AUTOSET_STR_BASE * bossMultiplier);
				autoCon = (short)(Properties.MOB_AUTOSET_CON_BASE * bossMultiplier);
				autoDex = (short)(Properties.MOB_AUTOSET_DEX_BASE * bossMultiplier);
				autoQui = (short)(Properties.MOB_AUTOSET_QUI_BASE * bossMultiplier);
				autoInt = (short)(Properties.MOB_AUTOSET_INT_BASE * bossMultiplier);
				autoEmp = (short)(Properties.MOB_AUTOSET_EMP_BASE * bossMultiplier);
				autoCha = (short)(Properties.MOB_AUTOSET_CHA_BASE * bossMultiplier);
				autoPie = (short)(Properties.MOB_AUTOSET_PIE_BASE * bossMultiplier);
				
				multStr = (Properties.MOB_AUTOSET_STR_MULTIPLIER * bossMultiplier);
				multCon = (Properties.MOB_AUTOSET_CON_MULTIPLIER * bossMultiplier);
				multDex = (Properties.MOB_AUTOSET_DEX_MULTIPLIER * bossMultiplier);
				multQui = (Properties.MOB_AUTOSET_QUI_MULTIPLIER * bossMultiplier);
				multInt = (Properties.MOB_AUTOSET_INT_MULTIPLIER * bossMultiplier);
				multEmp = (Properties.MOB_AUTOSET_EMP_MULTIPLIER * bossMultiplier);
				multCha = (Properties.MOB_AUTOSET_CHA_MULTIPLIER * bossMultiplier);
				multPie = (Properties.MOB_AUTOSET_PIE_MULTIPLIER * bossMultiplier);
			}
			
			// We have to check both the DB and template values to account for mobs changing levels.
			// Otherwise, high level mobs retain their stats when their level is lowered by a GM.
			if (NPCTemplate != null && NPCTemplate.ReplaceMobValues)
			{
				#region Base Level
				// This determines how to handle stat scaling for regular and epic NPCs with existing NPCTemplates.
				// Stats are scaled based on the lowest level parsed from NPCTemplate.Level rather than assuming level 1.
				// For example, NPCTemplate.Level is "12-15;20-25". AutoSetStats would then treat the current stats for the
				// NPCTemplate as the base values for the lowest level in this range, which is 12. It then uses the
				// multiplier (mobScale) to increase the stat based on the mob's level, if it is above the base level (12),
				// using the 'difference' variable.
				// A level 13 mob with this template has a difference of 1 level from the base level, and as such would have
				// the multiplier applied once (1 x multiplier) to all stats. A level 25 mob has a difference of 13 from the
				// base level (Level - baseLevel) and thus applies the multiplier 13 times (13 x multiplier).

				// We use the minimum level as the base level and max level for whatever else sounds fun
				// Default values
				var minLevel = Level;
				
				// First, we'll determine the template's base level, meaning the lowest level value found in NPCTemplate.Level
				// This assumes ranges and separators are found in the value, which is then split. The lowest value is then
				// used as the level for which NPC template stats apply before autoscaling is factored in.
				if (!string.IsNullOrEmpty(NPCTemplate.Level))
				{
					// Look for separators and ranges
					if (NPCTemplate.Level.Contains(';') || NPCTemplate.Level.Contains('-'))
					{
						var split = Util.SplitCSV(NPCTemplate.Level, true); // Create list and remove separators or express ranges
						var levelRange = new List<byte>(); // Store list of levels from which to grab minimum int value
						if (levelRange.Count > 0)
							levelRange.Clear(); // Pre-usage cleanup
						
						// If we've added level entries to the split list
						if (split.Count > 0)
						{
							foreach (var level in split)
								if (byte.TryParse(level, out var levelEntry) && !levelRange.Contains(levelEntry))
									levelRange.Add(levelEntry);

							if (levelRange.Count > 0)
								minLevel = levelRange.AsQueryable().Min();
						}
						// Try one more time to parse NPCTemplate.Level
						else if (byte.TryParse(NPCTemplate.Level, out var tryLevel))
							minLevel = tryLevel; // Grab the lowest value
					}
					// No separators or level ranges detected, so just treat as a single level entry
					else if (byte.TryParse(NPCTemplate.Level, out var onlyLevel))
						minLevel = onlyLevel; // Grab the lowest value
				}
				// Use the mob's values instead
				else
					minLevel = Level;

				// Check mob's current level against the base level before we scale further (add these results to the existing stats
				// If the mob is higher level than baseLevel, then scale more
				// The reason for this condition is that ideally the existing stat values for a mob or template are to be treated as "base level" values, meaning
				// if a template's Strength is set to 40, then we'll take that value and use it for the lowest level range before autoscaling to the highest
				// mob level for that template
				// Subtract level difference and multiply stats against multiplier
				// If 0 or less, then don't add anything more
				var lvlDiff = (short)(Level - minLevel);
				if (lvlDiff < 0)
					lvlDiff = 0;
				var maxLevel = 1.5;
				if (isEpic)
					maxLevel = epicMultiplier;
				if (isBoss)
					maxLevel = bossMultiplier;
				#endregion Base Level

				// Compare mob's existing stats against the NPC template
				// If template stats are lower than the mob's, then use mob stats
				// This is a precaution in case the template is not current (so we don't accidentally break all the mobs)
				if (mob != null)
				{
					Strength = (mob.Strength <= NPCTemplate.Strength && NPCTemplate.Strength < (autoStr + Level) + (minLevel * maxLevel)) ? (short)(NPCTemplate.Strength + (lvlDiff * multStr)) : mob.Strength;
					if ((Strength != NPCTemplate.Strength &&
					     mob.Strength > (autoCon + Level) + (minLevel * maxLevel)) || Level >= 70)
						Strength = (short) (NPCTemplate.Strength + (lvlDiff * multStr));
					Constitution = (mob.Constitution <= NPCTemplate.Constitution && NPCTemplate.Constitution < (autoCon + Level) + (minLevel * maxLevel)) ? (short)(NPCTemplate.Constitution + (lvlDiff * multCon)) : mob.Constitution;
					Dexterity = (mob.Dexterity <= NPCTemplate.Dexterity && NPCTemplate.Strength < (autoDex + Level) + (minLevel * maxLevel)) ? (short)(NPCTemplate.Dexterity + (lvlDiff * multDex)) : mob.Dexterity;
					Quickness = (mob.Quickness <= NPCTemplate.Quickness) ? (short)(NPCTemplate.Quickness + (lvlDiff * multQui)) : mob.Quickness;
					Empathy = (mob.Empathy <= NPCTemplate.Empathy && NPCTemplate.Empathy < (autoEmp + Level) + (minLevel * maxLevel)) ? (short)(NPCTemplate.Empathy + (lvlDiff * multEmp)) : mob.Empathy;
					Intelligence = (mob.Intelligence <= NPCTemplate.Intelligence) ? (short)(NPCTemplate.Intelligence + (lvlDiff * multInt)) : mob.Intelligence;
					Charisma = (mob.Charisma <= NPCTemplate.Charisma) ?(short)(NPCTemplate.Charisma + (lvlDiff * multCha)) : mob.Charisma;
					Piety = (mob.Piety <= NPCTemplate.Piety) ? (short)(NPCTemplate.Piety + (lvlDiff * multPie)) : mob.Piety;
				}
				// If mob is null (i.e., just created using '/mob create'), then use the NPC template where possible
				else
				{
					Strength = (autoStr <= NPCTemplate.Strength && NPCTemplate.Strength < (autoStr + Level) + (minLevel * maxLevel)) ? (short)(NPCTemplate.Strength + (lvlDiff * multStr)) : (short)(autoStr + (minLevel * multStr));
					Constitution = (autoCon <= NPCTemplate.Constitution && NPCTemplate.Constitution < (autoCon + Level) + (minLevel * maxLevel)) ? (short)(NPCTemplate.Constitution + (lvlDiff * multCon)) : (short)(autoCon + (minLevel * multCon));
					Dexterity = (autoDex <= NPCTemplate.Dexterity && NPCTemplate.Dexterity < (autoDex + Level) + (minLevel * maxLevel)) ? (short)(NPCTemplate.Dexterity + (lvlDiff * multDex)) : (short)(autoDex + (minLevel * multDex));
					Quickness = (autoQui <= NPCTemplate.Quickness) ? (short)(NPCTemplate.Quickness + (lvlDiff * multQui)) : (short)(autoQui + (minLevel * multQui));
					Empathy = (autoEmp <= NPCTemplate.Empathy && NPCTemplate.Empathy < (autoEmp + Level) + (minLevel * maxLevel)) ? (short)(NPCTemplate.Empathy + (lvlDiff * multEmp)) : (short)(autoEmp + (minLevel * multEmp));
					Intelligence = (autoInt <= NPCTemplate.Intelligence) ? (short)(NPCTemplate.Intelligence + (lvlDiff * multInt)) : (short)(autoInt + (minLevel * multInt));
					Charisma = (autoCha <= NPCTemplate.Charisma) ? (short)(NPCTemplate.Charisma + (lvlDiff * multCha)) : (short)(autoCha + (minLevel * multCha));
					Piety = (autoPie <= NPCTemplate.Piety) ? (short)(NPCTemplate.Piety + (lvlDiff * multPie)) : (short)(autoPie + (minLevel * multPie));
				}
			}
			else
			{
				if (mob != null && !isBoss)
				{
					if (mob.Strength >= 1 || (mob.Strength < (autoStr + Level) + (Level * 2) && Level < 65))
						Strength = mob.Strength;
					else
						Strength = (short)(autoStr + (Level * multStr));
					if (mob.Constitution >= 1 || (mob.Constitution < (autoCon + Level) + (Level * 2) && Level < 65))
						Constitution = mob.Constitution;
					else
						Empathy = (short)(autoEmp + (Level * multEmp));
					Quickness = mob.Quickness;
					Dexterity = mob.Dexterity;
					Intelligence = mob.Intelligence;
					if (mob.Empathy >= 1 || (mob.Empathy < (autoEmp + Level) + (Level * 2) && Level < 65))
						Empathy = mob.Empathy;
					else
						Empathy = (short)(autoEmp + (Level * multEmp));
					Piety = mob.Piety;
					Charisma = mob.Charisma;
				}
				else if (mob == null)
				{
					// This is usually a mob about to be loaded from its DB entry,
					//	but it could also be a new mob created by a GM command, so we need to assign stats.
					Strength = 0;
					Constitution = 0;
					Quickness = 0;
					Dexterity = 0;
					Intelligence = 0;
					Empathy = 0;
					Piety = 0;
					Charisma = 0;
				}
				else if (isBoss)
				{
					Strength = mob.Strength;
					Constitution = mob.Constitution;
					Quickness = mob.Quickness;
					Dexterity = mob.Dexterity;
					Intelligence = mob.Intelligence;
					Empathy = mob.Empathy;
					Piety = mob.Piety;
					Charisma = mob.Charisma;
					return;
				}

				// Mob stats must be set above 0 in order to scale with level
				if (!isBoss)
				{
					if (Strength <= 1 || (Strength > (autoStr + Level) + (Level * 2) && Level < 65)) 
						Strength = autoStr;
					Strength += (Level > 1) ? (short) (Level * multStr) : (short) multStr;
					if (Constitution <= 1 || (Constitution > (autoCon + Level) + (Level * 2) && Level < 65))
						Constitution = autoCon;
					Constitution += (Level > 1) ? (short) (Level * multCon) : (short) multCon;
					if (Dexterity <= 1 || (Dexterity > (autoDex + Level) + (Level * 2) && Level < 65))
						Dexterity = autoDex;
					Dexterity += (Level > 1) ? (short) (Level * multDex) : (short) multDex;
					if (Quickness <= 1)
						Quickness = autoQui;
					Quickness += (Level > 1) ? (short) (Level * multQui) : (short) multQui;
					if (Empathy <= 1 || (Empathy > (autoEmp + Level) + (Level * 2) && Level < 65))
						Empathy = autoEmp;
					Empathy += (Level > 1) ? (short) (Level * multEmp) : (short) multEmp;
					if (Intelligence <= 1)
						Intelligence = autoInt;
					Intelligence += (Level > 1) ? (short) (Level * multInt) : (short) multInt;
					if (Charisma <= 1)
						Charisma = autoCha;
					Charisma += (Level > 1) ? (short) (Level * multCha) : (short) multCha;
					if (Piety <= 1)
						Piety = autoPie;
					Piety += (Level > 1) ? (short) (Level * multPie) : (short) multPie;
				}
			}
		}

		/// <summary>
		/// Gets or Sets the effective level of the Object
		/// </summary>
		public override int EffectiveLevel
		{
			get
			{
				IControlledBrain brain = Brain as IControlledBrain;
				if (brain != null)
					return brain.Owner.EffectiveLevel;
				return base.EffectiveLevel;
			}
		}

		/// <summary>
		/// Gets or sets the Realm of this NPC
		/// </summary>
		public override eRealm Realm
		{
			get
			{
				IControlledBrain brain = Brain as IControlledBrain;
				if (brain != null)
					return brain.Owner.Realm; // always realm of the owner
				return base.Realm;
			}
			set
			{
				base.Realm = value;
				if (ObjectState == eObjectState.Active)
				{
					foreach (GamePlayer player in GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE))
					{
						player.Out.SendNPCCreate(this);
						if (m_inventory != null)
							player.Out.SendLivingEquipmentUpdate(this);
					}
				}
			}
		}

		/// <summary>
		/// Gets or sets the name of this npc
		/// </summary>
		public override string Name
		{
			get { return base.Name; }
			set
			{
				base.Name = value;
				if (ObjectState == eObjectState.Active)
				{
					foreach (GamePlayer player in GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE))
					{
						player.Out.SendNPCCreate(this);
						if (m_inventory != null)
							player.Out.SendLivingEquipmentUpdate(this);
					}
				}
			}
		}

		/// <summary>
		/// Holds the suffix.
		/// </summary>
		private string m_suffix = string.Empty;
		/// <summary>
		/// Gets or sets the suffix.
		/// </summary>
		public string Suffix
		{
			get { return m_suffix; }
			set
			{
				if (value == null)
					m_suffix = string.Empty;
				else
				{
					if (value == m_suffix)
						return;
					else
						m_suffix = value;
				}
			}
		}

		/// <summary>
		/// Gets or sets the guild name
		/// </summary>
		public override string GuildName
		{
			get { return base.GuildName; }
			set
			{
				base.GuildName = value;
				if (ObjectState == eObjectState.Active)
				{
					foreach (GamePlayer player in GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE))
					{
						player.Out.SendNPCCreate(this);
						if (m_inventory != null)
							player.Out.SendLivingEquipmentUpdate(this);
					}
				}
			}
		}

		/// <summary>
		/// Holds the examine article.
		/// </summary>
		private string m_examineArticle = string.Empty;
		/// <summary>
		/// Gets or sets the examine article.
		/// </summary>
		public string ExamineArticle
		{
			get { return m_examineArticle; }
			set
			{
				if (value == null)
					m_examineArticle = string.Empty;
				else
				{
					if (value == m_examineArticle)
						return;
					else
						m_examineArticle = value;
				}
			}
		}

		/// <summary>
		/// Holds the message article.
		/// </summary>
		private string m_messageArticle = string.Empty;
		/// <summary>
		/// Gets or sets the message article.
		/// </summary>
		public string MessageArticle
		{
			get { return m_messageArticle; }
			set
			{
				if (value == null)
					m_messageArticle = string.Empty;
				else
				{
					if (value == m_messageArticle)
						return;
					else
						m_messageArticle = value;
				}
			}
		}

		private Faction m_faction = null;
		/// <summary>
		/// Gets the Faction of the NPC
		/// </summary>
		public Faction Faction
		{
			get { return m_faction; }
			set
			{
				m_faction = value;
			}
		}

		private ArrayList m_linkedFactions;
		/// <summary>
		/// The linked factions for this NPC
		/// </summary>
		public ArrayList LinkedFactions
		{
			get { return m_linkedFactions; }
			set { m_linkedFactions = value; }
		}

		private bool m_isConfused;
		/// <summary>
		/// Is this NPC currently confused
		/// </summary>
		public bool IsConfused
		{
			get { return m_isConfused; }
			set { m_isConfused = value; }
		}

		private ushort m_bodyType;
		/// <summary>
		/// The NPC's body type
		/// </summary>
		public ushort BodyType
		{
			get { return m_bodyType; }
			set { m_bodyType = value; }
		}

		private ushort m_houseNumber;
		/// <summary>
		/// The NPC's current house
		/// </summary>
		public ushort HouseNumber
		{
			get { return m_houseNumber; }
			set { m_houseNumber = value; }
		}
		#endregion

		#region Stats


		/// <summary>
		/// Change a stat value
		/// (delegate to GameNPC)
		/// </summary>
		/// <param name="stat">The stat to change</param>
		/// <param name="val">The new value</param>
		public override void ChangeBaseStat(eStat stat, short val)
		{
			int oldstat = GetBaseStat(stat);
			base.ChangeBaseStat(stat, val);
			int newstat = GetBaseStat(stat);
			GameNPC npc = this;
			if (this != null && oldstat != newstat)
			{
				switch (stat)
				{
					case eStat.STR: npc.Strength = (short)newstat; break;
					case eStat.DEX: npc.Dexterity = (short)newstat; break;
					case eStat.CON: npc.Constitution = (short)newstat; break;
					case eStat.QUI: npc.Quickness = (short)newstat; break;
					case eStat.INT: npc.Intelligence = (short)newstat; break;
					case eStat.PIE: npc.Piety = (short)newstat; break;
					case eStat.EMP: npc.Empathy = (short)newstat; break;
					case eStat.CHR: npc.Charisma = (short)newstat; break;
				}
			}
		}

		/// <summary>
		/// Gets NPC's constitution
		/// </summary>
		public virtual short Constitution
		{
			get
			{
				return m_charStat[eStat.CON - eStat._First];
			}
			set { m_charStat[eStat.CON - eStat._First] = value; }
		}

		/// <summary>
		/// Gets NPC's dexterity
		/// </summary>
		public virtual short Dexterity
		{
			get { return m_charStat[eStat.DEX - eStat._First]; }
			set { m_charStat[eStat.DEX - eStat._First] = value; }
		}

		/// <summary>
		/// Gets NPC's strength
		/// </summary>
		public virtual short Strength
		{
			get { return m_charStat[eStat.STR - eStat._First]; }
			set { m_charStat[eStat.STR - eStat._First] = value; }
		}

		/// <summary>
		/// Gets NPC's quickness
		/// </summary>
		public virtual short Quickness
		{
			get { return m_charStat[eStat.QUI - eStat._First]; }
			set { m_charStat[eStat.QUI - eStat._First] = value; }
		}

		/// <summary>
		/// Gets NPC's intelligence
		/// </summary>
		public virtual short Intelligence
		{
			get { return m_charStat[eStat.INT - eStat._First]; }
			set { m_charStat[eStat.INT - eStat._First] = value; }
		}

		/// <summary>
		/// Gets NPC's piety
		/// </summary>
		public virtual short Piety
		{
			get { return m_charStat[eStat.PIE - eStat._First]; }
			set { m_charStat[eStat.PIE - eStat._First] = value; }
		}

		/// <summary>
		/// Gets NPC's empathy
		/// </summary>
		public virtual short Empathy
		{
			get { return m_charStat[eStat.EMP - eStat._First]; }
			set { m_charStat[eStat.EMP - eStat._First] = value; }
		}

		/// <summary>
		/// Gets NPC's charisma
		/// </summary>
		public virtual short Charisma
		{
			get { return m_charStat[eStat.CHR - eStat._First]; }
			set { m_charStat[eStat.CHR - eStat._First] = value; }
		}
		#endregion

		#region Flags/Position/SpawnPosition/UpdateTick/Tether
		/// <summary>
		/// Various flags for this npc
		/// </summary>
		[Flags]
		public enum eFlags : uint
		{
			/// <summary>
			/// The npc is translucent (like a ghost)
			/// </summary>
			GHOST = 0x01,
			/// <summary>
			/// The npc is stealthed (nearly invisible, like a stealthed player; new since 1.71)
			/// </summary>
			STEALTH = 0x02,
			/// <summary>
			/// The npc doesn't show a name above its head but can be targeted
			/// </summary>
			DONTSHOWNAME = 0x04,
			/// <summary>
			/// The npc doesn't show a name above its head and can't be targeted
			/// </summary>
			CANTTARGET = 0x08,
			/// <summary>
			/// Not in nearest enemyes if different vs player realm, but can be targeted if model support this
			/// </summary>
			PEACE = 0x10,
			/// <summary>
			/// The npc is flying (z above ground permitted)
			/// </summary>
			FLYING = 0x20,
			/// <summary>
			/// npc's torch is lit
			/// </summary>
			TORCH = 0x40,
			/// <summary>
			/// npc is a statue (no idle animation, no target...)
			/// </summary>
			STATUE = 0x80,
			/// <summary>
			/// npc is swimming
			/// </summary>
			SWIMMING = 0x100
		}

		/// <summary>
		/// Holds various flags of this npc
		/// </summary>
		protected eFlags m_flags;
		/// <summary>
		/// Spawn point
		/// </summary>
		protected Point3D m_spawnPoint;
		/// <summary>
		/// Spawn Heading
		/// </summary>
		protected ushort m_spawnHeading;


		/// <summary>
		/// package ID defined form this NPC
		/// </summary>
		protected string m_packageID;

		public string PackageID
		{
			get { return m_packageID; }
			set { m_packageID = value; }
		}

		/// <summary>
		/// The last time this NPC sent the 0x09 update packet
		/// </summary>
		protected volatile uint m_lastUpdateTickCount = uint.MinValue;
		/// <summary>
		/// The last time this NPC was actually updated to at least one player
		/// </summary>
		protected volatile uint m_lastVisibleToPlayerTick = uint.MinValue;

		/// <summary>
		/// Gets or Sets the flags of this npc
		/// </summary>
		public virtual eFlags Flags
		{
			get { return m_flags; }
			set
			{
				eFlags oldflags = m_flags;
				m_flags = value;
				if (ObjectState == eObjectState.Active)
				{
					if (oldflags != m_flags)
					{
						foreach (GamePlayer player in GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE))
						{
							player.Out.SendNPCCreate(this);
							if (m_inventory != null)
								player.Out.SendLivingEquipmentUpdate(this);
						}
					}
				}
			}
		}


		public override bool IsUnderwater
		{
			get { return (m_flags & eFlags.SWIMMING) == eFlags.SWIMMING || base.IsUnderwater; }
		}


		/// <summary>
		/// Shows wether any player sees that mob
		/// we dont need to calculate things like AI if mob is in no way
		/// visible to at least one player
		/// </summary>
		public virtual bool IsVisibleToPlayers
		{
			get { return (uint)Environment.TickCount - m_lastVisibleToPlayerTick < 60000; }
		}

		/// <summary>
		/// Gets or sets the spawnposition of this npc
		/// </summary>
		public virtual Point3D SpawnPoint
		{
			get { return m_spawnPoint; }
			set { m_spawnPoint = value; }
		}

		/// <summary>
		/// Gets or sets the spawnposition of this npc
		/// </summary>
		[Obsolete("Use GameNPC.SpawnPoint")]
		public virtual int SpawnX
		{
			get { return m_spawnPoint.X; }
			set { m_spawnPoint.X = value; }
		}
		/// <summary>
		/// Gets or sets the spawnposition of this npc
		/// </summary>
		[Obsolete("Use GameNPC.SpawnPoint")]
		public virtual int SpawnY
		{
			get { return m_spawnPoint.Y; }
			set { m_spawnPoint.Y = value; }
		}
		/// <summary>
		/// Gets or sets the spawnposition of this npc
		/// </summary>
		[Obsolete("Use GameNPC.SpawnPoint")]
		public virtual int SpawnZ
		{
			get { return m_spawnPoint.Z; }
			set { m_spawnPoint.Z = value; }
		}

		/// <summary>
		/// Gets or sets the spawnheading of this npc
		/// </summary>
		public virtual ushort SpawnHeading
		{
			get { return m_spawnHeading; }
			set { m_spawnHeading = value; }
		}

		/// <summary>
		/// Gets or sets the current speed of the npc
		/// </summary>
		public override short CurrentSpeed
		{
			set
			{
				SaveCurrentPosition();

				if (base.CurrentSpeed != value)
				{
					base.CurrentSpeed = value;
					BroadcastUpdate();
				}
			}
		}

		/// <summary>
		/// Stores the currentwaypoint that npc has to wander to
		/// </summary>
		protected PathPoint m_currentWayPoint = null;

		/// <summary>
		/// Gets sets the speed for traveling on path
		/// </summary>
		public short PathingNormalSpeed
		{
			get { return m_pathingNormalSpeed; }
			set { m_pathingNormalSpeed = value; }
		}
		/// <summary>
		/// Stores the speed for traveling on path
		/// </summary>
		protected short m_pathingNormalSpeed;

		/// <summary>
		/// Gets the current X of this living. Don't modify this property
		/// to try to change position of the mob while active. Use the
		/// MoveTo function instead
		/// </summary>
		public override int X
		{
			get
			{
				if (!IsMoving)
					return base.X;

				if (TargetPosition.X != 0 || TargetPosition.Y != 0 || TargetPosition.Z != 0)
				{
					long expectedDistance = FastMath.Abs((long)TargetPosition.X - m_x);

					if (expectedDistance == 0)
						return TargetPosition.X;

					long actualDistance = FastMath.Abs((long)(MovementElapsedTicks * TickSpeedX));

					if (expectedDistance - actualDistance < 0)
						return TargetPosition.X;
				}

				return base.X;
			}
		}

		/// <summary>
		/// Gets the current Y of this NPC. Don't modify this property
		/// to try to change position of the mob while active. Use the
		/// MoveTo function instead
		/// </summary>
		public override int Y
		{
			get
			{
				if (!IsMoving)
					return base.Y;

				if (TargetPosition.X != 0 || TargetPosition.Y != 0 || TargetPosition.Z != 0)
				{
					long expectedDistance = FastMath.Abs((long)TargetPosition.Y - m_y);

					if (expectedDistance == 0)
						return TargetPosition.Y;

					long actualDistance = FastMath.Abs((long)(MovementElapsedTicks * TickSpeedY));

					if (expectedDistance - actualDistance < 0)
						return TargetPosition.Y;
				}
				return base.Y;
			}
		}

		/// <summary>
		/// Gets the current Z of this NPC. Don't modify this property
		/// to try to change position of the mob while active. Use the
		/// MoveTo function instead
		/// </summary>
		public override int Z
		{
			get
			{
				if (!IsMoving)
					return base.Z;

				if (TargetPosition.X != 0 || TargetPosition.Y != 0 || TargetPosition.Z != 0)
				{
					long expectedDistance = FastMath.Abs((long)TargetPosition.Z - m_z);

					if (expectedDistance == 0)
						return TargetPosition.Z;

					long actualDistance = FastMath.Abs((long)(MovementElapsedTicks * TickSpeedZ));

					if (expectedDistance - actualDistance < 0)
						return TargetPosition.Z;
				}
				return base.Z;
			}
		}

		/// <summary>
		/// The stealth state of this NPC
		/// </summary>
		public override bool IsStealthed
		{
			get
			{
				return false;// (Flags & eFlags.STEALTH) != 0;
			}
		}

		bool m_wasStealthed = false;
		public bool WasStealthed
        {
			get
            {
				return m_wasStealthed;
            }
        }

		protected int m_maxdistance;
		/// <summary>
		/// The Mob's max distance from its spawn before return automatically
		/// if MaxDistance > 0 ... the amount is the normal value
		/// if MaxDistance = 0 ... no maxdistance check
		/// if MaxDistance less than 0 ... the amount is calculated in procent of the value and the aggrorange (in StandardMobBrain)
		/// </summary>
		public int MaxDistance
		{
			get { return m_maxdistance; }
			set { m_maxdistance = value; }
		}

		protected int m_roamingRange;
		/// <summary>
		/// radius for roaming
		/// </summary>
		public int RoamingRange
		{
			get { return m_roamingRange; }
			set { m_roamingRange = value; }
		}

		private int m_tetherRange;

		/// <summary>
		/// The mob's tether range; if mob is pulled farther than this distance
		/// it will return to its spawn point.
		/// if TetherRange > 0 ... the amount is the normal value
		/// if TetherRange less or equal 0 ... no tether check
		/// </summary>
		public int TetherRange
		{
			get { return m_tetherRange; }
			set { m_tetherRange = value; }
		}

		/// <summary>
		/// True, if NPC is out of tether range, false otherwise; if no tether
		/// range is specified, this will always return false.
		/// </summary>
		public bool IsOutOfTetherRange
		{
			get
			{
				if (TetherRange > 0)
				{
					if (this.IsWithinRadius(this.SpawnPoint, TetherRange))
						return false;
					else
						return true;
				}
				else
				{
					return false;
				}
			}
		}

		#endregion

		#region Movement
		/// <summary>
		/// Timer to be set if an OnArriveAtTarget
		/// handler is set before calling the WalkTo function
		/// </summary>
		protected ArriveAtTargetAction m_arriveAtTargetAction;

		/// <summary>
		/// Is the mob roaming towards a target?
		/// </summary>
		public bool IsRoaming
		{
			get
			{
				return m_arriveAtTargetAction != null && m_arriveAtTargetAction.IsAlive;
			}
		}

		/// <summary>
		/// Timer to be set if an OnCloseToTarget
		/// handler is set before calling the WalkTo function
		/// </summary>
		//protected CloseToTargetAction m_closeToTargetAction;
		/// <summary>
		/// Object that this npc is following as weakreference
		/// </summary>
		protected WeakReference m_followTarget;
		/// <summary>
		/// Max range to keep following
		/// </summary>
		protected int m_followMaxDist;
		/// <summary>
		/// Min range to keep to the target
		/// </summary>
		protected int m_followMinDist;
		/// <summary>
		/// Timer with purpose of follow updating
		/// </summary>
		protected ECSGameTimer m_followTimer;
		/// <summary>
		/// Property entry on follow timer, wether the follow target is in range
		/// </summary>
		protected const string FOLLOW_TARGET_IN_RANGE = "FollowTargetInRange";
		/// <summary>
		/// Minimum allowed attacker follow distance to avoid issues with client / server resolution (herky jerky motion)
		/// </summary>
		protected const int MIN_ALLOWED_FOLLOW_DISTANCE = 100;
		/// <summary>
		/// Minimum allowed pet follow distance
		/// </summary>
		protected const int MIN_ALLOWED_PET_FOLLOW_DISTANCE = 90;
		/// <summary>
		/// At what health percent will npc give up range attack and rush the attacker
		/// </summary>
		protected const int MINHEALTHPERCENTFORRANGEDATTACK = 70;

		private string m_pathID;
		public string PathID
		{
			get { return m_pathID; }
			set { m_pathID = value; }
		}

		private IPoint3D m_targetPosition = new Point3D(0, 0, 0);

		/// <summary>
		/// The target position.
		/// </summary>
		public virtual IPoint3D TargetPosition
		{
			get
			{
				return m_targetPosition;
			}

			protected set
			{
				if (value != m_targetPosition)
				{
					SaveCurrentPosition();
					m_targetPosition = value;
				}
			}
		}

		/// <summary>
		/// The target object.
		/// </summary>
		public override GameObject TargetObject
		{
			get
			{
				return base.TargetObject;
			}
			set
			{
				GameObject previousTarget = TargetObject;
				GameObject newTarget = value;

				base.TargetObject = newTarget;

				if (previousTarget != null && newTarget != previousTarget)
					previousTarget.Notify(GameNPCEvent.SwitchedTarget, this,
										  new SwitchedTargetEventArgs(previousTarget, newTarget));
			}
		}

		private GameObject m_cachedTarget;

		public GameObject CachedTarget
        {
			get {  return m_cachedTarget; }
			set {  m_cachedTarget = value; }
        }

		public void ResetHeading()
		{
			TurnTo(SpawnHeading);
			IsReturningToSpawnPoint = false;
		}

		/// <summary>
		/// Updates the tick speed for this living.
		/// </summary>
		protected override void UpdateTickSpeed()
		{
			if (!IsMoving)
			{
				SetTickSpeed(0, 0, 0);
				return;
			}

			if (TargetPosition.X != 0 || TargetPosition.Y != 0 || TargetPosition.Z != 0)
			{
				double dist = this.GetDistanceTo(new Point3D(TargetPosition.X, TargetPosition.Y, TargetPosition.Z));

				if (dist <= 0)
				{
					SetTickSpeed(0, 0, 0);
					return;
				}
		
				double dx = (double)(TargetPosition.X - m_x) / dist;
				double dy = (double)(TargetPosition.Y - m_y) / dist;
				double dz = (double)(TargetPosition.Z - m_z) / dist;

				SetTickSpeed(dx, dy, dz, CurrentSpeed);
			
				return;
			}

			base.UpdateTickSpeed();
		}

		/// <summary>
		/// True if the mob is at its target position, else false.
		/// </summary>
		public bool IsAtTargetPosition
		{
			get
			{
				return (X == TargetPosition.X && Y == TargetPosition.Y && Z == TargetPosition.Z);
			}
		}

		/// <summary>
		/// Turns the npc towards a specific spot
		/// </summary>
		/// <param name="tx">Target X</param>
		/// <param name="ty">Target Y</param>
		public virtual void TurnTo(int tx, int ty)
		{
			TurnTo(tx, ty, true);
		}

		/// <summary>
		/// Turns the npc towards a specific spot
		/// optionally sends update to client
		/// </summary>
		/// <param name="tx">Target X</param>
		/// <param name="ty">Target Y</param>
		public virtual void TurnTo(int tx, int ty, bool sendUpdate)
		{
			if (IsStunned || IsMezzed) return;

			Notify(GameNPCEvent.TurnTo, this, new TurnToEventArgs(tx, ty));

			if (sendUpdate)
				Heading = GetHeading(new Point2D(tx, ty));
			else
				base.Heading = GetHeading(new Point2D(tx, ty));
		}

		/// <summary>
		/// Turns the npc towards a specific heading
		/// </summary>
		/// <param name="newHeading">the new heading</param>
		public virtual void TurnTo(ushort heading)
		{
			TurnTo(heading, true);
		}

		/// <summary>
		/// Turns the npc towards a specific heading
		/// optionally sends update to client
		/// </summary>
		/// <param name="newHeading">the new heading</param>
		public virtual void TurnTo(ushort heading, bool sendUpdate)
		{
			if (IsStunned || IsMezzed) return;

			Notify(GameNPCEvent.TurnToHeading, this, new TurnToHeadingEventArgs(heading));

			if (sendUpdate)
				if (Heading != heading) Heading = heading;
				else
				if (base.Heading != heading) base.Heading = heading;
		}

		/// <summary>
		/// Turns the NPC towards a specific gameObject
		/// which can be anything ... a player, item, mob, npc ...
		/// </summary>
		/// <param name="target">GameObject to turn towards</param>
		public virtual void TurnTo(GameObject target)
		{
			TurnTo(target, true);
		}

		/// <summary>
		/// Turns the NPC towards a specific gameObject
		/// which can be anything ... a player, item, mob, npc ...
		/// optionally sends update to client
		/// </summary>
		/// <param name="target">GameObject to turn towards</param>
		public virtual void TurnTo(GameObject target, bool sendUpdate)
		{
			if (target == null || target.CurrentRegion != CurrentRegion)
				return;

			TurnTo(target.X, target.Y, sendUpdate);
		}

		/// <summary>
		/// Turns the NPC towards a specific gameObject
		/// which can be anything ... a player, item, mob, npc ...
		/// and turn back after specified duration
		/// </summary>
		/// <param name="target">GameObject to turn towards</param>
		/// <param name="duration">restore heading after this duration</param>
		public virtual void TurnTo(GameObject target, int duration)
		{
			if (target == null || target.CurrentRegion != CurrentRegion)
				return;

			// Store original heading if not set already.

			RestoreHeadingAction restore = (RestoreHeadingAction)TempProperties.getProperty<object>(RESTORE_HEADING_ACTION_PROP, null);

			if (restore == null)
			{
				restore = new RestoreHeadingAction(this);
				TempProperties.setProperty(RESTORE_HEADING_ACTION_PROP, restore);
			}

			TurnTo(target);
			restore.Start(duration);
		}

		/// <summary>
		/// The property used to store the NPC heading restore action
		/// </summary>
		protected const string RESTORE_HEADING_ACTION_PROP = "NpcRestoreHeadingAction";

		/// <summary>
		/// Restores the NPC heading after some time
		/// </summary>
		protected class RestoreHeadingAction : RegionAction
		{
			/// <summary>
			/// The NPCs old heading
			/// </summary>
			protected readonly ushort m_oldHeading;

			/// <summary>
			/// The NPCs old position
			/// </summary>
			protected readonly Point3D m_oldPosition;

			/// <summary>
			/// Creates a new TurnBackAction
			/// </summary>
			/// <param name="actionSource">The source of action</param>
			public RestoreHeadingAction(GameNPC actionSource)
				: base(actionSource)
			{
				m_oldHeading = actionSource.Heading;
				m_oldPosition = new Point3D(actionSource);
			}

			/// <summary>
			/// Called on every timer tick
			/// </summary>
			protected override void OnTick()
			{
				GameNPC npc = (GameNPC)m_actionSource;

				npc.TempProperties.removeProperty(RESTORE_HEADING_ACTION_PROP);

				if (npc.ObjectState != eObjectState.Active) return;
				if (!npc.IsAlive) return;
				if (npc.attackComponent.AttackState) return;
				if (npc.IsMoving) return;
				if (npc.Equals(m_oldPosition)) return;
				if (npc.Heading == m_oldHeading) return; // already set? oO

				npc.TurnTo(m_oldHeading);
			}
		}

		/// <summary>
		/// Gets the last time this mob was updated
		/// </summary>
		public uint LastUpdateTickCount
		{
			get { return m_lastUpdateTickCount; }
		}

		/// <summary>
		/// Gets the last this this NPC was actually update to at least one player.
		/// </summary>
		public uint LastVisibleToPlayersTickCount
		{
			get { return m_lastVisibleToPlayerTick; }
		}

		/// <summary>
		/// Delayed action that fires an event when an NPC arrives at its target
		/// </summary>
		protected class ArriveAtTargetAction : RegionAction
		{
			/// <summary>
			/// Constructs a new ArriveAtTargetAction
			/// </summary>
			/// <param name="actionSource">The action source</param>
			public ArriveAtTargetAction(GameNPC actionSource)
				: base(actionSource)
			{
			}

			/// <summary>
			/// This function is called when the Mob arrives at its target spot
			/// This time was estimated using walking speed and distance.
			/// It fires the ArriveAtTarget event
			/// </summary>
			protected override void OnTick()
			{
				GameNPC npc = (GameNPC)m_actionSource;

				bool arriveAtSpawnPoint = npc.IsReturningToSpawnPoint;

				npc.StopMoving();
				npc.Notify(GameNPCEvent.ArriveAtTarget, npc);

				if (arriveAtSpawnPoint)
					npc.Notify(GameNPCEvent.ArriveAtSpawnPoint, npc);
			}
		}

		public virtual void CancelWalkToTimer()
		{
			if (m_arriveAtTargetAction != null)
			{
				m_arriveAtTargetAction.Stop();
				m_arriveAtTargetAction = null;
			}
		}

		/// <summary>
		/// Ticks required to arrive at a given spot.
		/// </summary>
		/// <param name="target"></param>
		/// <param name="speed"></param>
		/// <returns></returns>
		public virtual int GetTicksToArriveAt(IPoint3D target, int speed)
		{
			return GetDistanceTo(target) * 1000 / speed;
		}

		/// <summary>
		/// Make the current (calculated) position permanent.
		/// </summary>
		public void SaveCurrentPosition()
		{
			SavePosition(this);
		}

		/// <summary>
		/// Make the target position permanent.
		/// </summary>
		private void SavePosition(IPoint3D target)
		{
			X = target.X;
			Y = target.Y;
			Z = target.Z;

			MovementStartTick = Environment.TickCount;
		}

		/// <summary>
		/// Walk to a certain spot at a given speed.
		/// </summary>
		/// <param name="tx"></param>
		/// <param name="ty"></param>
		/// <param name="tz"></param>
		/// <param name="speed"></param>
		public virtual void WalkTo(int targetX, int targetY, int targetZ, short speed)
		{
			WalkTo(new Point3D(targetX, targetY, targetZ), speed);
		}

		/// <summary>
		/// Walk to a certain spot at a given speed.
		/// </summary>
		/// <param name="p"></param>
		/// <param name="speed"></param>
		public virtual void WalkTo(IPoint3D target, short speed)
		{
			if (IsTurningDisabled)
				return;

			if (speed > MaxSpeed)
				speed = MaxSpeed;

			if (speed <= 0)
				return;


		
			TargetPosition = target; // this also saves the current position
			

			if (IsWithinRadius(TargetPosition, CONST_WALKTOTOLERANCE))
			{
				// No need to start walking.

				//Notify(GameNPCEvent.ArriveAtTarget, this);
				return;
			}


			//update existing component
			//register moveComponent w/ the movement-to-be-processed queue


			//kill everything below this line?
			CancelWalkToTimer();
		

			m_Heading = GetHeading(TargetPosition);
			m_currentSpeed = speed;
			MovementStartTick = Environment.TickCount; //Adding this to prevent pets from warping when using GoTo and Here on the same target twice.
			UpdateTickSpeed();
			
			Notify(GameNPCEvent.WalkTo, this, new WalkToEventArgs(TargetPosition, speed));
			
			StartArriveAtTargetAction(GetTicksToArriveAt(TargetPosition, speed));
			BroadcastUpdate();
		}

		private void StartArriveAtTargetAction(int requiredTicks)
		{
			m_arriveAtTargetAction = new ArriveAtTargetAction(this);
			m_arriveAtTargetAction.Start((requiredTicks > 1) ? requiredTicks : 1);
		}

		/// <summary>
		/// Walk to the spawn point
		/// </summary>
		public virtual void WalkToSpawn()
		{
			WalkToSpawn((short)(50));
		}

		/// <summary>
		/// Walk to the spawn point
		/// </summary>
		public virtual void CancelWalkToSpawn()
		{
			CancelWalkToTimer();
			IsReturningHome = false;
			IsReturningToSpawnPoint = false;
		}

		public bool IsNearSpawn()
        {
			return IsWithinRadius(SpawnPoint, CONST_WALKTOTOLERANCE);

		}

		/// <summary>
		/// Walk to the spawn point with specified speed
		/// </summary>
		public virtual void WalkToSpawn(short speed)
		{
			attackComponent.NPCStopAttack();
			StopFollowing();

			StandardMobBrain brain = Brain as StandardMobBrain;

			if (brain != null && brain.HasAggro)
			{
				brain.ClearAggroList();
			}

			TargetObject = null;

			IsReturningHome = true;
			IsReturningToSpawnPoint = true;
			WalkTo(SpawnPoint, speed);
		}

		/// <summary>
		/// This function is used to start the mob walking. It will
		/// walk in the heading direction until the StopMovement function
		/// is called
		/// </summary>
		/// <param name="speed">walk speed</param>
		public virtual void Walk(short speed)
		{
			Notify(GameNPCEvent.Walk, this, new WalkEventArgs(speed));

			CancelWalkToTimer();
			SaveCurrentPosition();
			TargetPosition.Clear();

			m_currentSpeed = speed;

			MovementStartTick = Environment.TickCount;
			UpdateTickSpeed();
			BroadcastUpdate();
		}

		/// <summary>
		/// Gets the NPC current follow target
		/// </summary>
		public GameObject CurrentFollowTarget
		{
			get { return m_followTarget.Target as GameObject; }
		}

		/// <summary>
		/// Stops the movement of the mob.
		/// </summary>
		public virtual void StopMoving()
		{
			CancelWalkToSpawn();

			if (IsMoving)
				CurrentSpeed = 0;
		}

		/// <summary>
		/// Stops the movement of the mob and forcibly moves it to the
		/// given target position.
		/// </summary>
		public virtual void StopMovingAt(IPoint3D target)
		{
			CancelWalkToSpawn();

			if (IsMoving)
			{
				m_currentSpeed = 0;
				UpdateTickSpeed();
			}

			SavePosition(target);
			BroadcastUpdate();
		}

		public const int STICKMINIMUMRANGE = 75;
		public const int STICKMAXIMUMRANGE = 5000;

		/// <summary>
		/// Follow given object
		/// </summary>
		/// <param name="target">Target to follow</param>
		/// <param name="minDistance">Min distance to keep to the target</param>
		/// <param name="maxDistance">Max distance to keep following</param>
		public virtual void Follow(GameObject target, int minDistance, int maxDistance)
		{
				if (target == null || target.ObjectState != eObjectState.Active)
					return;
			
				if (m_followTimer.IsAlive && m_followTarget.Target == target && m_followMinDist == minDistance && m_followMaxDist == maxDistance)
					return;
				else
				{
					m_followTimer.Stop();
				}
			
				m_followMaxDist = maxDistance;
				m_followMinDist = minDistance;
				m_followTarget.Target = target;
				m_followTimer.StartExistingTimer(100);
			
		
		}

		/// <summary>
		/// Stop following
		/// </summary>
		public virtual void StopFollowing()
		{
			lock (m_followTimer)
			{
				if (m_followTimer.IsAlive)
				{
					m_followTimer.Stop();
				}
					

				m_followTarget.Target = null;
				StopMoving();
			}
		}

		/// <summary>
		/// Will be called if follow mode is active
		/// and we reached the follow target
		/// </summary>
		public virtual void FollowTargetInRange()
		{
			if (attackComponent.AttackState)
			{
				// if in last attack the enemy was out of range, we can attack him now immediately
				AttackData ad = (AttackData)TempProperties.getProperty<object>(LAST_ATTACK_DATA, null);
				if (ad != null && ad.AttackResult == eAttackResult.OutOfRange)
				{
					//m_attackAction.Start(1);// schedule for next tick
                    attackComponent.attackAction.StartTime = 1;
				}
			}
			//sirru
			else if (attackComponent.Attackers.Count == 0 && this.Spells.Count > 0 && this.TargetObject != null && GameServer.ServerRules.IsAllowedToAttack(this, (this.TargetObject as GameLiving), true))
			{
				if (TargetObject.Realm == 0 || Realm == 0)
					m_lastAttackTickPvE = m_CurrentRegion.Time;
				else m_lastAttackTickPvP = m_CurrentRegion.Time;
				if (this.CurrentRegion.Time - LastAttackedByEnemyTick > 10 * 1000)
				{
					// Aredhel: Erm, checking for spells in a follow method, what did we create
					// brain classes for again?

					//Check for negatively casting spells
					StandardMobBrain stanBrain = (StandardMobBrain)Brain;
					if (stanBrain != null)
						((StandardMobBrain)stanBrain).CheckSpells(StandardMobBrain.eCheckSpellType.Offensive);
				}
			}
		}

		/// <summary>
		/// Keep following a specific object at a max distance
		/// </summary>
		protected virtual int FollowTimerCallback(ECSGameTimer callingTimer)
		{
			double followSpeedScaler = 2.5; //This is used to scale the follow speed based on the distance from target

			if (IsCasting)
				return ServerProperties.Properties.GAMENPC_FOLLOWCHECK_TIME;

			bool wasInRange = m_followTimer.Properties.getProperty(FOLLOW_TARGET_IN_RANGE, false);
			m_followTimer.Properties.removeProperty(FOLLOW_TARGET_IN_RANGE);

			GameObject followTarget = (GameObject)m_followTarget.Target;
			GameLiving followLiving = followTarget as GameLiving;

			//Stop following if target living is dead
			if (followLiving != null && !followLiving.IsAlive)
			{
				StopFollowing();
				Notify(GameNPCEvent.FollowLostTarget, this, new FollowLostTargetEventArgs(followTarget));
				return 0;
			}

			//Stop following if we have no target
			if (followTarget == null || followTarget.ObjectState != eObjectState.Active || CurrentRegionID != followTarget.CurrentRegionID)
			{
				StopFollowing();
				Notify(GameNPCEvent.FollowLostTarget, this, new FollowLostTargetEventArgs(followTarget));
				return 0;
			}

			//Calculate the difference between our position and the players position
			float diffx = (long)followTarget.X - X;
			float diffy = (long)followTarget.Y - Y;
			float diffz = (long)followTarget.Z - Z;

			//SH: Removed Z checks when one of the two Z values is zero(on ground)
			//Tolakram: a Z of 0 does not indicate on the ground.  Z varies based on terrain  Removed 0 Z check
			float distance = (float)Math.Sqrt(diffx * diffx + diffy * diffy + diffz * diffz);

			//if distance is greater then the max follow distance, stop following and return home
			if ((int)distance > m_followMaxDist)
			{
				StopFollowing();
				Notify(GameNPCEvent.FollowLostTarget, this, new FollowLostTargetEventArgs(followTarget));
				this.WalkToSpawn();
				return 0;
			}
			int newX, newY, newZ;

			if (this.Brain is StandardMobBrain)
			{
				StandardMobBrain brain = this.Brain as StandardMobBrain;

				//if the npc hasn't hit or been hit in a while, stop following and return home
				if (!(Brain is IControlledBrain))
				{
					if (attackComponent.AttackState && brain != null && followLiving != null)
					{
						long seconds = 20 + ((brain.GetAggroAmountForLiving(followLiving) / (MaxHealth + 1)) * 100);
						long lastattacked = LastAttackTick;
						long lasthit = LastAttackedByEnemyTick;
						if ((GameLoop.GameLoopTime - lastattacked > seconds * 1000 && GameLoop.GameLoopTime - lasthit > seconds * 1000)
							&& lasthit != 0)
						{
							//StopFollow();
							Notify(GameNPCEvent.FollowLostTarget, this, new FollowLostTargetEventArgs(followTarget));
							//brain.ClearAggroList();
							//this.WalkToSpawn();
							LastAttackedByEnemyTickPvE = 0;
							LastAttackedByEnemyTickPvP = 0;
							brain.FSM.SetCurrentState(eFSMStateType.RETURN_TO_SPAWN);
							return 0;
						}
					}
				}

				//If we're part of a formation, we can get out early.
				newX = followTarget.X;
				newY = followTarget.Y;
				newZ = followTarget.Z;
				
				if (TargetObject != null && TargetObject.Realm != this.Realm)
				{
					//do nothing 
				}
				//else if (brain.CheckFormation(ref newX, ref newY, ref newZ) || TargetObject?.Realm == this.Realm)
				else if (brain.CheckFormation(ref newX, ref newY, ref newZ))
				{
					short followspeed= (short) Math.Max(Math.Min(MaxSpeed,GetDistance(new Point2D(newX, newY))*followSpeedScaler),50);
					//log.Debug($"Followspeed: {followspeed}");
					WalkTo(newX, newY, (ushort) newZ, followspeed);
					//WalkTo(newX, newY, (ushort)newZ, MaxSpeed);
					
					return ServerProperties.Properties.GAMENPC_FOLLOWCHECK_TIME;
				}
			}

			// Tolakram - Distances under 100 do not calculate correctly leading to the mob always being told to walkto
			int minAllowedFollowDistance = MIN_ALLOWED_FOLLOW_DISTANCE;

			// pets can follow closer.  need to implement /fdistance command to make this adjustable
			if (this.Brain is IControlledBrain)
				minAllowedFollowDistance = MIN_ALLOWED_PET_FOLLOW_DISTANCE;

			//Are we in range yet?
			if ((int)distance <= (m_followMinDist < minAllowedFollowDistance ? minAllowedFollowDistance : m_followMinDist))
			{
				StopMoving();
				TurnTo(followTarget);
				if (!wasInRange)
				{
					m_followTimer.Properties.setProperty(FOLLOW_TARGET_IN_RANGE, true);
					FollowTargetInRange();
				}
				return ServerProperties.Properties.GAMENPC_FOLLOWCHECK_TIME;
			}

			// follow on distance
			diffx = (diffx / distance) * m_followMinDist;
			diffy = (diffy / distance) * m_followMinDist;
			diffz = (diffz / distance) * m_followMinDist;

			//Subtract the offset from the target's position to get
			//our target position
			newX = (int)(followTarget.X - diffx);
			newY = (int)(followTarget.Y - diffy);
			newZ = (int)(followTarget.Z - diffz);
			
			if (Brain is ControlledNpcBrain controlledNpcBrain)
			{
				if (InCombat || Brain is BomberBrain || TargetObject != null)
					WalkTo(newX, newY, (ushort)newZ, MaxSpeed);
				// else if (!IsWithinRadius(new Point2D(newX, newY),200)) // MaxSpeed < GetDistance(new Point2D(newX, newY)))
				// 	WalkTo(newX, newY, (ushort) newZ, MaxSpeed); //(short)Math.Min(MaxSpeed, followLiving.CurrentSpeed + 50));
				else //If close, slow down followspeed to target. This is based on distance and followSpeedScaler
				{
					// WalkTo(newX, newY, (ushort) newZ, (short)185);//(GetDistance(new Point2D(newX, newY)) + 191));
					short followspeed = (short) Math.Max(Math.Min(MaxSpeed,GetDistance(new Point2D(newX, newY))*followSpeedScaler),50);
					WalkTo(newX, newY, (ushort) newZ, followspeed);
				}
			}
			else
				WalkTo(newX, newY, (ushort)newZ, MaxSpeed);
			return ServerProperties.Properties.GAMENPC_FOLLOWCHECK_TIME;
			
		}

		/// <summary>
		/// Disables the turning for this living
		/// </summary>
		/// <param name="add"></param>
		public override void DisableTurning(bool add)
		{
			bool old = IsTurningDisabled;
			base.DisableTurning(add);
			if (old != IsTurningDisabled)
				BroadcastUpdate();
		}

		#endregion

		#region Path (Movement)
		/// <summary>
		/// Gets sets the currentwaypoint that npc has to wander to
		/// </summary>
		public PathPoint CurrentWayPoint
		{
			get { return m_currentWayPoint; }
			set { m_currentWayPoint = value; }
		}

		/// <summary>
		/// Is the NPC returning home, if so, we don't want it to think
		/// </summary>
		public bool IsReturningHome
		{
			get { return m_isReturningHome; }
			set { m_isReturningHome = value; }
		}

		protected bool m_isReturningHome = false;

		/// <summary>
		/// Whether or not the NPC is on its way back to the spawn point.
		/// [Aredhel: I decided to add this property in order not to mess
		/// with SMB and IsReturningHome. Also, to prevent outside classes
		/// from interfering the setter is now protected.]
		/// </summary>
		public bool IsReturningToSpawnPoint { get; protected set; }

		/// <summary>
		/// Gets if npc moving on path
		/// </summary>
		public bool IsMovingOnPath
		{
			get { return m_IsMovingOnPath; }
		}
		/// <summary>
		/// Stores if npc moving on path
		/// </summary>
		protected bool m_IsMovingOnPath = false;

		/// <summary>
		/// let the npc travel on its path
		/// </summary>
		/// <param name="speed">Speed on path</param>
		public void MoveOnPath(short speed)
		{
			if (IsMovingOnPath)
				StopMovingOnPath();

			if (CurrentWayPoint == null)
			{
				if (log.IsWarnEnabled)
					log.Warn("No path to travel on for " + Name);
				return;
			}

			PathingNormalSpeed = speed;

			if (this.IsWithinRadius(CurrentWayPoint, 100))
			{
				// reaching a waypoint can start an ambient sentence
				FireAmbientSentence(eAmbientTrigger.moving, this);

				if (CurrentWayPoint.Type == ePathType.Path_Reverse && CurrentWayPoint.FiredFlag)
					CurrentWayPoint = CurrentWayPoint.Prev;
				else
				{
					if ((CurrentWayPoint.Type == ePathType.Loop) && (CurrentWayPoint.Next == null))
						CurrentWayPoint = MovementMgr.FindFirstPathPoint(CurrentWayPoint);
					else
						CurrentWayPoint = CurrentWayPoint.Next;
				}
			}

			if (CurrentWayPoint != null)
			{
				GameEventMgr.AddHandler(this, GameNPCEvent.ArriveAtTarget, new DOLEventHandler(OnArriveAtWaypoint));
				WalkTo(CurrentWayPoint, Math.Min(speed, (short)CurrentWayPoint.MaxSpeed));
				m_IsMovingOnPath = true;
				Notify(GameNPCEvent.PathMoveStarts, this);
			}
			else
			{
				StopMovingOnPath();
			}
		}

		/// <summary>
		/// Stop moving on path.
		/// </summary>
		public void StopMovingOnPath()
		{
			if (!IsMovingOnPath)
				return;

			GameEventMgr.RemoveHandler(this, GameNPCEvent.ArriveAtTarget, new DOLEventHandler(OnArriveAtWaypoint));
			Notify(GameNPCEvent.PathMoveEnds, this);
			m_IsMovingOnPath = false;
		}

		/// <summary>
		/// decides what to do on reached waypoint in path
		/// </summary>
		/// <param name="e"></param>
		/// <param name="n"></param>
		/// <param name="args"></param>
		protected void OnArriveAtWaypoint(DOLEvent e, object n, EventArgs args)
		{
			if (!IsMovingOnPath || n != this)
				return;

			if (CurrentWayPoint != null)
			{
				WaypointDelayAction waitTimer = new WaypointDelayAction(this);
				waitTimer.Start(Math.Max(1, CurrentWayPoint.WaitTime * 100));
			}
			else
				StopMovingOnPath();
		}

		/// <summary>
		/// Delays movement to the next waypoint
		/// </summary>
		protected class WaypointDelayAction : RegionAction
		{
			/// <summary>
			/// Constructs a new WaypointDelayAction
			/// </summary>
			/// <param name="actionSource"></param>
			public WaypointDelayAction(GameObject actionSource)
				: base(actionSource)
			{
			}

			/// <summary>
			/// Called on every timer tick
			/// </summary>
			protected override void OnTick()
			{
				GameNPC npc = (GameNPC)m_actionSource;
				if (!npc.IsMovingOnPath)
					return;
				PathPoint oldPathPoint = npc.CurrentWayPoint;
				PathPoint nextPathPoint = npc.CurrentWayPoint.Next;
				if ((npc.CurrentWayPoint.Type == ePathType.Path_Reverse) && (npc.CurrentWayPoint.FiredFlag))
					nextPathPoint = npc.CurrentWayPoint.Prev;

				if (nextPathPoint == null)
				{
					switch (npc.CurrentWayPoint.Type)
					{
						case ePathType.Loop:
							{
								npc.CurrentWayPoint = MovementMgr.FindFirstPathPoint(npc.CurrentWayPoint);
								npc.Notify(GameNPCEvent.PathMoveStarts, npc);
								break;
							}
						case ePathType.Once:
							npc.CurrentWayPoint = null;//to stop
							break;
						case ePathType.Path_Reverse://invert sens when go to end of path
							if (oldPathPoint.FiredFlag)
								npc.CurrentWayPoint = npc.CurrentWayPoint.Next;
							else
								npc.CurrentWayPoint = npc.CurrentWayPoint.Prev;
							break;
					}
				}
				else
				{
					if ((npc.CurrentWayPoint.Type == ePathType.Path_Reverse) && (npc.CurrentWayPoint.FiredFlag))
						npc.CurrentWayPoint = npc.CurrentWayPoint.Prev;
					else
						npc.CurrentWayPoint = npc.CurrentWayPoint.Next;
				}
				oldPathPoint.FiredFlag = !oldPathPoint.FiredFlag;

				if (npc.CurrentWayPoint != null)
				{
					npc.WalkTo(npc.CurrentWayPoint, (short)Math.Min(npc.PathingNormalSpeed, npc.CurrentWayPoint.MaxSpeed));
				}
				else
				{
					npc.StopMovingOnPath();
				}
			}
		}
		#endregion

		#region Inventory
		
		private NpcTemplate m_npcTemplate = null;
		/// <summary>
		/// The NPC's template
		/// </summary>
		public NpcTemplate NPCTemplate
		{
			get { return m_npcTemplate; }
			set { m_npcTemplate = value; }
		}
		/// <summary>
		/// Loads the equipment template of this npc
		/// </summary>
		/// <param name="equipmentTemplateID">The template id</param>
		public virtual void LoadEquipmentTemplateFromDatabase(string equipmentTemplateID)
		{
			EquipmentTemplateID = equipmentTemplateID;
			if (EquipmentTemplateID != null && EquipmentTemplateID.Length > 0)
			{
				GameNpcInventoryTemplate template = new GameNpcInventoryTemplate();
				if (template.LoadFromDatabase(EquipmentTemplateID))
				{
					m_inventory = template.CloseTemplate();
				}
				if (Inventory != null)
				{
					//if the distance slot isnt empty we use that
					//Seems to always
					if (Inventory.GetItem(eInventorySlot.DistanceWeapon) != null)
						SwitchWeapon(eActiveWeaponSlot.Distance);
					else
                    {
						InventoryItem twohand = Inventory.GetItem(eInventorySlot.TwoHandWeapon);
						InventoryItem onehand = Inventory.GetItem(eInventorySlot.RightHandWeapon);

						if (twohand != null && onehand != null)
							//Let's add some random chance
							SwitchWeapon(Util.Chance(50) ? eActiveWeaponSlot.TwoHanded : eActiveWeaponSlot.Standard);
						else if (twohand != null)
							//Hmm our right hand weapon may have been null
							SwitchWeapon(eActiveWeaponSlot.TwoHanded);
						else if (onehand != null)
							//Hmm twohand was null lets default down here
							SwitchWeapon(eActiveWeaponSlot.Standard);
					}
				}
				//else
					//log.Error("Inventory for " + this.Name + " is empty.");
			}
		}

		/// <summary>
		/// Loads the equipment template of this npc
		/// </summary>
		/// <param name="template">The template ID</param>
		public virtual void LoadEquipmentTemplateFromDatabase(INpcTemplate template)
		{
			var templatedInventory = new List<string>();
			
			// Try to reach the NPCEquipment table and load that
			if (!Util.IsEmpty(template.Inventory))
			{
				bool equipHasItems = false;
				GameNpcInventoryTemplate inventoryTemp = new GameNpcInventoryTemplate();
				
				// Use a ';' split to allow NPCTemplates to support multiple equipment IDs
				var equipIDs = Util.SplitCSV(template.Inventory);
				if (!template.Inventory.Contains(':'))
				{
					foreach (var str in equipIDs)
					{
						templatedInventory.Add(str);
					}

					var equipid = "";

					if (templatedInventory.Count >= 0)
					{
						if (templatedInventory.Count <= 1)
							equipid = template.Inventory;
						else
							equipid = templatedInventory[Util.Random(templatedInventory.Count - 1)];
					}
					if (inventoryTemp.LoadFromDatabase(equipid))
						equipHasItems = true;
				}

				#region Manually Adding Equipment
				// If nothing is found in the NPCEquipment table, manually parse the data
				// This is considered "legacy code"
				if (!equipHasItems && template.Inventory.Contains(':'))
				{
					// Create a list to store equipment models
					var tempModels = new List<int>();

					// Go through each slot separated by ';'
					foreach (var str in equipIDs)
					{
						// Clean out the previous entry, if any exists
						tempModels.Clear();
						// Split the equipment into slot and model(s)
						var slotXModels = str.Split(':');
						
						// Each entry should consist of SLOT : MODELS
						if (slotXModels.Length == 2)
						{
							// Identify the slots associated with each entry
							if (int.TryParse(slotXModels[0], out var slot))
							{
								// Add models to the list
								var models = slotXModels[1].Split('|');
								
								foreach (var strModel in models)
								{
									// Add items to the list if successfully parsed
									if (int.TryParse(strModel, out var model))
										tempModels.Add(model);
								}

								// If some models are found, randomly pick one and add it the NPC's equipment
								if (tempModels.Count > 0)
									equipHasItems |= inventoryTemp.AddNPCEquipment((eInventorySlot)slot, tempModels[Util.Random(tempModels.Count - 1)]);
							}
						}
					}
				}
				#endregion Manually Adding Equipment

				// Items added, make it into a new inventory
				if (equipHasItems)
				{
					Inventory = new GameNPCInventory(inventoryTemp);
					var twohand = Inventory.GetItem(eInventorySlot.TwoHandWeapon);
					var onehand = Inventory.GetItem(eInventorySlot.RightHandWeapon);
					var lefthand = Inventory.GetItem(eInventorySlot.LeftHandWeapon);
					var distance = Inventory.GetItem(eInventorySlot.DistanceWeapon);
					
					// Default to ranged attacks, if a weapon is available
					if (distance != null)
						SwitchWeapon(eActiveWeaponSlot.Distance);
					// Switch to melee weapons
					else
                    {
	                    if (twohand != null && onehand != null)
						{
								// Let's pick a weapon at random
								SwitchWeapon(Util.Chance(50) ? eActiveWeaponSlot.TwoHanded : eActiveWeaponSlot.Standard);
						}
						else if (twohand != null)
							// If right-hand weapon is maybe null for some reason
							SwitchWeapon(eActiveWeaponSlot.TwoHanded);
						else if (onehand != null && lefthand != null)
							// If there's a left-hand weapon, let's use it
							SwitchWeapon(eActiveWeaponSlot.Standard);
						else if (onehand != null)
							// Let's just default to things here
							SwitchWeapon(eActiveWeaponSlot.Standard);
                    }
				}

				if (template.VisibleActiveWeaponSlot > 0)
					VisibleActiveWeaponSlots = template.VisibleActiveWeaponSlot;
			}
		}
		
		#endregion Inventory

		private bool m_loadedFromScript = true;
		public bool LoadedFromScript
		{
			get { return m_loadedFromScript; }
			set { m_loadedFromScript = value; }
		}

		#region Load/Delete/SaveFromDatabase

		#region LoadFromDatabase
		/// <summary>
		/// Load a npc from the npc template
		/// </summary>
		/// <param name="obj">template to load from</param>
		public override void LoadFromDatabase(DataObject obj)
		{
			if (obj == null) return;
			base.LoadFromDatabase(obj);
			if (!(obj is Mob)) return;
			m_loadedFromScript = false;
			Mob dbMob = (Mob)obj;
			NPCTemplate = NpcTemplateMgr.GetTemplate(dbMob.NPCTemplateID);
			
			//log.Error("Couldn't fetch NPCTemplate for " + dbMob.Name + ".");
			ClassType = dbMob.ClassType;
			Level = dbMob.Level;
			TranslationId = dbMob.TranslationId;
			Name = dbMob.Name;
			Suffix = dbMob.Suffix;
			GuildName = dbMob.Guild;
			ExamineArticle = dbMob.ExamineArticle;
			MessageArticle = dbMob.MessageArticle;
			Model = dbMob.Model;
			Gender = (eGender)dbMob.Gender;
			Realm = (eRealm)dbMob.Realm;
			Size = dbMob.Size;
			EquipmentTemplateID = dbMob.EquipmentTemplateID;
			LoadEquipmentTemplateFromDatabase(dbMob.EquipmentTemplateID);
			ItemsListTemplateID = dbMob.ItemsListTemplateID;
			Flags = (eFlags)dbMob.Flags;
			MeleeDamageType = (eDamageType)dbMob.MeleeDamageType;
			if (MeleeDamageType == 0)
			{
				MeleeDamageType = eDamageType.Slash;
			}
			ParryChance = m_parryChance;
			EvadeChance = m_evadeChance;
			BlockChance = m_blockChance;
			LeftHandSwingChance = m_leftHandSwingChance;
			Spells = m_spells;
			Styles = m_styles;
			RespawnInterval = dbMob.RespawnInterval * 1000;
			if (RespawnInterval <= 0)
				RespawnInterval = 80 * 1000;

			/* Set with AutoSetStats
			Strength = dbMob.Strength;
			Constitution = dbMob.Constitution; 
			Dexterity = dbMob.Dexterity;
			Quickness = dbMob.Quickness;
			Intelligence = dbMob.Intelligence;
			Empathy = dbMob.Empathy;
			Charisma = dbMob.Charisma;
			Piety = dbMob.Piety;
			*/

			AggroLevel = dbMob.AggroLevel;
			AggroRange = dbMob.AggroRange;
			Race = (short)dbMob.Race;
			BodyType = (ushort)dbMob.BodyType;
			MaxDistance = dbMob.MaxDistance;
			TetherRange = 1000;
			ItemsListTemplateID = dbMob.ItemsListTemplateID;
			m_visibleActiveWeaponSlots = dbMob.VisibleWeaponSlots;
			PackageID = dbMob.PackageID;
			Faction = FactionMgr.GetFactionByID(dbMob.FactionID);
			X = dbMob.X;
			Y = dbMob.Y;
			Z = dbMob.Z;
			Heading = (ushort)(dbMob.Heading & 0xFFF);
			CurrentSpeed = 0;
			MaxSpeedBase = (short)dbMob.Speed;
			CurrentRegionID = dbMob.Region;
			HouseNumber = (ushort)dbMob.HouseNumber;
			RoamingRange = dbMob.RoamingRange;
			IsCloakHoodUp = dbMob.IsCloakHoodUp;
			OwnerID = dbMob.OwnerID;
			PathID = dbMob.PathID;

			if (Inventory != null) 
				SwitchWeapon(ActiveWeaponSlot);

			#region Mob Brains
			if (dbMob.Brain != "")
			{
				try
				{
					ABrain brain = null;
					foreach (Assembly asm in ScriptMgr.GameServerScripts)
					{
						brain = (ABrain) asm.CreateInstance(dbMob.Brain, false);
						if (brain != null)
							break;
					}

					if (brain != null)
						SetOwnBrain(brain);
				}
				catch
				{
					log.ErrorFormat(
						"GameNPC error in LoadFromDatabase: can not instantiate brain of type {0} for npc {1}, name = {2}.",
						dbMob.Brain, dbMob.ClassType, dbMob.Name);
				}
			}

			IOldAggressiveBrain aggroBrain = Brain as IOldAggressiveBrain;

			if (aggroBrain != null)
			{
				if (dbMob.NPCTemplateID != -1 && NPCTemplate != null && NPCTemplate.ReplaceMobValues)
				{
					aggroBrain.AggroLevel = (NPCTemplate.AggroLevel == 0 && dbMob.AggroLevel != 0)
						? dbMob.AggroLevel
						: NPCTemplate.AggroLevel;
					aggroBrain.AggroRange = (NPCTemplate.AggroRange == 0 && dbMob.AggroRange != 0)
						? dbMob.AggroRange
						: NPCTemplate.AggroRange;
				}
				else
				{
					aggroBrain.AggroLevel = dbMob.AggroLevel;
					aggroBrain.AggroRange = dbMob.AggroRange;
				}

				if (aggroBrain.AggroRange == Constants.USE_AUTOVALUES)
				{
					if (Realm == eRealm.None)
					{
						aggroBrain.AggroRange = 400;

						if (Name != Name.ToLower())
						{
							aggroBrain.AggroRange = 500;
						}

						if (CurrentRegion.IsDungeon)
						{
							aggroBrain.AggroRange = 300;
						}
					}
					else
					{
						aggroBrain.AggroRange = 500;
					}
				}

				if (aggroBrain.AggroLevel == Constants.USE_AUTOVALUES)
				{
					aggroBrain.AggroLevel = 0;

					if (Level > 5)
						aggroBrain.AggroLevel = 30;
					if (Name != Name.ToLower())
					{
						aggroBrain.AggroLevel = 30;
						if (Realm != eRealm.None)
							aggroBrain.AggroLevel = 60;
					}
				}
			}
			#endregion Mob Brains
			
			if (dbMob.NPCTemplateID != -1 && NPCTemplate != null && NPCTemplate.ReplaceMobValues && m_loadedFromScript == false)
				LoadTemplate(NPCTemplate);
			/*
						if (Inventory != null)
							SwitchWeapon(ActiveWeaponSlot);
			*/
		}
		#endregion LoadFromDatabase

		/// <summary>
		/// Deletes the mob from the database
		/// </summary>
		public override void DeleteFromDatabase()
		{
			if (Brain != null && Brain is IControlledBrain)
			{
				return;
			}

			if (InternalID != null)
			{
				Mob mob = GameServer.Database.FindObjectByKey<Mob>(InternalID);
				if (mob != null)
					GameServer.Database.DeleteObject(mob);
			}
		}

		#region SaveIntoDatabase
		/// <summary>
		/// Saves a mob into the db if it exists, it is
		/// updated, else it creates a new object in the DB
		/// </summary>
		public override void SaveIntoDatabase()
		{
			// do not allow saving in an instanced region
			if (CurrentRegion.IsInstance)
			{
				LoadedFromScript = true;
				return;
			}

			if (Brain != null && Brain is IControlledBrain)
			{
				// do not allow saving of controlled npc's
				return;
			}

			Mob mob = null;
			if (InternalID != null)
			{
				mob = GameServer.Database.FindObjectByKey<Mob>(InternalID);
			}

			if (mob == null)
			{
				if (LoadedFromScript == false)
				{
					mob = new Mob();
				}
				else
				{
					return;
				}
			}

			mob.TranslationId = TranslationId;
			mob.Name = Name;
			mob.Suffix = Suffix;
			mob.Guild = GuildName;
			mob.ExamineArticle = ExamineArticle;
			mob.MessageArticle = MessageArticle;
			mob.X = X;
			mob.Y = Y;
			mob.Z = Z;
			mob.Heading = Heading;
			mob.Speed = MaxSpeedBase;
			mob.Region = CurrentRegionID;
			mob.Realm = (byte)Realm;
			mob.Model = Model;
			mob.Size = Size;
			mob.Level = Level;

			// Stats
			mob.Constitution = Constitution;
			mob.Dexterity = Dexterity;
			mob.Strength = Strength;
			mob.Quickness = Quickness;
			mob.Intelligence = Intelligence;
			mob.Piety = Piety;
			mob.Empathy = Empathy;
			mob.Charisma = Charisma;

			mob.ClassType = this.GetType().ToString();
			mob.Flags = (uint)Flags;
			mob.Speed = MaxSpeedBase;
			mob.RespawnInterval = m_respawnInterval / 1000;
			mob.HouseNumber = HouseNumber;
			mob.RoamingRange = RoamingRange;
			if (Brain.GetType().FullName != typeof(StandardMobBrain).FullName)
				mob.Brain = Brain.GetType().FullName;
			IOldAggressiveBrain aggroBrain = Brain as IOldAggressiveBrain;
			if (aggroBrain != null)
			{
				mob.AggroLevel = aggroBrain.AggroLevel;
				mob.AggroRange = aggroBrain.AggroRange;
			}
			mob.EquipmentTemplateID = EquipmentTemplateID;

			if (m_faction != null)
				mob.FactionID = m_faction.ID;

			mob.MeleeDamageType = (int)MeleeDamageType;

			if (NPCTemplate != null)
			{
				mob.NPCTemplateID = NPCTemplate.TemplateId;
			}
			else
			{
				mob.NPCTemplateID = -1;
			}

			mob.Race = Race;
			mob.BodyType = BodyType;
			mob.PathID = PathID;
			mob.MaxDistance = m_maxdistance;
			mob.IsCloakHoodUp = m_isCloakHoodUp;
			mob.Gender = (byte)Gender;
			mob.VisibleWeaponSlots = this.m_visibleActiveWeaponSlots;
			mob.PackageID = PackageID;
			mob.OwnerID = OwnerID;

			if (InternalID == null)
			{
				GameServer.Database.AddObject(mob);
				InternalID = mob.ObjectId;
			}
			else
			{
				GameServer.Database.SaveObject(mob);
			}
		}
		#endregion SaveIntoDatabase
		
		#endregion Load/Delete/SaveFromDatabase

		#region LoadTemplate
		
		/// <summary>
		/// Load a NPC template onto this NPC
		/// </summary>
		/// <param name="template"></param>
		public virtual void LoadTemplate(INpcTemplate template)
		{
			if (template == null)
				return;

			// Save the template for later
			NPCTemplate = template as NpcTemplate;

			// These stats aren't found in the mob table, so always get them from the template
			TetherRange = template.TetherRange;
			ParryChance = template.ParryChance;
			EvadeChance = template.EvadeChance;
			BlockChance = template.BlockChance;
			LeftHandSwingChance = template.LeftHandSwingChance;

			#region Preliminary ReplaceMobValues
			// We need to set the stats first, followed by level, before assigning spells
			// so that both stats (Str,Dex,Con) and spells are scaled correctly
			if (template.ReplaceMobValues)
			{
				// Set the class type as stat autoscaling has conditions pertaining to this
				ClassType = template.ClassType;
				//ClassType = template.ClassType;

				#region Level
				// Apply the level before Str/Con/Dex so that autoscaling correctly occurs with AutoSetStats()
				// Select Level randomly based on ranges in field (e.g., '40-45;47;49')
				if (!string.IsNullOrEmpty(template.Level))
				{
					if (template.Level.Contains(';') || template.Level.Contains('-'))
					{
						var split = Util.SplitCSV(template.Level, true);

						if (split.Count > 0)
						{
							byte.TryParse(split[Util.Random(0, split.Count - 1)], out var chosenLevel);
							Level = chosenLevel;
						}
						else
						{
							byte.TryParse(template.Level, out var chosenLevel);
							Level = chosenLevel;
						}
					}
					else
					{
						byte.TryParse(template.Level, out var chosenLevel);
						Level = chosenLevel;
					}
				}
				#endregion Level
				
				#region Stats
				// Stats are set/assigned using AutoSetStats()
				/*if (template.Strength > 1 && template.Strength > Strength)
					Strength = template.Strength;
				if (template.Constitution > 1 && template.Constitution > Constitution)
					Constitution = template.Constitution;
				if (template.Dexterity > 1 && template.Dexterity > Dexterity)
					Dexterity = template.Dexterity;
				if (template.Quickness > 1 && template.Quickness > Quickness)
					Quickness = template.Quickness;
				if (template.Intelligence > 1 && template.Intelligence > Intelligence)
					Intelligence = template.Intelligence;
				if (template.Empathy > 1 && template.Empathy > Empathy)
					Empathy = template.Empathy;
				if (template.Charisma > 1 && template.Charisma > Charisma)
					Charisma = template.Charisma;
				if (template.Piety > 1 && template.Piety > Piety)
					Piety = template.Piety;*/
				#endregion Stats
				
				// Add any magical bonuses to stats
				BuffBonusCategory4[(int)eStat.STR] += template.Strength;
				BuffBonusCategory4[(int)eStat.DEX] += template.Dexterity;
				BuffBonusCategory4[(int)eStat.CON] += template.Constitution;
				BuffBonusCategory4[(int)eStat.QUI] += template.Quickness;
				BuffBonusCategory4[(int)eStat.INT] += template.Intelligence;
				BuffBonusCategory4[(int)eStat.PIE] += template.Piety;
				BuffBonusCategory4[(int)eStat.EMP] += template.Empathy;
				BuffBonusCategory4[(int)eStat.CHR] += template.Charisma;
			}
			#endregion Preliminary ReplaceMobValues

			// Add all spells and scale based on the mob's level
			if (template.Spells != null) Spells = template.Spells;
			if (template.Styles != null)  Styles = template.Styles;
			if (template.Abilities != null)
			{
				lock (m_lockAbilities)
				{
					foreach (Ability ab in template.Abilities)
						m_abilities[ab.KeyName] = ab;
				}
			}
			
			// Replace mob values with everything below this point,
			// as these parameters already exist on the Mob table.
			if (!template.ReplaceMobValues)
				return;
			
			#region All Other ReplaceMobValues
			
			TranslationId = template.TranslationId;
			Name = template.Name;
			Suffix = template.Suffix;
			GuildName = template.GuildName;
			ExamineArticle = (string.IsNullOrEmpty(template.ExamineArticle) && !char.IsUpper(template.Name[0])) ? "the" : template.ExamineArticle;
			MessageArticle = (string.IsNullOrEmpty(template.MessageArticle) && !char.IsUpper(template.Name[0])) ? "The" : template.MessageArticle;
			Faction = FactionMgr.GetFactionByID(template.FactionId);
			MaxDistance = template.MaxDistance;
			Race = (short)template.Race;
			BodyType = template.BodyType;
			MaxSpeedBase = template.MaxSpeed < (short)0 ? (short)200 : template.MaxSpeed;
			Flags = (eFlags)template.Flags;
			PackageID = template.PackageID;
			MeleeDamageType = template.MeleeDamageType;
			if (MeleeDamageType == 0)
			{
				MeleeDamageType = eDamageType.Slash;
			}
			m_ownBrain = new StandardMobBrain
			{
				Body = this,
				AggroLevel = template.AggroLevel,
				AggroRange = template.AggroRange
			};

			#region Models, Sizes, Gender
			
			// Select Model randomly
			if (!string.IsNullOrEmpty(template.Model) && (template.Model.Contains(';') || template.Model.Contains('-')))
			{
				var split = Util.SplitCSV(template.Model, true);
					
				if (split.Count > 0)
				{
					if (ushort.TryParse(split[Util.Random(0, split.Count - 1)], out var chosenModel))
						Model = chosenModel;
				}
				else
				{
					if (ushort.TryParse(split[Util.Random(0, split.Count - 1)], out var chosenModel))
						Model = chosenModel;
				}
			}
			else if (!string.IsNullOrEmpty(template.Model))
			{
				if (ushort.TryParse(template.Model, out var chosenModel))
					Model = chosenModel;
			}
			
			// Select Size randomly
			if (!string.IsNullOrEmpty(template.Size) && (template.Size.Contains(';') || template.Size.Contains('-')))
			{
				var split = Util.SplitCSV(template.Size, true);
					
				if (split.Count > 0)
				{
					if (byte.TryParse(split[Util.Random(0, split.Count - 1)], out var chosenSize))
						Size = chosenSize;
				}
				else
				{
					if (byte.TryParse(template.Size, out var chosenSize))
						Size = chosenSize;
				}
			}
			else if (!string.IsNullOrEmpty(template.Size))
			{
				if (byte.TryParse(template.Size, out var chosenSize))
					Size = chosenSize;
			}
			
			#region Gender
			
			// Set default Gender based on Model
			if (Model != 0 && Model != 1)
			{
				// Set default value
				eGender chosenGender = (eGender)template.Gender;
				
				// Since most templates do not have a Gender set yet, we're going to change what we can as part of the initial server build
				if (chosenGender == eGender.Neutral)
				{
					// Check the mob's Model and then assign gender based on this
					switch (Model)
					{
						// Male models
						case 8 or 9 or 10 or 14 or 16 or 17 or 18 or 20 or 27 or 28 or 32 or 33 or 34 or 39 or 40
							or 41 or 42 or 48 or 49 or 50 or 51 or 61 or 62 or 63 or 64 or 73 or 74 or 78 or 79 or 80
							or 84 or 85 or 86 or 90 or 91 or 92 or 137 or 138 or 139 or 140 or 141 or 142 or 143 or 144
							or 153 or 154 or 155 or 156 or 157 or 158 or 159 or 160 or 169 or 170 or 171 or 172 or 173
							or 174 or 175 or 176 or 185 or 186 or 187 or 188 or 189 or 190 or 191 or 192 or 201 or 202
							or 203 or 204 or 205 or 212 or 213 or 214 or 215 or 221 or 222 or 223 or 224 or 225 or 231
							or 232 or 233 or 234 or 235 or 254 or 255 or 256 or 257 or 262 or 263 or 264 or 265 or 270
							or 271 or 272 or 273 or 278 or 279 or 280 or 281 or 286 or 287 or 288 or 289 or 290 or 291
							or 292 or 293 or 302 or 303 or 304 or 305 or 306 or 307 or 308 or 309 or 318 or 319 or 320
							or 321 or 322 or 323 or 324 or 325 or 334 or 335 or 336 or 337 or 338 or 339 or 340 or 341
							or 350 or 351 or 352 or 353 or 354 or 360 or 361 or 362 or 363 or 364 or 370 or 371 or 372
							or 373 or 374 or 380 or 381 or 382 or 383 or 384 or 390 or 415 or 416 or 417 or 418 or 419
							or 420 or 421 or 422 or 423 or 424 or 471 or 472 or 473 or 474 or 479 or 480 or 481 or 482
							or 487 or 488 or 489 or 490 or 495 or 496 or 497 or 488 or 503 or 504 or 505 or 506 or 511
							or 512 or 513 or 514 or 515 or 529 or 520 or 521 or 522 or 527 or 528 or 529 or 530 or 535
							or 536 or 537 or 538 or 543 or 544 or 545 or 546 or 551 or 552 or 553 or 554 or 559 or 560
							or 561 or 562 or 621 or 645 or 652 or 674 or 675 or 676 or 677 or 680 or 683 or 700 or 701
							or 702 or 703 or 704 or 705 or 706 or 707 or 716 or 717 or 718 or 719 or 720 or 721 or 722
							or 723 or 732 or 733 or 734 or 735 or 736 or 737 or 738 or 739 or 748 or 749 or 750 or 751
							or 752 or 753 or 754 or 755 or 773 or 774 or 775 or 776 or 777 or 778 or 779 or 780 or 789
							or 790 or 791 or 792 or 793 or 794 or 795 or 796 or 805 or 806 or 807 or 808 or 832 or 833
							or 834 or 835 or 840 or 849 or 850 or 851 or 852 or 868 or 867 or 868 or 870 or 872 or 874
							or 889 or 918 or 954 or 956 or 958 or 960 or 962 or 964 or 1210 or 1211 or 1265 or 1270
							or 1271 or 1272 or 1273 or 1274 or 1741 or 1742 or 1743 or 1744 or 1976 or 1982 or 1984
							or 2022 or 2078 or 2080 or 2082 or 2084 or 2086 or 2088 or 2090 or 2092 or 2094 or 2096
							or 2098 or 2100 or 2102 or 2104 or 2106 or 2108 or 2110 or 2119 or 2133 or 2186 or 2211
							or 2215 or 2310 or 2312 or 2314 or 2316 or 2347 or 2348 or 2349 or 2350 or 2364 or 2370:
							chosenGender = eGender.Male;
							break;
						// Female models
						case 5 or 6 or 7 or 19 or 35 or 36 or 37 or 38 or 43 or 44 or 45 or 46 or 52 or 53 or 54 or 55
							or 65 or 66 or 67 or 68 or 75 or 76 or 77 or 81 or 82 or 83 or 87 or 88 or 89 or 145 or 146
							or 147 or 148 or 149 or 150 or 151 or 152 or 161 or 162 or 163 or 164 or 165 or 166 or 167
							or 168 or 177 or 178 or 179 or 180 or 181 or 182 or 183 or 184 or 193 or 194 or 195 or 196
							or 197 or 198 or 199 or 200 or 206 or 207 or 208 or 209 or 210 or 211 or 214 or 216 or 217
							or 218 or 219 or 220 or 226 or 227 or 228 or 229 or 230 or 236 or 237 or 238 or 239 or 240
							or 244 or 258 or 259 or 260 or 261 or 266 or 267 or 268 or 269 or 274 or 275 or 276 or 277
							or 282 or 283 or 284 or 285 or 294 or 295 or 296 or 297 or 298 or 299 or 300 or 301 or 310
							or 311 or 312 or 313 or 314 or 315 or 316 or 317 or 326 or 327 or 328 or 329 or 330 or 331
							or 332 or 333 or 342 or 343 or 344 or 345 or 346 or 347 or 348 or 349 or 355 or 356 or 357
							or 358 or 359 or 365 or 366 or 367 or 368 or 369 or 375 or 376 or 377 or 378 or 379 or 385
							or 386 or 387 or 388 or 389 or 425 or 426 or 427 or 428 or 429 or 430 or 431 or 432 or 433
							or 434 or 435 or 436 or 437 or 438 or 439 or 475 or 476 or 477 or 478 or 483 or 484 or 845
							or 486 or 491 or 492 or 493 or 494 or 499 or 500 or 501 or 502 or 507 or 508 or 509 or 510
							or 516 or 517 or 518 or 523 or 524 or 525 or 526 or 531 or 532 or 533 or 534 or 539 or 540
							or 541 or 542 or 547 or 548 or 549 or 550 or 555 or 556 or 557 or 558 or 563 or 564 or 565
							or 566 or 622 or 631 or 638 or 644 or 646 or 681 or 682 or 708 or 709 or 710 or 711 or 712
							or 713 or 714 or 715 or 724 or 725 or 726 or 727 or 728 or 729 or 730 or 731 or 740 or 741
							or 742 or 743 or 744 or 745 or 746 or 747 or 756 or 757 or 758 or 759 or 760 or 761 or 762
							or 763 or 781 or 782 or 783 or 784 or 785 or 786 or 787 or 788 or 797 or 798 or 799 or 800
							or 801 or 802 or 803 or 804 or 809 or 810 or 811 or 812 or 836 or 837 or 838 or 839 or 841
							or 845 or 853 or 854 or 855 or 856 or 861 or 864 or 865 or 869 or 871 or 873 or 875 or 890
							or 945 or 955 or 957 or 959 or 961 or 963 or 965 or 1015 or 1018 or 1020 or 1030 or 1883
							or 1983 or 1985 or 2023 or 2079 or 2081 or 2083 or 2085 or 2087 or 2089 or 2091 or 2093
							or 2095 or 2097 or 2099 or 2101 or 2103 or 2105 or 2107 or 2109 or 2111 or 2120 or 2169
							or 2170 or 2212 or 2216 or 2311 or 2313 or 2315 or 2317 or 2365:
							chosenGender = eGender.Female;
							break;
						// For all other models, just leave them alone
						default:
							chosenGender = (eGender)template.Gender;
							break;
					}
				}
				Gender = chosenGender;
			}
			#endregion Gender
			
			#endregion Models, Sizes, Gender

			#region Inventory

			EquipmentTemplateID = template.EquipmentTemplateID;
			ItemsListTemplateID = template.ItemsListTemplateID;
			VisibleActiveWeaponSlots = template.VisibleActiveWeaponSlot;

			#endregion Inventory
			#endregion All Other ReplaceMobValues
		}
		
		#endregion LoadTemplate

		/// <summary>
		/// Switches the active weapon to another one
		/// </summary>
		/// <param name="slot">the new eActiveWeaponSlot</param>
		public override void SwitchWeapon(eActiveWeaponSlot slot)
		{
			base.SwitchWeapon(slot);
			if (ObjectState == eObjectState.Active)
			{
				// Update active weapon appearence
				BroadcastLivingEquipmentUpdate();
			}
		}
		/// <summary>
		/// Equipment templateID
		/// </summary>
		protected string m_equipmentTemplateID;
		/// <summary>
		/// The equipment template id of this npc
		/// </summary>
		public string EquipmentTemplateID
		{
			get { return m_equipmentTemplateID; }
			set { m_equipmentTemplateID = value; }
		}
		
		/// <summary>
		/// Item List templateID
		/// </summary>
		protected string m_itemsListTemplateID;
		public virtual string ItemsListTemplateID
		{
			get => m_itemsListTemplateID;
			set { m_itemsListTemplateID = value; }
		}
		
		private int m_aggrolevel;
		/// <summary>
		/// The Mob's Aggro Level
		/// </summary>
		public virtual int AggroLevel
		{
			get { return m_aggrolevel; }
			set { m_aggrolevel = value; }
		}
		
		private int m_aggrorange;
		/// <summary>
		/// The Mob's Aggro Range
		/// </summary>
		public virtual int AggroRange
		{
			get { return m_aggrorange; }
			set { m_aggrorange = value; }
		}

		private string m_type;
		/// <summary>
		/// The Mob's ClassType
		/// </summary>
		public string ClassType
		{
			get
			{
				return m_type;
			}
			set
			{
				m_type = value;
			}
		}

		//#endregion

		#region Quest
		/// <summary>
		/// Holds all the quests this npc can give to players
		/// </summary>
		protected readonly ArrayList m_questListToGive = new ArrayList();

		/// <summary>
		/// Gets the questlist of this player
		/// </summary>
		public IList QuestListToGive
		{
			get { return m_questListToGive; }
		}

		/// <summary>
		/// Adds a scripted quest type to the npc questlist
		/// </summary>
		/// <param name="questType">The quest type to add</param>
		/// <returns>true if added, false if the npc has already the quest!</returns>
		public void AddQuestToGive(Type questType)
		{
			lock (m_questListToGive.SyncRoot)
			{
				if (HasQuest(questType) == null)
				{
					AbstractQuest newQuest = (AbstractQuest)Activator.CreateInstance(questType);
					if (newQuest != null) m_questListToGive.Add(newQuest);
				}
			}
		}

		/// <summary>
		/// removes a scripted quest from this npc
		/// </summary>
		/// <param name="questType">The questType to remove</param>
		/// <returns>true if added, false if the npc has already the quest!</returns>
		public bool RemoveQuestToGive(Type questType)
		{
			lock (m_questListToGive.SyncRoot)
			{
				foreach (AbstractQuest q in m_questListToGive)
				{
					if (q.GetType().Equals(questType))
					{
						m_questListToGive.Remove(q);
						return true;
					}
				}
			}
			return false;
		}

		/// <summary>
		/// Check if the npc can give the specified quest to a player
		/// Used for scripted quests
		/// </summary>
		/// <param name="questType">The type of the quest</param>
		/// <param name="player">The player who search a quest</param>
		/// <returns>the number of time the quest can be done again</returns>
		public int CanGiveQuest(Type questType, GamePlayer player)
		{
			lock (m_questListToGive.SyncRoot)
			{
				foreach (AbstractQuest q in m_questListToGive)
				{
					if (q.GetType().Equals(questType) && q.CheckQuestQualification(player) && player.HasFinishedQuest(questType) < q.MaxQuestCount)
					{
						return q.MaxQuestCount - player.HasFinishedQuest(questType);
					}
				}
			}
			return 0;
		}

		/// <summary>
		/// Return the proper indicator for quest
		/// TODO: check when finish indicator is set
		/// * when you have done the NPC quest
		/// * when you are at the last step
		/// </summary>
		/// <param name="questType">Type of quest</param>
		/// <param name="player">player requesting the quest</param>
		/// <returns></returns>
		public eQuestIndicator SetQuestIndicator(Type questType, GamePlayer player)
		{
			if (CanShowOneQuest(player)) return eQuestIndicator.Available;
			if (player.HasFinishedQuest(questType) > 0) return eQuestIndicator.Finish;
			return eQuestIndicator.None;
		}

		protected GameNPC m_teleporterIndicator = null;

		/// <summary>
		/// Should this NPC have an associated teleporter indicator
		/// </summary>
		public virtual bool ShowTeleporterIndicator
		{
			get { return false; }
		}

		/// <summary>
		/// Should the NPC show a quest indicator, this can be overriden for custom handling
		/// Checks both scripted and data quests
		/// </summary>
		/// <param name="player"></param>
		/// <returns>True if the NPC should show quest indicator, false otherwise</returns>
		public virtual eQuestIndicator GetQuestIndicator(GamePlayer player)
		{
			// Available one ?
			if (CanShowOneQuest(player))
				return eQuestIndicator.Available;

			// Finishing one ?
			if (CanFinishOneQuest(player))
				return eQuestIndicator.Finish;

			return eQuestIndicator.None;
		}

		/// <summary>
		/// Check if the npc can show a quest indicator to a player
		/// Checks both scripted and data quests
		/// </summary>
		/// <param name="player">The player to check</param>
		/// <returns>true if yes, false if the npc can give any quest</returns>
		public bool CanShowOneQuest(GamePlayer player)
		{
			// Scripted quests
			lock (m_questListToGive.SyncRoot)
			{
				foreach (AbstractQuest q in m_questListToGive)
				{
					Type questType = q.GetType();
					int doingQuest = (player.IsDoingQuest(questType) != null ? 1 : 0);
					if (q.CheckQuestQualification(player) && player.HasFinishedQuest(questType) + doingQuest < q.MaxQuestCount)
						return true;
				}
			}

			// Data driven quests
			lock (m_dataQuests)
			{
				foreach (DataQuest quest in DataQuestList)
				{
					if (quest.ShowIndicator &&
						quest.CheckQuestQualification(player))
					{
						return true;
					}
				}
			}

			return false;
		}

		/// <summary>
		/// Check if the npc can finish one of DataQuest/RewardQuest Player is doing
		/// This can't be check with AbstractQuest as they don't implement anyway of knowing who is the last target or last step !
		/// </summary>
		/// <param name="player">The player to check</param>
		/// <returns>true if this npc is the last step of one quest, false otherwise</returns>
		public bool CanFinishOneQuest(GamePlayer player)
		{
			// browse Quests.
			List<AbstractQuest> dqs;
			lock (((ICollection)player.QuestList).SyncRoot)
			{
				dqs = new List<AbstractQuest>(player.QuestList);
			}

			foreach (AbstractQuest q in dqs)
			{
				// Handle Data Quest here.

				DataQuest quest = null;
				if (q is DataQuest)
				{
					quest = (DataQuest)q;
				}

				if (quest != null && (quest.TargetName == Name && (quest.TargetRegion == 0 || quest.TargetRegion == CurrentRegionID)))
				{
					switch (quest.StepType)
					{
						case DataQuest.eStepType.DeliverFinish:
						case DataQuest.eStepType.InteractFinish:
						case DataQuest.eStepType.KillFinish:
						case DataQuest.eStepType.WhisperFinish:
						case DataQuest.eStepType.CollectFinish:
							return true;
					}
				}

				// Handle Reward Quest here.

				RewardQuest rwQuest = null;

				if (q is RewardQuest)
				{
					rwQuest = (RewardQuest)q;
				}

				if (rwQuest != null && rwQuest.QuestGiver == this)
				{
					bool done = true;
					foreach (RewardQuest.QuestGoal goal in rwQuest.Goals)
					{
						done &= goal.IsAchieved;
					}

					if (done)
					{
						return true;
					}
				}
			}

			return false;
		}


		/// <summary>
		/// Give a quest a to specific player
		/// used for scripted quests
		/// </summary>
		/// <param name="questType">The quest type</param>
		/// <param name="player">The player that gets the quest</param>
		/// <param name="startStep">The starting quest step</param>
		/// <returns>true if added, false if the player do already the quest!</returns>
		public bool GiveQuest(Type questType, GamePlayer player, int startStep)
		{
			AbstractQuest quest = HasQuest(questType);
			if (quest != null)
			{
				AbstractQuest newQuest = (AbstractQuest)Activator.CreateInstance(questType, new object[] { player, startStep });
				if (newQuest != null && player.AddQuest(newQuest))
				{
					player.Out.SendNPCsQuestEffect(this, GetQuestIndicator(player));
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Checks if this npc already has a specified quest
		/// used for scripted quests
		/// </summary>
		/// <param name="questType">The quest type</param>
		/// <returns>the quest if the npc have the quest or null if not</returns>
		protected AbstractQuest HasQuest(Type questType)
		{
			lock (m_questListToGive.SyncRoot)
			{
				foreach (AbstractQuest q in m_questListToGive)
				{
					if (q.GetType().Equals(questType))
						return q;
				}
			}
			return null;
		}

		#endregion

		#region Riding
		//NPC's can have riders :-)
		/// <summary>
		/// Holds the rider of this NPC as weak reference
		/// </summary>
		public GamePlayer[] Riders;

		/// <summary>
		/// This function is called when a rider mounts this npc
		/// Since only players can ride NPC's you should use the
		/// GamePlayer.MountSteed function instead to make sure all
		/// callbacks are called correctly
		/// </summary>
		/// <param name="rider">GamePlayer that is the rider</param>
		/// <param name="forced">if true, mounting can't be prevented by handlers</param>
		/// <returns>true if mounted successfully</returns>
		public virtual bool RiderMount(GamePlayer rider, bool forced)
		{
			int exists = RiderArrayLocation(rider);
			if (exists != -1)
				return false;

			rider.MoveTo(CurrentRegionID, X, Y, Z, Heading);

			Notify(GameNPCEvent.RiderMount, this, new RiderMountEventArgs(rider, this));
			int slot = GetFreeArrayLocation();
			Riders[slot] = rider;
			rider.Steed = this;
			return true;
		}

		/// <summary>
		/// This function is called when a rider mounts this npc
		/// Since only players can ride NPC's you should use the
		/// GamePlayer.MountSteed function instead to make sure all
		/// callbacks are called correctly
		/// </summary>
		/// <param name="rider">GamePlayer that is the rider</param>
		/// <param name="forced">if true, mounting can't be prevented by handlers</param>
		/// <param name="slot">The desired slot to mount</param>
		/// <returns>true if mounted successfully</returns>
		public virtual bool RiderMount(GamePlayer rider, bool forced, int slot)
		{
			int exists = RiderArrayLocation(rider);
			if (exists != -1)
				return false;

			if (Riders[slot] != null)
				return false;

			//rider.MoveTo(CurrentRegionID, X, Y, Z, Heading);

			Notify(GameNPCEvent.RiderMount, this, new RiderMountEventArgs(rider, this));
			Riders[slot] = rider;
			rider.Steed = this;
			return true;
		}

		/// <summary>
		/// Called to dismount a rider from this npc.
		/// Since only players can ride NPC's you should use the
		/// GamePlayer.MountSteed function instead to make sure all
		/// callbacks are called correctly
		/// </summary>
		/// <param name="forced">if true, the dismounting can't be prevented by handlers</param>
		/// <param name="player">the player that is dismounting</param>
		/// <returns>true if dismounted successfully</returns>
		public virtual bool RiderDismount(bool forced, GamePlayer player)
		{
			if (Riders.Length <= 0)
				return false;

			int slot = RiderArrayLocation(player);
			if (slot < 0)
			{
				return false;
			}
			Riders[slot] = null;

			Notify(GameNPCEvent.RiderDismount, this, new RiderDismountEventArgs(player, this));
			player.Steed = null;

			return true;
		}

		/// <summary>
		/// Get a free array location on the NPC
		/// </summary>
		/// <returns></returns>
		public int GetFreeArrayLocation()
		{
			for (int i = 0; i < MAX_PASSENGERS; i++)
			{
				if (Riders[i] == null)
					return i;
			}
			return -1;
		}

		/// <summary>
		/// Get the riders array location
		/// </summary>
		/// <param name="player">the player to get location of</param>
		/// <returns></returns>
		public int RiderArrayLocation(GamePlayer player)
		{
			for (int i = 0; i < MAX_PASSENGERS; i++)
			{
				if (Riders[i] == player)
					return i;
			}
			return -1;
		}

		/// <summary>
		/// Get the riders slot on the npc
		/// </summary>
		/// <param name="player"></param>
		/// <returns></returns>
		public int RiderSlot(GamePlayer player)
		{
			int location = RiderArrayLocation(player);
			if (location == -1)
				return location;
			return location + SLOT_OFFSET;
		}

		/// <summary>
		/// The maximum passengers the NPC can take
		/// </summary>
		public virtual int MAX_PASSENGERS
		{
			get { return 1; }
		}

		/// <summary>
		/// The minimum number of passengers required to move
		/// </summary>
		public virtual int REQUIRED_PASSENGERS
		{
			get { return 1; }
		}

		/// <summary>
		/// The slot offset for this NPC
		/// </summary>
		public virtual int SLOT_OFFSET
		{
			get { return 0; }
		}

		/// <summary>
		/// Gets a list of the current riders
		/// </summary>
		public GamePlayer[] CurrentRiders
		{
			get
			{
				List<GamePlayer> list = new List<GamePlayer>(MAX_PASSENGERS);
				for (int i = 0; i < MAX_PASSENGERS; i++)
				{
					if (Riders == null || i >= Riders.Length)
						break;

					GamePlayer player = Riders[i];
					if (player != null)
						list.Add(player);
				}
				return list.ToArray();
			}
		}
		#endregion

		#region Add/Remove/Create/Remove/Update

		/// <summary>
		/// Broadcasts the NPC Update to all players around
		/// </summary>
		public override void BroadcastUpdate()
		{
			base.BroadcastUpdate();

			m_lastUpdateTickCount = (uint)Environment.TickCount;
		}

		/// <summary>
		/// callback that npc was updated to the world
		/// so it must be visible to at least one player
		/// </summary>
		public void NPCUpdatedCallback()
		{
			m_lastVisibleToPlayerTick = (uint)Environment.TickCount;
			lock (BrainSync)
			{
				ABrain brain = Brain;
				if (brain != null)
					brain.Start();
			}
		}
		/// <summary>
		/// Adds the npc to the world
		/// </summary>
		/// <returns>true if the npc has been successfully added</returns>
		public override bool AddToWorld()
		{
			if (!base.AddToWorld()) return false;

			if (MAX_PASSENGERS > 0)
				Riders = new GamePlayer[MAX_PASSENGERS];

			bool anyPlayer = false;
			foreach (GamePlayer player in GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE))
			{
				if (player == null) continue;
				player.Out.SendNPCCreate(this);
				if (m_inventory != null)
					player.Out.SendLivingEquipmentUpdate(this);

				// If any player was initialized, update last visible tick to enable brain
				anyPlayer = true;
			}

			if (anyPlayer)
				m_lastVisibleToPlayerTick = (uint)Environment.TickCount;

			m_spawnPoint.X = X;
			m_spawnPoint.Y = Y;
			m_spawnPoint.Z = Z;
			m_spawnHeading = Heading;
			lock (BrainSync)
			{
				ABrain brain = Brain;
				if (brain != null)
					brain.Start();
			}

			if (Mana <= 0 && MaxMana > 0)
				Mana = MaxMana;
			else if (Mana > 0 && MaxMana > 0 && Mana < MaxMana)  //Only start PowerRegen if needed
				StartPowerRegeneration();

			//If the Mob has a Path assigned he will now walk on it!
			if (MaxSpeedBase > 0 && CurrentSpellHandler == null && !IsMoving
				&& !attackComponent.AttackState && !InCombat && !IsMovingOnPath && !IsReturningHome
				//Check everything otherwise the Server will crash
				&& PathID != null && PathID != "" && PathID != "NULL")
			{
				PathPoint path = MovementMgr.LoadPath(PathID);
				if (path != null)
				{
					CurrentWayPoint = path;
					MoveOnPath((short)path.MaxSpeed);
				}
			}

			if (m_houseNumber > 0 && !(this is GameConsignmentMerchant))
			{
				log.Info("NPC '" + Name + "' added to house " + m_houseNumber);
				CurrentHouse = HouseMgr.GetHouse(m_houseNumber);
				if (CurrentHouse == null)
					log.Warn("House " + CurrentHouse + " for NPC " + Name + " doesn't exist !!!");
				else
					log.Info("Confirmed number: " + CurrentHouse.HouseNumber.ToString());
			}

			// [Ganrod] Nidel: spawn full life
			if (!InCombat && IsAlive && base.Health < MaxHealth)
			{
				base.Health = MaxHealth;
			}

			// create the ambiant text list for this NPC
			BuildAmbientTexts();
			if (GameServer.Instance.ServerStatus == eGameServerStatus.GSS_Open)
				FireAmbientSentence(eAmbientTrigger.spawning, this);


			if (ShowTeleporterIndicator)
			{
				if (m_teleporterIndicator == null)
				{
					m_teleporterIndicator = new GameNPC();
					m_teleporterIndicator.Name = "";
					m_teleporterIndicator.Model = 1923;
					m_teleporterIndicator.Flags ^= eFlags.PEACE;
					m_teleporterIndicator.Flags ^= eFlags.CANTTARGET;
					m_teleporterIndicator.Flags ^= eFlags.DONTSHOWNAME;
					m_teleporterIndicator.Flags ^= eFlags.FLYING;
					m_teleporterIndicator.X = X;
					m_teleporterIndicator.Y = Y;
					m_teleporterIndicator.Z = Z + 1;
					m_teleporterIndicator.CurrentRegionID = CurrentRegionID;
				}

				m_teleporterIndicator.AddToWorld();
			}

			if (Flags.HasFlag(eFlags.STEALTH))
				m_wasStealthed = true;
			return true;
		}

		/// <summary>
		/// Fill the ambient text list for this NPC
		/// </summary>
		protected virtual void BuildAmbientTexts()
		{
			// list of ambient texts
			if (!string.IsNullOrEmpty(Name))
				ambientTexts = GameServer.Instance.NpcManager.AmbientBehaviour[Name];
		}

		/// <summary>
		/// Removes the npc from the world
		/// </summary>
		/// <returns>true if the npc has been successfully removed</returns>
		public override bool RemoveFromWorld()
		{
			if (IsMovingOnPath)
				StopMovingOnPath();
			if (MAX_PASSENGERS > 0)
			{
				foreach (GamePlayer player in CurrentRiders)
				{
					player.DismountSteed(true);
				}
			}

			if (ObjectState == eObjectState.Active)
			{
				foreach (GamePlayer player in GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE))
					player.Out.SendObjectRemove(this);
			}
			if (!base.RemoveFromWorld()) return false;

			lock (BrainSync)
			{
				ABrain brain = Brain;
				brain.Stop();
			}
			EffectList.CancelAll();

			if (ShowTeleporterIndicator && m_teleporterIndicator != null)
			{
				m_teleporterIndicator.RemoveFromWorld();
				m_teleporterIndicator = null;
			}

			return true;
		}

		/// <summary>
		/// Move an NPC within the same region without removing from world
		/// </summary>
		/// <param name="regionID"></param>
		/// <param name="x"></param>
		/// <param name="y"></param>
		/// <param name="z"></param>
		/// <param name="heading"></param>
		/// <param name="forceMove">Move regardless of combat check</param>
		/// <returns>true if npc was moved</returns>
		public virtual bool MoveInRegion(ushort regionID, int x, int y, int z, ushort heading, bool forceMove)
		{
			if (m_ObjectState != eObjectState.Active)
				return false;

			// pets can't be moved across regions
			if (regionID != CurrentRegionID)
				return false;

			if (forceMove == false)
			{
				// do not move a pet in combat, player can passive / follow to bring pet to them
				if (InCombat)
					return false;

				ControlledNpcBrain controlledBrain = Brain as ControlledNpcBrain;

				// only move pet if it's following the owner
				if (controlledBrain != null && controlledBrain.WalkState != eWalkState.Follow)
					return false;
			}

			Region rgn = WorldMgr.GetRegion(regionID);

			if (rgn == null || rgn.GetZone(x, y) == null)
				return false;

			// For a pet move simple erase the pet from all clients and redraw in the new location

			Notify(GameObjectEvent.MoveTo, this, new MoveToEventArgs(regionID, x, y, z, heading));

			if (ObjectState == eObjectState.Active)
			{
				foreach (GamePlayer player in GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE))
				{
					player.Out.SendObjectRemove(this);
				}
			}

			m_x = x;
			m_y = y;
			m_z = z;
			m_Heading = heading;

			foreach (GamePlayer player in GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE))
			{
				if (player == null) continue;

				player.Out.SendNPCCreate(this);

				if (m_inventory != null)
				{
					player.Out.SendLivingEquipmentUpdate(this);
				}
			}

			return true;
		}

		/// <summary>
		/// Gets or Sets the current Region of the Object
		/// </summary>
		public override Region CurrentRegion
		{
			get { return base.CurrentRegion; }
			set
			{
				Region oldRegion = CurrentRegion;
				base.CurrentRegion = value;
				Region newRegion = CurrentRegion;
				if (oldRegion != newRegion && newRegion != null)
				{
					if (m_followTimer != null) m_followTimer.Stop();
					m_followTimer = new ECSGameTimer(this);
					m_followTimer.Callback = new ECSGameTimer.ECSTimerCallback(FollowTimerCallback);
				}
			}
		}

		/// <summary>
		/// Marks this object as deleted!
		/// </summary>
		public override void Delete()
		{
			lock (m_respawnTimerLock)
			{
				if (m_respawnTimer != null)
				{
					m_respawnTimer.Stop();
					m_respawnTimer = null;
				}
			}
			lock (BrainSync)
			{
				ABrain brain = Brain;
				brain.Stop();
			}
			StopFollowing();
			TempProperties.removeProperty(CHARMED_TICK_PROP);
			base.Delete();
		}

		#endregion

		#region AI

		/// <summary>
		/// Holds the own NPC brain
		/// </summary>
		protected ABrain m_ownBrain;

		/// <summary>
		/// Holds the all added to this npc brains
		/// </summary>
		private ArrayList m_brains = new ArrayList(1);

		/// <summary>
		/// The sync object for brain changes
		/// </summary>
		private readonly object m_brainSync = new object();

		/// <summary>
		/// Gets the brain sync object
		/// </summary>
		public object BrainSync
		{
			get { return m_brainSync; }
		}

		/// <summary>
		/// Gets the current brain of this NPC
		/// </summary>
		public ABrain Brain
		{
			get
			{
				ArrayList brains = m_brains;
				if (brains.Count > 0)
					return (ABrain)brains[brains.Count - 1];
				return m_ownBrain;
			}
		}

		/// <summary>
		/// Sets the NPC own brain
		/// </summary>
		/// <param name="brain">The new brain</param>
		/// <returns>The old own brain</returns>
		public virtual ABrain SetOwnBrain(ABrain brain)
		{
			if (brain == null)
				return null;
			if (brain.IsActive)
				throw new ArgumentException("The new brain is already active.", "brain");

			lock (BrainSync)
			{
				ABrain oldBrain = m_ownBrain;
				bool activate = oldBrain.IsActive;
				if (activate)
					oldBrain.Stop();
				m_ownBrain = brain;
				m_ownBrain.Body = this;
				if (activate)
					m_ownBrain.Start();

				return oldBrain;
			}
		}

		/// <summary>
		/// Adds a temporary brain to Npc, last added brain is active
		/// </summary>
		/// <param name="newBrain"></param>
		public virtual void AddBrain(ABrain newBrain)
		{
			if (newBrain == null)
				throw new ArgumentNullException("newBrain");
			if (newBrain.IsActive)
				throw new ArgumentException("The new brain is already active.", "newBrain");

			lock (BrainSync)
			{
				Brain.Stop();
				ArrayList brains = new ArrayList(m_brains);
				brains.Add(newBrain);
				m_brains = brains; // make new array list to avoid locks in the Brain property
				newBrain.Body = this;
				newBrain.Start();
			}
		}

		/// <summary>
		/// Removes a temporary brain from Npc
		/// </summary>
		/// <param name="removeBrain">The brain to remove</param>
		/// <returns>True if brain was found</returns>
		public virtual bool RemoveBrain(ABrain removeBrain)
		{
			if (removeBrain == null)
			{
				//Console.WriteLine("removeBrain is null!");
				return false;
			}

			lock (BrainSync)
			{
				ArrayList brains = new ArrayList(m_brains);
				int index = brains.IndexOf(removeBrain);
				if (index < 0)
				{
					//Console.WriteLine("Brain index < 0");
					return false;
				}
				bool active = brains[index] == Brain;
				if (active)
					removeBrain.Stop();
				brains.RemoveAt(index);
				m_brains = brains;
				if (active)
					Brain.Start();

				return true;
			}
		}
		#endregion

		#region GetAggroLevelString

		/// <summary>
		/// How friendly this NPC is to player
		/// </summary>
		/// <param name="player">GamePlayer that is examining this object</param>
		/// <param name="firstLetterUppercase"></param>
		/// <returns>aggro state as string</returns>
		public virtual string GetAggroLevelString(GamePlayer player, bool firstLetterUppercase)
		{
			// "aggressive", "hostile", "neutral", "friendly"
			// TODO: correct aggro strings
			// TODO: some merchants can be aggressive to players even in same realm
			// TODO: findout if trainers can be aggro at all

			//int aggro = CalculateAggroLevelToTarget(player);

			// "aggressive towards you!", "hostile towards you.", "neutral towards you.", "friendly."
			// TODO: correct aggro strings
			string aggroLevelString = "";
			int aggroLevel;
			IOldAggressiveBrain aggroBrain = Brain as IOldAggressiveBrain;
			//Calculate Faction aggro - base AggroLevel needs to be greater tha 0 for Faction aggro calc to work.
			if (Faction != null && aggroBrain != null && aggroBrain.AggroLevel > 0)
			{
				aggroLevel = Faction.GetAggroToFaction(player);
				
				if (GameServer.ServerRules.IsSameRealm(this, player, true))
				{
					if (firstLetterUppercase) aggroLevelString = LanguageMgr.GetTranslation(player.Client.Account.Language, "GameNPC.GetAggroLevelString.Friendly2");
					else aggroLevelString = LanguageMgr.GetTranslation(player.Client.Account.Language, "GameNPC.GetAggroLevelString.Friendly1");
				}
				else if (aggroLevel > 75)
					aggroLevelString = LanguageMgr.GetTranslation(player.Client.Account.Language, "GameNPC.GetAggroLevelString.Aggressive1");
				else if (aggroLevel > 50)
					aggroLevelString = LanguageMgr.GetTranslation(player.Client.Account.Language, "GameNPC.GetAggroLevelString.Hostile1");
				else if (aggroLevel > 25)
					aggroLevelString = LanguageMgr.GetTranslation(player.Client.Account.Language, "GameNPC.GetAggroLevelString.Neutral1");
				else
					aggroLevelString = LanguageMgr.GetTranslation(player.Client.Account.Language, "GameNPC.GetAggroLevelString.Friendly1");
			}
			else
			{
				if (GameServer.ServerRules.IsSameRealm(this, player, true))
				{
					if (firstLetterUppercase) aggroLevelString = LanguageMgr.GetTranslation(player.Client.Account.Language, "GameNPC.GetAggroLevelString.Friendly2");
					else aggroLevelString = LanguageMgr.GetTranslation(player.Client.Account.Language, "GameNPC.GetAggroLevelString.Friendly1");
				}
				else if (aggroBrain != null && aggroBrain.AggroLevel > 0)
				{
					if (firstLetterUppercase) aggroLevelString = LanguageMgr.GetTranslation(player.Client.Account.Language, "GameNPC.GetAggroLevelString.Aggressive2");
					else aggroLevelString = LanguageMgr.GetTranslation(player.Client.Account.Language, "GameNPC.GetAggroLevelString.Aggressive1");
				}
				else
				{
					if (firstLetterUppercase) aggroLevelString = LanguageMgr.GetTranslation(player.Client.Account.Language, "GameNPC.GetAggroLevelString.Neutral2");
					else aggroLevelString = LanguageMgr.GetTranslation(player.Client.Account.Language, "GameNPC.GetAggroLevelString.Neutral1");
				}
			}
			return LanguageMgr.GetTranslation(player.Client.Account.Language, "GameNPC.GetAggroLevelString.TowardsYou", aggroLevelString);
		}

		public string GetPronoun(int form, bool capitalize, string lang)
		{
			switch (Gender)
			{
				case eGender.Male:
					switch (form)
					{
						case 1:
							return Capitalize(capitalize, LanguageMgr.GetTranslation(lang, "GameLiving.Pronoun.Male.Possessive"));
						case 2:
							return Capitalize(capitalize, LanguageMgr.GetTranslation(lang, "GameLiving.Pronoun.Male.Objective"));
						default:
							return Capitalize(capitalize, LanguageMgr.GetTranslation(lang, "GameLiving.Pronoun.Male.Subjective"));
					}

				case eGender.Female:
					switch (form)
					{
						case 1:
							return Capitalize(capitalize, LanguageMgr.GetTranslation(lang, "GameLiving.Pronoun.Female.Possessive"));
						case 2:
							return Capitalize(capitalize, LanguageMgr.GetTranslation(lang, "GameLiving.Pronoun.Female.Objective"));
						default:
							return Capitalize(capitalize, LanguageMgr.GetTranslation(lang, "GameLiving.Pronoun.Female.Subjective"));
					}
				default:
					switch (form)
					{
						case 1:
							return Capitalize(capitalize, LanguageMgr.GetTranslation(lang, "GameLiving.Pronoun.Neutral.Possessive"));
						case 2:
							return Capitalize(capitalize, LanguageMgr.GetTranslation(lang, "GameLiving.Pronoun.Neutral.Objective"));
						default:
							return Capitalize(capitalize, LanguageMgr.GetTranslation(lang, "GameLiving.Pronoun.Neutral.Subjective"));
					}
			}
		}

		/// <summary>
		/// Gets the proper pronoun including capitalization.
		/// </summary>
		/// <param name="form">1=his; 2=him; 3=he</param>
		/// <param name="capitalize"></param>
		/// <returns></returns>
		public override string GetPronoun(int form, bool capitalize)
		{
			String language = ServerProperties.Properties.DB_LANGUAGE;

			switch (Gender)
			{
				case eGender.Male:
					switch (form)
					{
						case 1:
							return Capitalize(capitalize, LanguageMgr.GetTranslation(language,
																					 "GameLiving.Pronoun.Male.Possessive"));
						case 2:
							return Capitalize(capitalize, LanguageMgr.GetTranslation(language,
																					 "GameLiving.Pronoun.Male.Objective"));
						default:
							return Capitalize(capitalize, LanguageMgr.GetTranslation(language,
																					 "GameLiving.Pronoun.Male.Subjective"));
					}

				case eGender.Female:
					switch (form)
					{
						case 1:
							return Capitalize(capitalize, LanguageMgr.GetTranslation(language,
																					 "GameLiving.Pronoun.Female.Possessive"));
						case 2:
							return Capitalize(capitalize, LanguageMgr.GetTranslation(language,
																					 "GameLiving.Pronoun.Female.Objective"));
						default:
							return Capitalize(capitalize, LanguageMgr.GetTranslation(language,
																					 "GameLiving.Pronoun.Female.Subjective"));
					}
				default:
					switch (form)
					{
						case 1:
							return Capitalize(capitalize, LanguageMgr.GetTranslation(language,
																					 "GameLiving.Pronoun.Neutral.Possessive"));
						case 2:
							return Capitalize(capitalize, LanguageMgr.GetTranslation(language,
																					 "GameLiving.Pronoun.Neutral.Objective"));
						default:
							return Capitalize(capitalize, LanguageMgr.GetTranslation(language,
																					 "GameLiving.Pronoun.Neutral.Subjective"));
					}
			}
		}

		/// <summary>
		/// Adds messages to ArrayList which are sent when object is targeted
		/// </summary>
		/// <param name="player">GamePlayer that is examining this object</param>
		/// <returns>list with string messages</returns>
		public override IList GetExamineMessages(GamePlayer player)
		{
			switch (player.Client.Account.Language)
			{
				case "EN":
				{
					IList list = base.GetExamineMessages(player);
					// Message: You examine {0}. {1} is {2}.
					list.Add(LanguageMgr.GetTranslation(player.Client.Account.Language, "GameNPC.GetExamineMessages.YouExamine", GetName(0, false), GetPronoun(0, true), GetAggroLevelString(player, false)));
					return list;
				}
				default:
					{
						IList list = new ArrayList(4);
						// Message: You examine {0}. {1} is {2}.
						list.Add(LanguageMgr.GetTranslation(player.Client.Account.Language, "GameNPC.GetExamineMessages.YouExamine",
															GetName(0, false, player.Client.Account.Language, this),
															GetPronoun(0, true, player.Client.Account.Language), GetAggroLevelString(player, false)));
						return list;
					}
			}
		}

		/*		/// <summary>
				/// Pronoun of this NPC in case you need to refer it in 3rd person
				/// http://webster.commnet.edu/grammar/cases.htm
				/// </summary>
				/// <param name="firstLetterUppercase"></param>
				/// <param name="form">0=Subjective, 1=Possessive, 2=Objective</param>
				/// <returns>pronoun of this object</returns>
				public override string GetPronoun(bool firstLetterUppercase, int form)
				{
					// TODO: when mobs will get gender
					if(PlayerCharacter.Gender == 0)
						// male
						switch(form)
						{
							default: // Subjective
								if(firstLetterUppercase) return "He"; else return "he";
							case 1:	// Possessive
								if(firstLetterUppercase) return "His"; else return "his";
							case 2:	// Objective
								if(firstLetterUppercase) return "Him"; else return "him";
						}
					else
						// female
						switch(form)
						{
							default: // Subjective
								if(firstLetterUppercase) return "She"; else return "she";
							case 1:	// Possessive
								if(firstLetterUppercase) return "Her"; else return "her";
							case 2:	// Objective
								if(firstLetterUppercase) return "Her"; else return "her";
						}

					// it
					switch(form)
					{
						// Subjective
						default: if(firstLetterUppercase) return "It"; else return "it";
						// Possessive
						case 1:	if(firstLetterUppercase) return "Its"; else return "its";
						// Objective
						case 2: if(firstLetterUppercase) return "It"; else return "it";
					}
				}*/
		#endregion

		#region Interact/WhisperReceive/SayTo

		/// <summary>
		/// The possible ambient triggers for GameNPC actions (e.g., killing, roaming, dying)
		/// </summary>
		public enum eAmbientTrigger
		{
			spawning,
			dying,
			aggroing,
			fighting,
			roaming,
			killing,
			moving,
			interact,
			seeing
		}

		/// <summary>
		/// The ambient texts
		/// </summary>
		public IList<MobXAmbientBehaviour> ambientTexts;

		/// <summary>
		/// This function is called from the ObjectInteractRequestHandler
		/// </summary>
		/// <param name="player">GamePlayer that interacts with this object</param>
		/// <returns>false if interaction is prevented</returns>
		public override bool Interact(GamePlayer player)
		{
			if (!base.Interact(player)) return false;
			//if (!GameServer.ServerRules.IsSameRealm(this, player, true) && Faction.GetAggroToFaction(player) > 25)
			if (!GameServer.ServerRules.IsSameRealm(this, player, true) && Faction != null && Faction.GetAggroToFaction(player) > 50)
			{
				player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "GameNPC.Interact.DirtyLook",
					GetName(0, true, player.Client.Account.Language, this)), eChatType.CT_System, eChatLoc.CL_SystemWindow);

				Notify(GameObjectEvent.InteractFailed, this, new InteractEventArgs(player));
				return false;
			}
			if (MAX_PASSENGERS > 1)
			{
				string name = "";
				if (this is GameTaxiBoat)
					name = "boat";
				if (this is GameSiegeRam)
					name = "ram";

				if (this is GameSiegeRam && player.Realm != this.Realm)
				{
					player.Out.SendMessage($"This siege equipment is owned by an enemy realm!", eChatType.CT_System, eChatLoc.CL_SystemWindow);
					return false;
				}
				

				if (RiderSlot(player) != -1)
				{
					player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "GameNPC.Interact.AlreadyRiding", name), eChatType.CT_System, eChatLoc.CL_SystemWindow);
					return false;
				}

				if (GetFreeArrayLocation() == -1)
				{
					player.Out.SendMessage(LanguageMgr.GetTranslation(player.Client.Account.Language, "GameNPC.Interact.IsFull", name), eChatType.CT_System, eChatLoc.CL_SystemWindow);
					return false;
				}

				if (player.IsRiding)
				{
					player.DismountSteed(true);
				}

				if (player.IsOnHorse)
				{
					player.IsOnHorse = false;
				}

				player.MountSteed(this, true);
			}
			
			FireAmbientSentence(eAmbientTrigger.interact, player);
			return true;
		}

		/// <summary>
		/// ToDo
		/// </summary>
		/// <param name="source"></param>
		/// <param name="text"></param>
		/// <returns></returns>
		public override bool WhisperReceive(GameLiving source, string text)
		{
			if (!base.WhisperReceive(source, text))
				return false;
			if (source is GamePlayer == false)
				return true;

			GamePlayer player = (GamePlayer)source;

			//TODO: Guards in rvr areas doesn't need check
			if (text == "task")
			{
				if (source.TargetObject == null)
					return false;
				if (KillTask.CheckAvailability(player, (GameLiving)source.TargetObject))
				{
					KillTask.BuildTask(player, (GameLiving)source.TargetObject);
					return true;
				}
				else if (MoneyTask.CheckAvailability(player, (GameLiving)source.TargetObject))
				{
					MoneyTask.BuildTask(player, (GameLiving)source.TargetObject);
					return true;
				}
				else if (CraftTask.CheckAvailability(player, (GameLiving)source.TargetObject))
				{
					CraftTask.BuildTask(player, (GameLiving)source.TargetObject);
					return true;
				}
			}
			return true;
		}

		public override bool ReceiveItem(GameLiving source, InventoryItem item)
		{
			if (this.DataQuestList.Count > 0)
			{
				foreach (DataQuest quest in DataQuestList)
				{
					quest.Notify(GameLivingEvent.ReceiveItem, this, new ReceiveItemEventArgs(source, this, item));
				}
			}
			return base.ReceiveItem(source, item);
		}

		/// <summary>
		/// Format "say" message and send it to target in popup window
		/// </summary>
		/// <param name="target"></param>
		/// <param name="message"></param>
		public virtual void SayTo(GamePlayer target, string message, bool announce = true)
		{
			SayTo(target, eChatLoc.CL_PopupWindow, message, announce);
		}

		/// <summary>
		/// Format "say" message and send it to target
		/// </summary>
		/// <param name="target"></param>
		/// <param name="loc">chat location of the message</param>
		/// <param name="message"></param>
		public virtual void SayTo(GamePlayer target, eChatLoc loc, string message, bool announce = true)
		{
			if (target == null)
				return;

			TurnTo(target);
			string resultText = LanguageMgr.GetTranslation(target.Client.Account.Language, "GameNPC.SayTo.Says", GetName(0, true, target.Client.Account.Language, this), message);
			switch (loc)
			{
				case eChatLoc.CL_PopupWindow:
					target.Out.SendMessage(resultText, eChatType.CT_System, eChatLoc.CL_PopupWindow);
					if (announce)
					{
						Message.ChatToArea(this, LanguageMgr.GetTranslation(target.Client.Account.Language, "GameNPC.SayTo.SpeaksTo", GetName(0, true, target.Client.Account.Language, this), target.GetName(0, false)), eChatType.CT_System, WorldMgr.SAY_DISTANCE, target);
					}
					break;
				case eChatLoc.CL_ChatWindow:
					target.Out.SendMessage(resultText, eChatType.CT_Say, eChatLoc.CL_ChatWindow);
					break;
				case eChatLoc.CL_SystemWindow:
					target.Out.SendMessage(resultText, eChatType.CT_System, eChatLoc.CL_SystemWindow);
					break;
			}
		}
		#endregion

		#region Combat

		/// <summary>
		/// The property that holds charmed tick if any
		/// </summary>
		public const string CHARMED_TICK_PROP = "CharmedTick";

		/// <summary>
		/// The duration of no exp after charmed, in game ticks
		/// </summary>
		public const int CHARMED_NOEXP_TIMEOUT = 60000;

		public const string LAST_LOS_TARGET_PROPERTY = "last_LOS_checkTarget";
		public const string LAST_LOS_TICK_PROPERTY = "last_LOS_checkTick";
		public const string NUM_LOS_CHECKS_INPROGRESS = "num_LOS_progress";

		public object LOS_LOCK = new object();

		public GameObject m_targetLOSObject = null;

        public virtual void StopAttack()
        {
            attackComponent.NPCStopAttack();
        }

        /// <summary>
        /// Starts a melee attack on a target
        /// </summary>
        /// <param name="target">The object to attack</param>
        public virtual void StartAttack(GameObject target)
        {
            attackComponent.StartAttack(target);
            //if(m_followTimer != null) m_followTimer.Stop();
			if(CurrentFollowTarget!=target)
			{
				StopFollowing();
				Follow(target, m_followMinDist, m_followMaxDist);
			}
            
            FireAmbientSentence(eAmbientTrigger.fighting, target);
            //if (target == null)
            //    return;

            //TargetObject = target;

            //long lastTick = this.TempProperties.getProperty<long>(LAST_LOS_TICK_PROPERTY);

            //if (ServerProperties.Properties.ALWAYS_CHECK_PET_LOS &&
            //    Brain != null &&
            //    Brain is IControlledBrain &&
            //    (target is GamePlayer || (target is GameNPC && (target as GameNPC).Brain != null && (target as GameNPC).Brain is IControlledBrain)))
            //{
            //    GameObject lastTarget = (GameObject)this.TempProperties.getProperty<object>(LAST_LOS_TARGET_PROPERTY, null);
            //    if (lastTarget != null && lastTarget == target)
            //    {
            //        if (lastTick != 0 && CurrentRegion.Time - lastTick < ServerProperties.Properties.LOS_PLAYER_CHECK_FREQUENCY * 1000)
            //            return;
            //    }

            //    GamePlayer losChecker = null;
            //    if (target is GamePlayer)
            //    {
            //        losChecker = target as GamePlayer;
            //    }
            //    else if (target is GameNPC && (target as GameNPC).Brain is IControlledBrain)
            //    {
            //        losChecker = ((target as GameNPC).Brain as IControlledBrain).GetPlayerOwner();
            //    }
            //    else
            //    {
            //        // try to find another player to use for checking line of site
            //        foreach (GamePlayer player in this.GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE))
            //        {
            //            losChecker = player;
            //            break;
            //        }
            //    }

            //    if (losChecker == null)
            //    {
            //        return;
            //    }

            //    lock (LOS_LOCK)
            //    {
            //        int count = TempProperties.getProperty<int>(NUM_LOS_CHECKS_INPROGRESS, 0);

            //        if (count > 10)
            //        {
            //            log.DebugFormat("{0} LOS count check exceeds 10, aborting LOS check!", Name);

            //            // Now do a safety check.  If it's been a while since we sent any check we should clear count
            //            if (lastTick == 0 || CurrentRegion.Time - lastTick > ServerProperties.Properties.LOS_PLAYER_CHECK_FREQUENCY * 1000)
            //            {
            //                log.Debug("LOS count reset!");
            //                TempProperties.setProperty(NUM_LOS_CHECKS_INPROGRESS, 0);
            //            }

            //            return;
            //        }

            //        count++;
            //        TempProperties.setProperty(NUM_LOS_CHECKS_INPROGRESS, count);

            //        TempProperties.setProperty(LAST_LOS_TARGET_PROPERTY, target);
            //        TempProperties.setProperty(LAST_LOS_TICK_PROPERTY, CurrentRegion.Time);
            //        m_targetLOSObject = target;

            //    }

            //    losChecker.Out.SendCheckLOS(this, target, new CheckLOSResponse(this.NPCStartAttackCheckLOS));
            //    return;
            //}

            //ContinueStartAttack(target);
        }

        /// <summary>
        /// We only attack if we have LOS
        /// </summary>
        /// <param name="player"></param>
        /// <param name="response"></param>
        /// <param name="targetOID"></param>
        public void NPCStartAttackCheckLOS(GamePlayer player, ushort response, ushort targetOID)
		{
			lock (LOS_LOCK)
			{
				int count = TempProperties.getProperty<int>(NUM_LOS_CHECKS_INPROGRESS, 0);
				count--;
				TempProperties.setProperty(NUM_LOS_CHECKS_INPROGRESS, Math.Max(0, count));
			}

			if ((response & 0x100) == 0x100)
			{
				// make sure we didn't switch targets
				if (TargetObject != null && m_targetLOSObject != null && TargetObject == m_targetLOSObject)
					attackComponent.ContinueStartAttack(m_targetLOSObject);
			}
			else
			{
				if (m_targetLOSObject != null && m_targetLOSObject is GameLiving && Brain != null && Brain is IOldAggressiveBrain)
				{
					// there will be a think delay before mob attempts to attack next target
					(Brain as IOldAggressiveBrain).RemoveFromAggroList(m_targetLOSObject as GameLiving);
				}
			}
		}


		//public virtual void ContinueStartAttack(GameObject target)
		//{
		//	StopMoving();
		//	StopMovingOnPath();

		//	if (Brain != null && Brain is IControlledBrain)
		//	{
		//		if ((Brain as IControlledBrain).AggressionState == eAggressionState.Passive)
		//			return;

		//		GamePlayer owner = null;

		//		if ((owner = ((IControlledBrain)Brain).GetPlayerOwner()) != null)
		//			owner.Stealth(false);
		//	}

		//	SetLastMeleeAttackTick();
		//	StartMeleeAttackTimer();

		//	base.StartAttack(target);

		//	if (AttackState)
		//	{
		//		// if we're moving we need to lock down the current position
		//		if (IsMoving)
		//			SaveCurrentPosition();

		//		if (ActiveWeaponSlot == eActiveWeaponSlot.Distance)
		//		{
		//			// Archer mobs sometimes bug and keep trying to fire at max range unsuccessfully so force them to get just a tad closer.
		//			Follow(target, AttackRange - 30, STICKMAXIMUMRANGE);
		//		}
		//		else
		//		{
		//			Follow(target, STICKMINIMUMRANGE, STICKMAXIMUMRANGE);
		//		}
		//	}

		//}

		private int scalingFactor = 15;

		private int orbsReward = 0;
		
		
		public override double GetWeaponSkill(InventoryItem weapon)
		{
			/*
			 * https://camelotherald.fandom.com/wiki/Weapon_Skill
			[[[[LEVEL *DAMAGE_TABLE * (200 + BONUS * ITEM_BONUS) / 500]
			*(100 + STAT) / 100]
			*(100 + SPEC) / 100]
			*(100 + WEAPONSKILL_BONUS) / 100]
			*/
			int weaponskill = 0;
  
			weaponskill = (Level + 1) 
				* (int)(ScalingFactor/4) //mob damage table calc, basically
				* (200 + GetModified(eProperty.MeleeDamage)) / 500 //melee damage buffs
				* ((100 + Strength) / 100) //NPCs only use STR to calculate, can skip str or str/dex check
				* ((100 + GetModified(eProperty.WeaponSkill)) / 100); //weaponskill buffs
  
			return weaponskill;
        }
		

		public override void RangedAttackFinished()
		{
			base.RangedAttackFinished();

			if (ServerProperties.Properties.ALWAYS_CHECK_PET_LOS &&
				Brain != null &&
				Brain is IControlledBrain &&
				(TargetObject is GamePlayer || (TargetObject is GameNPC && (TargetObject as GameNPC).Brain != null && (TargetObject as GameNPC).Brain is IControlledBrain)))
			{
				GamePlayer player = null;

				if (TargetObject is GamePlayer)
				{
					player = TargetObject as GamePlayer;
				}
				else if (TargetObject is GameNPC && (TargetObject as GameNPC).Brain != null && (TargetObject as GameNPC).Brain is IControlledBrain)
				{
					if (((TargetObject as GameNPC).Brain as IControlledBrain).Owner is GamePlayer)
					{
						player = ((TargetObject as GameNPC).Brain as IControlledBrain).Owner as GamePlayer;
					}
				}

				if (player != null)
				{
					player.Out.SendCheckLOS(this, TargetObject, new CheckLOSResponse(NPCStopRangedAttackCheckLOS));
					if (ServerProperties.Properties.ENABLE_DEBUG)
					{
						log.Debug(Name + " sent LOS check to player " + player.Name);
					}
				}
			}
		}


		/// <summary>
		/// If we don't have LOS we stop attack
		/// </summary>
		/// <param name="player"></param>
		/// <param name="response"></param>
		/// <param name="targetOID"></param>
		public void NPCStopRangedAttackCheckLOS(GamePlayer player, ushort response, ushort targetOID)
		{
			if ((response & 0x100) != 0x100)
			{
				if (ServerProperties.Properties.ENABLE_DEBUG)
				{
					log.Debug(Name + " FAILED stop ranged attack LOS check to player " + player.Name);
				}

                attackComponent.NPCStopAttack();
			}
		}


		public void SetLastMeleeAttackTick()
		{
			if (TargetObject?.Realm == 0 || Realm == 0)
				m_lastAttackTickPvE = GameLoop.GameLoopTime;
			else
				m_lastAttackTickPvP = GameLoop.GameLoopTime;
		}

		public void StartMeleeAttackTimer()
		{
			if (attackComponent.Attackers.Count == 0)
			{
				if (SpellTimer == null)
					SpellTimer = new SpellAction(this);

				if (!SpellTimer.IsAlive)
					SpellTimer.Start(1);
			}
		}

		/// <summary>
		/// Returns the Damage this NPC does on an attack, adding 2H damage bonus if appropriate
		/// </summary>
		/// <param name="weapon">the weapon used for attack</param>
		/// <returns></returns>
		public virtual double AttackDamage(InventoryItem weapon)
		{
			double damage = attackComponent.AttackDamage(weapon);

			if (ActiveWeaponSlot == eActiveWeaponSlot.TwoHanded && m_blockChance > 0)
				switch (this)
				{
					case Keeps.GameKeepGuard guard:
						if (ServerProperties.Properties.GUARD_2H_BONUS_DAMAGE)
							damage *= (100 + m_blockChance) / 100.00;
						break;
					case GamePet pet:
						if (ServerProperties.Properties.PET_2H_BONUS_DAMAGE)
							damage *= (100 + m_blockChance) / 100.00;
						break;
					default:
						if (ServerProperties.Properties.MOB_2H_BONUS_DAMAGE)
							damage *= (100 + m_blockChance) / 100.00;
						break;
				}

			return damage;
		}

		/// <summary>
		/// Gets/sets the object health
		/// </summary>
		public override int Health
		{
			get
			{
				return base.Health;
			}
			set
			{
				base.Health = value;
				//Slow mobs down when they are hurt!
				short maxSpeed = MaxSpeed;
				if (CurrentSpeed > maxSpeed)
					CurrentSpeed = maxSpeed;
			}
		}

		/// <summary>
		/// npcs can always have mana to cast
		/// </summary>
		public override int Mana
		{
			get { return 5000; }
		}

		/// <summary>
		/// The Max Mana for this NPC
		/// </summary>
		public override int MaxMana
		{
			get { return 1000; }
		}

		/// <summary>
		/// The Concentration for this NPC
		/// </summary>
		public override int Concentration
		{
			get
			{
				return 500;
			}
		}

		/// <summary>
		/// Tests if this MOB should give XP and loot based on the XPGainers
		/// </summary>
		/// <returns>true if it should deal XP and give loot</returns>
		public virtual bool IsWorthReward
		{
			get
			{
				if (CurrentRegion == null || CurrentRegion.Time - CHARMED_NOEXP_TIMEOUT < TempProperties.getProperty<long>(CHARMED_TICK_PROP))
					return false;
				if (this.Brain is IControlledBrain)
					return false;
				lock (m_xpGainers.SyncRoot)
				{
					if (m_xpGainers.Keys.Count == 0) return false;
					foreach (DictionaryEntry de in m_xpGainers)
					{
						GameObject obj = (GameObject)de.Key;
						if (obj is GamePlayer)
						{
							//If a gameplayer with privlevel > 1 attacked the
							//mob, then the players won't gain xp ...
							if (((GamePlayer)obj).Client.Account.PrivLevel > 1)
								return false;
							//If a player to which we are gray killed up we
							//aren't worth anything either
							if (((GamePlayer)obj).IsObjectGreyCon(this))
								return false;
						}
						else
						{
							//If object is no gameplayer and realm is != none
							//then it means that a npc has hit this living and
							//it is not worth any xp ...
							//if(obj.Realm != (byte)eRealm.None)
							//If grey to at least one living then no exp
							if (obj is GameLiving && ((GameLiving)obj).IsObjectGreyCon(this))
								return false;
						}
					}
					return true;
				}
			}
			set
			{
			}
		}

		protected void ControlledNPC_Release()
		{
			if (this.ControlledBrain != null)
			{
				//log.Info("On tue le pet !");
				this.Notify(GameLivingEvent.PetReleased, ControlledBrain.Body);
			}
		}

		/// <summary>
		/// Called when this living dies
		/// </summary>
		public override void Die(GameObject killer)
		{
			Brain?.KillFSM();

			FireAmbientSentence(eAmbientTrigger.dying, killer);

			if (ControlledBrain != null)
				ControlledNPC_Release();

			if (killer != null)
			{
				if (killer is GamePet pet) killer = pet.Owner;
				if (IsWorthReward)
					DropLoot(killer);

				Message.SystemToArea(this, GetName(0, true) + " dies!", eChatType.CT_PlayerDied, killer);
				if (killer is GamePlayer)
					((GamePlayer)killer).Out.SendMessage(GetName(0, true) + " dies!", eChatType.CT_PlayerDied, eChatLoc.CL_SystemWindow);
			}
			StopFollowing();

			if (Group != null)
				Group.RemoveMember(this);

			if (killer != null)
			{
				// Handle faction alignement changes // TODO Review
				if ((Faction != null) && (killer is GamePlayer))
				{
					lock (this.XPGainers.SyncRoot)
					{ 
						// Get All Attackers. // TODO check if this shouldn't be set to Attackers instead of XPGainers ?
						foreach (DictionaryEntry de in this.XPGainers)
						{
							GameLiving living = de.Key as GameLiving;
							GamePlayer player = living as GamePlayer;

							// Get Pets Owner (// TODO check if they are not already treated as attackers ?)
							if (living is GameNPC && (living as GameNPC).Brain is IControlledBrain)
								player = ((living as GameNPC).Brain as IControlledBrain).GetPlayerOwner();

							if (player != null && player.ObjectState == GameObject.eObjectState.Active && player.IsAlive && player.IsWithinRadius(this, WorldMgr.MAX_EXPFORKILL_DISTANCE))
							{
								Faction.KillMember(player);
							}
						}
					}
				}

				// deal out exp and realm points based on server rules
				GameServer.ServerRules.OnNPCKilled(this, killer);
				base.Die(killer);
			}

			Delete();

			// remove temp properties
			TempProperties.removeAllProperties();

			if (!(this is GamePet))
				StartRespawn();
		}

		/// <summary>
		/// Stores the melee damage type of this NPC
		/// </summary>
		protected eDamageType m_meleeDamageType = eDamageType.Slash;

		/// <summary>
		/// Gets or sets the melee damage type of this NPC
		/// </summary>
		public virtual eDamageType MeleeDamageType
		{
			get { return m_meleeDamageType; }
			set { m_meleeDamageType = value; }
		}

		/// <summary>
		/// Returns the damage type of the current attack
		/// </summary>
		/// <param name="weapon">attack weapon</param>
		public eDamageType AttackDamageType(InventoryItem weapon)
		{
			return m_meleeDamageType;
		}

		/// <summary>
		/// Stores the NPC evade chance
		/// </summary>
		protected byte m_evadeChance;
		/// <summary>
		/// Stores the NPC block chance
		/// </summary>
		protected byte m_blockChance;
		/// <summary>
		/// Stores the NPC parry chance
		/// </summary>
		protected byte m_parryChance;
		/// <summary>
		/// Stores the NPC left hand swing chance
		/// </summary>
		protected byte m_leftHandSwingChance;

		/// <summary>
		/// Gets or sets the NPC evade chance
		/// </summary>
		public virtual byte EvadeChance
		{
			get { return m_evadeChance; }
			set { m_evadeChance = value; }
		}

		/// <summary>
		/// Gets or sets the NPC block chance
		/// </summary>
		public virtual byte BlockChance
		{
			get
			{
				//When npcs have two handed weapons, we don't want them to block
				if (ActiveWeaponSlot != eActiveWeaponSlot.Standard)
					return 0;

				return m_blockChance;
			}
			set
			{
				m_blockChance = value;
			}
		}

		/// <summary>
		/// Gets or sets the NPC parry chance
		/// </summary>
		public virtual byte ParryChance
		{
			get { return m_parryChance; }
			set { m_parryChance = value; }
		}

		/// <summary>
		/// Gets or sets the NPC left hand swing chance
		/// </summary>
		public byte LeftHandSwingChance
		{
			get { return m_leftHandSwingChance; }
			set { m_leftHandSwingChance = value; }
		}

		/// <summary>
		/// Calculates how many times left hand swings
		/// </summary>
		/// <returns></returns>
		public int CalculateLeftHandSwingCount()
		{
			if (Util.Chance(m_leftHandSwingChance))
				return 1;
			return 0;
		}

		/// <summary>
		/// Checks whether Living has ability to use lefthanded weapons
		/// </summary>
		public bool CanUseLefthandedWeapon
		{
			get { return m_leftHandSwingChance > 0; }
            set { CanUseLefthandedWeapon =  value; }
		}

		/// <summary>
		/// Method to switch the npc to Melee attacks
		/// </summary>
		/// <param name="target"></param>
		public void SwitchToMelee(GameObject target)
		{
			// Tolakram: Order is important here.  First StopAttack, then switch weapon
			StopFollowing();
            attackComponent.NPCStopAttack();

			InventoryItem twohand = Inventory.GetItem(eInventorySlot.TwoHandWeapon);
			InventoryItem righthand = Inventory.GetItem(eInventorySlot.RightHandWeapon);

			if (twohand != null && righthand == null)
				SwitchWeapon(eActiveWeaponSlot.TwoHanded);
			else if (twohand != null && righthand != null)
			{
				if (Util.Chance(50))
					SwitchWeapon(eActiveWeaponSlot.TwoHanded);
				else SwitchWeapon(eActiveWeaponSlot.Standard);
			}
			else
				SwitchWeapon(eActiveWeaponSlot.Standard);

			attackComponent.StartAttack(target);
		}

		/// <summary>
		/// Method to switch the guard to Ranged attacks
		/// </summary>
		/// <param name="target"></param>
		public void SwitchToRanged(GameObject target)
		{
			StopFollowing();
            attackComponent.NPCStopAttack();
			SwitchWeapon(eActiveWeaponSlot.Distance);
			attackComponent.StartAttack(target);
		}

		/// <summary>
		/// Draw the weapon, but don't actually start a melee attack.
		/// </summary>		
		public virtual void DrawWeapon()
		{
			if (!attackComponent.AttackState)
			{
                attackComponent.AttackState = true;

				BroadcastUpdate();

                attackComponent.AttackState = false;
			}
		}

		/// <summary>
		/// If npcs cant move, they cant be interupted from range attack
		/// </summary>
		/// <param name="attacker"></param>
		/// <param name="attackType"></param>
		/// <returns></returns>
		protected override bool OnInterruptTick(GameLiving attacker, AttackData.eAttackType attackType)
		{
			if (this.MaxSpeedBase == 0)
			{
				if (attackType == AttackData.eAttackType.Ranged || attackType == AttackData.eAttackType.Spell)
				{
					if (this.IsWithinRadius(attacker, 150) == false)
						return false;
				}
			}

			// Experimental - this prevents interrupts from causing ranged attacks to always switch to melee
			if (attackComponent.AttackState)
			{
				if (ActiveWeaponSlot == eActiveWeaponSlot.Distance && HealthPercent < MINHEALTHPERCENTFORRANGEDATTACK)
				{
					SwitchToMelee(attacker);
				}
				else if (ActiveWeaponSlot != eActiveWeaponSlot.Distance &&
						 Inventory != null &&
						 Inventory.GetItem(eInventorySlot.DistanceWeapon) != null &&
						 GetDistanceTo(attacker) > 500)
				{
					SwitchToRanged(attacker);
				}
			}

			return base.OnInterruptTick(attacker, attackType);
		}

		/// <summary>
		/// The time to wait before each mob respawn
		/// </summary>
		protected int m_respawnInterval;
		/// <summary>
		/// A timer that will respawn this mob
		/// </summary>
		protected ECSGameTimer m_respawnTimer;
		/// <summary>
		/// The sync object for respawn timer modifications
		/// </summary>
		protected readonly object m_respawnTimerLock = new object();
		/// <summary>
		/// The Respawn Interval of this mob in milliseconds
		/// </summary>
		public virtual int RespawnInterval
		{
			get
			{
				if (m_respawnInterval > 0 || m_respawnInterval < 0)
					return m_respawnInterval;

				int minutes = Util.Random(ServerProperties.Properties.NPC_MIN_RESPAWN_INTERVAL, ServerProperties.Properties.NPC_MIN_RESPAWN_INTERVAL + 5);

				if (Name != Name.ToLower())
				{
					minutes += 5;
				}

				if (Level <= 65 && Realm == 0)
				{
					return minutes * 60000;
				}
				else if (Realm != 0)
				{
					// 5 to 10 minutes for realm npc's
					return Util.Random(5 * 60000, 10 * 60000);
				}
				else
				{
					int add = (Level - 65) + ServerProperties.Properties.NPC_MIN_RESPAWN_INTERVAL;
					return (minutes + add) * 60000;
				}
			}
			set
			{
				m_respawnInterval = value;
			}
		}

		/// <summary>
		/// True if NPC is alive, else false.
		/// </summary>
		public override bool IsAlive
		{
			get
			{
				bool alive = base.IsAlive;
				if (alive && IsRespawning)
					return false;
				return alive;
			}
		}

		/// <summary>
		/// True, if the mob is respawning, else false.
		/// </summary>
		public bool IsRespawning
		{
			get
			{
				if (m_respawnTimer == null)
					return false;
				return m_respawnTimer.IsAlive;
			}
		}

		/// <summary>
		/// Starts the Respawn Timer
		/// </summary>
		public virtual void StartRespawn()
		{
			if (IsAlive) return;

			if (this.Brain is IControlledBrain)
				return;

			if (m_healthRegenerationTimer != null)
			{
				m_healthRegenerationTimer.Stop();
				m_healthRegenerationTimer = null;
			}

			int respawnInt = RespawnInterval;
			int minBound = (int) Math.Floor(respawnInt * .95);
			int maxBound = (int) Math.Floor(respawnInt * 1.05);
			respawnInt = Util.Random(minBound, maxBound);
			if (respawnInt > 0)
			{
				lock (m_respawnTimerLock)
				{
					if (m_respawnTimer == null)
					{
						m_respawnTimer = new ECSGameTimer(this);
						m_respawnTimer.Callback = new ECSGameTimer.ECSTimerCallback(RespawnTimerCallback);
					}
					else if (m_respawnTimer.IsAlive)
					{
						m_respawnTimer.Stop();
					}
					// register Mob as "respawning"
					CurrentRegion.MobsRespawning.TryAdd(this, respawnInt);

					m_respawnTimer.Start(respawnInt);
				}
			}
		}
		/// <summary>
		/// The callback that will respawn this mob
		/// </summary>
		/// <param name="respawnTimer">the timer calling this callback</param>
		/// <returns>the new interval</returns>
		protected virtual int RespawnTimerCallback(ECSGameTimer respawnTimer)
		{
			int dummy;
			// remove Mob from "respawning"
			CurrentRegion.MobsRespawning.TryRemove(this, out dummy);

			lock (m_respawnTimerLock)
			{
				if (m_respawnTimer != null)
				{
					m_respawnTimer.Stop();
					m_respawnTimer = null;
				}
			}

			//DOLConsole.WriteLine("respawn");
			//TODO some real respawn handling
			if (IsAlive) return 0;
			if (ObjectState == eObjectState.Active) return 0;
			
			/*
			if (m_level >= 5 && m_databaseLevel < 60)
			{
				int minBound = (int) Math.Round(m_databaseLevel * .9);
				int maxBound = (int) Math.Round(m_databaseLevel * 1.1);
				this.Level = (byte)  Util.Random(minBound, maxBound);
			}*/

			//Heal this mob, move it to the spawnlocation
			Health = MaxHealth;
			Mana = MaxMana;
			Endurance = MaxEndurance;
			int origSpawnX = m_spawnPoint.X;
			int origSpawnY = m_spawnPoint.Y;
			//X=(m_spawnX+Random(750)-350); //new SpawnX = oldSpawn +- 350 coords
			//Y=(m_spawnY+Random(750)-350);	//new SpawnX = oldSpawn +- 350 coords
			X = m_spawnPoint.X;
			Y = m_spawnPoint.Y;
			Z = m_spawnPoint.Z;
			Heading = m_spawnHeading;
			SpawnTick = GameLoop.GameLoopTime;
			AddToWorld();
			m_spawnPoint.X = origSpawnX;
			m_spawnPoint.Y = origSpawnY;
			return 0;
		}

		/// <summary>
		/// Callback timer for health regeneration
		/// </summary>
		/// <param name="selfRegenerationTimer">the regeneration timer</param>
		/// <returns>the new interval</returns>
		protected int HealthRegenerationTimerCallback(ECSGameTimer selfRegenerationTimer)
		{
			int period = m_healthRegenerationPeriod;
			if (!InCombat)
			{
				int oldPercent = HealthPercent;
				period = base.HealthRegenerationTimerCallback(selfRegenerationTimer);
				if (oldPercent != HealthPercent)
					BroadcastUpdate();
			}
			return (Health < MaxHealth) ? period : 0;
		}

		/// <summary>
		/// The chance for a critical hit
		/// </summary>
		public int AttackCriticalChance(InventoryItem weapon)
		{
			if (m_activeWeaponSlot == eActiveWeaponSlot.Distance)
			{
				if (rangeAttackComponent.RangedAttackType == eRangedAttackType.Critical)
					return 0; // no crit damage for crit shots
				else
					return GetModified(eProperty.CriticalArcheryHitChance);
			}

			return GetModified(eProperty.CriticalMeleeHitChance);
		}

		/// <summary>
		/// Stop attacking and following, but stay in attack mode (e.g. in
		/// order to cast a spell instead).
		/// </summary>
		public virtual void HoldAttack()
		{
			//if (m_attackAction != null)
			//	m_attackAction.Stop();
            if (attackComponent.attackAction != null)
                attackComponent.attackAction.CleanupAttackAction();
			StopFollowing();
		}

		/// <summary>
		/// Continue a previously started attack.
		/// </summary>
		public virtual void ContinueAttack(GameObject target)
		{
			//if (m_attackAction != null && target != null)
            if (attackComponent.attackAction != null && target != null)
            {
			    Follow(target, STICKMINIMUMRANGE, MaxDistance);
			    //m_attackAction.Start(1);
                attackComponent.attackAction.StartTime = 1;
			}
		}

        ///// <summary>
        ///// Stops all attack actions, including following target
        ///// </summary>
        //public override void StopAttack()
        //{
        //	base.StopAttack();
        //	StopFollowing();

        //	// Tolakram: If npc has a distance weapon it needs to be made active after attack is stopped
        //	if (Inventory != null && Inventory.GetItem(eInventorySlot.DistanceWeapon) != null && ActiveWeaponSlot != eActiveWeaponSlot.Distance)
        //		SwitchWeapon(eActiveWeaponSlot.Distance);
        //}

        public override void OnAttackedByEnemy(AttackData ad)
        {
			if(Brain is StandardMobBrain standardMobBrain && Brain is not NecromancerPetBrain)
            {
	           // Console.WriteLine($"dmg {ad.Damage} crit {ad.CriticalDamage} mod {Math.Abs(ad.Modifier)}");
				standardMobBrain.AddToAggroList(ad.Attacker, ad.Damage + ad.CriticalDamage + Math.Abs(ad.Modifier));
				standardMobBrain.OnAttackedByEnemy(ad);
            }

			if ((Flags & eFlags.STEALTH) != 0)
				Flags ^= GameNPC.eFlags.STEALTH;


			base.OnAttackedByEnemy(ad);
        }

        /// <summary>
        /// This method is called to drop loot after this mob dies
        /// </summary>
        /// <param name="killer">The killer</param>
        public virtual void DropLoot(GameObject killer)
		{
			// TODO: mobs drop "a small chest" sometimes
			ArrayList droplist = new ArrayList();
			ArrayList autolootlist = new ArrayList();
			ArrayList aplayer = new ArrayList();

			lock (m_xpGainers.SyncRoot)
			{
				if (m_xpGainers.Keys.Count == 0) return;

				ItemTemplate[] lootTemplates = LootMgr.GetLoot(this, killer);

				foreach (ItemTemplate lootTemplate in lootTemplates)
				{
					if (lootTemplate == null) continue;
					GameStaticItem loot = null;
					if (GameMoney.IsItemMoney(lootTemplate.Name))
					{
						long value = lootTemplate.Price;
						//GamePlayer killerPlayer = killer as GamePlayer;

						//[StephenxPimentel] - Zone Bonus XP Support
						if (ServerProperties.Properties.ENABLE_ZONE_BONUSES)
						{
							GamePlayer killerPlayer = killer as GamePlayer;
							if (killer is GameNPC)
							{
								if (killer is GameNPC && ((killer as GameNPC).Brain is IControlledBrain))
									killerPlayer = ((killer as GameNPC).Brain as IControlledBrain).GetPlayerOwner();
								else return;
							}

							int zoneBonus = (((int)value * ZoneBonus.GetCoinBonus(killerPlayer) / 100));
							if (zoneBonus > 0)
							{
								long amount = (long)(zoneBonus * ServerProperties.Properties.MONEY_DROP);
								killerPlayer.AddMoney(amount,
													  ZoneBonus.GetBonusMessage(killerPlayer, (int)(zoneBonus * ServerProperties.Properties.MONEY_DROP), ZoneBonus.eZoneBonusType.COIN),
													  eChatType.CT_Important, eChatLoc.CL_SystemWindow);
								InventoryLogging.LogInventoryAction(this, killerPlayer, eInventoryActionType.Loot, amount);
							}
						}

						if (Keeps.KeepBonusMgr.RealmHasBonus(DOL.GS.Keeps.eKeepBonusType.Coin_Drop_5, (eRealm)killer.Realm))
							value += (value / 100) * 5;
						else if (Keeps.KeepBonusMgr.RealmHasBonus(DOL.GS.Keeps.eKeepBonusType.Coin_Drop_3, (eRealm)killer.Realm))
							value += (value / 100) * 3;

						//this will need to be changed when the ML for increasing money is added
						if (value != lootTemplate.Price)
						{
							GamePlayer killerPlayer = killer as GamePlayer;
							if (killerPlayer != null)
								killerPlayer.Out.SendMessage(LanguageMgr.GetTranslation(killerPlayer.Client, "GameNPC.DropLoot.AdditionalMoney", Money.GetString(value - lootTemplate.Price)), eChatType.CT_Loot, eChatLoc.CL_SystemWindow);
						}

						//Mythical Coin bonus property (Can be used for any equipped item, bonus 235)
						if (killer is GamePlayer)
						{
							GamePlayer killerPlayer = killer as GamePlayer;
							if (killerPlayer.GetModified(eProperty.MythicalCoin) > 0)
							{
								value += (value * killerPlayer.GetModified(eProperty.MythicalCoin)) / 100;
								killerPlayer.Out.SendMessage(LanguageMgr.GetTranslation(killerPlayer.Client,
																						"GameNPC.DropLoot.ItemAdditionalMoney", Money.GetString(value - lootTemplate.Price)), eChatType.CT_Loot, eChatLoc.CL_SystemWindow);
							}
						}

						loot = new GameMoney(value, this);
						loot.Name = lootTemplate.Name;
						loot.Model = (ushort)lootTemplate.Model;
					}
					else if (lootTemplate.Name.StartsWith("scroll|"))
					{
						String[] scrollData = lootTemplate.Name.Split('|');

						if (scrollData.Length >= 3)
						{
							String artifactID = scrollData[1];
							int pageNumber = UInt16.Parse(scrollData[2]);
							loot = ArtifactMgr.CreateScroll(artifactID, pageNumber);
						}

						if (loot == null)
						{
							log.Error($"Artifact scroll could not be created for data string [{lootTemplate.Name}]");
							continue;
						}
						else
						{
							loot.X = X;
							loot.Y = Y;
							loot.Z = Z;
							loot.Heading = Heading;
							loot.CurrentRegion = CurrentRegion;
							(loot as WorldInventoryItem).Item.IsCrafted = false;
							(loot as WorldInventoryItem).Item.Creator = Name;
						}
					}
					else
					{
						InventoryItem invitem;

						if (lootTemplate is ItemUnique)
						{
							GameServer.Database.AddObject(lootTemplate);
							invitem = GameInventoryItem.Create(lootTemplate as ItemUnique);
						}
						else
							invitem = GameInventoryItem.Create(lootTemplate);

						loot = new WorldInventoryItem(invitem);
						loot.X = X;
						loot.Y = Y;
						loot.Z = Z;
						loot.Heading = Heading;
						loot.CurrentRegion = CurrentRegion;
						(loot as WorldInventoryItem).Item.IsCrafted = false;
						(loot as WorldInventoryItem).Item.Creator = Name;

						// This may seem like an odd place for this code, but loot-generating code further up the line
						// is dealing strictly with ItemTemplate objects, while you need the InventoryItem in order
						// to be able to set the Count property.
						// Converts single drops of loot with PackSize > 1 (and MaxCount >= PackSize) to stacks of Count = PackSize
						if (((WorldInventoryItem)loot).Item.PackSize > 1 && ((WorldInventoryItem)loot).Item.MaxCount >= ((WorldInventoryItem)loot).Item.PackSize)
						{
							((WorldInventoryItem)loot).Item.Count = ((WorldInventoryItem)loot).Item.PackSize;
						}
					}

					GamePlayer playerAttacker = null;
					foreach (GameObject gainer in m_xpGainers.Keys)
					{
						if (gainer is GamePlayer)
						{
							playerAttacker = gainer as GamePlayer;
							if (loot.Realm == 0)
								loot.Realm = ((GamePlayer)gainer).Realm;
						}
						loot.AddOwner(gainer);
						if (gainer is GameNPC)
						{
							IControlledBrain brain = ((GameNPC)gainer).Brain as IControlledBrain;
							if (brain != null)
							{
								playerAttacker = brain.GetPlayerOwner();
								loot.AddOwner(brain.GetPlayerOwner());
							}
						}
					}
					if (playerAttacker == null) return; // no loot if mob kills another mob


					droplist.Add(loot.GetName(1, false));
					loot.AddToWorld();

					foreach (GameObject gainer in m_xpGainers.Keys)
					{
						if (gainer is GamePlayer)
						{
							GamePlayer player = gainer as GamePlayer;
							if (player.Autoloot && loot.IsWithinRadius(player, 2400)) // should be large enough for most casters to autoloot
							{
								if (player.Group == null || (player.Group != null && player == player.Group.Leader))
									aplayer.Add(player);
								autolootlist.Add(loot);
							}
						}
					}
				}
			}

			BroadcastLoot(droplist);

			if (autolootlist.Count > 0)
			{
				foreach (GameObject obj in autolootlist)
				{
					foreach (GamePlayer player in aplayer)
					{
						player.PickupObject(obj, true);
						break;
					}
				}
			}
		}

		/// <summary>
		/// The enemy is healed, so we add to the xp gainers list
		/// </summary>
		/// <param name="enemy"></param>
		/// <param name="healSource"></param>
		/// <param name="changeType"></param>
		/// <param name="healAmount"></param>
		public override void EnemyHealed(GameLiving enemy, GameObject healSource, eHealthChangeType changeType, int healAmount)
		{
			base.EnemyHealed(enemy, healSource, changeType, healAmount);

			if (changeType != eHealthChangeType.Spell)
				return;
			if (enemy == healSource)
				return;
			if (!IsAlive)
				return;

			var attackerLiving = healSource as GameLiving;
			if (attackerLiving == null)
				return;

			Group attackerGroup = attackerLiving.Group;
			if (attackerGroup != null)
			{
				// collect "helping" group players in range
				var xpGainers = attackerGroup.GetMembersInTheGroup()
					.Where(l => this.IsWithinRadius(l, WorldMgr.MAX_EXPFORKILL_DISTANCE) && l.IsAlive && l.ObjectState == eObjectState.Active).ToArray();

				float damageAmount = (float)healAmount / xpGainers.Length;

				foreach (GameLiving living in xpGainers)
				{
					// add players in range for exp to exp gainers
					this.AddXPGainer(living, damageAmount);
				}
			}
			else
			{
				this.AddXPGainer(healSource, (float)healAmount);
			}
			//DealDamage needs to be called after addxpgainer!
		}

		#endregion

		#region Spell
		private List<Spell> m_spells = new List<Spell>(0);

		//public bool SortedSpells = false;
		/// <summary>
		/// property of spell array of NPC
		/// </summary>
		public virtual IList Spells
		{
			get { return m_spells; }
			set
			{
				if (value == null || value.Count < 1)
				{
					m_spells.Clear();
					InstantHarmfulSpells = null;
					HarmfulSpells = null;
					InstantHealSpells = null;
					HealSpells = null;
					InstantMiscSpells = null;
					MiscSpells = null;
				}
				else
				{
					m_spells = value.Cast<Spell>().ToList();
					//if(!SortedSpells)
						SortSpells();
				}
			}
		}

		/// <summary>
		/// Harmful spell list and accessor
		/// </summary>
		public List<Spell> HarmfulSpells { get; set; } = null;

		/// <summary>
		/// Whether or not the NPC can cast harmful spells with a cast time.
		/// </summary>
		public bool CanCastHarmfulSpells
		{
			get { return (HarmfulSpells != null && HarmfulSpells.Count > 0); }
		}

		/// <summary>
		/// Instant harmful spell list and accessor
		/// </summary>
		public List<Spell> InstantHarmfulSpells { get; set; } = null;

		/// <summary>
		/// Whether or not the NPC can cast harmful instant spells.
		/// </summary>
		public bool CanCastInstantHarmfulSpells
		{
			get { return (InstantHarmfulSpells != null && InstantHarmfulSpells.Count > 0); }
		}

		/// <summary>
		/// Healing spell list and accessor
		/// </summary>
		public List<Spell> HealSpells { get; set; } = null;

		/// <summary>
		/// Whether or not the NPC can cast heal spells with a cast time.
		/// </summary>
		public bool CanCastHealSpells
		{
			get { return (HealSpells != null && HealSpells.Count > 0); }
		}

		/// <summary>
		/// Instant healing spell list and accessor
		/// </summary>
		public List<Spell> InstantHealSpells { get; set; } = null;

		/// <summary>
		/// Whether or not the NPC can cast instant healing spells.
		/// </summary>
		public bool CanCastInstantHealSpells
		{
			get { return (InstantHealSpells != null && InstantHealSpells.Count > 0); }
		}

		/// <summary>
		/// Miscellaneous spell list and accessor
		/// </summary>
		public List<Spell> MiscSpells { get; set; } = null;

		/// <summary>
		/// Whether or not the NPC can cast miscellaneous spells with a cast time.
		/// </summary>
		public bool CanCastMiscSpells
		{
			get { return (MiscSpells != null && MiscSpells.Count > 0); }
		}

		/// <summary>
		/// Instant miscellaneous spell list and accessor
		/// </summary>
		public List<Spell> InstantMiscSpells { get; set; } = null;

		/// <summary>
		/// Whether or not the NPC can cast miscellaneous instant spells.
		/// </summary>
		public bool CanCastInstantMiscSpells
		{
			get { return (InstantMiscSpells != null && InstantMiscSpells.Count > 0); }
		}

		/// <summary>
		/// Sort spells into specific lists
		/// </summary>
		public virtual void SortSpells()
		{
			if (Spells.Count < 1)
				return;

			// Clear the lists
			if (InstantHarmfulSpells != null)
				InstantHarmfulSpells.Clear();
			if (HarmfulSpells != null)
				HarmfulSpells.Clear();

			if (InstantHealSpells != null)
				InstantHealSpells.Clear();
			if (HealSpells != null)
				HealSpells.Clear();

			if (InstantMiscSpells != null)
				InstantMiscSpells.Clear();
			if (MiscSpells != null)
				MiscSpells.Clear();

			// Sort spells into lists
			foreach (Spell spell in m_spells)
			{
				if (spell == null)
					continue;


				if (spell.IsHarmful)
				{
					if (spell.IsInstantCast)
					{
						if (InstantHarmfulSpells == null)
							InstantHarmfulSpells = new List<Spell>(1);
						InstantHarmfulSpells.Add(spell);
					}
					else
					{
						if (HarmfulSpells == null)
							HarmfulSpells = new List<Spell>(1);
						HarmfulSpells.Add(spell);
					}
				}
				else if (spell.IsHealing)
				{
					if (spell.IsInstantCast)
					{
						if (InstantHealSpells == null)
							InstantHealSpells = new List<Spell>(1);
						InstantHealSpells.Add(spell);
					}
					else
					{
						if (HealSpells == null)
							HealSpells = new List<Spell>(1);
						HealSpells.Add(spell);
					}
				}
				else
				{
					if (spell.IsInstantCast)
					{
						if (InstantMiscSpells == null)
							InstantMiscSpells = new List<Spell>(1);
						InstantMiscSpells.Add(spell);
					}
					else
					{
						if (MiscSpells == null)
							MiscSpells = new List<Spell>(1);
						MiscSpells.Add(spell);
					}
				}
			} // foreach

			//SortedSpells = true;
		}
		#endregion

		#region Styles
		/// <summary>
		/// Styles for this NPC
		/// </summary>
		private IList m_styles = new List<Style>(0);
		public IList Styles
		{
			get { return m_styles; }
			set
			{
				m_styles = value;
				this.SortStyles();
			}
		}

		/// <summary>
		/// Chain styles for this NPC
		/// </summary>
		public List<Style> StylesChain { get; protected set; } = null;

		/// <summary>
		/// Defensive styles for this NPC
		/// </summary>
		public List<Style> StylesDefensive { get; protected set; } = null;

		/// <summary>
		/// Back positional styles for this NPC
		/// </summary>
		public List<Style> StylesBack { get; protected set; } = null;

		/// <summary>
		/// Side positional styles for this NPC
		/// </summary>
		public List<Style> StylesSide { get; protected set; } = null;

		/// <summary>
		/// Front positional styles for this NPC
		/// </summary>
		public List<Style> StylesFront { get; protected set; } = null;

		/// <summary>
		/// Anytime styles for this NPC
		/// </summary>
		public List<Style> StylesAnytime { get; protected set; } = null;

		/// <summary>
		/// Sorts styles by type for more efficient style selection later
		/// </summary>
		public virtual void SortStyles()
		{
			if (StylesChain != null)
				StylesChain.Clear();

			if (StylesDefensive != null)
				StylesDefensive.Clear();

			if (StylesBack != null)
				StylesBack.Clear();

			if (StylesSide != null)
				StylesSide.Clear();

			if (StylesFront != null)
				StylesFront.Clear();

			if (StylesAnytime != null)
				StylesAnytime.Clear();

			if (m_styles == null)
				return;

			foreach (Style style in m_styles)
			{
				if (style == null)
				{
					if (log.IsWarnEnabled)
					{
						var sError = $"GameNPC.SortStyles(): NULL style for NPC named {Name}";
						if (m_InternalID != null)
							sError += $", InternalID {this.m_InternalID}";
						if (m_npcTemplate != null)
							sError += $", NPCTemplateID {m_npcTemplate.TemplateId}";
						log.Warn(sError);
					}
					continue; // Keep sorting, as a later style may not be null
				}// if (s == null)

				switch (style.OpeningRequirementType)
				{
					case Style.eOpening.Defensive:
						if (StylesDefensive == null)
							StylesDefensive = new List<Style>(1);
						StylesDefensive.Add(style);
						break;
					case Style.eOpening.Positional:
						switch ((Style.eOpeningPosition)style.OpeningRequirementValue)
						{
							case Style.eOpeningPosition.Back:
								if (StylesBack == null)
									StylesBack = new List<Style>(1);
								StylesBack.Add(style);
								break;
							case Style.eOpeningPosition.Side:
								if (StylesSide == null)
									StylesSide = new List<Style>(1);
								StylesSide.Add(style);
								break;
							case Style.eOpeningPosition.Front:
								if (StylesFront == null)
									StylesFront = new List<Style>(1);
								StylesFront.Add(style);
								break;
							default:
								log.Warn($"GameNPC.SortStyles(): Invalid OpeningRequirementValue for positional style, {style.Name}: ID {style.ID}, ClassID {style.ClassID}");
								break;
						}
						break;
					default:
						if (style.OpeningRequirementValue > 0)
						{
							if (StylesChain == null)
								StylesChain = new List<Style>(1);
							StylesChain.Add(style);
						}
						else
						{
							if (StylesAnytime == null)
								StylesAnytime = new List<Style>(1);
							StylesAnytime.Add(style);
						}
						break;
				}// switch (s.OpeningRequirementType)
			}// foreach
		}// SortStyles()

		/// <summary>
		/// Can we use this style without spamming a stun style?
		/// </summary>
		/// <param name="style">The style to check.</param>
		/// <returns>True if we should use the style, false if it would be spamming a stun effect.</returns>
		public bool CheckStyleStun(Style style)
		{
			if (TargetObject is GameLiving living && style.Procs.Count > 0)
				foreach (Tuple<Spell, int, int> t in style.Procs)
					if (t != null && t.Item1 is Spell spell
						&& spell.SpellType == (byte)eSpellType.StyleStun && living.HasEffect(t.Item1))
							return false;

			return true;
		}

		///// <summary>
		///// Picks a style, prioritizing reactives an	d chains over positionals and anytimes
		///// </summary>
		///// <returns>Selected style</returns>
		//public override Style GetStyleToUse()
		//{
		//	if (m_styles == null || m_styles.Count < 1 || TargetObject == null)
		//		return null;

		//	// Chain and defensive styles skip the GAMENPC_CHANCES_TO_STYLE,
		//	//	or they almost never happen e.g. NPC blocks 10% of the time,
		//	//	default 20% style chance means the defensive style only happens
		//	//	2% of the time, and a chain from it only happens 0.4% of the time.
		//	if (StylesChain != null && StylesChain.Count > 0)
		//		foreach (Style s in StylesChain)
		//			if (StyleProcessor.CanUseStyle(this, s, AttackWeapon))
		//				return s;

		//	if (StylesDefensive != null && StylesDefensive.Count > 0)
		//		foreach (Style s in StylesDefensive)
		//			if (StyleProcessor.CanUseStyle(this, s, AttackWeapon)
		//				&& CheckStyleStun(s)) // Make sure we don't spam stun styles like Brutalize
		//				return s;

		//	if (Util.Chance(Properties.GAMENPC_CHANCES_TO_STYLE))
		//	{
		//		// Check positional styles
		//		// Picking random styles allows mobs to use multiple styles from the same position
		//		//	e.g. a mob with both Pincer and Ice Storm side styles will use both of them.
		//		if (StylesBack != null && StylesBack.Count > 0)
		//		{
		//			Style s = StylesBack[Util.Random(0, StylesBack.Count - 1)];
		//			if (StyleProcessor.CanUseStyle(this, s, AttackWeapon))
		//				return s;
		//		}

		//		if (StylesSide != null && StylesSide.Count > 0)
		//		{
		//			Style s = StylesSide[Util.Random(0, StylesSide.Count - 1)];
		//			if (StyleProcessor.CanUseStyle(this, s, AttackWeapon))
		//				return s;
		//		}

		//		if (StylesFront != null && StylesFront.Count > 0)
		//		{
		//			Style s = StylesFront[Util.Random(0, StylesFront.Count - 1)];
		//			if (StyleProcessor.CanUseStyle(this, s, AttackWeapon))
		//				return s;
		//		}

		//		// Pick a random anytime style
		//		if (StylesAnytime != null && StylesAnytime.Count > 0)
		//			return StylesAnytime[Util.Random(0, StylesAnytime.Count - 1)];
		//	}

		//	return null;
		//} // GetStyleToUse()

		/// <summary>
		/// The Abilities for this NPC
		/// </summary>
		public Dictionary<string, Ability> Abilities
		{
			get
			{
				Dictionary<string, Ability> tmp = new Dictionary<string, Ability>();

				lock (m_lockAbilities)
				{
					tmp = new Dictionary<string, Ability>(m_abilities);
				}

				return tmp;
			}
		}

		private SpellAction m_spellaction = null;
		/// <summary>
		/// The timer that controls an npc's spell casting
		/// </summary>
		public SpellAction SpellTimer
		{
			get { return m_spellaction; }
			set { m_spellaction = value; }
		}

		/// <summary>
		/// Callback after spell execution finished and next spell can be processed
		/// </summary>
		/// <param name="handler"></param>
		public override void OnAfterSpellCastSequence(ISpellHandler handler)
		{
			/*
			if (SpellTimer != null)
			{
				if (this == null || this.ObjectState != eObjectState.Active || !this.IsAlive || this.TargetObject == null || (this.TargetObject is GameLiving && this.TargetObject.ObjectState != eObjectState.Active || !(this.TargetObject as GameLiving).IsAlive))
					SpellTimer.Stop();
				else
				{
					int interval = 1500;

					if (Brain != null)
					{
						interval = Math.Min(interval, Brain.ThinkInterval);
					}

					SpellTimer.Start(interval);
				}
			}
			*/
			

			if (CurrentSpellHandler != null)
			{
				//prevent from relaunch
				base.OnAfterSpellCastSequence(handler);
			}

			if(TargetObject == null)
            {
				TargetObject = CachedTarget;
            }

			// Notify Brain of Cast Finishing.
			if (Brain != null)
				Brain.Notify(GameNPCEvent.CastFinished, this, new CastingEventArgs(handler));
		}

		/// <summary>
		/// The spell action of this living
		/// </summary>
		public class SpellAction : RegionAction
		{
			/// <summary>
			/// Constructs a new attack action
			/// </summary>
			/// <param name="owner">The action source</param>
			public SpellAction(GameLiving owner)
				: base(owner)
			{
			}

			/// <summary>
			/// Called on every timer tick
			/// </summary>
			protected override void OnTick()
			{
				GameNPC owner = null;
				if (m_actionSource != null && m_actionSource is GameNPC)
					owner = (GameNPC)m_actionSource;
				else
				{
					Stop();
					return;
				}

				if (owner.TargetObject == null || !owner.attackComponent.AttackState)
				{
					Stop();
					return;
				}

				//If we started casting a spell, stop the timer and wait for
				//GameNPC.OnAfterSpellSequenceCast to start again
				if (owner.Brain is StandardMobBrain && ((StandardMobBrain)owner.Brain).CheckSpells(StandardMobBrain.eCheckSpellType.Offensive))
				{
					Stop();
					return;
				}
				else
				{
					//If we aren't a distance NPC, lets make sure we are in range to attack the target!
					if (owner.ActiveWeaponSlot != eActiveWeaponSlot.Distance && !owner.IsWithinRadius(owner.TargetObject, STICKMINIMUMRANGE))
						((GameNPC)owner).Follow(owner.TargetObject, STICKMINIMUMRANGE, STICKMAXIMUMRANGE);
				}

				if (owner.Brain != null)
				{
					Interval = Math.Min(1500, owner.Brain.CastInterval);
				}
				else
				{
					Interval = 1500;
				}
			}
		}

		private const string LOSTEMPCHECKER = "LOSTEMPCHECKER";
		private const string LOSCURRENTSPELL = "LOSCURRENTSPELL";
		private const string LOSCURRENTLINE = "LOSCURRENTLINE";
		private const string LOSSPELLTARGET = "LOSSPELLTARGET";


		/// <summary>
		/// Cast a spell, with optional LOS check
		/// </summary>
		/// <param name="spell"></param>
		/// <param name="line"></param>
		/// <param name="checkLOS"></param>
		public virtual bool CastSpell(Spell spell, SpellLine line, bool checkLOS)
		{
			bool cast = false;
			if (IsIncapacitated)
				return false;

			if (checkLOS)
			{
				cast = CastSpell(spell, line);
			}
			else
			{
				Spell spellToCast = null;

				if (line.KeyName == GlobalSpellsLines.Mob_Spells)
				{
					// NPC spells will get the level equal to their caster
					spellToCast = (Spell)spell.Clone();
					spellToCast.Level = Level;
				}
				else
				{
					spellToCast = spell;
				}

				cast = base.CastSpell(spellToCast, line);
			}

			return cast;
		}

		/// <summary>
		/// Cast a spell with LOS check to a player
		/// </summary>
		/// <param name="spell"></param>
		/// <param name="line"></param>
 		/// <returns>Whether the spellcast started successfully</returns>
		public override bool CastSpell(Spell spell, SpellLine line)
		{
			if (IsIncapacitated)
				return false;

			if (TempProperties.getProperty<Spell>(LOSCURRENTSPELL, null) != null)
			{
				return false;
			}
			bool casted = false;
			Spell spellToCast = null;

			if (line.KeyName == GlobalSpellsLines.Mob_Spells)
			{
				// NPC spells will get the level equal to their caster
				spellToCast = (Spell)spell.Clone();
				spellToCast.Level = Level;
			}
			else
			{
				spellToCast = spell;
			}

			// Let's do a few checks to make sure it doesn't just wait on the LOS check
			int tempProp = TempProperties.getProperty<int>(LOSTEMPCHECKER);
			if (tempProp <= 0)
			{
				GamePlayer LOSChecker = TargetObject as GamePlayer;

				if (LOSChecker == null && this is GamePet pet)
				{
					if (pet.Owner is GamePlayer player)
						LOSChecker = player;
					else if (pet.Owner is CommanderPet petComm && petComm.Owner is GamePlayer owner)
						LOSChecker = owner;
				}

				if (LOSChecker == null)
				{
					foreach (GamePlayer ply in GetPlayersInRadius(350))
					{
						if (ply != null)
						{
							LOSChecker = ply;
							break;
						}
					}
				}

				if (spellToCast.Range > 0 && !IsWithinRadius(TargetObject, spellToCast.Range))
					return false;

				if (LOSChecker == null)
				{
					TempProperties.setProperty(LOSTEMPCHECKER, 0);
					casted = base.CastSpell(spellToCast, line);
				}
				else
				{
					TempProperties.setProperty(LOSTEMPCHECKER, 10);
					TempProperties.setProperty(LOSCURRENTSPELL, spellToCast);
					TempProperties.setProperty(LOSCURRENTLINE, line);
					TempProperties.setProperty(LOSSPELLTARGET, TargetObject);
					//LOSChecker.Out.SendCheckLOS(LOSChecker, this, new CheckLOSResponse(StartSpellAttackCheckLOS)); //is this checking LOS between player and pet?
					LOSChecker.Out.SendCheckLOS(this, TargetObject, new CheckLOSResponse(StartSpellAttackCheckLOS)); 
					casted = true;
				}
			}
			else
				TempProperties.setProperty(LOSTEMPCHECKER, tempProp - 1);

			return casted;
		}

		public void StartSpellAttackCheckLOS(GamePlayer player, ushort response, ushort targetOID)
		{
			SpellLine line = TempProperties.getProperty<SpellLine>(LOSCURRENTLINE, null);
			Spell spell = TempProperties.getProperty<Spell>(LOSCURRENTSPELL, null);
			GameObject target = TempProperties.getProperty<GameObject>(LOSSPELLTARGET, null);
			GameObject lasttarget = TargetObject;

			TempProperties.removeProperty(LOSSPELLTARGET);
			TempProperties.removeProperty(LOSTEMPCHECKER);
			TempProperties.removeProperty(LOSCURRENTLINE);
			TempProperties.removeProperty(LOSCURRENTSPELL);
			TempProperties.setProperty(LOSTEMPCHECKER, 0);

			if ((response & 0x100) == 0x100 && line != null && spell != null)
			{
				TargetObject = target;

				GameLiving living = TargetObject as GameLiving;

				if (living != null && living.EffectList.GetOfType<NecromancerShadeEffect>() != null)
				{
					if (living is GamePlayer && (living as GamePlayer).ControlledBrain != null)
					{
						TargetObject = (living as GamePlayer).ControlledBrain.Body;
					}
				}

				base.CastSpell(spell, line);
				TargetObject = lasttarget;
			}
			else
			{
				Notify(GameLivingEvent.CastFailed, this, new CastFailedEventArgs(null, CastFailedEventArgs.Reasons.TargetNotInView));
			}
		}

		#endregion

		#region Notify

		/// <summary>
		/// Handle event notifications
		/// </summary>
		/// <param name="e">The event</param>
		/// <param name="sender">The sender</param>
		/// <param name="args">The arguements</param>
		public override void Notify(DOLEvent e, object sender, EventArgs args)
		{
			base.Notify(e, sender, args);

			ABrain brain = Brain;
			if (brain != null)
				brain.Notify(e, sender, args);

		}

		/// <summary>
		/// Handles all ambient messages triggered by a mob or NPC action
		/// </summary>
		/// <param name="trigger">The action triggering the message (e.g., aggroing, dying, roaming)</param>
		/// <param name="living">The entity triggering the action (e.g., a player)</param>
		public void FireAmbientSentence(eAmbientTrigger trigger, GameObject living)
		{
			if (IsSilent || ambientTexts == null || ambientTexts.Count == 0) return;
			if (trigger == eAmbientTrigger.interact && living == null) return; // Do not trigger interact messages with a corpse
			List<MobXAmbientBehaviour> mxa = (from i in ambientTexts where i.Trigger == trigger.ToString() select i).ToList();
			if (mxa.Count == 0) return;

			// grab random sentence
			var chosen = mxa[Util.Random(mxa.Count - 1)];
			if (!Util.Chance(chosen.Chance)) return;

			string controller = string.Empty;
			if (Brain is IControlledBrain) // Used for '{controller}' trigger keyword, use the name of the mob's owner (else returns blank)--this is used when a pet has an ambient trigger.
			{
				GamePlayer playerOwner = ((IControlledBrain) Brain).GetPlayerOwner();
				if (playerOwner != null)
					controller = playerOwner.Name;
			}

			string text = chosen.Text;

			if (TargetObject == null)
			{
				text = chosen.Text.Replace("{sourcename}", Brain?.Body?.Name) // '{sourcename}' returns the mob or NPC name
					.Replace("{targetname}", living?.Name) // '{targetname}' returns the mob/NPC target's name
					.Replace("{controller}", controller); // '{controller}' returns the result of the controller var (use this when pets have dialogue)
				
				// Replace trigger keywords
				if (living is GamePlayer)
					text = text.Replace("{class}", ((GamePlayer) living).CharacterClass.Name).Replace("{race}", ((GamePlayer) living).RaceName);
				if (living is GameNPC)
					text = text.Replace("{class}", "NPC").Replace("{race}", "NPC");
			}
			else
			{
				text = chosen.Text.Replace("{sourcename}", Brain.Body.Name) // '{sourcename}' returns the mob or NPC name
					.Replace("{targetname}", TargetObject == null ? string.Empty : TargetObject.Name) // '{targetname}' returns the mob/NPC target's name
					.Replace("{controller}", controller); // '{controller}' returns the result of the controller var (use this when pets have dialogue)
				
				// Replace trigger keywords
				if (TargetObject is GamePlayer)
					text = text.Replace("{class}", ((GamePlayer) TargetObject).CharacterClass.Name).Replace("{race}", ((GamePlayer) TargetObject).RaceName);
				if (TargetObject is GameNPC)
					text = text.Replace("{class}", "NPC").Replace("{race}", "NPC");
			}
			// Replace trigger keywords

			if (chosen.Emote != 0)
			{
				Emote((eEmote)chosen.Emote);
			}
			
			// Replace trigger keywords
			if (TargetObject is GamePlayer && living is GamePlayer)
				text = text.Replace("{class}", ((GamePlayer) living).CharacterClass.Name).Replace("{race}", ((GamePlayer) living).RaceName);
			if (TargetObject is GameNPC && living is GameNPC)
				text = text.Replace("{class}", "NPC").Replace("{race}", "NPC");
			
			/*// Determines message delivery method for trigger voice
			if (chosen.Voice.StartsWith("b")) // Broadcast message without "[Broadcast] {0}:" string start
			{
				foreach (GamePlayer player in CurrentRegion.GetPlayersInRadius(X, Y, Z, 25000, false, false))
				{
					player.Out.SendMessage(text, eChatType.CT_Broadcast, eChatLoc.CL_ChatWindow);
				}
				return;
			}
			if (chosen.Voice.StartsWith("y")) // Yell message (increased range) without "{0} yells," string start
			{
				Yell(text);
				return;
			}*/
			
			// Determines message delivery method for triggers
			switch (chosen.Voice)
			{
				case "b": // Broadcast message without "[Broadcast] {0}:" string start
				{
					foreach (GamePlayer player in CurrentRegion.GetPlayersInRadius(X, Y, Z, 25000, false, false))
					{
					  player.Out.SendMessage(text, eChatType.CT_Broadcast, eChatLoc.CL_ChatWindow);
					}
					return;
				}
				case "y": // Yell message (increased range) without "{0} yells," string start
				{
					Yell(text);
					return;
				}
				case "s": // Return custom System message in System/Combat window to all players within range
				{
					Message.MessageToArea(Brain.Body, text, eChatType.CT_System, eChatLoc.CL_SystemWindow, 512, null);
					return;
				}
				case "c": // Return custom Say message in Chat window to all players within range, without "{0} says," string start
				{
					Message.MessageToArea(Brain.Body, text, eChatType.CT_Say, eChatLoc.CL_ChatWindow, 512, null);
					return;
				}
				case "p": // Return custom System message in popup dialog only to player interating with the NPC
					// For interact triggers
				{
					((GamePlayer) living).Out.SendMessage(text, eChatType.CT_System, eChatLoc.CL_PopupWindow);
					return;
				}
				default: // Return Say message with "{0} says," string start included (contrary to parameter description)
				{
					Say(text);
					return;
				}
			}
		}
		#endregion

		#region ControlledNPCs

		public override void SetControlledBrain(IControlledBrain controlledBrain)
		{
			if (ControlledBrain == null)
				InitControlledBrainArray(1);

			ControlledBrain = controlledBrain;
		}
		/// <summary>
		/// Gets the controlled object of this NPC
		/// </summary>
		public override IControlledBrain ControlledBrain
		{
			get
			{
				if (m_controlledBrain == null) return null;
				return m_controlledBrain[0];
			}
		}

		/// <summary>
		/// Gets the controlled array of this NPC
		/// </summary>
		public IControlledBrain[] ControlledNpcList
		{
			get { return m_controlledBrain; }
		}

		/// <summary>
		/// Adds a pet to the current array of pets
		/// </summary>
		/// <param name="controlledNpc">The brain to add to the list</param>
		/// <returns>Whether the pet was added or not</returns>
		public virtual bool AddControlledNpc(IControlledBrain controlledNpc)
		{
			return true;
		}

		/// <summary>
		/// Removes the brain from
		/// </summary>
		/// <param name="controlledNpc">The brain to find and remove</param>
		/// <returns>Whether the pet was removed</returns>
		public virtual bool RemoveControlledNpc(IControlledBrain controlledNpc)
		{
			return true;
		}

		#endregion

		/// <summary>
		/// Whether this NPC is available to add on a fight.
		/// </summary>
		public virtual bool IsAvailable
		{
			get { return !(Brain is IControlledBrain) && !InCombat; }
		}

		/// <summary>
		/// Whether this NPC is aggressive.
		/// </summary>
		public virtual bool IsAggressive
		{
			get
			{
				ABrain brain = Brain;
				return (brain == null) ? false : (brain is IOldAggressiveBrain);
			}
		}

		/// <summary>
		/// Whether this NPC is a friend or not.
		/// </summary>
		/// <param name="npc">The NPC that is checked against.</param>
		/// <returns></returns>
		public virtual bool IsFriend(GameNPC npc)
		{
            if (Faction == null || npc.Faction == null)
                return false;
            return (npc.Faction == Faction || Faction.FriendFactions.Contains(npc.Faction));
        }

		/// <summary>
		/// Broadcast loot to the raid.
		/// </summary>
		/// <param name="dropMessages">List of drop messages to broadcast.</param>
		protected virtual void BroadcastLoot(ArrayList droplist)
		{
			if (droplist.Count > 0)
			{
				String lastloot;
				foreach (GamePlayer player in GetPlayersInRadius(WorldMgr.INFO_DISTANCE))
				{
					lastloot = "";
					foreach (string str in droplist)
					{
						// Suppress identical messages (multiple item drops).
						if (str != lastloot)
						{
							player.Out.SendMessage(String.Format(LanguageMgr.GetTranslation(player.Client.Account.Language, "GameNPC.DropLoot.Drops",
								GetName(0, true, player.Client.Account.Language, this), str)), eChatType.CT_Loot, eChatLoc.CL_SystemWindow);
							lastloot = str;
						}
					}
				}
			}
		}


		/// <summary>
		/// Gender of this NPC.
		/// </summary>
		public override eGender Gender { get; set; }

		public GameNPC Copy()
		{
			return Copy(null);
		}


		/// <summary>
		/// Create a copy of the GameNPC
		/// </summary>
		/// <param name="copyTarget">A GameNPC to copy this GameNPC to (can be null)</param>
		/// <returns>The GameNPC this GameNPC was copied to</returns>
		public GameNPC Copy(GameNPC copyTarget)
		{
			if (copyTarget == null)
				copyTarget = new GameNPC();

			copyTarget.TranslationId = TranslationId;
			copyTarget.BlockChance = BlockChance;
			copyTarget.BodyType = BodyType;
			copyTarget.CanUseLefthandedWeapon = CanUseLefthandedWeapon;
			copyTarget.Charisma = Charisma;
			copyTarget.Constitution = Constitution;
			copyTarget.CurrentRegion = CurrentRegion;
			copyTarget.Dexterity = Dexterity;
			copyTarget.Empathy = Empathy;
			copyTarget.Endurance = Endurance;
			copyTarget.EquipmentTemplateID = EquipmentTemplateID;
			copyTarget.EvadeChance = EvadeChance;
			copyTarget.Faction = Faction;
			copyTarget.Flags = Flags;
			copyTarget.GuildName = GuildName;
			copyTarget.ExamineArticle = ExamineArticle;
			copyTarget.MessageArticle = MessageArticle;
			copyTarget.Heading = Heading;
			copyTarget.Intelligence = Intelligence;
			copyTarget.IsCloakHoodUp = IsCloakHoodUp;
			copyTarget.IsCloakInvisible = IsCloakInvisible;
			copyTarget.IsHelmInvisible = IsHelmInvisible;
			copyTarget.LeftHandSwingChance = LeftHandSwingChance;
			copyTarget.Level = Level;
			copyTarget.LoadedFromScript = LoadedFromScript;
			copyTarget.MaxSpeedBase = MaxSpeedBase;
			copyTarget.MeleeDamageType = MeleeDamageType;
			copyTarget.Model = Model;
			copyTarget.Name = Name;
			copyTarget.Suffix = Suffix;
			copyTarget.NPCTemplate = NPCTemplate;
			copyTarget.ParryChance = ParryChance;
			copyTarget.PathID = PathID;
			copyTarget.PathingNormalSpeed = PathingNormalSpeed;
			copyTarget.Quickness = Quickness;
			copyTarget.Piety = Piety;
			copyTarget.Race = Race;
			copyTarget.Realm = Realm;
			copyTarget.RespawnInterval = RespawnInterval;
			copyTarget.RoamingRange = RoamingRange;
			copyTarget.Size = Size;
			copyTarget.SaveInDB = SaveInDB;
			copyTarget.Strength = Strength;
			copyTarget.TetherRange = TetherRange;
			copyTarget.MaxDistance = MaxDistance;
			copyTarget.X = X;
			copyTarget.Y = Y;
			copyTarget.Z = Z;
			copyTarget.OwnerID = OwnerID;
			copyTarget.PackageID = PackageID;

			if (Abilities != null && Abilities.Count > 0)
			{
				foreach (Ability targetAbility in Abilities.Values)
				{
					if (targetAbility != null)
						copyTarget.AddAbility(targetAbility);
				}
			}

			ABrain brain = null;
			foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
			{
				brain = (ABrain)assembly.CreateInstance(Brain.GetType().FullName, true);
				if (brain != null)
					break;
			}

			if (brain == null)
			{
				log.Warn("GameNPC.Copy():  Unable to create brain:  " + Brain.GetType().FullName + ", using StandardMobBrain.");
				brain = new StandardMobBrain();
			}

			StandardMobBrain newBrainSMB = brain as StandardMobBrain;
			StandardMobBrain thisBrainSMB = this.Brain as StandardMobBrain;

			if (newBrainSMB != null && thisBrainSMB != null)
			{
				newBrainSMB.AggroLevel = thisBrainSMB.AggroLevel;
				newBrainSMB.AggroRange = thisBrainSMB.AggroRange;
			}

			copyTarget.SetOwnBrain(brain);

			if (Inventory != null && Inventory.AllItems.Count > 0)
			{
				GameNpcInventoryTemplate inventoryTemplate = Inventory as GameNpcInventoryTemplate;

				if (inventoryTemplate != null)
					copyTarget.Inventory = inventoryTemplate.CloneTemplate();
			}

			if (Spells != null && Spells.Count > 0)
				copyTarget.Spells = new List<Spell>(Spells.Cast<Spell>());

			if (Styles != null && Styles.Count > 0)
				copyTarget.Styles = new ArrayList(Styles);

			if (copyTarget.Inventory != null)
				copyTarget.SwitchWeapon(ActiveWeaponSlot);

			return copyTarget;
		}

		/// <summary>
		/// Constructs a NPC
		/// NOTE: Most npcs are generated as GameLiving objects and then used as GameNPCs when needed.
		/// 	As a result, this constructor is rarely called.
		/// </summary>
		public GameNPC()
			: this(new StandardMobBrain())
		{
		}

		public GameNPC(ABrain defaultBrain) : base()
		{
			Level = 1;
			m_health = MaxHealth;
			m_Realm = 0;
			m_name = "new mob";
			m_model = 408;
			//Fill the living variables
			//			CurrentSpeed = 0; // cause position addition recalculation
			MaxSpeedBase = 200;
			GuildName = "";

			m_brainSync = m_brains.SyncRoot;
			m_followTarget = new WeakRef(null);

			m_size = 50; //Default size
			TargetPosition = new Point3D();
			m_followMinDist = 100;
			m_followMaxDist = 3000;
			m_flags = 0;
			m_maxdistance = 0;
			m_roamingRange = 0; // default to non roaming - tolakram
			m_ownerID = "";

			if (m_spawnPoint == null)
				m_spawnPoint = new Point3D();

			//m_factionName = "";
			LinkedFactions = new ArrayList(1);
			if (m_ownBrain == null)
			{
				m_ownBrain = defaultBrain;
				m_ownBrain.Body = this;
			}
		}

		/// <summary>
		/// create npc from template
		/// NOTE: Most npcs are generated as GameLiving objects and then used as GameNPCs when needed.
		/// 	As a result, this constructor is rarely called.
		/// </summary>
		/// <param name="template">template of generator</param>
		public GameNPC(INpcTemplate template)
			: this()
		{
			if (template == null) return;

			// When creating a new mob from a template, we have to get all values from the template
			if (template is NpcTemplate npcTemplate)
				npcTemplate.ReplaceMobValues = true;

			LoadTemplate(template);
		}

		// camp bonus
		private double m_campBonus = 1;
		/// <summary>
		/// gets/sets camp bonus experience this gameliving grants
		/// </summary>
		public virtual double CampBonus
		{
			get
			{
				return m_campBonus;
			}
			set
			{
				m_campBonus = value;
			}
		}

        public int ScalingFactor { get => scalingFactor; set => scalingFactor = value; }
        
        public int OrbsReward { get => orbsReward; set => orbsReward = value; }
	}
}
