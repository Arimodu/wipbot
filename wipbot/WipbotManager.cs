using IPA;
using IPA.Utilities;
using ModestTree;
using Newtonsoft.Json;
using SiraUtil.Logging;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using wipbot.Interfaces;
using wipbot.Models;
using wipbot.UI;
using wipbot.Utils;
using Zenject;

namespace wipbot
{
    internal class WipbotManager : IInitializable, IDisposable
    {
        [Inject] private readonly UnityMainThreadDispatcher mainThreadDispatcher;
        [Inject] private readonly LevelSelectionNavigationController selectionController;
        [Inject] private readonly LevelCollectionNavigationController navigationController;
        [Inject] private readonly LevelSearchViewController searchController;
        [Inject] private readonly WipbotButtonController WipbotButtonController;
        [Inject] private readonly WBConfig Config;
        [Inject] private readonly SiraLog Logger;
        [Inject] private readonly IChatIntegration ChatIntegration;

        private readonly ExtendedQueue<QueueItem> WipQueue = new ExtendedQueue<QueueItem>();
        private readonly BlockingCollection<QueueItem> DownloadQueue = new BlockingCollection<QueueItem>();
        private Thread DownloadThread;

        private bool IsDownloading = false;

        public void Initialize()
        {
            WipbotButtonController.OnWipButtonPressed += OnWipButtonPressed;
            ChatIntegration.OnMessageReceived += OnMessageReceived;
            RenameOldSongFolders();
        }

        public void OnMessageReceived(ChatMessage ChatMessage)
        {
            Logger.Debug($"Chat message received: {ChatMessage.UserName}: {ChatMessage.Content}");
            try
            {
                string[] msgSplit = ChatMessage.Content.Split(' ');
                if (msgSplit[0].ToLower().StartsWith(Config.CommandRequestWip))
                {
                    int requestLimit =
                        ChatMessage.IsBroadcaster ? 99 :
                        ChatMessage.IsModerator ? Config.QueueLimits.Moderator :
                        ChatMessage.IsVip ? Config.QueueLimits.Vip :
                        ChatMessage.IsSubscriber ? Config.QueueLimits.Subscriber :
                        Config.QueueLimits.User;
                    int requestCount = WipQueue.Count(item => item.UserName == ChatMessage.UserName);
                    Logger.Debug($"Request limit: {requestLimit}, request count: {requestCount}");
                    if (msgSplit.Length > 1 && msgSplit[1].ToLower() == Config.KeywordUndoRequest)
                    {
                        WipQueue.Remove(WipQueue.Where(x => x.UserName == ChatMessage.UserName).FirstOrDefault());
                        WipbotButtonController.UpdateButtonState(WipQueue.ToArray()); // Updating the button state would actually be helpful - rip another 3 hours :(
                        Logger.Debug($"Removed request from {ChatMessage.UserName}");
                    }
                    else if (requestLimit == 0)
                    {
                        ChatIntegration.SendChatMessage(Config.ErrorMessageNoPermission);
                        Logger.Debug($"No permission to request from {ChatMessage.UserName}");
                    }
                    else if (requestCount >= requestLimit)
                    {
                        ChatIntegration.SendChatMessage(Config.ErrorMessageUserMaxRequests);
                        Logger.Debug($"User {ChatMessage.UserName} has reached their request limit");
                    }
                    else if (WipQueue.Count >= Config.QueueSize)
                    {
                        ChatIntegration.SendChatMessage(Config.ErrorMessageQueueFull);
                        Logger.Debug($"Queue is full");
                    }
                    else if (msgSplit.Length > 1 && msgSplit[1] == "***")
                    {
                        ChatIntegration.SendChatMessage(Config.ErrorMessageLinkBlocked);
                        Logger.Debug($"Link blocked");
                    }
                    // Note: Removed length checking because it seems there is a bug in ChatPlex where its first message after scene change appends some garbage at the end separated by a space
                    else if ((msgSplit[1].All(Config.RequestCodeCharacterWhitelist.Contains) == false && Config.UrlWhitelist.Any(msgSplit[1].Contains) == false))
                    {
                        ChatIntegration.SendChatMessage(Config.MessageInvalidRequest);
                        Logger.Debug($"Invalid request from {ChatMessage.UserName}");
                    }
                    else
                    {
                        string wipUrl = msgSplit[1];

                        if (msgSplit[1].IndexOf(".") == -1)
                        {
                            wipUrl = "";
                            for (int i = 0; i < Config.RequestCodePrefixDownloadUrlPairs.Count; i += 2)
                            {
                                if (msgSplit[1].StartsWith(Config.RequestCodePrefixDownloadUrlPairs[i]))
                                {
                                    wipUrl = Config.RequestCodePrefixDownloadUrlPairs[i + 1].Replace("%s", msgSplit[1]);
                                    break;
                                }
                            }
                        }

                        for (int i = 0; i < Config.UrlFindReplace.Count; i += 2)
                            wipUrl = wipUrl.Replace(Config.UrlFindReplace[i], Config.UrlFindReplace[i + 1]);

                        WipQueue.Enqueue(new QueueItem() { UserName = ChatMessage.UserName, DownloadUrl = wipUrl });
                        ChatIntegration.SendChatMessage(Config.MessageWipRequested);
                        WipbotButtonController.UpdateButtonState(WipQueue.ToArray());
                    }
                }
            }
            catch (Exception e)
            {
                ChatIntegration.SendChatMessage(Config.ErrorMessageOther.Replace("%s", e.Message));
                Logger.Error(e);
            }
        }

