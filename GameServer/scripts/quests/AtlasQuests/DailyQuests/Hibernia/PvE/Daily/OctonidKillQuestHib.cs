using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DOL.Database;
using DOL.Events;
using DOL.GS;
using DOL.GS.API;
using DOL.GS.PacketHandler;
using DOL.GS.PlayerTitles;
using DOL.GS.Quests;
using log4net;

namespace DOL.GS.DailyQuest.Hibernia
{
	public class OctonidKillQuestHib : Quests.DailyQuest
	{
		/// <summary>
		/// Defines a logger for this class.
		/// </summary>
		private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

		protected const string questTitle = "[Daily] Octonid Invasion";
		protected const int minimumLevel = 40;
		protected const int maximumLevel = 50;

		// Kill Goal
		protected const int MAX_KILLED = 10;
		
		private static GameNPC Dean = null; // Start NPC

		private int OctonidKilled = 0;

		// Constructors
		public OctonidKillQuestHib() : base()
		{
		}

		public OctonidKillQuestHib(GamePlayer questingPlayer) : base(questingPlayer)
		{
		}

		public OctonidKillQuestHib(GamePlayer questingPlayer, int step) : base(questingPlayer, step)
		{
		}

		public OctonidKillQuestHib(GamePlayer questingPlayer, DBQuest dbQuest) : base(questingPlayer, dbQuest)
		{
		}
		
		public override int Level
		{
			get
			{
				// Quest Level
				return minimumLevel;
			}
		}

		[ScriptLoadedEvent]
		public static void ScriptLoaded(DOLEvent e, object sender, EventArgs args)
		{
			if (!ServerProperties.Properties.LOAD_QUESTS)
				return;
			

			#region defineNPCs

			GameNPC[] npcs = WorldMgr.GetNPCsByName("Dean", eRealm.Hibernia);

			if (npcs.Length > 0)
				foreach (GameNPC npc in npcs)
					if (npc.CurrentRegionID == 200 && npc.X == 334962 && npc.Y == 420687)
					{
						Dean = npc;
						break;
					}

			if (Dean == null)
			{
				if (log.IsWarnEnabled)
					log.Warn("Could not find Dean , creating it ...");
				Dean = new GameNPC();
				Dean.Model = 355;
				Dean.Name = "Dean";
				Dean.GuildName = "Advisor to the King";
				Dean.Realm = eRealm.Hibernia;
				//Druim Ligen Location
				Dean.CurrentRegionID = 200;
				Dean.Size = 50;
				Dean.Level = 59;
				Dean.X = 334962;
				Dean.Y = 420687;
				Dean.Z = 5184;
				Dean.Heading = 1571;
				Dean.AddToWorld();
				if (SAVE_INTO_DATABASE)
				{
					Dean.SaveIntoDatabase();
				}
			}

			#endregion

			#region defineItems
			#endregion

			#region defineObject
			#endregion

			GameEventMgr.AddHandler(GamePlayerEvent.AcceptQuest, new DOLEventHandler(SubscribeQuest));
			GameEventMgr.AddHandler(GamePlayerEvent.DeclineQuest, new DOLEventHandler(SubscribeQuest));

			GameEventMgr.AddHandler(Dean, GameObjectEvent.Interact, new DOLEventHandler(TalkToDean));
			GameEventMgr.AddHandler(Dean, GameLivingEvent.WhisperReceive, new DOLEventHandler(TalkToDean));

			/* Now we bring to Dean the possibility to give this quest to players */
			Dean.AddQuestToGive(typeof (OctonidKillQuestHib));

			if (log.IsInfoEnabled)
				log.Info("Quest \"" + questTitle + "\" initialized");
		}

