﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Discord;

namespace DiscordBot
{
    class GamesSync
    {
        const string TblName = "GAMESYNCSERVERS";

        public Server[] SyncingServers;

        private Timer timer;

        public void Init()
        {
            DefineServers();

            SetUpTimer(new TimeSpan(0, 1, 0));
            //CheckServers(); // Yes or no?
        }

        private void DefineServers()
        {
            ulong[] serverIds = GetAllSyncingServerIds();
            List<Server> servers = new List<Server>();
            foreach (ulong serverId in serverIds)
            {
                servers.Add(Bot.Client.GetServer(serverId));
                //Channel mainChannel = Bot.Client.GetChannel(serverId);
                //servers.Add(mainChannel.Server);
            }
            SyncingServers = servers.ToArray();
        }

        #region TimeStuff

        private void SetUpTimer(TimeSpan timeToGo)
        {
            if (timeToGo < TimeSpan.Zero)
            {
                Bot.NotifyDevs("Unhandled exception(?) in SetUpTimer()\r\n" + timeToGo.ToString());
                return; //time already passed
            }
            timer = new System.Threading.Timer(x =>
            {
                CheckServers();
            }, null, timeToGo, Timeout.InfiniteTimeSpan);
        }

        #endregion TimeStuff

        #region Methods

        public void CheckServers()
        {
            DefineServers();
            foreach (Server server in SyncingServers)
            {
                if(server != null)
                    CheckUsersOfServer(server);
            }
            SetUpTimer(new TimeSpan(0, 1, 0));
        }

        private void CheckUsersOfServer(Server server)
        {
            var currentPlayingUsers = server.Users
                .Where(r =>
                r.IsBot.Equals(false))
                .Where(r =>
                r.Status.Value.Equals("online") ||
                r.Status.Value.Equals("idle") ||
                r.Status.Value.Equals("dnd") ||
                r.Status.Value.Equals("invisible"))
                .Where(r =>
                r.CurrentGame.HasValue);
            if (currentPlayingUsers != null && currentPlayingUsers.Count() == 0)
                return;

            string[] games = GetAvailableGames();

            string[] restrictedGames = GetRestrictedGames();

            List<string> currentGames = new List<string>();
            foreach (User user in currentPlayingUsers)
            {
                currentGames.Add(user.CurrentGame.Value.Name);
            }

            games = UpdateGames(currentGames.ToArray(), games);

            foreach (User currentPlayingUser in currentPlayingUsers)
            {
                if (!restrictedGames.Contains(currentPlayingUser.CurrentGame.Value.Name)) // Prevent user is 'Playing Admin'
                    CompareRolesForUserAndAssign(currentPlayingUser, games);
            }
        }

        private string[] UpdateGames(string[] currentGames, string[] games)
        {
            foreach (string game in currentGames)
            {
                if (!games.Contains(game))
                {
                    AddGame(game, false);
                }
            }
            return GetAvailableGames();
        }

        private void CompareRolesForUserAndAssign(User user, string[] games)
        {
            string currentGame = user.CurrentGame.Value.Name;
            if (games.Contains(currentGame))
            {
                var gameRoles = user.Server.FindRoles(currentGame, true);
                if (gameRoles.Count() == 1 && !user.HasRole(gameRoles.First()))
                {
                    // Role in server and user does not have it
                    // user.AddRoles(new Role[] { gameRole.First() });
                    TryAssignRoleToUser(user, gameRoles.First());
                }
                else
                {
                    if (gameRoles.Count() > 1)
                    {
                        Bot.NotifyDevs("One or more roles found for **" + currentGame + "** in Server **" + user.Server.Name + "** for user **" + user.Name + "**");
                    }
                    return; // Role isn't in server
                }
            }
        }

        private bool TryAssignRoleToUser(User user, Role role)
        {
            try
            {
                user.AddRoles(new Role[] { role });
                Bot.NotifyDevs("Added Role **" + role.Name + "** to user **" + user.Name + "** on server **" + user.Server.Name + "**");
                return true;
            }
            catch (Exception e)
            {
                Bot.NotifyDevs(Supporter.BuildExceptionMessage(e, this.GetType().FullName));
                return false;
            }
        }

        private string[] GetAvailableGames()
        {
            var gamesEnum = Jenkins.Database.Tables["GAMES"].AsEnumerable().Where(r =>
                r.Field<bool>("ISRESTRICTED").Equals(false));
            List<string> gamesList = new List<string>();
            foreach (var game in gamesEnum)
            {
                gamesList.Add(game.Field<string>("NAME"));
            }
            return gamesList.ToArray();
        }

        private string[] GetRestrictedGames()
        {
            var gamesEnum = Jenkins.Database.Tables["GAMES"].AsEnumerable().Where(r =>
                r.Field<bool>("ISRESTRICTED").Equals(true));
            List<string> gamesList = new List<string>();
            foreach (var game in gamesEnum)
            {
                gamesList.Add(game.Field<string>("NAME"));
            }
            return gamesList.ToArray();
        }

        public ulong[] GetAllSyncingServerIds()
        {
            var servers = Jenkins.Database.Tables[TblName].AsEnumerable();
            List<ulong> serverId = new List<ulong>();
            foreach (var server in servers)
            {
                serverId.Add(server.Field<ulong>("SERVERID"));
            }
            return serverId.ToArray();
        }

        public string[] GetAllSyncingServerNames()
        {
            List<string> serverNames = new List<string>();
            foreach (ulong serverId in GetAllSyncingServerIds())
            {
                serverNames.Add(Bot.Client.GetServer(serverId).Name);
            }
            return serverNames.ToArray();
        }

        public void AddServer(ulong id)
        {
            Server server;
            if (Supporter.TryGetServerById(id, out server))
            {
                Jenkins.Database.Tables[TblName].Rows.Add(server.Id);
                Jenkins.Write();
            }
        }

        public void DelServer(ulong id)
        {
            var serversTable = Jenkins.Database.Tables[TblName].AsEnumerable();
            Server server;
            if (Supporter.TryGetServerById(id, out server))
            {
                serversTable.Where(r => r.Field<ulong>("SERVERID").Equals(id)).First().Delete();
                Jenkins.Write();
            }
        }

        public void AddGame(string name, bool isRestricted)
        {
            if (!Jenkins.Database.Tables["GAMES"].Rows.Contains(name))
            {
                Jenkins.Database.Tables["GAMES"].Rows.Add(name, isRestricted);
                Jenkins.Write();
            }
        }

        public void DelGame(string name)
        {
            var gamesTable = Jenkins.Database.Tables["GAMES"].AsEnumerable();
            gamesTable.Where(r => r.Field<ulong>("NAME").Equals(name)).First().Delete();
            Jenkins.Write();
        }

        public bool IsServerInSync(ulong serverId)
        {
            var serversTable = Jenkins.Database.Tables[TblName].AsEnumerable();
            var servers = serversTable.Where(r => r.Field<ulong>("SERVERID").Equals(serverId));
            return (servers != null && servers.Count() >= 1);
        }

        #endregion Methods
    }
}