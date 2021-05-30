﻿using HarmonyLib;
using BepInEx.Logging;
using System.Reflection;
using System.Collections.Generic;

namespace RWF
{
    public static class PatchLogger
    {
        public static readonly ManualLogSource logger = Logger.CreateLogSource("RWF::Patches");
        private static readonly Dictionary<string, ManualLogSource> loggers = new Dictionary<string, ManualLogSource>();

        public static ManualLogSource Get(string name) {
            if (!loggers.ContainsKey(name)) {
                loggers.Add(name, Logger.CreateLogSource($"RWF::Patches::{name}"));
            }

            return loggers[name];
        }
    }

    public static class PatchUtils
    {
        public static void ApplyPatches() {
            var harmony = new Harmony("io.olavim.plugins.rounds.teams");
            harmony.PatchAll(Assembly.GetExecutingAssembly());

            PatchLogger.logger.LogInfo("initialized");
        }

        public static int FindAvailablePlayerID() {
            for (int i = 0; i < RWFMod.instance.MaxPlayers; i++) {
                var player = PlayerManager.instance.players.Find(p => p.playerID == i);
                if (player == null) {
                    return i;
                }
            }

            return -1;
        }
    }
}
