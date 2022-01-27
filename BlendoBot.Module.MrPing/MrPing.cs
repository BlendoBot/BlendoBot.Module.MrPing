using BlendoBot.Core.Entities;
using BlendoBot.Core.Module;
using BlendoBot.Core.Services;
using BlendoBot.Module.MrPing.Properties;
using DSharpPlus.Entities;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BlendoBot.Module.MrPing;

[Module(Guid = "com.biendeo.blendobot.module.mrping", Name = "Mr. Ping Challenge", Author = "Biendeo", Version = "2.0.0", Url = "https://github.com/BlendoBot/BlendoBot.Module.MrPing")]
public class MrPing : IModule, IDisposable {
	public MrPing(IDiscordInteractor discordInteractor, IFilePathProvider filePathProvider, ILogger logger, IModuleManager moduleManager) {
		DiscordInteractor = discordInteractor;
		FilePathProvider = filePathProvider;
		Logger = logger;
		ModuleManager = moduleManager;

		MrPingCommand = new(this);
		MrPingListener = new(this);
	}
	internal ulong GuildId { get; private set; }

	internal readonly MrPingCommand MrPingCommand;
	internal readonly MrPingListener MrPingListener;

	internal readonly IDiscordInteractor DiscordInteractor;
	internal readonly IFilePathProvider FilePathProvider;
	internal readonly ILogger Logger;
	internal readonly IModuleManager ModuleManager;

	private readonly List<Challenge> activeChallenges = new();

	private Thread challengeWatcherThread;
	private bool isTerminating;

	public const int MaxPings = 100;

	public async Task<bool> Startup(ulong guildId) {
		GuildId = guildId;

		using MrPingDbContext dbContext = MrPingDbContext.Get(this);
		await dbContext.Database.EnsureCreatedAsync();

		isTerminating = false;
		challengeWatcherThread = new(new ThreadStart(ChallengeWatcherThread));
		challengeWatcherThread.Start();

		return ModuleManager.RegisterCommand(this, MrPingCommand, out _) && ModuleManager.RegisterMessageListener(this, MrPingListener);
	}

	public void Dispose() {
		Dispose(true);
		GC.SuppressFinalize(this);
	}

	protected virtual void Dispose(bool disposing) {
		if (disposing) {
			isTerminating = true;
			challengeWatcherThread.Join();
		}
	}

	private async void ChallengeWatcherThread() {
		while (!isTerminating) {
			Challenge grabbedChallenge = null;
			lock (activeChallenges) {
				try {
					if (activeChallenges.Any() && activeChallenges.First().TimeRemaining.TotalMilliseconds < 0.0) {
						grabbedChallenge = activeChallenges.First();
						activeChallenges.RemoveAt(0);
					}
				} catch (Exception e) {
					Logger.Log(this, new LogEventArgs {
						Type = LogType.Error,
						Message = $"Received exception when checking Mr Ping :\n{e}"
					});
				}
			}
			if (grabbedChallenge != null) {
				await FinishChallenge(grabbedChallenge);
			}
			Thread.Sleep(500);
		}
	}

	public void NewChallenge(DiscordUser target, DiscordUser author, int pingCount, DiscordChannel channel) {
		lock (activeChallenges) {
			activeChallenges.Add(new Challenge(DateTime.UtcNow, channel, author, target, pingCount));
		}
	}

	public async Task PingUser(DiscordUser target, DiscordUser author, DiscordChannel channel) {
		Challenge challenge = null;
		bool challengeCompleted = false;
		lock (activeChallenges) {
			challenge = activeChallenges.Find(c => c.Target == target);
			if (challenge != null && !challenge.Completed && challenge.Channel == channel) {
				challenge.AddPing(author);
				if (challengeCompleted = challenge.Completed) {
					activeChallenges.Remove(challenge);
				}
			}
		}
		if (challengeCompleted) {
			await FinishChallenge(challenge);
		}
	}

