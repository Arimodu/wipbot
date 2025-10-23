using System;
using System.Collections.Generic;

namespace wipbot.Utils
{
    internal class ConfigMigrator
    {
        private static readonly List<Action<WBConfig>> Migrations = [
            // Migration from version 0 to 1
            // You can put a reason here ;)
            (cfg) =>
            {
                cfg.ButtonPositionX = 151;
                cfg.ButtonPositionY = -23;
                cfg.ButtonFontSize = 3;
                cfg.ButtonPrefWidth = 13;
                cfg.ButtonPrefHeight = 7;
            },

            // Migration from version 1 to 2
            // Add support for vivify
            (cfg) =>
            {
                if (!cfg.FileExtensionWhitelist.Contains("vivify"))
                {
                    cfg.FileExtensionWhitelist.Add("vivify");
                }
            },

            // Migration from version 2 to 3
            // Reposition button... (?)
            (cfg) =>
            {
                cfg.ButtonPositionX = 138;
                cfg.ButtonPositionY = -4;
                cfg.ButtonPrefWidth = 11;
                cfg.ButtonPrefHeight = 6;
            },

            // Migration from version 3 to 4
            // Update URL
            (cfg) =>
            {
                var index = cfg.RequestCodePrefixDownloadUrlPairs.FindIndex(x => x.Equals("http://catse.net/wips/%s.zip"));
                if (index != -1)
                {
                    cfg.RequestCodePrefixDownloadUrlPairs[index] = "https://wipbot.com/wips/%s.zip";
                }
                cfg.MessageHelp = cfg.MessageHelp.Replace("http://catse.net/wip", "https://wipbot.com");
            }
        ];

        public static WBConfig MigrateConfig(WBConfig cfg)
        {
            int currentVersion = cfg.ConfigVersion;
            int targetVersion = Migrations.Count; // Latest version is the number of migrations

            for (int i = currentVersion; i < targetVersion; i++)
            {
                try
                {
                    Migrations[i].Invoke(cfg);
                }
                catch (Exception e)
                {
                    // No logger here and I want to keep this static, this will be redirected to the log file either way
                    Console.WriteLine($"Failed to apply config migration {i}");
                    Console.WriteLine(e);
                }
                finally
                {
                    cfg.ConfigVersion = i + 1;
                }
            }

            return cfg;
        }
    }
}
