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
		"&rp",
		ePrivLevel.Player,
		"toggle receiving realm points",
		"/rp <on/off>")]
	public class RPCommandHandler : AbstractCommandHandler, ICommandHandler
	{
		public void OnCommand(GameClient client, string[] args)
		{
			if (args.Length < 2)
			{
				DisplaySyntax(client);
				return;
			}

			if (IsSpammingCommand(client.Player, "rp"))
				return;


			if (client.Player.Level < 40)
			{
				DisplayMessage(client, "This command is only available to players above level 39.");
				return;
			}

			switch (args[1].ToLower())
			{
				case "on":
					client.Player.GainRP = true;
					client.Out.SendMessage("Your rp flag is ON. You will gain realm points. Use '/rp off' to stop gaining realm points.", eChatType.CT_Important, eChatLoc.CL_SystemWindow);
					break;
				case "off":
					client.Player.GainRP = false;
					client.Out.SendMessage("Your rp flag is OFF. You will no longer gain realm points. Use '/rp on' to start gaining realm points again.", eChatType.CT_Important, eChatLoc.CL_SystemWindow);
					break;
			}
		}
	}
}