	private async Task FinishChallenge(Challenge challenge) {
		using MrPingDbContext dbContext = MrPingDbContext.Get(this);
		foreach ((DiscordUser user, int pings) in challenge.SeenPings) {
			UserStats stats = dbContext.UserStats.FirstOrDefault(s => s.UserId == user.Id);
			if (stats == null) {
				stats = new(user.Id);
				dbContext.UserStats.Add(stats);
			}
			stats.PingsSent += pings;
			if (challenge.Completed && challenge.LastPing.Id == user.Id) {
				++stats.ChallengesSelfFinished;
			}
		}
		await dbContext.SaveChangesAsync();

		UserStats authorStats = dbContext.UserStats.FirstOrDefault(s => s.UserId == challenge.Author.Id);
		if (authorStats == null) {
			authorStats = new(challenge.Author.Id);
			dbContext.UserStats.Add(authorStats);
		}
		++authorStats.ChallengesSent;
		authorStats.PingsPrescribed += challenge.TargetPings;
		if (challenge.Completed) {
			++authorStats.ChallengesCompleted;
		}
		await dbContext.SaveChangesAsync();

		UserStats targetStats = dbContext.UserStats.FirstOrDefault(s => s.UserId == challenge.Target.Id);
		if (targetStats == null) {
			targetStats = new(challenge.Target.Id);
			dbContext.UserStats.Add(targetStats);
		}
		++targetStats.ChallengesReceived;
		targetStats.PingsReceived += challenge.TotalPings;
		await dbContext.SaveChangesAsync();
	}

	public string GetActiveChallenges(DiscordChannel channel) {
		List<Challenge> releventChallenges = activeChallenges.FindAll(c => c.Channel == channel);
		if (releventChallenges.Count > 0) {
			StringBuilder sb = new();
			sb.AppendLine("Current Mr Ping challenges:");
			int countedChallenges = 0;
			foreach (Challenge challenge in releventChallenges) {
				// Safety check to not print too much.
				if (sb.Length > 1900) {
					break;
				}
				sb.AppendLine($"Ends <t:{(int)(challenge.EndTime - DateTime.UnixEpoch).TotalSeconds}:R> {challenge.Target.Mention} ({challenge.TotalPings}/{challenge.TargetPings})");
				++countedChallenges;
			}
			if (countedChallenges != releventChallenges.Count) {
				sb.AppendLine($"And {releventChallenges.Count - countedChallenges} more...");
			}
			return sb.ToString();
		} else {
			return $"No active Mr Ping challenges! You should start one!";
		}
	}

	public DiscordEmbed GetStatsMessage() {
		using MrPingDbContext dbContext = MrPingDbContext.Get(this);
		DiscordEmbedBuilder embedBuilder = new();
		embedBuilder.Title = "Mr. Ping Challenge Stats";
		if (dbContext.UserStats.Any()) {
			List<UserStats> stats = dbContext.UserStats.ToList();

			UserStats mostPingsSent = stats.MaxBy(s => s.PingsSent);
			embedBuilder.AddField("Most active fella", $"<@{mostPingsSent.UserId}> ({mostPingsSent.PingsSent} pings sent)");

			UserStats mostPingsReceived = stats.MaxBy(s => s.PingsReceived);
			embedBuilder.AddField("Most popular prize", $"<@{mostPingsReceived.UserId}> ({mostPingsReceived.PingsReceived} pings received)");

			UserStats mostChallengesReceived = stats.MaxBy(s => s.ChallengesReceived);
			embedBuilder.AddField("Unluckiest person", $"<@{mostChallengesReceived.UserId}> ({mostChallengesReceived.ChallengesReceived} challenges received)");

			UserStats mostChallengesSent = stats.MaxBy(s => s.ChallengesSent);
			embedBuilder.AddField("Cruelist crew member", $"<@{mostChallengesSent.UserId}> ({mostChallengesSent.ChallengesSent} challenges sent)");

			UserStats mostChallengesCompleted = stats.MaxBy(s => s.ChallengesCompleted);
			embedBuilder.AddField("Most successful dude", $"<@{mostChallengesCompleted.UserId}> ({mostChallengesCompleted.PingsSent} successful challenges)");

			UserStats mostChallengesSelfFinished = stats.MaxBy(s => s.ChallengesSelfFinished);
			embedBuilder.AddField("Ping stealer", $"<@{mostChallengesSelfFinished.UserId}> ({mostChallengesSelfFinished.ChallengesSelfFinished} challenges personally finished)");

			UserStats mostPercentageSuccessfulPings = stats.MaxBy(s => s.PercentageSuccessfulPings);
			embedBuilder.AddField("Easy target", $"<@{mostPercentageSuccessfulPings.UserId}> ({mostPercentageSuccessfulPings.PercentageSuccessfulPings * 100.0:0.00}% pings sent)");
		} else {
			embedBuilder.AddField("No data", "You must do a Mr. Ping challenge before you can view stats!");
		}
		return embedBuilder.Build();
	}