		[ScriptUnloadedEvent]
		public static void ScriptUnloaded(DOLEvent e, object sender, EventArgs args)
		{
			//if not loaded, don't worry
			if (Dean == null)
				return;
			// remove handlers
			GameEventMgr.RemoveHandler(GamePlayerEvent.AcceptQuest, new DOLEventHandler(SubscribeQuest));
			GameEventMgr.RemoveHandler(GamePlayerEvent.DeclineQuest, new DOLEventHandler(SubscribeQuest));

			GameEventMgr.RemoveHandler(Dean, GameObjectEvent.Interact, new DOLEventHandler(TalkToDean));
			GameEventMgr.RemoveHandler(Dean, GameLivingEvent.WhisperReceive, new DOLEventHandler(TalkToDean));

			/* Now we remove to Dean the possibility to give this quest to players */
			Dean.RemoveQuestToGive(typeof (OctonidKillQuestHib));
		}

		protected static void TalkToDean(DOLEvent e, object sender, EventArgs args)
		{
			//We get the player from the event arguments and check if he qualifies		
			GamePlayer player = ((SourceEventArgs) args).Source as GamePlayer;
			if (player == null)
				return;

			if(Dean.CanGiveQuest(typeof (OctonidKillQuestHib), player)  <= 0)
				return;

			//We also check if the player is already doing the quest
			OctonidKillQuestHib quest = player.IsDoingQuest(typeof (OctonidKillQuestHib)) as OctonidKillQuestHib;

			if (e == GameObjectEvent.Interact)
			{
				if (quest != null)
				{
					switch (quest.Step)
					{
						case 1:
							Dean.SayTo(player, "You will find Octonids in the South East of World\'s End.");
							break;
						case 2:
							Dean.SayTo(player, "Hello " + player.Name + ", did you [kill] the Octonids?");
							break;
					}
				}
				else
				{
					Dean.SayTo(player, "Hello "+ player.Name +", I am Dean. I help the king with logistics, and he's tasked me with getting things done around here. "+
					                   "The Octonids out in World's End are devouring the natural flora and fauna of the Shrouded Isles. They may soon destroy the ecosystem entirely.\n"+
					                   "\nCan you [clear the Octonids] to save the Shrouded Isles?");
				}
			}
				// The player whispered to the NPC
			else if (e == GameLivingEvent.WhisperReceive)
			{
				WhisperReceiveEventArgs wArgs = (WhisperReceiveEventArgs) args;
				if (quest == null)
				{
					switch (wArgs.Text.ToLower())
					{
						case "clear the octonids":
							player.Out.SendQuestSubscribeCommand(Dean, QuestMgr.GetIDForQuestType(typeof(OctonidKillQuestHib)), "Will you help Dean "+questTitle+"");
							break;
					}
				}
				else
				{
					switch (wArgs.Text)
					{
						case "kill":
							if (quest.Step == 2)
							{
								player.Out.SendMessage("Thank you for your contribution!", eChatType.CT_Chat, eChatLoc.CL_PopupWindow);
								quest.FinishQuest();
							}
							break;
						case "abort":
							player.Out.SendCustomDialog("Do you really want to abort this quest, \nall items gained during quest will be lost?", new CustomDialogResponse(CheckPlayerAbortQuest));
							break;
					}
				}
			}
		}
		
		public override bool CheckQuestQualification(GamePlayer player)
		{
			// if the player is already doing the quest his level is no longer of relevance
			if (player.IsDoingQuest(typeof (OctonidKillQuestHib)) != null)
				return true;

			// This checks below are only performed is player isn't doing quest already

			//if (player.HasFinishedQuest(typeof(Academy_47)) == 0) return false;

			//if (!CheckPartAccessible(player,typeof(CityOfCamelot)))
			//	return false;

			if (player.Level < minimumLevel || player.Level > maximumLevel)
				return false;

			return true;
		}
		
		public override void LoadQuestParameters()
		{
			OctonidKilled = GetCustomProperty(QuestPropertyKey) != null ? int.Parse(GetCustomProperty(QuestPropertyKey)) : 0;
		}

		public override void SaveQuestParameters()
		{
			SetCustomProperty(QuestPropertyKey, OctonidKilled.ToString());
		}


