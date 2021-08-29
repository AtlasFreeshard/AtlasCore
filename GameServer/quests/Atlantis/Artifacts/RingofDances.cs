﻿/*
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
using System.Collections.Generic;
using System.Text;
using DOL.Events;
using DOL.GS.Quests;
using Atlas.DataLayer.Models;
using DOL.GS.PacketHandler;
using System.Collections;

namespace DOL.GS.Quests.Atlantis.Artifacts
{
	/// <summary>
	/// Quest for the A Flask artifact.
	/// </summary>
	/// <author>Aredhel</author>
	class RingofDances : ArtifactQuest
	{
		private static int m_requiredLevel = 45;

		/// <summary>
		/// The name of the quest (not necessarily the same as
		/// the name of the reward).
		/// </summary>
		public override String Name
		{
			get { return "Ring of Dances"; }
		}

		/// <summary>
		/// The reward for this quest.
		/// </summary>
		private static int m_artifactID = 1061;
		public override int ArtifactID
		{
			get { return m_artifactID; }
		}

		/// <summary>
		/// Description for the current step.
		/// </summary>
		public override string Description
		{
			get
			{
				switch (Step)
				{
					default:
						return base.Description;
				}
			}
		}

		public RingofDances()
			: base() { }

		public RingofDances(GamePlayer questingPlayer)
			: base(questingPlayer) { }

		/// <summary>
		/// This constructor is needed to load quests from the DB.
		/// </summary>
		/// <param name="questingPlayer"></param>
		/// <param name="dbQuest"></param>
		public RingofDances(GamePlayer questingPlayer, Atlas.DataLayer.Models.Quest dbQuest)
			: base(questingPlayer, dbQuest) { }

		/// <summary>
		/// Quest initialisation.
		/// </summary>
		public static void Init()
		{
			ArtifactQuest.Init(m_artifactID, typeof(RingofDances));
		}

		/// <summary>
		/// Check if player is eligible for this quest.
		/// </summary>
		/// <param name="player"></param>
		/// <returns></returns>
		public override bool CheckQuestQualification(GamePlayer player)
		{
			if (!base.CheckQuestQualification(player))
				return false;

			// TODO: Check if this is the correct level for the quest.
			return (player.Level >= m_requiredLevel);
		}

		/// <summary>
		/// Handle an item given to the scholar.
		/// </summary>
		/// <param name="source"></param>
		/// <param name="item"></param>
		/// <param name="target"></param>
		/// <returns></returns>
		public override bool ReceiveItem(GameLiving source, GameLiving target, InventoryItem item)
		{
			if (base.ReceiveItem(source, target, item))
				return true;

			GamePlayer player = source as GamePlayer;
			Scholar scholar = target as Scholar;
			if (player == null || scholar == null)
				return false;

			if (Step > -1 && ArtifactMgr.GetArtifactID(item.Name) == ArtifactID)
			{
				var versions = ArtifactMgr.GetArtifactVersions(ArtifactID,
					(eCharacterClass)player.CharacterClass.ID, (eRealm)player.Realm);

				if (versions.Count > 0 && RemoveItem(player, item))
				{
					GiveItem(scholar, player, ArtifactID, versions[0]);
					String reply = String.Format("Here is the {0}, {1} {2} {3} {4}, {5}!",
						"restored to its original power. It is a fine item and I wish I could keep",
						"it, but it is for you and you alone. Do not destroy it because you will never",
						"have access to its full power again. Take care of it and it shall aid you in",
						"the trials",
						ArtifactID,
						player.CharacterClass.Name);
					scholar.TurnTo(player);
					scholar.SayTo(player, eChatLoc.CL_PopupWindow, reply);
					FinishQuest();
					return true;
				}
			}

			return false;
		}

		/// <summary>
		/// Handle whispers to the scholar.
		/// </summary>
		/// <param name="source"></param>
		/// <param name="target"></param>
		/// <param name="text"></param>
		/// <returns></returns>
		public override bool WhisperReceive(GameLiving source, GameLiving target, string text)
		{
			if (base.WhisperReceive(source, target, text))
				return true;

			GamePlayer player = source as GamePlayer;
			Scholar scholar = target as Scholar;
			if (player == null || scholar == null)
				return false;

			var artifact = ArtifactMgr.GetArtifacts().FirstOrDefault(x => x.Id == ArtifactID);
			if (Step == -1 && text.ToLower() == artifact.Name.ToLower())
			{
				/* Commenting out to give a template for future development
				String reply = String.Format("Vara was a very skilled healer and she put her skills {0} {1} {2}",
					"into the Healer's Embrace cloak. It would help me to unlock them if I was to read",
					"her Medical Log. Please give me Vara's Medical Log now so that I may awaken the",
					"magic within the Cloak for you.");
				scholar.TurnTo(player);
				scholar.SayTo(player, eChatLoc.CL_PopupWindow, reply);
				Step = 2;
				return true;*/
			}

			return false;
		}

		public override void Notify(DOLEvent e, object sender, EventArgs args)
		{
			// Need to do anything here?
		}
	}
}