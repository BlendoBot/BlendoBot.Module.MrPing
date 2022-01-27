using BlendoBot.Core.Messages;
using BlendoBot.Core.Module;
using DSharpPlus.EventArgs;
using System.Linq;
using System.Threading.Tasks;

namespace BlendoBot.Module.MrPing;

internal class MrPingListener : IMessageListener {
	private readonly MrPing module;

	public MrPingListener(MrPing module) {
		this.module = module;
	}

	public IModule Module => module;

	public async Task OnMessage(MessageCreateEventArgs e) {
		if (e.MentionedUsers.Any()) {
			await module.PingUser(e.MentionedUsers[0], e.Author, e.Channel);
		}
	}
}
