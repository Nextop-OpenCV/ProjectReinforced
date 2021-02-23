﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using ProjectReinforced.Types;
using ProjectReinforced.Others;

namespace ProjectReinforced.Clients
{
    public class ClientManager
    {
        public static LolClient Lol { get; } = new LolClient();
        public static RainbowClient R6 { get; } = new RainbowClient();

        public IGameClient CurrentClient
        {
            get
            {
                foreach (IGameClient client in _clients) if (client.IsRunning && client.IsActive) return client;
                return null;
            }
        }

        public GameType CurrentGame => CurrentClient?.GAME_TYPE ?? GameType.None;

        private static readonly IGameClient[] _clients = {Lol, R6};

        public static async Task Initialize()
        {
            foreach (IGameClient client in _clients)
            {
                if (client.IsRunning)
                {
                    await client.InitializeAsync();
                }
            }
        }

        public static bool IsRunning(string name)
        {
            return Process.GetProcessesByName(name).Length > 0;
        }

        public static bool IsActive(string name)
        {
            IntPtr mwHandle = Win32.FindWindow(null, name);
            IntPtr cwHandle = Win32.GetForegroundWindow();

            bool isActive = mwHandle == cwHandle;

            Win32.CloseHandle(mwHandle);
            Win32.CloseHandle(cwHandle);

            return isActive;
        }

        public static IGameClient GetClient(GameType game)
        {
            foreach (var client in _clients) if (game == client.GAME_TYPE) return client;
            return null;
        }
    }
}