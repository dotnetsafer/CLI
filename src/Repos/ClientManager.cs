﻿using System;
using Bytehide.CLI.Models;
using Microsoft.Extensions.Logging;
using SecureLocalStorage;
using Shield.Client;

namespace Bytehide.CLI.Repos
{
    public class ClientManager
    {
        private readonly ILogger<Program> _iLogger;

        private SecureLocalStorage.SecureLocalStorage Storage { get; }

        private UserConfig UserConfig { get; set; }

        public ShieldClient Client { get; set; }

        private readonly string _userConfig = "user_config";

        /// <summary>
        /// Used for manage local storage user properties such a Bytehide api key and account setting.
        /// </summary>
        public ClientManager(ILogger<Program> iLogger)
        {
            _iLogger = iLogger;
            Storage = new SecureLocalStorage.SecureLocalStorage(new CustomLocalStorageConfig(null, "bytehide_shield_cli")
                .WithDefaultKeyBuilder());

            UserConfig = Storage.Exists(_userConfig) ? Storage.Get<UserConfig>(_userConfig) ?? new UserConfig() : new UserConfig();

            if (string.IsNullOrEmpty(UserConfig.ApiKey)) return;

            try
            {
                Client = new ShieldClient(UserConfig.ApiKey, iLogger);
            }
            catch (Exception)
            {
                Client = null;
            }
        }

        /// <summary>
        /// Gets if current computer user has any Bytehide key stored.
        /// </summary>
        /// <returns></returns>
        public bool HasKey() =>
            !string.IsNullOrEmpty(UserConfig.ApiKey);

        /// <summary>
        /// Checks if <param name="key">key</param> is valid.
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public bool IsValidKey(string key)
        {
            try
            {
                return ShieldClient.CreateInstance(key) is not null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Checks if current computer user has active and valid key.
        /// </summary>
        /// <returns></returns>
        public bool HasValidKey() => HasKey() && IsValidKey(UserConfig.ApiKey);

        /// <summary>
        /// Gets current user key.
        /// </summary>
        public string Key => UserConfig.ApiKey;

        /// <summary>
        /// Update current local user Bytehide key and instance client.
        /// </summary>
        /// <param name="key"></param>
        public void UpdateKey(string key)
        {
            try
            {
                Client = new ShieldClient(key, _iLogger);
                UserConfig.ApiKey = key;
                Storage.Set(_userConfig, UserConfig);
            }
            catch (Exception)
            {
                Client = null;
            }

        }
        public void ClearClient()
        {
            UserConfig.ApiKey = null;
            Storage.Set(_userConfig, UserConfig);
            Client = null;
        }
        /// <summary>
        /// Checks if current local user has a valid client instanced.
        /// </summary>
        /// <returns></returns>
        public bool HasValidClient() => Client is not null && Client.CheckConnection(out _);
    }
}
