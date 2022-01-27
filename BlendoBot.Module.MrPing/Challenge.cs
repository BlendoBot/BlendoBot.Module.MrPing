using DSharpPlus.Entities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BlendoBot.Module.MrPing;

internal class Challenge {
	public Challenge(DateTime startTime, DiscordChannel channel, DiscordUser author, DiscordUser target, int targetPings) {
		StartTime = startTime;
		Channel = channel;
		Author = author;
		Target = target;
		TargetPings = targetPings;
		LastPing = null;
	}
	public DateTime StartTime { get; }
	public DateTime EndTime => StartTime.AddMinutes(10);
	public TimeSpan TimeRemaining => EndTime - DateTime.UtcNow;
	public DiscordChannel Channel { get; }

	public DiscordUser Author { get; }
	public DiscordUser Target { get; }
	public int TargetPings { get; }
	private readonly Dictionary<ulong, (DiscordUser User, int Pings)> seenPings = new();
	public int TotalPings => seenPings.Values.Sum(x => x.Pings);
	public List<(DiscordUser User, int Pings)> SeenPings => seenPings.Values.ToList();
	public DiscordUser LastPing { get; private set; }

	public bool Completed => TotalPings >= TargetPings;

	public void AddPing(DiscordUser pingUser) {
		if (!seenPings.ContainsKey(pingUser.Id)) {
			seenPings.Add(pingUser.Id, (pingUser, 1));
		} else {
			seenPings[pingUser.Id] = (pingUser, seenPings[pingUser.Id].Pings + 1);
		}
		LastPing = pingUser;
	}
}
