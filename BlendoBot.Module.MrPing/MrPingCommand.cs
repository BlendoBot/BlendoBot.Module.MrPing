using BlendoBot.Core.Command;
using BlendoBot.Core.Entities;
using BlendoBot.Core.Module;
using BlendoBot.Core.Utility;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace BlendoBot.Module.MrPing;

internal class MrPingCommand : ICommand {
	public MrPingCommand(MrPing module) {
		this.module = module;
	}

	private readonly MrPing module;
	public IModule Module => module;
	public string Guid => "mrping.command";
	public string DesiredTerm => "mrping";
	public string Description => "Subjects someone to the Mr. Ping Challenge!";
	public Dictionary<string, string> Usage => new() {
		{ string.Empty, "Creates a new Mr Ping challenge for a random victim." },
		{ "list", "Lists all outstanding challenges." },
		{ "stats", "Shows some neat stats about challenges in this guild." }
	};

	private readonly Random random = new();

	public async Task OnMessage(MessageCreateEventArgs e, string[] tokenizedMessage) {
		if (tokenizedMessage.Length == 1 && tokenizedMessage[0].ToLower() == "list") {
			DiscordMessage message = await module.DiscordInteractor.Send(this, new SendEventArgs {
				Message = "Loading Mr. Ping challenges...",
				Channel = e.Channel,
				Tag = "MrPingList"
			});
			await message.ModifyAsync(module.GetActiveChallenges(e.Channel));
		} else if (tokenizedMessage.Length == 1 && tokenizedMessage[0].ToLower() == "stats") {
			DiscordMessage message = await module.DiscordInteractor.Send(this, new SendEventArgs {
				Message = "Loading Mr. Ping stats...",
				Channel = e.Channel,
				Tag = "MrPingStats"
			});
			await message.ModifyAsync(string.Empty, module.GetStatsMessage());
		} else if (tokenizedMessage.Length == 0) {

			// Edit the Mr Ping image to randomly pick a user on the server, and a random number
			// of pings (up to 100).

			//! New change, BlendoBot will appear as if it's typing in the channel while it's waiting.
			await e.Channel.TriggerTypingAsync();

			// First, choose a user from the server.
			// Purge the list of anyone not valid:
			List<DiscordMember> filteredMembers = new();
			foreach (DiscordMember member in e.Channel.Users) {
				//TODO: User presence is null for every user, but I only want to challenge people who are online/away and not busy/offline.
				if (!member.IsBot && member.PermissionsIn(e.Channel).HasPermission(Permissions.SendMessages)) {
					filteredMembers.Add(member);
				}
			}

			if (filteredMembers.Count == 0) {
				await module.DiscordInteractor.Send(this, new SendEventArgs {
					Message = "No one is available for the Mr. Ping Challenge. 👀",
					Channel = e.Channel,
					Tag = "MrPingErrorNoUsers"
				});
				return;
			}

			// Let's randomly pick someone from those filtered members.
			DiscordMember chosenMember = filteredMembers[(int)(random.NextDouble() * filteredMembers.Count)];

			// A random number from 1 to 100 will be chosen.
			int numberOfPings = (int)(random.NextDouble() * MrPing.MaxPings + 1);

			string imagePath = await MrPing.CreateImage(chosenMember, numberOfPings);

			await module.DiscordInteractor.Send(this, new SendEventArgs {
				FilePath = imagePath,
				Channel = e.Channel,
				Tag = "MrPingSuccess"
			});

			module.NewChallenge(chosenMember, e.Author, numberOfPings, e.Channel);

			if (File.Exists(imagePath)) {
				File.Delete(imagePath);
			}
		} else {
			await module.DiscordInteractor.Send(this, new SendEventArgs {
				Message = $"Not sure what you meant! Please refer to {$"{module.ModuleManager.GetHelpTermForCommand(this)}".Code()} for usage information.",
				Channel = e.Channel,
				Tag = "MrPingInvalidArguments"
			});
		}
	}
}
