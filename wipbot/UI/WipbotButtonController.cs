using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.ViewControllers;
using SiraUtil.Logging;
using System;
using System.Text;
using UnityEngine;
using wipbot.Interfaces;
using wipbot.Models;
using Zenject;

#pragma warning disable IDE0051 // Remove unused private members
namespace wipbot.UI
{
    internal class WipbotButtonController : BSMLAutomaticViewController, IInitializable
    {
        [Inject] private readonly SiraLog Logger;
        [Inject] private readonly WBConfig Config;
        [Inject] private readonly IChatIntegration ChatIntegration;
        [Inject] private readonly UnityMainThreadDispatcher MainThreadDispatcher;
        [Inject] private readonly LevelSelectionNavigationController navigationController;
        internal event Action OnWipButtonPressed;
        private bool _grayButtonActive = true;
        private bool _blueButtonActive = false;
        private string _blueButtonText = "wip";
        private string _blueButtonHint = "";

        [UIComponent("gray-button")]
        private readonly RectTransform grayButtonTransform;

        [UIComponent("blue-button")]
        private readonly RectTransform blueButtonTransform;

        [UIValue("gray-button-active")]
        public bool GrayButtonActive
        {
            get => _grayButtonActive;
            set { _grayButtonActive = value; MainThreadDispatcher.Enqueue(() => NotifyPropertyChanged()); }
        }

        [UIValue("blue-button-active")]
        public bool BlueButtonActive
        {
            get => _blueButtonActive;
            set { _blueButtonActive = value; MainThreadDispatcher.Enqueue(() => NotifyPropertyChanged()); }
        }

        [UIValue("blue-button-text")]
        public string BlueButtonText
        {
            get => _blueButtonText;
            set { _blueButtonText = value; MainThreadDispatcher.Enqueue(() => NotifyPropertyChanged()); }
        }

        [UIValue("blue-button-hint")]
        public string BlueButtonHint
        {
            get => _blueButtonHint;
            set { _blueButtonHint = value; MainThreadDispatcher.Enqueue(() => NotifyPropertyChanged()); }
        }

        public void Initialize()
        {
            if (grayButtonTransform != null) return;
            BSMLParser.Instance.Parse(
              $@"
                <bg
                  xmlns:xsi='http://www.w3.org/2001/XMLSchema-instance'
                  xsi:schemaLocation='https://monkeymanboy.github.io/BSML-Docs/ https://raw.githubusercontent.com/monkeymanboy/BSML-Docs/gh-pages/BSMLSchema.xsd'
                >
                  <button
                    id='gray-button'
                    active='~gray-button-active'
                    text='wip'
                    font-size='{Config.ButtonFontSize}'
                    on-click='gray-button-click'
                    anchor-pos-x='{Config.ButtonPositionX}'
                    anchor-pos-y='{Config.ButtonPositionY + 2}'
                    pref-height='{Config.ButtonPrefHeight}'
                    pref-width='{Config.ButtonPrefWidth}'
                  />
                  <action-button
                    id='blue-button'
                    active='~blue-button-active'
                    text='~blue-button-text'
                    hover-hint='~blue-button-hint'
                    word-wrapping='false'
                    font-size='{Config.ButtonFontSize}'
                    on-click='blue-button-click'
                    anchor-pos-x='{Config.ButtonPositionX - 80}'
                    anchor-pos-y='{Config.ButtonPositionY + 5}'
                    pref-height='{Config.ButtonPrefHeight}'
                    pref-width='{Config.ButtonPrefWidth}'
                  />
                </bg>
            ", navigationController.gameObject, this);

            Config.OnChanged += Config_OnChanged;
        }

        private void Config_OnChanged(WBConfig newConfig)
        {
            var currGrayPos = grayButtonTransform.position;
            var currBluePos = blueButtonTransform.position;

            var confGrayPos = new Vector3(newConfig.ButtonPositionX, newConfig.ButtonPositionY + 2, currGrayPos.z);
            var confBluePos = new Vector3(newConfig.ButtonPositionX - 80, newConfig.ButtonPositionY + 5, currBluePos.z);

            if (currGrayPos != confGrayPos)
            {
                grayButtonTransform.position = confGrayPos;
                blueButtonTransform.position = confBluePos;
            }

            MainThreadDispatcher.Enqueue(() => NotifyPropertyChanged());
        }

        [UIAction("blue-button-click")]
        void DownloadButtonPressed()
        {
            OnWipButtonPressed?.Invoke();
        }

        [UIAction("gray-button-click")]
        void GrayButtonPressed()
        {
            ChatIntegration.SendChatMessage(Config.MessageHelp);
        }

        internal void UpdateButtonState(QueueItem[] queueState)
        {
            try
            {
                BlueButtonText = "wip(" + queueState.Length + ")";
                GrayButtonActive = queueState.Length == 0;

                var stringBuilder = new StringBuilder();

                for (int i = 0; i < queueState.Length; i++)
                {
                    stringBuilder.Append(i + 1);
                    stringBuilder.Append(": ");
                    stringBuilder.Append(queueState[i].UserName);
                    stringBuilder.Append("; ");
                }

                BlueButtonHint = stringBuilder.ToString();

                BlueButtonActive = queueState.Length > 0;
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }
        }
    }
}
