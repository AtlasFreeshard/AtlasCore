﻿using System.Text.RegularExpressions;
using DOL.GS.ServerProperties;

namespace DOL.GS {
    public class GameEpicBoss : GameNPC {
        public GameEpicBoss() : base()
        {
            ScalingFactor = 80;
            OrbsReward = Properties.EPICBOSS_ORBS;
        }
        public override void Die(GameObject killer)//current orb reward for epic boss is 1500
        {
            if (MaxHealth <= 40000 && MaxHealth > 30000)// 750 orbs for normal nameds
                OrbsReward = Properties.EPICBOSS_ORBS / 2;

            if (MaxHealth <= 30000 && MaxHealth >= 10000)// 375 orbs for normal nameds
                OrbsReward = Properties.EPICBOSS_ORBS / 4;

            // debug
            log.Debug($"{Name} killed by {killer.Name}");

            if (killer is GamePet pet) killer = pet.Owner; 
            
            var playerKiller = killer as GamePlayer;
            
            var achievementMob = Regex.Replace(Name, @"\s+", "");
            
            var killerBG = (BattleGroup)playerKiller?.TempProperties.getProperty<object>(BattleGroup.BATTLEGROUP_PROPERTY, null);
            
            if (killerBG != null && (killerBG.Members.Contains(playerKiller) || (bool)killerBG.Members[playerKiller]!))
            {
                foreach (var bgPlayer in killerBG.GetPlayersInTheBattleGroup())
                {
                    if (bgPlayer.IsWithinRadius(this, WorldMgr.MAX_EXPFORKILL_DISTANCE))
                    {
                        if (bgPlayer.Level < 45) continue;
                        AtlasROGManager.GenerateOrbAmount(bgPlayer,OrbsReward);
                        AtlasROGManager.GenerateBeetleCarapace(bgPlayer);
                        bgPlayer.Achieve($"{achievementMob}-Credit");
                    }
                }
            }
            else if (playerKiller?.Group != null)
            {
                foreach (var groupPlayer in playerKiller.Group.GetPlayersInTheGroup())
                {
                    if (groupPlayer.IsWithinRadius(this, WorldMgr.MAX_EXPFORKILL_DISTANCE))
                    {
                        if (groupPlayer.Level < 45) continue;
                        AtlasROGManager.GenerateOrbAmount(groupPlayer,OrbsReward);
                        AtlasROGManager.GenerateBeetleCarapace(groupPlayer);
                        groupPlayer.Achieve($"{achievementMob}-Credit");
                    }
                }
            }
            else if (playerKiller != null)
            {
                if (playerKiller.Level >= 45)
                {
                    AtlasROGManager.GenerateOrbAmount(playerKiller,OrbsReward);
                    AtlasROGManager.GenerateBeetleCarapace(playerKiller);
                    playerKiller.Achieve($"{achievementMob}-Credit");;
                }
            }

            base.ProcessDeath(killer);
        }
    }
}
