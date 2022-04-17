using System;
using System.IO;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using System.Linq;
using System.Collections.Generic;
using Instagram_Reels_Bot.Helpers;
using Instagram_Reels_Bot.Services;
using Instagram_Reels_Bot.DataTables;

namespace Instagram_Reels_Bot.Modules
{
	public class SlashCommands : InteractionModuleBase<ShardedInteractionContext>
	{
		// Dependencies can be accessed through Property injection, public properties with public setters will be set by the service provider
		public InteractionService Commands { get; set; }

		private CommandHandler _handler;
		private Subscriptions _subscriptions;

		// Constructor injection is also a valid way to access the dependecies
		public SlashCommands(CommandHandler handler, Subscriptions subs)
		{
			_handler = handler;
			_subscriptions = subs;
		}

		[SlashCommand("link","Procesa enlace de Instagram.", runMode: RunMode.Async)]
		public async Task Link(string url, [Summary(description: "Número de orden en el carrusel del post.")][MinValue(1)] int index = 1)
        {
			// Check whitelist:
			if (!Whitelist.IsServerOnList(((Context.Guild == null) ? (0) : (Context.Guild.Id))))
			{
				// Self-hosted whitelist notification for official bot:
				if (Context.Client.CurrentUser.Id == 815695225678463017)
				{
					await RespondAsync("This bot is now self-host only. Learn more about this change in the updates channel on the support server: https://discord.gg/8dkjmGSbYE", ephemeral: true);
				}
				else
				{
					await RespondAsync("This guild is not on the whitelist. The command was blocked.", ephemeral: true);
				}
				return;
			}

			//Buy more time to process posts:
			await DeferAsync(false);

			// Get IG account:
			InstagramProcessor instagram = new InstagramProcessor(InstagramProcessor.AccountFinder.GetIGAccount());

			//Process Post:
			InstagramProcessorResponse response = await instagram.PostRouter(url, Context.Guild, index);

            if (!response.success)
            {
				//Failed to process post:
				await FollowupAsync(response.error, ephemeral: true);
				return;
            }

			//Create embed builder:
			IGEmbedBuilder embed = new IGEmbedBuilder(response, Context.User.Username);

			//Create component builder:
			IGComponentBuilder component = new IGComponentBuilder(response, Context.User.Id);

			if (response.isVideo)
			{
				if (response.stream != null)
                {
					//Response with stream:
					using (Stream stream = new MemoryStream(response.stream))
					{
						FileAttachment attachment = new FileAttachment(stream, "IGMedia.mp4", "Vídeo de Instagram.");

						await Context.Interaction.FollowupWithFileAsync(attachment, embed: embed.AutoSelector(), components: component.AutoSelector());
					}
				}
                else
                {
					//Response without stream:
					await FollowupAsync(response.contentURL.ToString(), embed: embed.AutoSelector(), components: component.AutoSelector());
                }

			}
            else
            {
				if (response.stream != null)
				{
					using (Stream stream = new MemoryStream(response.stream))
					{
						FileAttachment attachment = new FileAttachment(stream, "IGMedia.jpg", "Imagen de Instagram.");
						await Context.Interaction.FollowupWithFileAsync(attachment, embed: embed.AutoSelector(), allowedMentions: AllowedMentions.None, components: component.AutoSelector());
					}
				}
				else
				{
					await FollowupAsync(embed: embed.AutoSelector(), components: component.AutoSelector());
				}
			}
			
		}
		[SlashCommand("profile", "Obtener información de un perfil de Instagram.", runMode: RunMode.Async)]
		public async Task Profile([Summary("username", "Nombre de usuario de la cuenta de Instagram.")] string username)
        {
			// Check whitelist:
			if (!Whitelist.IsServerOnList(((Context.Guild == null) ? (0) : (Context.Guild.Id))))
			{
				// Self-hosted whitelist notification for official bot:
				if (Context.Client.CurrentUser.Id == 815695225678463017)
				{
					await RespondAsync("This bot is now self-host only. Learn more about this change in the updates channel on the support server: https://discord.gg/8dkjmGSbYE", ephemeral: true);
				}
				else
				{
					await RespondAsync("This guild is not on the whitelist. The command was blocked.", ephemeral: true);
				}
				return;
			}

			//Buy more time to process posts:
			await DeferAsync(false);

			// Get IG account:
			InstagramProcessor instagram = new InstagramProcessor(InstagramProcessor.AccountFinder.GetIGAccount());

			//Create url:
			string url = username;
			if (!Uri.IsWellFormedUriString(username, UriKind.Absolute))
				url = "https://instagram.com/" + username;

			// Process profile:
			InstagramProcessorResponse response = await instagram.PostRouter(url, (int)Context.Guild.PremiumTier, 1);

			// Check for failed post:
			if (!response.success)
			{
				await FollowupAsync(response.error);
				return;
			}
			// If not a profile for some reason, treat otherwise:
			if (!response.onlyAccountData)
			{
				await FollowupAsync("No parece ser un perfil de usuario. Prueba usando `/link` para los posts.");
				return;
			}

			IGEmbedBuilder embed = new IGEmbedBuilder(response, Context.User.Username);
			IGComponentBuilder component = new IGComponentBuilder(response, Context.User.Id);

			await FollowupAsync(embed: embed.AutoSelector(), allowedMentions: AllowedMentions.None, components: component.AutoSelector());
		}
		[SlashCommand("help", "Para ayuda con el bot.", runMode: RunMode.Async)]
		public async Task Help()
		{
			// Check whitelist:
			if (!Whitelist.IsServerOnList(((Context.Guild == null) ? (0) : (Context.Guild.Id))))
			{
				// Self-hosted whitelist notification for official bot:
				if (Context.Client.CurrentUser.Id == 815695225678463017)
				{
					await RespondAsync("This bot is now self-host only. Learn more about this change in the updates channel on the support server: https://discord.gg/8dkjmGSbYE", ephemeral: true);
				}
				else
				{
					await RespondAsync("This guild is not on the whitelist. The command was blocked.", ephemeral: true);
				}
				return;
			}
			//response embed:
			var embed = new EmbedBuilder();
			embed.Title = "Help With Instagram Embed";
			embed.Url = "https://discord.gg/6K3tdsYd6J";
			embed.Description = "This bot uploads videos and images from an Instagram post provided via a link. The bot also allows for subscribing to new posts from accounts using the `/subscribe` command.";
			embed.AddField("Embedding Individual Posts", "To embed the contents of an Instagram url, simply paste the link into the chat and the bot will do the rest (as long as it has permission to).\nYou can also use the `/link` along with a URL.\nFor posts with multiple slides, use the `/link` command along with the optional `Index:` parameter to select the specific slide.\nTo get information about an Instagram account, use `/profile [username]` or `/link` with a link to the profile. These commands will NOT subscribe you to an account or get reoccuring updates from that account. Use `/subscribe` for that.");
			embed.AddField("Subscriptions", "Note: The subscriptions module is currently under beta testing to limited guilds.\nTo subscribe to an account, use `/subscribe` and the users Instagram account to get new posts from that account delivered to the channel where the command is executed.\nTo unsubscribe from an account, use `/unsubscribe` and the username of the Instagram account in the channel that is subscribed to the account. You can also use `/unsubscribeall` to unsubscribe from all Instagram accounts.\nUse `/subscribed` to list all of the Instagram accounts that the guild is subscribed to.");
			embed.AddField("Roles", "Only users with the role `InstagramBotSubscribe` (case sensitive) or guild administrator permission are allowed to unsubscribe and subscribe to accounts.");
			embed.AddField("Permissions", "The following channel permissions are required for the bot's operation:\n" +
				"- `Send Messages`\n" +
				"- `View Channel`\n" +
                "- `Attach Files`\n" +
                "- `Manage Messages` (optional-used to remove duplicate embeds)");
			// Only display on official bot.
			if (Context.Client.CurrentUser.Id == 815695225678463017)
			{
				embed.AddField("Legal", "[Terms of Use](https://github.com/bman46/InstagramEmbedDiscordBot/blob/master/legal/TermsAndConditions.md)\n[Privacy Policy](https://github.com/bman46/InstagramEmbedDiscordBot/blob/master/legal/Privacy.md)");
            }
            else
            {
				embed.AddField("Support", "Please note that this bot is self-hosted. For any support, ask the server owner/mods.");
			}
			embed.WithColor(new Color(131, 58, 180));

			ComponentBuilder component = new ComponentBuilder();

			// Only on official bot:
			if (Context.Client.CurrentUser.Id == 815695225678463017)
			{
				ButtonBuilder button = new ButtonBuilder();
				button.Label = "Support Server";
				button.Style = ButtonStyle.Link;
				button.Url = "https://discord.gg/6K3tdsYd6J";
				component.WithButton(button);
			}

			await RespondAsync(embed: embed.Build(), ephemeral: false, components: component.Build());
		}
		[SlashCommand("github", "Visit our github page", runMode: RunMode.Async)]
		public async Task Github()
		{
			// Only on official bot:
			if (Context.Client.CurrentUser.Id == 815695225678463017)
			{
				//response embed:
				var embed = new Discord.EmbedBuilder();
				embed.Title = "GitHub";
				embed.Url = "https://github.com/bman46/InstagramEmbedDiscordBot";
				embed.Description = "View the source code, download code to host your own version, contribute to the bot, and file issues for improvements or bugs. [Github](https://github.com/bman46/InstagramEmbedDiscordBot)";
				embed.WithColor(new Color(131, 58, 180));

				ButtonBuilder buttonGithub = new ButtonBuilder();
				buttonGithub.Label = "GitHub";
				buttonGithub.Style = ButtonStyle.Link;
				buttonGithub.Url = "https://github.com/bman46/InstagramEmbedDiscordBot";
				ComponentBuilder component = new ComponentBuilder().WithButton(buttonGithub);
			}

			await RespondAsync(embed: embed.Build(), ephemeral: true, components: component.Build());
		}
		[SlashCommand("suscribirse", "Obtener actualizaciones cuando el usuario suba un post nuevo.", runMode: RunMode.Async)]
		[RequireBotPermission(ChannelPermission.SendMessages)]
		[RequireBotPermission(ChannelPermission.AttachFiles)]
		[RequireUserPermission(GuildPermission.Administrator, Group = "UserPerm")]
		[RequireRole("InstagramBotSubscribe", Group = "UserPerm")]
		[RequireContext(ContextType.Guild)]
		public async Task Subscribe([Summary("username", "Nombre de usuario de la cuenta de Instagram a seguir.")]string username)
		{
			// Check whitelist:
			if (!Whitelist.IsServerOnList(((Context.Guild == null) ? (0) : (Context.Guild.Id))))
			{
				// Self-hosted whitelist notification for official bot:
				if (Context.Client.CurrentUser.Id == 815695225678463017)
				{
					await RespondAsync("This bot is now self-host only. Learn more about this change in the updates channel on the support server: https://discord.gg/8dkjmGSbYE", ephemeral: true);
				}
				else
				{
					await RespondAsync("This guild is not on the whitelist. The command was blocked.", ephemeral: true);
				}
				return;
			}

			//Ensure subscriptions are enabled:
			if (!_subscriptions.ModuleEnabled)
			{
				await RespondAsync("Las suscripciones están deshabilitadas.", ephemeral: true);
				return;
			}

			//Buy more time to process posts:
			await DeferAsync(true);

			// Get IG account:
			InstagramProcessor instagram = new InstagramProcessor(InstagramProcessor.AccountFinder.GetIGAccount());

			// Account limits:
			int subcount = await _subscriptions.GuildSubscriptionCountAsync(Context.Guild.Id);
			int maxcount = await _subscriptions.MaxSubscriptionsCountForGuildAsync(Context.Guild.Id);
			if (subcount >= maxcount)
            {
				await FollowupAsync("Ya estás suscrito a "+ subcount +" cuenta de Instagram, tu límite está en "+maxcount+" cuentas. Usa `/desuscribirse` para eliminar algunas cuentas.");
				return;
			}

			long IGID;
            try
            {
				IGID = await instagram.GetUserIDFromUsername(username);
            }
			catch(Exception e)
            {
				//Possibly incorrect username:
				Console.WriteLine("Get username failure: " + e);
				await FollowupAsync("Error al obtener el ID de Instagram. ¿Es el nombre de usuario correcto?");
				return;
            }
            if (!await instagram.AccountIsPublic(IGID))
            {
				await FollowupAsync("La cuenta parece privada y no puede ser visitada por el bot.");
				return;
			}
			//Subscribe:
			try
			{
				await _subscriptions.SubscribeToAccount(IGID, Context.Channel.Id, Context.Guild.Id);
			}catch(ArgumentException e) when (e.Message.Contains("Already subscribed"))
            {
				await FollowupAsync("Ya estás suscrito a esta cuenta.");
				return;
			}
			//Notify:
			await Context.Channel.SendMessageAsync("Este canal se ha suscrito a " + username + " en Instagram por " + Context.User.Mention, allowedMentions: AllowedMentions.None);
			await FollowupAsync("Correcto. Recibirás las actualizaciones en este canal cuando se ejecuten las actualizaciones cada cierto intervalo de tiempo.");
		}
		[SlashCommand("desuscribirse", "Eliminar suscripción de ciertos usuarios.", runMode: RunMode.Async)]
		[RequireUserPermission(GuildPermission.Administrator, Group = "UserPerm")]
		[RequireRole("InstagramBotSubscribe", Group = "UserPerm")]
		[RequireContext(ContextType.Guild)]
		public async Task Unsubscribe()
		{
			//Ensure subscriptions are enabled:
			if (!_subscriptions.ModuleEnabled)
			{
				await RespondAsync("Las suscripciones están deshabilitadas.", ephemeral: true);
				return;
			}

			//Buy more time to process:
			await DeferAsync(false);

			// Get Accounts:
			var subs = await _subscriptions.GuildSubscriptionsAsync(Context.Guild.Id);

			// Create Dropdown with channels:
			var menuBuilder = new SelectMenuBuilder()
				.WithCustomId("unsubscribe")
				.WithPlaceholder("Selecciona la suscripción a eliminar.")
				.WithMinValues(0);

			// Get IG account:
			InstagramProcessor instagram = new InstagramProcessor(InstagramProcessor.AccountFinder.GetIGAccount());

			// Add users to dropdown:
			foreach (FollowedIGUser user in subs)
			{
				foreach (RespondChannel chan in user.SubscribedChannels)
				{
					// Get username:
					string username = await instagram.GetIGUsername(user.InstagramID);
					string channelName = "Unknown";

					// Should channel be deleted or otherwise unknown:
					try
					{
						// Get channel name:
						channelName = Context.Guild.GetChannel(ulong.Parse(chan.ChannelID)).Name;
					}
					catch { }

					// Add account option to menu:
					SelectMenuOptionBuilder optBuilder = new SelectMenuOptionBuilder()
						.WithLabel(username)
						.WithValue(user.InstagramID+"-"+chan.ChannelID)
						.WithDescription(username+" in channel "+ channelName);
					menuBuilder.AddOption(optBuilder);
				}
			}

            // Check for subs:
            if (subs.Length < 1)
            {
				await FollowupAsync("No hay suscripciones.");
				return;
            }

			// Make embed:
			var embed = new EmbedBuilder();
			embed.Title = "Desuscribirse";
			embed.Description = "Selecciona desde el menú desplegable las cuentas de las que quieres desuscribirte.";
			embed.WithColor(new Color(131, 58, 180));

			// Set max count:
			menuBuilder.WithMaxValues(menuBuilder.Options.Count);
			// Component Builder:
			var builder = new ComponentBuilder()
				.WithSelectMenu(menuBuilder)
				.WithButton("Delete Message", $"delete-message-{Context.User.Id}", style: ButtonStyle.Danger);

			// Send message
			await FollowupAsync(embed: embed.Build(), components: builder.Build());
		}
		[SlashCommand("desuscribirall", "Desuscribirse de todas las cuentas de Instagram.", runMode: RunMode.Async)]
		[RequireUserPermission(GuildPermission.Administrator, Group = "UserPerm")]
		[RequireRole("InstagramBotSubscribe", Group = "UserPerm")]
		[RequireContext(ContextType.Guild)]
		public async Task UnsubscribeAll()
        {
			//Ensure subscriptions are enabled:
			if (!_subscriptions.ModuleEnabled)
			{
				await RespondAsync("Las suscripciones están deshabilitadas.", ephemeral: true);
				return;
			}

			//Buy more time to process posts:
			await DeferAsync(false);

			var subs = await _subscriptions.GuildSubscriptionsAsync(Context.Guild.Id);
			int errorCount = 0;
			foreach (FollowedIGUser user in subs)
			{
				foreach (RespondChannel chan in user.SubscribedChannels)
				{
					if (chan.GuildID.Equals(Context.Guild.Id.ToString()))
					{
						try
						{
							await _subscriptions.UnsubscribeToAccount(long.Parse(user.InstagramID), ulong.Parse(chan.ChannelID), Context.Guild.Id);
						}
						catch (Exception e)
						{
							Console.WriteLine(e);
							errorCount++;
						}
					}
				}
			}
            if (errorCount > 0)
            {
				await FollowupAsync("Error al desuscribirse de " + errorCount + " cuenta(s).");
            }
            else
            {
                if (subs.Length == 0)
                {
					await FollowupAsync("No estás suscrito a ninguna cuenta.");
                }
                else
                {
					await FollowupAsync("Correcto. Se ha eliminado la suscripción para todas las cuentas.");
				}
			}
		}
		[SlashCommand("suscripciones", "Lista de cuentas a las que estás suscrito.", runMode: RunMode.Async)]
		[RequireContext(ContextType.Guild)]
		public async Task Subscribed()
		{
			// Check whitelist:
			if (!Whitelist.IsServerOnList(((Context.Guild == null) ? (0) : (Context.Guild.Id))))
			{
				// Self-hosted whitelist notification for official bot:
				if (Context.Client.CurrentUser.Id == 815695225678463017)
				{
					await RespondAsync("This bot is now self-host only. Learn more about this change in the updates channel on the support server: https://discord.gg/8dkjmGSbYE", ephemeral: true);
				}
				else
				{
					await RespondAsync("This guild is not on the whitelist. The command was blocked.", ephemeral: true);
				}
				return;
			}

			//Ensure subscriptions are enabled:
			if (!_subscriptions.ModuleEnabled)
			{
				await RespondAsync("Las suscripciones están deshabilitadas.", ephemeral: true);
				return;
			}

			// buy time:
			await DeferAsync(false);

			// Get IG account:
			InstagramProcessor instagram = new InstagramProcessor(InstagramProcessor.AccountFinder.GetIGAccount());

			List<Embed> embeds = new List<Embed>();

			var embed = new EmbedBuilder();
			embed.Title = "Suscripciones";
			embed.WithColor(new Color(131, 58, 180));

			var subs = await _subscriptions.GuildSubscriptionsAsync(Context.Guild.Id);
			embed.Description = subs.Count() + " de " + await _subscriptions.MaxSubscriptionsCountForGuildAsync(Context.Guild.Id) + " suscripciones usadas.\n**Cuentas de Instagram:**";

			string accountOutput = "";
			string channelOutput = "";
			foreach(FollowedIGUser user in subs)
            {
				foreach(RespondChannel chan in user.SubscribedChannels)
                {
                    if (chan.GuildID.Equals(Context.Guild.Id.ToString()))
                    {
						string chanMention = "No existe el canal.\n";
                        try
                        {
							chanMention = "<#"+Context.Guild.GetChannel(ulong.Parse(chan.ChannelID)).Id+">\n";
						}catch(Exception e)
                        {
							Console.WriteLine(e);
                        }
						string accountMention = "- [" + await instagram.GetIGUsername(user.InstagramID) + "](https://www.instagram.com/" + await instagram.GetIGUsername(user.InstagramID) + ")\n";
						if((accountOutput+ accountMention).Length<=1024 && (channelOutput + chanMention).Length <= 1024)
                        {
							accountOutput += accountMention;
							channelOutput += chanMention;
                        }
                        else
                        {
							embed.AddField("Cuenta", accountOutput, true);
							embed.AddField("Canal", channelOutput, true);
							embeds.Add(embed.Build());

							//Restart new embed:
							embed = new EmbedBuilder();
							embed.WithColor(new Color(131, 58, 180));
							accountOutput = accountMention;
							accountOutput = chanMention;
						}
					}
                }
			}
			if (subs.Length == 0)
            {
				embed.Description = "No estás suscrito a ninguna cuenta. Empieza usando el comando `/suscribirse`";
            }
            else
            {
				embed.AddField("Cuenta", accountOutput, true);
				embed.AddField("Canal", channelOutput, true);
			}
			embeds.Add(embed.Build());
			await FollowupAsync(embeds: embeds.ToArray());
		}
	}
}
