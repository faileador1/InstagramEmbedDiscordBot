﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Discord;
using Discord.WebSocket;
using Instagram_Reels_Bot.DataTables;
using Instagram_Reels_Bot.Helpers;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Instagram_Reels_Bot.Services
{
    /// <summary>
    /// Allows users to get the lastest posts from an account
    /// </summary>
	public class Subscriptions
    {
        private readonly IConfiguration _config;
        private readonly IServiceProvider services;
        private System.Timers.Timer UpdateTimer;
        private readonly DiscordShardedClient _client;
        //CosmosDB:
        private static string EndpointUri;
        private static string PrimaryKey;
        // The Cosmos db client instance
        private CosmosClient CosmosClient;
        // Add the Database:
        private Database Database;
        // Followed Accounts Container
        private Container FollowedAccountsContainer;

        /// <summary>
        /// Initialize sub
        /// </summary>
        /// <param name="services"></param>
        public Subscriptions(DiscordShardedClient client, IConfiguration config)
        {
            // Dependancy injection:
            _config = config;
            _client = client;

            //Dont set database locations unless AllowSubscriptions is true:
            if (config["AllowSubscriptions"].ToLower() != "true")
            {
                Console.WriteLine("Subscriptions not allowed.");
                return;
            }

            //Set cosmos DB info:
            EndpointUri = config["EndpointUrl"];
            PrimaryKey = config["PrimaryKey"];
        }
        /// <summary>
        /// Subscribe a channel to an Instagram account.
        /// TODO: check count per guild.
        /// </summary>
        /// <param name="instagramID"></param>
        /// <param name="channelID"></param>
        /// <param name="guildID"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public async Task SubscribeToAccount(long instagramID, ulong channelID, ulong guildID)
        {
            FollowedIGUser databaseValue;
            try
            {
                ItemResponse<FollowedIGUser> response = await this.FollowedAccountsContainer.ReadItemAsync<FollowedIGUser>(instagramID.ToString(), new PartitionKey(instagramID));
                databaseValue = response.Resource;
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                databaseValue = null;
            }
            //Create new Entry:
            if (databaseValue == null)
            {
                List<RespondChannel> chans = new List<RespondChannel>();
                chans.Add(new RespondChannel(guildID, channelID));
                databaseValue = new FollowedIGUser
                {
                    InstagramID = instagramID.ToString(),
                    SubscribedChannels = chans
                };
                //Create the Item:
                await this.FollowedAccountsContainer.CreateItemAsync<FollowedIGUser>(databaseValue, new PartitionKey(databaseValue.InstagramID));
            }
            else
            {
                foreach(RespondChannel chan in databaseValue.SubscribedChannels)
                {
                    if(ulong.Parse(chan.ChannelID) == channelID)
                    {
                        throw new Exception("Already subscribed");
                    }
                }
                databaseValue.SubscribedChannels.Add(new RespondChannel(guildID, channelID));
                await this.FollowedAccountsContainer.UpsertItemAsync<FollowedIGUser>(databaseValue, new PartitionKey(databaseValue.InstagramID));
            }
        }
        /// <summary>
        /// Starts the subscription tasks.
        /// </summary>
        /// <returns></returns>
        public async Task InitializeAsync()
        {
            Console.WriteLine("Starting the subscription task...");
            if(string.IsNullOrEmpty(PrimaryKey)|| string.IsNullOrEmpty(EndpointUri))
            {
                Console.WriteLine("Databases not setup.");
                return;
            }
            //Connect to Database:
            this.CosmosClient = new CosmosClient(EndpointUri, PrimaryKey);

            //link and create the database if it is missing:
            this.Database = await this.CosmosClient.CreateDatabaseIfNotExistsAsync("InstagramEmbedDatabase");
            this.FollowedAccountsContainer = await this.Database.CreateContainerIfNotExistsAsync("FollowedAccounts", "/id");

            // Timer:
            UpdateTimer = new System.Timers.Timer(3600000.0 * double.Parse(_config["HoursToCheckForNewContent"])); //one hour in milliseconds
            UpdateTimer.Elapsed += new ElapsedEventHandler(GetLatestsPosts);
            UpdateTimer.Start();
        }
        /// <summary>
        /// Main loop to parse through all subscribed accounts and upload their contents.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void GetLatestsPosts(object sender, System.Timers.ElapsedEventArgs e)
        {
            await GetLatestsPosts();
        }
        public async Task GetLatestsPosts()
        {
            Console.WriteLine("Getting new posts!");
            using (FeedIterator<FollowedIGUser> dbfeed = FollowedAccountsContainer.GetItemQueryIterator<FollowedIGUser>()) {
                while (dbfeed.HasMoreResults)
                {
                    foreach (var igAccount in await dbfeed.ReadNextAsync())
                    {
                        Console.WriteLine("Checking " + igAccount.InstagramID);
                        //Set last check as now:
                        igAccount.LastCheckTime = DateTime.Now;
                        var newIGPosts = await InstagramProcessor.PostsSinceDate(long.Parse(igAccount.InstagramID), igAccount.LastPostDate);
                        if (newIGPosts.Length>0 && newIGPosts[newIGPosts.Length - 1].success)
                        {
                            //Set the most recent posts date:
                            igAccount.LastPostDate = newIGPosts[newIGPosts.Length-1].postDate;
                        }
                        foreach(InstagramProcessorResponse response in newIGPosts)
                        {
                            if (!response.success)
                            {
                                //Failed to process post:
                                Console.WriteLine("Failed to process post.");
                                return;
                            }
                            else if (response.isVideo)
                            {
                                if (response.stream != null)
                                {
                                    //Response with stream:
                                    using (Stream stream = new MemoryStream(response.stream))
                                    {
                                        FileAttachment attachment = new FileAttachment(stream, "IGMedia.mp4", "An Instagram Video.");
                                        foreach (RespondChannel subbedGuild in igAccount.SubscribedChannels)
                                        {
                                            // get channel:
                                            IMessageChannel chan = null;
                                            try
                                            {
                                                chan = _client.GetChannel(ulong.Parse(subbedGuild.ChannelID)) as IMessageChannel;
                                            }
                                            catch (Exception e)
                                            {
                                                Console.WriteLine("Cannot find channel. Removing from DB.");
                                                igAccount.SubscribedChannels.Remove(subbedGuild);
                                            }
                                            if (chan != null)
                                            {
                                                //send message
                                                await chan.SendFileAsync(attachment, "New post!");
                                            }
                                        }
                                    }
                                }
                                else
                                {
                                    //Response without stream:
                                    foreach (RespondChannel subbedGuild in igAccount.SubscribedChannels)
                                    {
                                        // get channel:
                                        IMessageChannel chan = null;
                                        try
                                        {
                                            chan = _client.GetChannel(ulong.Parse(subbedGuild.ChannelID)) as IMessageChannel;
                                        }
                                        catch (Exception e)
                                        {
                                            Console.WriteLine("Cannot find channel. Removing from DB.");
                                            igAccount.SubscribedChannels.Remove(subbedGuild);
                                        }
                                        if (chan != null)
                                        {
                                            //send message
                                            await chan.SendMessageAsync("New post! " + response.contentURL);
                                        }
                                    }
                                }

                            }
                            else
                            {
                                //Account Name:
                                var account = new EmbedAuthorBuilder();
                                account.IconUrl = response.iconURL.ToString();
                                account.Name = response.accountName;
                                account.Url = response.postURL.ToString();

                                //Instagram Footer:
                                EmbedFooterBuilder footer = new EmbedFooterBuilder();
                                footer.IconUrl = "https://upload.wikimedia.org/wikipedia/commons/a/a5/Instagram_icon.png";
                                footer.Text = "Instagram";

                                var embed = new EmbedBuilder();
                                embed.Author = account;
                                embed.Footer = footer;
                                embed.Timestamp = new DateTimeOffset(response.postDate);
                                embed.Url = response.postURL.ToString();
                                embed.Description = (response.caption != null) ? (DiscordTools.Truncate(response.caption)) : ("");
                                embed.ImageUrl = "attachment://IGMedia.jpg";
                                embed.WithColor(new Color(131, 58, 180));
                                if (response.stream != null)
                                {
                                    using (Stream stream = new MemoryStream(response.stream))
                                    {
                                        FileAttachment attachment = new FileAttachment(stream, "IGMedia.jpg", "An Instagram Image.");
                                        foreach (RespondChannel subbedGuild in igAccount.SubscribedChannels)
                                        {
                                            // get channel:
                                            IMessageChannel chan = null;
                                            try
                                            {
                                                Console.WriteLine(subbedGuild.ChannelID);
                                                chan = _client.GetChannel(ulong.Parse(subbedGuild.ChannelID)) as IMessageChannel;
                                            }
                                            catch (Exception e)
                                            {
                                                Console.WriteLine("Cannot find channel. Removing from DB.");
                                                igAccount.SubscribedChannels.Remove(subbedGuild);
                                            }
                                            if (chan != null)
                                            {
                                                //send message
                                                await chan.SendFileAsync(attachment, embed: embed.Build());
                                            }
                                        }
                                    }
                                }
                                else
                                {
                                    embed.ImageUrl = response.contentURL.ToString();
                                    foreach (RespondChannel subbedGuild in igAccount.SubscribedChannels)
                                    {
                                        // get channel:
                                        IMessageChannel chan = null;
                                        try
                                        {
                                            chan = _client.GetChannel(ulong.Parse(subbedGuild.ChannelID)) as IMessageChannel;
                                        }
                                        catch (Exception e)
                                        {
                                            Console.WriteLine("Cannot find channel. Removing from DB.");
                                            igAccount.SubscribedChannels.Remove(subbedGuild);
                                        }
                                        if (chan != null)
                                        {
                                            //send message
                                            await chan.SendMessageAsync(embed: embed.Build());
                                        }
                                    }
                                }
                            }
                        }
                        // Delete if empty:
                        if (igAccount.SubscribedChannels.Count == 0)
                        {
                            await this.FollowedAccountsContainer.DeleteItemAsync<FollowedIGUser>(igAccount.InstagramID, new PartitionKey(igAccount.InstagramID));
                        }
                        else
                        {
                            //Update database:
                            await this.FollowedAccountsContainer.UpsertItemAsync<FollowedIGUser>(igAccount, new PartitionKey(igAccount.InstagramID));
                        }
                        //Wait to prevent spamming IG api:
                        Console.WriteLine("Complete. Onto the next post after sleep.");
                        Thread.Sleep(2000);
                    }
                }
            }
            Console.WriteLine("Done.");
        }
    }
}