		private static void CheckPlayerAbortQuest(GamePlayer player, byte response)
		{
			OctonidKillQuestHib quest = player.IsDoingQuest(typeof (OctonidKillQuestHib)) as OctonidKillQuestHib;

			if (quest == null)
				return;

			if (response == 0x00)
			{
				SendSystemMessage(player, "Good, now go out there and finish your work!");
			}
			else
			{
				SendSystemMessage(player, "Aborting Quest " + questTitle + ". You can start over again if you want.");
				quest.AbortQuest();
			}
		}

		protected static void SubscribeQuest(DOLEvent e, object sender, EventArgs args)
		{
			QuestEventArgs qargs = args as QuestEventArgs;
			if (qargs == null)
				return;

			if (qargs.QuestID != QuestMgr.GetIDForQuestType(typeof(OctonidKillQuestHib)))
				return;

			if (e == GamePlayerEvent.AcceptQuest)
				CheckPlayerAcceptQuest(qargs.Player, 0x01);
			else if (e == GamePlayerEvent.DeclineQuest)
				CheckPlayerAcceptQuest(qargs.Player, 0x00);
		}

		private static void CheckPlayerAcceptQuest(GamePlayer player, byte response)
		{
			if(Dean.CanGiveQuest(typeof (OctonidKillQuestHib), player)  <= 0)
				return;

			if (player.IsDoingQuest(typeof (OctonidKillQuestHib)) != null)
				return;

			if (response == 0x00)
			{
				player.Out.SendMessage("Thank you for helping Atlas.", eChatType.CT_Say, eChatLoc.CL_PopupWindow);
			}
			else
			{
				//Check if we can add the quest!
				if (!Dean.GiveQuest(typeof (OctonidKillQuestHib), player, 1))
					return;

				Dean.SayTo(player, "You will find the Octonids in World\'s End.");

			}
		}

		//Set quest name
		public override string Name
		{
			get { return questTitle; }
		}

		// Define Steps
		public override string Description
		{
			get
			{
				switch (Step)
				{
					case 1:
						return "Find Octonids South East in World\'s End. \nKilled: Octonids ("+ OctonidKilled +" | 10)";
					case 2:
						return "Return to Dean for your Reward.";
				}
				return base.Description;
			}
		}

		public override void Notify(DOLEvent e, object sender, EventArgs args)
		{
			GamePlayer player = sender as GamePlayer;

			if (player == null || player.IsDoingQuest(typeof(OctonidKillQuestHib)) == null)
				return;
			
			if (sender != m_questPlayer)
				return;
			
			if (Step == 1 && e == GameLivingEvent.EnemyKilled)
			{
				EnemyKilledEventArgs gArgs = (EnemyKilledEventArgs) args;
				if (gArgs.Target.Name.ToLower() == "octonid") 
				{
					OctonidKilled++;
					player.Out.SendMessage("[Daily] Octonid Killed: ("+OctonidKilled+" | "+MAX_KILLED+")", eChatType.CT_ScreenCenter, eChatLoc.CL_SystemWindow);
					player.Out.SendQuestUpdate(this);
					
					if (OctonidKilled >= MAX_KILLED)
					{
						// FinishQuest or go back to Dean
						Step = 2;
					}
				}
			}
			
		}
		
		public override string QuestPropertyKey
		{
			get => "OctonidKillQuestHib";
			set { ; }
		}

		public override void AbortQuest()
		{
			base.AbortQuest(); //Defined in Quest, changes the state, stores in DB etc ...
		}

		public override void FinishQuest()
		{
			m_questPlayer.GainExperience(eXPSource.Quest, (m_questPlayer.ExperienceForNextLevel - m_questPlayer.ExperienceForCurrentLevel)/10, false);
			m_questPlayer.AddMoney(Money.GetMoney(0,0,m_questPlayer.Level,50,Util.Random(50)), "You receive {0} as a reward.");
			AtlasROGManager.GenerateOrbAmount(m_questPlayer, 100);
			OctonidKilled = 0;
			base.FinishQuest(); //Defined in Quest, changes the state, stores in DB etc ...
		}
	}
}
