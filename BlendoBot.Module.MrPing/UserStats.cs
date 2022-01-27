using BlendoBot.Core.Services;
using DSharpPlus.Entities;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Threading.Tasks;

namespace BlendoBot.Module.MrPing;

internal class UserStats {
	[Key]
	public ulong UserId { get; set; }
	[NotMapped]
	public DiscordUser User { get; set; }
	public int PingsSent { get; set; }
	public int ChallengesReceived { get; set; }
	public int ChallengesSent { get; set; }
	public int PingsPrescribed { get; set; }
	public int PingsReceived { get; set; }
	public int ChallengesCompleted { get; set; }
	public int ChallengesSelfFinished { get; set; }
	public double PercentageSuccessfulPings => PingsReceived * 1.0 / Math.Max(1, PingsPrescribed);

	public UserStats(ulong userId) {
		UserId = userId;
		User = null;
	}

	public async Task UpdateCachedData(IDiscordInteractor discordInteractor) {
		User = await discordInteractor.GetUser(this, UserId);
	}
}
