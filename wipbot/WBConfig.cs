using IPA.Config.Stores.Attributes;
using IPA.Config.Stores.Converters;
using System;
using System.Collections.Generic;
using wipbot.Models;

namespace wipbot
{
    internal class WBConfig
    {
        public virtual int ZipMaxEntries { get; set; } = 100;
        public virtual int ZipMaxUncompressedSizeMB { get; set; } = 100;

        [UseConverter(typeof(ListConverter<string>))]
        [NonNullable]
        public virtual List<string> FileExtensionWhitelist { get; set; } = [
            "png", 
            "jpg", 
            "jpeg", 
            "dat", 
            "json", 
            "ogg", 
            "egg"
        ];

        [UseConverter(typeof(ListConverter<string>))]
        [NonNullable]
        public virtual List<string> RequestCodePrefixDownloadUrlPairs { get; set; } = [
          "0",
          "https://wipbot.com/wips/%s.zip"
        ];

        public virtual string RequestCodeCharacterWhitelist { get; set; } = "0123456789abcdefABCDEF";

        [UseConverter(typeof(ListConverter<string>))]
        [NonNullable]
        public virtual List<string> UrlWhitelist { get; set; } = [ 
            "https://cdn.discordapp.com/", 
            "https://drive.google.com/file/d/" 
        ];

        [UseConverter(typeof(ListConverter<string>))]
        [NonNullable]
        public virtual List<string> UrlFindReplace { get; set; } = [
            "https://drive.google.com/file/d/", 
            "https://drive.google.com/uc?id=", 
            "/view?usp=sharing", 
            "&export=download&confirm=t", 
            "/view?usp=drive_link", 
            "&export=download&confirm=t"
        ];

        public virtual string WipFolder { get; set; } = "Beat Saber_Data\\CustomWIPLevels\\";
        public virtual string CommandRequestWip { get; set; } = "!wip";
        public virtual string KeywordUndoRequest { get; set; } = "oops";
        public virtual QueueLimits QueueLimits { get; set; } = new QueueLimits { User = 2, Subscriber = 2, Vip = 2, Moderator = 2 };

        public virtual int ConfigVersion { get; set; } = 0;
        public virtual int QueueSize { get; set; } = 9;
        public virtual int ButtonPositionX { get; set; } = 138;
        public virtual int ButtonPositionY { get; set; } = -4;

        public virtual int ButtonFontSize { get; set; } = 3;
        public virtual int ButtonPrefWidth { get; set; } = 11;
        public virtual int ButtonPrefHeight { get; set; } = 6;

        public virtual string MessageHelp { get; set; } = "! To request a WIP, go to https://wipbot.com or upload the .zip anywhere on discord or on google drive, copy the download link and use the command !wip (link)";
        //public virtual string MessageInvalidRequest2 { get; set; } = "! Invalid request";
        public virtual string MessageWipRequested { get; set; } = "! WIP requested";
        public virtual string MessageUndoRequest { get; set; } = "! Removed your latest request from wip queue";
        //public virtual string MessageDownloadSuccess2 { get; set; } = "! Downloaded WIP from @%s";
        public virtual string MessageDownloadCancelled { get; set; } = "! WIP download cancelled";
        public virtual string ErrorMessageTooManyEntries { get; set; } = "! Error: Zip contains more than %i entries";
        public virtual string ErrorMessageInvalidFilename { get; set; } = "! Error: Zip contains file with invalid name";
        public virtual string ErrorMessageMaxLength { get; set; } = "! Error: Zip uncompressed length >%i MB";
        public virtual string ErrorMessageExtractionFailed { get; set; } = "! Error: Zip extraction failed";
        //public virtual string ErrorMessageBadExtension2 { get; set; } = "! Skipped %i files during extraction due to forbidden file extensions %s";
        public virtual string ErrorMessageMissingInfoDat { get; set; } = "! Error: WIP missing info.dat";
        //public virtual string ErrorMessageDownloadFailed2 { get; set; } = "! Error: WIP download failed (%s)";
        public virtual string ErrorMessageOther { get; set; } = "! Error: %s";
        public virtual string ErrorMessageLinkBlocked { get; set; } = "! Error: Your link was blocked by the channel's chat moderation settings";
        public virtual string ErrorMessageQueueFull { get; set; } = "! Error: The wip request queue is full";
        public virtual string ErrorMessageUserMaxRequests { get; set; } = "! Error: You already have the maximum number of wip requests in queue";
        public virtual string ErrorMessageNoPermission { get; set; } = "! Error: You don't have permission to use the wip command";

        public string MessageInvalidRequest => MessageHelp;
        public virtual string MessageDownloadStarted { get; set; } = "! WIP download started";
        public virtual string MessageDownloadSuccess { get; set; } = "! WIP download successful";
        public virtual string ErrorMessageZipContainsSubfolders { get; set; } = "! Error: Zip contains subfolders, not extracting";
        public virtual string ErrorMessageBadExtension { get; set; } = "! Skipped %i files during extraction due to bad file extension";
        public virtual string ErrorMessageDownloadFailed { get; set; } = "! Error: WIP download failed";

        public event Action<WBConfig> OnChanged;

        public virtual void Changed()
        {
            OnChanged?.Invoke(this);
        }
    }
}