	//TODO: https://docs.microsoft.com/en-gb/dotnet/core/compatibility/core-libraries/6.0/system-drawing-common-windows-only
#pragma warning disable CA1416 // Validate platform compatibility

	internal static async Task<string> CreateImage(DiscordMember member, int pings) {
		using Image image = Resources.MrPingTemplate;
		using HttpClient wc = new();
		byte[] avatarBytes = await wc.GetByteArrayAsync(member.AvatarUrl);
		using MemoryStream avatarStream = new(avatarBytes);
		using Image userAvatar = Image.FromStream(avatarStream);
		using Image userAvatarScaled = ResizeImage(userAvatar, 80, 80);
		using Graphics graphics = Graphics.FromImage(image);
		using Font intendedNameFont = new("Arial", 60);
		using Font nameFont = ResizeFont(graphics, $"@{member.Username} #{member.Discriminator}", new RectangleF(130, 285, 260, 35), intendedNameFont);
		using Font numberFont = new("Arial", 30);
		using StringFormat format = new() {
			Alignment = StringAlignment.Center,
			LineAlignment = StringAlignment.Center
		};
		graphics.SmoothingMode = SmoothingMode.AntiAlias;
		graphics.DrawImage(userAvatarScaled, new Point(30, 252));
		graphics.DrawString($"@{member.Username} #{member.Discriminator}", nameFont, Brushes.DarkBlue, new RectangleF(130, 285, 260, 35), format);
		graphics.DrawString($"{pings}", numberFont, Brushes.DarkRed, new RectangleF(-45, 317, 175, 70), format);
		graphics.Flush();

		string filePath = $"mrping-{Guid.NewGuid()}.png";
		image.Save(filePath);

		return filePath;
	}

	private static Font ResizeFont(Graphics g, string s, RectangleF r, Font font) {
		SizeF realSize = g.MeasureString(s, font);
		float heightRatio = r.Height / realSize.Height;
		float widthRatio = r.Width / realSize.Width;

		float scaleRatio = (heightRatio < widthRatio) ? heightRatio : widthRatio;

		float scaleSize = font.Size * scaleRatio;

		return new Font(font.FontFamily, scaleSize);
	}

	/// <summary>
	/// Resize the image to the specified width and height.
	/// </summary>
	/// <param name="image">The image to resize.</param>
	/// <param name="width">The width to resize to.</param>
	/// <param name="height">The height to resize to.</param>
	/// <returns>The resized image.</returns>
	private static Bitmap ResizeImage(Image image, int width, int height) {
		Rectangle destRect = new(0, 0, width, height);
		Bitmap destImage = new(width, height);

		destImage.SetResolution(image.HorizontalResolution, image.VerticalResolution);

		using (Graphics graphics = Graphics.FromImage(destImage)) {
			graphics.CompositingMode = CompositingMode.SourceCopy;
			graphics.CompositingQuality = CompositingQuality.HighQuality;
			graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
			graphics.SmoothingMode = SmoothingMode.HighQuality;
			graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

			using ImageAttributes wrapMode = new();
			wrapMode.SetWrapMode(WrapMode.TileFlipXY);
			graphics.DrawImage(image, destRect, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, wrapMode);
		}

		return destImage;
	}
}

#pragma warning restore CA1416 // Validate platform compatibility
