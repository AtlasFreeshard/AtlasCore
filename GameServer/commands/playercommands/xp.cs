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
using DOL.GS.PacketHandler;

namespace DOL.GS.Commands
{
	[CmdAttribute(
		"&xp",
		ePrivLevel.Player,
		"toggle receiving experience points",
		"/xp <on/off>")]
	public class XPCommandHandler : AbstractCommandHandler, ICommandHandler
	{
		public void OnCommand(GameClient client, string[] args)
		{
			if (args.Length < 2)
			{
				DisplaySyntax(client);
				return;
			}

			if (ServerProperties.Properties.EVENT_DISABLE_XP && client.Account.PrivLevel == 1)
			{
				client.Out.SendMessage("This command is disabled during the event.", eChatType.CT_Important, eChatLoc.CL_SystemWindow);
				return;
			}

			if (IsSpammingCommand(client.Player, "xp"))
				return;

			if (args[1].ToLower().Equals("on"))
			{
				client.Player.GainXP = true;
				client.Out.SendMessage("Your xp flag is ON. You will gain experience points. Use '/xp off' to stop gaining experience points.", eChatType.CT_Important, eChatLoc.CL_SystemWindow);
			}
			else if (args[1].ToLower().Equals("off"))
			{
				client.Player.GainXP = false;
				client.Out.SendMessage("Your xp flag is OFF. You will no longer gain experience points. Use '/xp on' to start gaining experience points again.", eChatType.CT_Important, eChatLoc.CL_SystemWindow);
			}
		}
	}
}