        private void OnWipButtonPressed()
        {
            if (IsDownloading)
            {
                IsDownloading = false;
                DownloadThread.Abort();
                return;
            }

            DownloadQueue.Add(WipQueue.Dequeue());
            if (DownloadThread == null || !DownloadThread.IsAlive)
            {
                DownloadThread = new Thread(DownloadThreadLoop)
                {
                    IsBackground = true
                };
                DownloadThread.Start();

                // Make sure that we dont leave threads behind after a game exit
                // Make sure that we are not subscribed to the event multiple times
                Application.quitting -= Application_quitting;
                Application.quitting += Application_quitting;
            }
        }

        private void Application_quitting() => DownloadThread.Abort();

        private async void DownloadThreadLoop()
        {
            try
            {
                foreach (var item in DownloadQueue.GetConsumingEnumerable())
                {
                    IsDownloading = true;
                    await DownloadAndExtractZipAsync(item.DownloadUrl, "UserData\\wipbot", Path.Combine(UnityGame.InstallPath, Config.WipFolder));
                }
            }
            catch (ThreadAbortException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }
            finally
            {
                IsDownloading = false;
            }
        }

        private async Task DownloadAndExtractZipAsync(string url, string downloadFolder, string extractFolder)
        {
            string tempFolderName = "wipbot_" + Convert.ToString(DateTimeOffset.Now.ToUnixTimeSeconds(), 16);
            string wipFolderPath = "";
            try
            {
                WipbotButtonController.BlueButtonText = "skip (0%)";
                ChatIntegration.SendChatMessage(Config.MessageDownloadStarted);
                WebClient webClient = new();
                webClient.DownloadProgressChanged += (s, e) =>
                {
                    WipbotButtonController.BlueButtonText = "skip (" + e.ProgressPercentage + "%)";
                };
                webClient.Headers.Add(HttpRequestHeader.UserAgent, "Beat Saber wipbot v1.14.0");

                if (!Directory.Exists(downloadFolder)) Directory.CreateDirectory(downloadFolder);
                await webClient.DownloadFileTaskAsync(new Uri(url), downloadFolder + "\\wipbot_tmp.zip");

                if (Directory.Exists(Path.Combine(extractFolder, tempFolderName))) Directory.Delete(Path.Combine(extractFolder, tempFolderName), true);
                Directory.CreateDirectory(Path.Combine(extractFolder, tempFolderName));

                try
                {
                    using (ZipArchive archive = ZipFile.OpenRead(downloadFolder + "\\wipbot_tmp.zip"))
                    {
                        if (archive.Entries.Count > Config.ZipMaxEntries)
                        {
                            ChatIntegration.SendChatMessage(Config.ErrorMessageTooManyEntries.Replace("%i", "" + Config.ZipMaxEntries));
                            return;
                        }

                        // We can do this here because we dont need to extract the zip to check this
                        if (!archive.Entries.Any(entry => Path.GetFileName(entry.FullName) == "Info.dat" ))
                        {
                            ChatIntegration.SendChatMessage(Config.ErrorMessageMissingInfoDat);
                            return;
                        }

                        // If an entry is a folder, dont extract it, could be a zip bomb
                        if (archive.Entries.Any(entry => entry.FullName.EndsWith("/")))
                        {
                            ChatIntegration.SendChatMessage(Config.ErrorMessageZipContainsSubfolders);
                            return;
                        }

                        if (archive.Entries.Sum(entry => entry.Length) > (Config.ZipMaxUncompressedSizeMB * 1000000))
                        {
                            ChatIntegration.SendChatMessage(Config.ErrorMessageMaxLength.Replace("%i", "" + Config.ZipMaxUncompressedSizeMB));
                            return;
                        }

                        if (archive.Entries.All(entry => Config.FileExtensionWhitelist.Contains(Path.GetExtension(entry.FullName).Remove(0, 1))))
                        {
                            archive.ExtractToDirectory(Path.Combine(extractFolder, tempFolderName));
                        }
                        else
                        {
                            int badFileTypesFound = 0;

                            foreach (ZipArchiveEntry entry in archive.Entries)
                            {
                                if (Config.FileExtensionWhitelist.Contains(Path.GetExtension(entry.FullName).Remove(0, 1))) entry.ExtractToFile(Path.Combine(extractFolder, tempFolderName, entry.FullName));
                                else badFileTypesFound++;
                            }

                            if (badFileTypesFound > 0)
                                ChatIntegration.SendChatMessage(Config.ErrorMessageBadExtension.Replace("%i", "" + badFileTypesFound));
                        }
                    }

                    // Rename the folder to the song name and date
                    var songDat = JsonConvert.DeserializeObject<InfoDat>(File.ReadAllText(Path.Combine(extractFolder, tempFolderName, "Info.dat")));
                    wipFolderPath = Path.Combine(extractFolder, GetFolderName(songDat, DateTimeOffset.Now));
                    Logger.Info($"Renaming {Path.Combine(extractFolder, tempFolderName)} to {wipFolderPath}");
                    Directory.Move(Path.Combine(extractFolder, tempFolderName), wipFolderPath);
                }
                catch (ThreadAbortException)
                {
                    throw; // Rethrow so we can catch it in the thread loop
                }
                catch (Exception e)
                {
                    ChatIntegration.SendChatMessage(Config.ErrorMessageExtractionFailed);
                    Logger.Error(e);
                    return;
                }
                finally
                {
                    File.Delete(downloadFolder + "\\wipbot_tmp.zip");
                    if (Directory.Exists(Path.Combine(extractFolder, tempFolderName))) Directory.Delete(Path.Combine(extractFolder, tempFolderName), true);
                }

                SongCore.Loader.Instance.RefreshSongs(false);
                SongCore.Loader.OnLevelPacksRefreshed += OnLevelsRefreshed;

                void OnLevelsRefreshed()
                {
                    SongCore.Loader.OnLevelPacksRefreshed -= OnLevelsRefreshed;

                    searchController.ResetFilter(false);
                    ForceSelectSongViewCategory(SelectLevelCategoryViewController.LevelCategory.All);

                    // Note to the unfortunate soul that might think this could be improved...
                    // No, it *has* to be like this, because navigationController will not properly select a song if its not a direct reference to the CustomLevelsRepository
                    // And no, songcore doesnt give you the reference, because it doesnt work, trust me, I spent 4 hours figuring this out
                    var (hash, _) = SongCore.Loader.LoadCustomLevel(wipFolderPath).Value;
                    var beatmapPack = SongCore.Loader.CustomLevelsRepository.beatmapLevelPacks.FirstOrDefault(levelPack => levelPack.AllBeatmapLevels().Any(level => level.levelID.Contains(hash)));
                    mainThreadDispatcher.EnqueueWithDelay(() => navigationController.SelectLevel(beatmapPack.AllBeatmapLevels().First(x => x.levelID.Contains(hash))), 500);
                }

                ChatIntegration.SendChatMessage(Config.MessageDownloadSuccess);
            }
            catch (ThreadAbortException)
            {
                throw; // Rethrow so we can catch it in the thread loop
            }
            catch (Exception e)
            {
                if (e is WebException)
                    ChatIntegration.SendChatMessage(Config.ErrorMessageDownloadFailed);
                else if (e is ThreadAbortException)
                    ChatIntegration.SendChatMessage(Config.MessageDownloadCancelled);
                else
                    ChatIntegration.SendChatMessage(Config.ErrorMessageOther.Replace("%s", e.Message));
                Logger.Error(e);
            }
            finally
            {
                WipbotButtonController.UpdateButtonState(WipQueue.ToArray());
            }
        }

