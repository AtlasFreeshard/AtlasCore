﻿using DOL.Events;
using DOL.GS.PacketHandler;
using DOL.Language;

namespace DOL.GS;

public class PlayerExpComponent
{
    private readonly GamePlayer _player;

    public PlayerExpComponent(GamePlayer player)
    {
        _player = player;
    }
    
    /// <summary>
    /// What is the maximum level a player can achieve?
    /// To alter this in a custom GamePlayer class you must override this method and
    /// provide your own XPForLevel array with MaxLevel + 1 entries
    /// </summary>
    public virtual byte MaxLevel => 50;
    
    /// <summary>
    /// How much experience is needed for a given level?
    /// </summary>
    public virtual long GetExperienceNeededForLevel(int level)
    {
        if (level > MaxLevel)
            return GetScaledExperienceAmountForLevel(MaxLevel);

        return level <= 0 ? GetScaledExperienceAmountForLevel(0) : GetScaledExperienceAmountForLevel(level - 1);
    }

    private static long GetScaledExperienceAmountForLevel(int level)
    {
        try
        {
            return ScaledXpForLevel[level];
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// A table that holds the required XP/Level
    /// This must include a final entry for MaxLevel + 1
    /// </summary>
    private static readonly long[] XpForLevel =
    {
        0, // xp to level 1
        50, // xp to level 2
        250, // xp to level 3
        850, // xp to level 4
        2300, // xp to level 5
        6350, // xp to level 6
        15950, // xp to level 7
        37950, // xp to level 8
        88950, // xp to level 9
        203950, // xp to level 10
        459950, // xp to level 11
        839950, // xp to level 12
        1399950, // xp to level 13
        2199950, // xp to level 14
        3399950, // xp to level 15
        5199950, // xp to level 16
        7899950, // xp to level 17
        11799950, // xp to level 18
        17499950, // xp to level 19
        25899950, // xp to level 20
        38199950, // xp to level 21
        54699950, // xp to level 22
        76999950, // xp to level 23
        106999950, // xp to level 24
        146999950, // xp to level 25
        199999950, // xp to level 26
        269999950, // xp to level 27
        359999950, // xp to level 28
        479999950, // xp to level 29
        639999950, // xp to level 30
        849999950, // xp to level 31
        1119999950, // xp to level 32
        1469999950, // xp to level 33
        1929999950, // xp to level 34
        2529999950, // xp to level 35
        3319999950, // xp to level 36
        4299999950, // xp to level 37
        5499999950, // xp to level 38
        6899999950, // xp to level 39
        8599999950, // xp to level 40
        12899999950, // xp to level 41
        20699999950, // xp to level 42
        29999999950, // xp to level 43
        40799999950, // xp to level 44
        53999999950, // xp to level 45
        69599999950, // xp to level 46
        88499999950, // xp to level 47
        110999999950, // xp to level 48
        137999999950, // xp to level 49
        169999999950, // xp to level 50
        999999999950, // xp to level 51
    };

    /// <summary>
    /// A table that holds the required XP/Level
    /// This must include a final entry for MaxLevel + 1
    /// </summary>
    private static readonly long[] ScaledXpForLevel =
    {
        0, // xp to level 1
        50, // xp to level 2
        250, // xp to level 3
        850, // xp to level 4
        2300, // xp to level 5
        6350, // xp to level 6
        15950, // xp to level 7
        37950, // xp to level 8
        88950, // xp to level 9
        203950, // xp to level 10
        459950, // xp to level 11
        839950, // xp to level 12
        1399950, // xp to level 13
        2199950, // xp to level 14
        3399950, // xp to level 15
        6499938, // xp to level 16
        9953937, // xp to level 17
        14985937, // xp to level 18
        22399936, // xp to level 19
        33410936, // xp to level 20
        49659935, // xp to level 21
        71656935, // xp to level 22
        101639934, // xp to level 23
        142309934, // xp to level 24
        196979933, // xp to level 25
        269999933, // xp to level 26
        367199932, // xp to level 27
        493199932, // xp to level 28
        662399931, // xp to level 29
        889599931, // xp to level 30
        1189999930, // xp to level 31
        1579199930, // xp to level 32
        2087399929, // xp to level 33
        2759899929, // xp to level 34
        3643199928, // xp to level 35
        4813999928, // xp to level 36
        6277999927, // xp to level 37
        8084999927, // xp to level 38
        10211999926, // xp to level 39
        12813999926, // xp to level 40
        16382999937, // xp to level 41
        20699999950, // xp to level 42
        29999999950, // xp to level 43
        40799999950, // xp to level 44
        53999999950, // xp to level 45
        69599999950, // xp to level 46
        88499999950, // xp to level 47
        110999999950, // xp to level 48
        137999999950, // xp to level 49
        169999999950, // xp to level 50
        999999999950, // xp to level 51
    };

    /// <summary>
    /// Gets or sets the current xp of this player
    /// </summary>
    public virtual long Experience
    {
        get => _player.DBCharacter?.Experience ?? 0;
        set
        {
            if (_player.DBCharacter != null)
                _player.DBCharacter.Experience = value;
        }
    }

    /// <summary>
    /// Returns the xp that are needed for the next level
    /// </summary>
    public virtual long ExperienceForNextLevel => GetExperienceNeededForLevel(_player.Level + 1);

    /// <summary>
    /// Returns the xp that were needed for the current level
    /// </summary>
    public virtual long ExperienceForCurrentLevel => GetExperienceNeededForLevel(_player.Level);

    /// <summary>
    /// Returns the xp that is needed for the second stage of current level
    /// </summary>
    public virtual long ExperienceForCurrentLevelSecondStage =>
        1 + ExperienceForCurrentLevel + (ExperienceForNextLevel - ExperienceForCurrentLevel) / 2;

    /// <summary>
    /// Returns how far into the level we have progressed
    /// A value between 0 and 1000 (1 bubble = 100)
    /// </summary>
    public virtual ushort LevelPermill
    {
        get
        {
            //No progress if we haven't even reached current level!
            if (Experience < ExperienceForCurrentLevel)
                return 0;
            //No progess after maximum level
            if (_player.Level > MaxLevel)
                return 0;
            return (ushort) (1000 * (Experience - ExperienceForCurrentLevel) /
                             (ExperienceForNextLevel - ExperienceForCurrentLevel));
        }
    }

    /// <summary>
    /// Called whenever this player gains experience
    /// </summary>
    /// <param name="expTotal"></param>
    /// <param name="expCampBonus"></param>
    /// <param name="expGroupBonus"></param>
    /// <param name="expOutpostBonus"></param>
    /// <param name="sendMessage"></param>
    public void GainExperience(eXPSource xpSource, long expTotal, long expCampBonus, long expGroupBonus,
        long atlasBonus, long expOutpostBonus, bool sendMessage)
    {
        GainExperience(xpSource, expTotal, expCampBonus, expGroupBonus, expOutpostBonus, atlasBonus, sendMessage, true);
    }

    /// <summary>
    /// Called whenever this player gains experience
    /// </summary>
    /// <param name="expTotal"></param>
    /// <param name="expCampBonus"></param>
    /// <param name="expGroupBonus"></param>
    /// <param name="expOutpostBonus"></param>
    /// <param name="sendMessage"></param>
    /// <param name="allowMultiply"></param>
    public void GainExperience(eXPSource xpSource, long expTotal, long expCampBonus, long expGroupBonus,
        long atlasBonus, long expOutpostBonus, bool sendMessage, bool allowMultiply)
    {
        GainExperience(xpSource, expTotal, expCampBonus, expGroupBonus, expOutpostBonus, atlasBonus, sendMessage,
            allowMultiply, true);
    }

    /// <summary>
    /// Called whenever this player gains experience
    /// </summary>
    /// <param name="expTotal"></param>
    /// <param name="expCampBonus"></param>
    /// <param name="expGroupBonus"></param>
    /// <param name="expOutpostBonus"></param>
    /// <param name="sendMessage"></param>
    /// <param name="allowMultiply"></param>
    /// <param name="notify"></param>
    public void GainExperience(eXPSource xpSource, long expTotal, long expCampBonus, long expGroupBonus,
        long expOutpostBonus, long atlasBonus, bool sendMessage, bool allowMultiply, bool notify)
    {
        if (!_player.GainXP && expTotal > 0)
            return;

        if (_player.HCFlag && _player.Group != null)
        {
            foreach (var player in _player.Group.GetPlayersInTheGroup())
            {
                if (player.Level > _player.Level + 5)
                    expTotal = 0;
            }

            if (expTotal == 0)
                _player.Out.SendMessage("This kill was not hardcore enough to gain experience.", eChatType.CT_System,
                    eChatLoc.CL_SystemWindow);
        }

        //xp rate modifier
        if (allowMultiply)
        {
            //we only want to modify the base rate, not the group or camp bonus
            expTotal -= expGroupBonus;
            expTotal -= expCampBonus;
            expTotal -= expOutpostBonus;
            expTotal -= atlasBonus;
            //[StephenxPimentel] - Zone Bonus XP Support
            if (ServerProperties.Properties.ENABLE_ZONE_BONUSES)
            {
                var zoneBonus = expTotal * ZoneBonus.GetXPBonus(_player) / 100;
                if (zoneBonus > 0)
                {
                    var tmpBonus = (long) (zoneBonus * ServerProperties.Properties.XP_RATE);
                    _player.Out.SendMessage(
                        ZoneBonus.GetBonusMessage(_player, (int) tmpBonus, ZoneBonus.eZoneBonusType.XP),
                        eChatType.CT_Important, eChatLoc.CL_SystemWindow);
                    GainExperience(eXPSource.Other, tmpBonus, 0, 0, 0, 0, false, false, false);
                }
            }


            if (_player.CurrentRegion.IsRvR)
                expTotal = (long) (expTotal * ServerProperties.Properties.RvR_XP_RATE);
            else
                expTotal = (long) (expTotal * ServerProperties.Properties.XP_RATE);

            // [Freya] Nidel: ToA Xp Bonus
            long xpBonus = _player.GetModified(eProperty.XpPoints);
            if (xpBonus != 0)
            {
                expTotal += (expTotal * xpBonus) / 100;
            }

            var hardXpCap = GameServer.ServerRules.GetExperienceForLiving(_player.Level) *
                ServerProperties.Properties.XP_HARDCAP_PERCENT / 100;

            if (expTotal > hardXpCap)
                expTotal = hardXpCap;

            expTotal += expOutpostBonus;
            expTotal += expGroupBonus;
            expTotal += expCampBonus;
            expTotal += atlasBonus;

        }

        // Get Champion Experience too
        _player.GainChampionExperience(expTotal);

        if (expTotal > 0 && notify)
            _player.Notify(GameLivingEvent.GainedExperience, this,
                new GainedExperienceEventArgs(expTotal, expCampBonus, expGroupBonus, expOutpostBonus, sendMessage,
                    allowMultiply, xpSource));

        if (_player.IsLevelSecondStage)
        {
            if (Experience + expTotal < ExperienceForCurrentLevelSecondStage)
            {
                expTotal = ExperienceForCurrentLevelSecondStage - Experience;
            }
        }
        else if (Experience + expTotal < ExperienceForCurrentLevel)
        {
            expTotal = ExperienceForCurrentLevel - Experience;
        }

        if (sendMessage && expTotal > 0)
        {
            var format = System.Globalization.NumberFormatInfo.InvariantInfo;
            var totalExpStr = expTotal.ToString("N0", format);
            var expCampBonusStr = "";
            var expGroupBonusStr = "";
            var expOutpostBonusStr = "";
            var expSoloBonusStr = "";

            if (expCampBonus > 0)
            {
                expCampBonusStr = LanguageMgr.GetTranslation(_player.Client.Account.Language,
                    "GamePlayer.GainExperience.CampBonus", expCampBonus.ToString("N0", format)) + " ";
            }

            if (expGroupBonus > 0)
            {
                expGroupBonusStr = LanguageMgr.GetTranslation(_player.Client.Account.Language,
                    "GamePlayer.GainExperience.GroupBonus", expGroupBonus.ToString("N0", format)) + " ";
            }

            if (expOutpostBonus > 0)
            {
                expOutpostBonusStr = LanguageMgr.GetTranslation(_player.Client.Account.Language,
                    "GamePlayer.GainExperience.OutpostBonus", expOutpostBonus.ToString("N0", format)) + " ";
            }

            if (atlasBonus > 0)
            {
                expSoloBonusStr = "(" + atlasBonus.ToString("N0", format) + " Atlas bonus)";
            }

            _player.Out.SendMessage(
                LanguageMgr.GetTranslation(_player.Client.Account.Language, "GamePlayer.GainExperience.YouGet",
                    totalExpStr) + expCampBonusStr + expGroupBonusStr + expOutpostBonusStr + expSoloBonusStr,
                eChatType.CT_Important, eChatLoc.CL_SystemWindow);
        }

        Experience += expTotal;

        if (expTotal >= 0)
        {
            switch (_player.Level)
            {
                //Level up
                case >= 5 when !_player.CharacterClass.HasAdvancedFromBaseClass():
                {
                    if (expTotal > 0)
                    {
                        _player.Out.SendMessage(
                            LanguageMgr.GetTranslation(_player.Client.Account.Language,
                                "GamePlayer.GainExperience.CannotRaise"), eChatType.CT_Important, eChatLoc.CL_SystemWindow);
                        _player.Out.SendMessage(
                            LanguageMgr.GetTranslation(_player.Client.Account.Language,
                                "GamePlayer.GainExperience.TalkToTrainer"), eChatType.CT_Important,
                            eChatLoc.CL_SystemWindow);
                    }

                    break;
                }
                case >= 40 when _player.Level < MaxLevel && !_player.IsLevelSecondStage && Experience >= ExperienceForCurrentLevelSecondStage:
                    _player.OnLevelSecondStage();
                    _player.Notify(GamePlayerEvent.LevelSecondStage, this);
                    break;
                default:
                {
                    if (_player.Level < MaxLevel && Experience >= ExperienceForNextLevel)
                    {
                        _player.Level++;
                    }

                    break;
                }
            }

            if (_player.Level >= 50)
            {

            }
        }

        _player.Out.SendUpdatePoints();
    }
}