        private void ForceSelectSongViewCategory(SelectLevelCategoryViewController.LevelCategory levelCategory)
        {
            try
            {
                var navController = selectionController._levelFilteringNavigationController;

                if (navController.selectedLevelCategory != levelCategory)
                {
                    var categorySelector = selectionController._levelFilteringNavigationController._selectLevelCategoryViewController;
                    if (categorySelector != null)
                    {
                        var iconSegmentControl = categorySelector._levelFilterCategoryIconSegmentedControl;
                        var categoryInfos = categorySelector._levelCategoryInfos;
                        var index = categoryInfos.Select(x => x.levelCategory).ToArray().IndexOf(levelCategory);

                        iconSegmentControl.SelectCellWithNumber(index);
                        categorySelector.LevelFilterCategoryIconSegmentedControlDidSelectCell(iconSegmentControl, index);
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Error("Failed to force category selection");
                Logger.Critical(e);
            }
        }

        private static string GetFolderName(InfoDat songDat, DateTimeOffset dateTime)
        {
            var sb = new StringBuilder();
            sb.Append("wipbot_(");
            if (!string.IsNullOrEmpty(songDat.SongName)) sb.Append(songDat.SongName).Append(" - ");
            if (!string.IsNullOrEmpty(songDat.SongSubName)) sb.Append(songDat.SongSubName).Append(" - ");
            if (!string.IsNullOrEmpty(songDat.SongAuthorName)) sb.Append(songDat.SongAuthorName).Append(" - ");
            if (!string.IsNullOrEmpty(songDat.LevelAuthorName)) sb.Append(songDat.LevelAuthorName).Append(" - ");
            sb.Remove(sb.Length - 3, 3).Append(")_").Append($"({dateTime:MMM dd, yyyy - HH:mm:ss})");
            var newFolderName = sb.ToString();
            return SanitizePath(newFolderName);
        }

        private static string SanitizePath(string path)
        {
            string invalidChars = new string(System.IO.Path.GetInvalidFileNameChars()) + new string(System.IO.Path.GetInvalidPathChars());
            foreach (char c in invalidChars)
            {
                path = path.Replace(c.ToString(), "_");
            }
            return path;
        }

        internal void RenameOldSongFolders()
        {
            try
            {
                int renamedFolders = 0;
                var wipDirectory = Path.Combine(Environment.CurrentDirectory, "Beat Saber_Data", "CustomWIPLevels");
                Directory.GetDirectories(wipDirectory).ToList().ForEach(wipFolder =>
                {
                    if (Path.GetFileName(wipFolder).StartsWith("wipbot_") && !Path.GetFileName(wipFolder).StartsWith("wipbot_("))
                    {
                        var infoDat = JsonConvert.DeserializeObject<InfoDat>(File.ReadAllText(Path.Combine(wipFolder, "info.dat")));
                        var newFolderPath = Path.Combine(wipDirectory, GetFolderName(infoDat, DateTimeOffset.FromUnixTimeSeconds(Convert.ToInt64(Path.GetFileName(wipFolder).Remove(0, 7), 16))));
                        Directory.Move(wipFolder, newFolderPath);
                        renamedFolders++;
                        Logger.Info($"Renamed {Path.GetFileName(wipFolder)} to {Path.GetFileName(newFolderPath)}");
                    }
                });

                if (renamedFolders > 0)
                {
                    Logger.Info($"Renamed {renamedFolders} old wip song folders to new schema");

                    // Trigger a full song reload if legacy named maps are renamed
                    SongCore.Loader.OnLevelPacksRefreshed += RefreshSongs;
                    void RefreshSongs()
                    {
                        SongCore.Loader.OnLevelPacksRefreshed -= RefreshSongs;
                        SongCore.Loader.Instance.RefreshSongs(true);
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }
        }

        [OnExit]
        public void Dispose()
        {
            Logger.Info("Disposing WipbotManager...");
            DownloadThread?.Abort();
            Application.quitting -= Application_quitting;
            DownloadQueue.Dispose();
        }
    }
}
